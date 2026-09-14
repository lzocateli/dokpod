<#
.SYNOPSIS
Obtém um access token efêmero por Authorization Code + PKCE e executa a prova UMA Dokpod.

.DESCRIPTION
Inicia um callback HTTP loopback, abre o login do client público dokpod-lab,
valida state, troca o authorization code por access token e chama o script
 test-keycloak-authorization-spike.ps1. O token permanece somente em memória,
nunca é exibido, persistido ou colocado em variável de ambiente permanente.

.PARAMETER BaseUrl
URL externa do Keycloak. Padrão: https://localhost:7443.

.PARAMETER Realm
Realm da aplicação. Padrão: dokpod.

.PARAMETER ClientId
Client público de laboratório. Padrão: dokpod-lab.

.PARAMETER RedirectUri
Callback loopback previamente cadastrado no client. Padrão:
http://127.0.0.1:8765/callback/. Também aceita localhost.

.PARAMETER EnvironmentId
GUID do ambiente usado na decisão UMA.

.PARAMETER Scope
Scope da decisão UMA. Padrão: environment:read.

.PARAMETER Expected
Resultado esperado: allowed, denied ou any. Padrão: allowed.

.PARAMETER RequestTimeoutSeconds
Timeout da troca do code por token. Padrão: 30.

.PARAMETER SkipSpike
Somente obtém o token em memória e encerra sem executar a decisão UMA. O token
não é exibido; esta opção serve apenas para composição por outro processo.

.PARAMETER DryRun
Mostra a sequência sem abrir browser, iniciar listener ou acessar rede.

.PARAMETER RemainingArguments
Aceita somente --help.

.EXAMPLE
./tools/scripts/get-keycloak-spike-token.ps1 --help

.EXAMPLE
./tools/scripts/get-keycloak-spike-token.ps1 -DryRun

.EXAMPLE
./tools/scripts/get-keycloak-spike-token.ps1 `
  -EnvironmentId 00000000-0000-0000-0000-000000000001 `
  -Scope environment:read `
  -Expected allowed

.NOTES
Requer PowerShell 7.4+, client dokpod-lab público com PKCE S256 e redirect URI
exato. O usuário conclui o login no browser. O script não lê arquivos de
secrets e não imprime tokens.

.LINK
../../docs/tutorial-obter-token-spike-uma.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidatePattern('^https?://')]
    [string] $BaseUrl = 'https://localhost:7443',

    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Realm = 'dokpod',

    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $ClientId = 'dokpod-lab',

    [ValidatePattern('^http://(?:localhost|127\.0\.0\.1):[0-9]+/callback/$')]
    [string] $RedirectUri = 'http://127.0.0.1:8765/callback/',

    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string] $EnvironmentId = '00000000-0000-0000-0000-000000000001',

    [ValidateSet('environment:read', 'environment:manage', 'container:start', 'container:stop', 'container:restart', 'container:delete', 'audit:read')]
    [string] $Scope = 'environment:read',

    [ValidateSet('allowed', 'denied', 'any')]
    [string] $Expected = 'allowed',

    [ValidateRange(1, 120)]
    [int] $RequestTimeoutSeconds = 30,

    [switch] $SkipSpike,
    [switch] $DryRun,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RemainingArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$BaseUrl = $BaseUrl.TrimEnd('/')
$authority = "$BaseUrl/realms/$Realm"
$tokenEndpoint = "$authority/protocol/openid-connect/token"
$authorizationEndpoint = "$authority/protocol/openid-connect/auth"
$useNoProxy = ([Uri]$BaseUrl).IsLoopback

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}
if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine("Argumento desconhecido: $($RemainingArguments -join ' '). Use --help.")
    exit 2
}
if ($Realm -eq 'master') { throw 'O fluxo não pode usar o realm administrativo master.' }
if ($DryRun) {
    [pscustomobject]@{
        authority = $authority
        clientId = $ClientId
        redirectUri = $RedirectUri
        environmentId = $EnvironmentId
        scope = $Scope
        expected = $Expected
        steps = @('generate-pkce', 'listen-loopback', 'browser-login', 'validate-state', 'exchange-code', 'run-uma-spike')
    } | ConvertTo-Json -Depth 4
    exit 0
}

function ConvertTo-Base64Url {
    param([Parameter(Mandatory)][byte[]] $Bytes)
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$bytes = [byte[]]::new(64)
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$verifier = ConvertTo-Base64Url $bytes
$hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier))
$challenge = ConvertTo-Base64Url $hash
$stateBytes = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($stateBytes)
$expectedState = ConvertTo-Base64Url $stateBytes

$query = [System.Web.HttpUtility]::ParseQueryString('')
$query['client_id'] = $ClientId
$query['redirect_uri'] = $RedirectUri
$query['response_type'] = 'code'
$query['scope'] = 'openid profile email'
$query['code_challenge'] = $challenge
$query['code_challenge_method'] = 'S256'
$query['state'] = $expectedState
$authorizationUri = "$authorizationEndpoint`?$($query.ToString())"

$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add($RedirectUri)
$listener.Start()
try {
    Write-Host 'Abrindo o login do Keycloak no browser. Conclua a autenticação do usuário de laboratório.'
    Start-Process $authorizationUri
    $context = $listener.GetContext()
    $callback = $context.Request.Url
    $responseText = '<html><body>Login recebido. Pode fechar esta janela.</body></html>'
    $responseBytes = [Text.Encoding]::UTF8.GetBytes($responseText)
    $context.Response.ContentType = 'text/html; charset=utf-8'
    $context.Response.ContentLength64 = $responseBytes.Length
    $context.Response.OutputStream.Write($responseBytes, 0, $responseBytes.Length)
    $context.Response.Close()
}
finally {
    $listener.Stop()
    $listener.Close()
}

$params = [System.Web.HttpUtility]::ParseQueryString($callback.Query)
if ($params['state'] -ne $expectedState) { throw 'State inválido no callback.' }
if (-not [string]::IsNullOrWhiteSpace($params['error'])) {
    throw "Keycloak recusou o login: $($params['error'])."
}
$code = $params['code']
if ([string]::IsNullOrWhiteSpace($code)) { throw 'Authorization code ausente no callback.' }

$tokenRequest = @{
    grant_type = 'authorization_code'
    client_id = $ClientId
    code = $code
    redirect_uri = $RedirectUri
    code_verifier = $verifier
}
$tokenParameters = @{
    Method = 'Post'
    Uri = $tokenEndpoint
    ContentType = 'application/x-www-form-urlencoded'
    Body = $tokenRequest
    TimeoutSec = $RequestTimeoutSeconds
    ErrorAction = 'Stop'
}
if ($useNoProxy) { $tokenParameters.NoProxy = $true }
$tokenResponse = Invoke-RestMethod @tokenParameters
$accessToken = $tokenResponse.access_token
if ([string]::IsNullOrWhiteSpace($accessToken)) { throw 'Keycloak não retornou access_token.' }

try {
    if ($SkipSpike) {
        Write-Output 'Token obtido em memória; -SkipSpike solicitado, nenhuma decisão UMA executada.'
        exit 0
    }

    $secureToken = ConvertTo-SecureString $accessToken -AsPlainText -Force
    & (Join-Path $PSScriptRoot 'test-keycloak-authorization-spike.ps1') `
        -UserAccessToken $secureToken `
        -EnvironmentId $EnvironmentId `
        -Scope $Scope `
        -Expected $Expected
    exit $LASTEXITCODE
}
finally {
    Remove-Variable accessToken, secureToken, tokenResponse, code, verifier, challenge, expectedState -ErrorAction SilentlyContinue
}
