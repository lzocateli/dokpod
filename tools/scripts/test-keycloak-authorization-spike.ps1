<#
.SYNOPSIS
Valida uma decisão UMA real do Keycloak para um ambiente Dokpod.

.DESCRIPTION
Envia uma solicitação UMA decision ao realm dokpod usando um access token
efêmero fornecido pelo processo. O token nunca é exibido, persistido ou incluído
na saída. O recurso deve existir no Authorization Services do client dokpod-api.

.PARAMETER BaseUrl
URL do Keycloak sem barra final. Padrão: http://localhost:8080.

.PARAMETER Realm
Realm da aplicação. Padrão: dokpod.

.PARAMETER UserAccessToken
Access token efêmero. Quando omitido, lê DOKPOD_SPIKE_USER_ACCESS_TOKEN do
ambiente já injetado pelo processo; o script não lê arquivos de secrets.

.PARAMETER EnvironmentId
GUID do ambiente autorizado. Padrão: 00000000-0000-0000-0000-000000000001.

.PARAMETER Scope
Scope avaliado. Padrão: environment:read.

.PARAMETER Iterations
Quantidade de decisões sequenciais. Padrão: 1.

.PARAMETER RequestTimeoutSeconds
Timeout de cada chamada. Padrão: 15.

.PARAMETER Expected
Resultado esperado: allowed, denied ou any. Padrão: any.

.PARAMETER DryRun
Valida parâmetros e mostra o plano sem acessar rede ou credenciais.

.PARAMETER RemainingArguments
Aceita somente --help.

.EXAMPLE
./tools/scripts/test-keycloak-authorization-spike.ps1 --help

.EXAMPLE
./tools/scripts/test-keycloak-authorization-spike.ps1 -DryRun

.EXAMPLE
$token = Read-Host 'Access token efêmero' -AsSecureString
./tools/scripts/test-keycloak-authorization-spike.ps1 -UserAccessToken $token -Expected allowed

.NOTES
Requer PowerShell 7.4 ou superior e um recurso UMA provisionado como
urn:dokpod:environment:{EnvironmentId} no client dokpod-api.
Secrets administrativos, client secrets e tokens nunca devem ser colocados em
argumentos persistidos, arquivos ou logs.

.LINK
../../docs/configuracao-keycloak.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidatePattern('^https?://')]
    [string] $BaseUrl = 'http://localhost:8080',

    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Realm = 'dokpod',

    [SecureString] $UserAccessToken,

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string] $EnvironmentId = '00000000-0000-0000-0000-000000000001',

    [ValidateSet('environment:read', 'environment:manage', 'container:start', 'container:stop', 'container:restart', 'container:delete', 'audit:read')]
    [string] $Scope = 'environment:read',

    [ValidateRange(1, 100)]
    [int] $Iterations = 1,

    [ValidateRange(1, 30)]
    [int] $RequestTimeoutSeconds = 15,

    [ValidateSet('allowed', 'denied', 'any')]
    [string] $Expected = 'any',

    [switch] $DryRun,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RemainingArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$BaseUrl = $BaseUrl.TrimEnd('/')
$baseUri = [Uri]$BaseUrl
$resource = "urn:dokpod:environment:$EnvironmentId"
$tokenEndpoint = "$BaseUrl/realms/$Realm/protocol/openid-connect/token"
$useNoProxy = $baseUri.IsLoopback

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine("Argumento desconhecido: $($RemainingArguments -join ' '). Use --help.")
    exit 2
}

if ($Realm -eq 'master') {
    throw 'O spike não pode usar o realm administrativo master.'
}

if ($baseUri.Scheme -ne 'https' -and -not $baseUri.IsLoopback) {
    throw 'BaseUrl deve usar HTTPS fora do ambiente local.'
}

if ($DryRun) {
    [pscustomobject]@{
        baseUrl = $BaseUrl
        realm = $Realm
        resource = $resource
        scope = $Scope
        iterations = $Iterations
        expected = $Expected
        operation = 'UMA decision via dokpod-api'
    } | ConvertTo-Json -Depth 3
    exit 0
}

function ConvertFrom-SecureValue {
    param([Parameter(Mandatory)][SecureString] $Value)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

if (-not $UserAccessToken) {
    $environmentToken = [Environment]::GetEnvironmentVariable('DOKPOD_SPIKE_USER_ACCESS_TOKEN')
    if ([string]::IsNullOrWhiteSpace($environmentToken)) {
        throw 'Forneça -UserAccessToken ou injete DOKPOD_SPIKE_USER_ACCESS_TOKEN no processo. Use --help.'
    }

    $UserAccessToken = ConvertTo-SecureString $environmentToken -AsPlainText -Force
}

$plainToken = ConvertFrom-SecureValue $UserAccessToken
$results = [System.Collections.Generic.List[object]]::new()

for ($index = 1; $index -le $Iterations; $index++) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $response = $null
    try {
        $parameters = @{
            Method = 'POST'
            Uri = $tokenEndpoint
            Headers = @{ Authorization = "Bearer $plainToken" }
            Body = @{
                grant_type = 'urn:ietf:params:oauth:grant-type:uma-ticket'
                audience = 'dokpod-api'
                permission = "$resource#$Scope"
                response_mode = 'decision'
            }
            ContentType = 'application/x-www-form-urlencoded'
            SkipHttpErrorCheck = $true
            MaximumRedirection = 0
            TimeoutSec = $RequestTimeoutSeconds
        }
        if ($useNoProxy) { $parameters.NoProxy = $true }
        $response = Invoke-WebRequest @parameters
        $body = $response.Content | ConvertFrom-Json
        $allowed = $response.StatusCode -ge 200 -and $response.StatusCode -lt 300 -and $body.result -eq $true
        $outcome = if ($allowed) { 'allowed' } else { 'denied' }
    }
    catch {
        $outcome = 'indeterminate'
        $statusCode = 0
        if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
    }
    finally {
        $stopwatch.Stop()
    }

    $statusCode = if ($response) { [int]$response.StatusCode } else { $statusCode }
    $results.Add([pscustomobject]@{
        iteration = $index
        outcome = $outcome
        statusCode = $statusCode
        durationMs = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
    })
}

if ($Expected -ne 'any' -and @($results | Where-Object { $_.outcome -ne $Expected }).Count -gt 0) {
    $results | ConvertTo-Json -Depth 3
    exit 1
}

$results | ConvertTo-Json -Depth 3
exit 0
