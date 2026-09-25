<#
.SYNOPSIS
Provisiona autorização UMA de um ambiente Dokpod para um usuário ou grupo.

.DESCRIPTION
Cria ou reutiliza o recurso `urn:dokpod:environment:{EnvironmentId}` no
Authorization Services do client `dokpod-api`, encontra um usuário ou grupo do
realm `dokpod`, cria uma policy e cria permissions resource por scope.

A operação é administrativa e idempotente. O runtime da API somente consulta
decisões UMA; ele não cria policies nem usa credencial administrativa.

.PARAMETER EnvironmentId
GUID opaco e estável do ambiente Dokpod.

.PARAMETER OwnerUsername
Usuário existente no realm que receberá a policy. Exclusivo com GroupPath.

.PARAMETER GroupPath
Grupo existente, por exemplo `/dokpod/administrators`. Exclusivo com OwnerUsername.

.PARAMETER Scope
Scope a conceder. Padrão: environment:read.

.PARAMETER BaseUrl
URL do Keycloak sem barra final. Padrão: http://localhost:8080.

.PARAMETER Realm
Realm administrado. Padrão: dokpod.

.PARAMETER DryRun
Exibe o plano sem acessar a rede, solicitar credenciais ou alterar o Keycloak.

.PARAMETER RemainingArguments
Aceita somente --help.

.EXAMPLE
./tools/scripts/provision-environment-authorization.ps1 --help

.EXAMPLE
./tools/scripts/provision-environment-authorization.ps1 `
  -EnvironmentId 00000000-0000-0000-0000-000000000001 `
  -GroupPath /dokpod/administrators `
  -Scope environment:read

.EXAMPLE
./tools/scripts/provision-environment-authorization.ps1 `
  -EnvironmentId 00000000-0000-0000-0000-000000000001 `
  -OwnerUsername dokpod-admin `
  -DryRun

.NOTES
Requer PowerShell 7.4+ e DOKPOD_PROVISIONER_CLIENT_SECRET injetado por ambiente
seguro. Não leia ou copie arquivos de secrets para este repositório. A
credencial de provisionamento não é usada pelo runtime da API.

.LINK
../../docs/configuracao-keycloak.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string] $EnvironmentId = '',

    [ValidatePattern('^[^\s]+$')]
    [string] $OwnerUsername,

    [ValidatePattern('^/dokpod/[a-z]+$')]
    [string] $GroupPath,

    [ValidateSet('environment:read', 'environment:manage', 'container:start', 'container:stop', 'container:restart', 'container:delete', 'audit:read')]
    [string] $Scope = 'environment:read',

    [ValidatePattern('^https?://')]
    [string] $BaseUrl = 'http://localhost:8080',

    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Realm = 'dokpod',

    [switch] $DryRun,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RemainingArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$BaseUrl = $BaseUrl.TrimEnd('/')
$baseUri = [Uri]$BaseUrl
$useNoProxy = $baseUri.IsLoopback
$resourceName = "urn:dokpod:environment:$EnvironmentId"
$clientId = 'dokpod-api'

if ($RemainingArguments -contains '--help') { Get-Help $PSCommandPath -Full; exit 0 }
if ($RemainingArguments.Count -gt 0) { throw "Argumento desconhecido: $($RemainingArguments -join ' '). Use --help." }
if ([string]::IsNullOrWhiteSpace($EnvironmentId)) { throw 'EnvironmentId é obrigatório. Use --help.' }
if ($Realm -eq 'master') { throw 'O realm master não pode ser administrado por este script.' }
if ([string]::IsNullOrWhiteSpace($OwnerUsername) -and [string]::IsNullOrWhiteSpace($GroupPath)) { throw 'Informe OwnerUsername ou GroupPath.' }
if (-not [string]::IsNullOrWhiteSpace($OwnerUsername) -and -not [string]::IsNullOrWhiteSpace($GroupPath)) { throw 'OwnerUsername e GroupPath são exclusivos.' }
if ($baseUri.Scheme -ne 'https' -and -not $baseUri.IsLoopback) { throw 'BaseUrl deve usar HTTPS fora do ambiente local.' }

if ($DryRun) {
    [pscustomobject]@{
        realm = $Realm
        client = $clientId
        resource = $resourceName
        scope = $Scope
        subject = if ($OwnerUsername) { "user:$OwnerUsername" } else { "group:$GroupPath" }
        operations = @('find-client', 'find-scope', 'create-or-reuse-resource', 'create-or-reuse-policy', 'create-or-reuse-permission')
    } | ConvertTo-Json -Depth 3
    exit 0
}

function Import-EnvironmentFile {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*(?:export\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)\s*$') {
            $name = $Matches.name
            if (-not [Environment]::GetEnvironmentVariable($name, 'Process')) {
                $value = $Matches.value.Trim()
                if ($value.Length -ge 2 -and $value[0] -eq '"' -and $value[$value.Length - 1] -eq '"') {
                    $value = $value.Substring(1, $value.Length - 2)
                }
                elseif ($value.Length -ge 2 -and $value[0] -eq "'" -and $value[$value.Length - 1] -eq "'") {
                    $value = $value.Substring(1, $value.Length - 2)
                }
                [Environment]::SetEnvironmentVariable($name, $value, 'Process')
            }
        }
    }
}

function Import-LocalEnvironment {
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) { return }

    Import-EnvironmentFile -Path (Join-Path $env:APPDATA 'Microsoft/UserSecrets/Dokpod/.env')
}

function Get-EnvironmentSecret {
    param([Parameter(Mandatory)][string[]] $Names)
    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }
    return $null
}

Import-LocalEnvironment
$provisionerSecret = Get-EnvironmentSecret @('DOKPOD_PROVISIONER_CLIENT_SECRET')
if ([string]::IsNullOrWhiteSpace($provisionerSecret)) { throw 'Defina DOKPOD_PROVISIONER_CLIENT_SECRET em ambiente seguro.' }

function Invoke-Api {
    param([ValidateSet('GET','POST','PUT')][string] $Method, [string] $Uri, [object] $Body)
    $parameters = @{ Method = $Method; Uri = $Uri; Headers = @{ Authorization = "Bearer $script:accessToken" }; ErrorAction = 'Stop' }
    if ($null -ne $Body) { $parameters.ContentType = 'application/json'; $parameters.Body = $Body | ConvertTo-Json -Depth 10 -Compress }
    if ($useNoProxy) { $parameters.NoProxy = $true }
    try { Invoke-RestMethod @parameters }
    catch { throw "Falha na API UMA em $Method ${Uri}: $($_.Exception.Message)" }
}

function Find-One {
    param([object[]] $Items, [string] $Description)
    $matches = @($Items)
    if ($matches.Count -ne 1) { throw "$Description deve resolver exatamente um resultado; encontrados $($matches.Count)." }
    $matches[0]
}

$tokenParameters = @{ Method = 'Post'; Uri = "$BaseUrl/realms/$Realm/protocol/openid-connect/token"; ContentType = 'application/x-www-form-urlencoded'; Body = @{ grant_type = 'client_credentials'; client_id = 'dokpod-provisioner'; client_secret = $provisionerSecret }; ErrorAction = 'Stop' }
if ($useNoProxy) { $tokenParameters.NoProxy = $true }
$script:accessToken = (Invoke-RestMethod @tokenParameters).access_token
if ([string]::IsNullOrWhiteSpace($script:accessToken)) { throw 'Keycloak não retornou token administrativo.' }

$apiClient = Find-One (Invoke-Api GET "$BaseUrl/admin/realms/$Realm/clients?clientId=$clientId") "client $clientId"
$authzBase = "$BaseUrl/admin/realms/$Realm/clients/$($apiClient.id)/authz/resource-server"
$scopeRecord = Find-One (Invoke-Api GET "$authzBase/scope?name=$([Uri]::EscapeDataString($Scope))&exact=true") "scope $Scope"
$resourceResponse = @(@(Invoke-Api GET "$authzBase/resource?name=$([Uri]::EscapeDataString($resourceName))&exactName=true") | ForEach-Object { $_ })
$resourceMatches = @($resourceResponse | Where-Object { $null -ne $_ })
if ($resourceMatches.Count -eq 0) {
    $resource = Invoke-Api POST "$authzBase/resource" @{
        name = $resourceName
        displayName = "Dokpod environment $EnvironmentId"
        scopes = @(@{ id = $scopeRecord.id; name = $Scope })
        ownerManagedAccess = $false
    }
    $resourceId = if ($resource.PSObject.Properties.Name -contains 'id' -and $resource.id) { $resource.id } else { $resource._id }
    $resource = Invoke-Api GET "$authzBase/resource/$resourceId"
    Write-Output "Recurso UMA criado: $resourceName"
}
else {
    $resource = Find-One $resourceMatches "recurso $resourceName"
    $resourceId = if ($resource.PSObject.Properties.Name -contains 'id' -and $resource.id) { $resource.id } else { $resource._id }
    $resource = Invoke-Api GET "$authzBase/resource/$resourceId"
    $resourceScopes = @(@($resource.scopes) | ForEach-Object { $_ })
    if ($resourceScopes.id -notcontains $scopeRecord.id) {
        $resource.scopes = @($resourceScopes + @(@{ id = $scopeRecord.id; name = $Scope }))
        Invoke-Api PUT "$authzBase/resource/$resourceId" $resource | Out-Null
        Write-Output "Recurso UMA atualizado com scope ${Scope}: $resourceName"
    }
    else {
        Write-Output "Recurso UMA já existe: $resourceName"
    }
}

if ($OwnerUsername) {
    $user = Find-One ((Invoke-Api GET "$BaseUrl/admin/realms/$Realm/users?username=$([Uri]::EscapeDataString($OwnerUsername))&exact=true") | Where-Object { $null -ne $_ -and $_.PSObject.Properties.Name -contains 'username' -and $_.username -eq $OwnerUsername }) "usuário $OwnerUsername"
    $policyName = "environment-$EnvironmentId-owner-$OwnerUsername"
    $policyType = 'user'
    $policyBody = @{ name = $policyName; type = 'user'; logic = 'POSITIVE'; decisionStrategy = 'UNANIMOUS'; users = @([string]$user.id) }
}
else {
    $segments = $GroupPath.Trim('/').Split('/')
    $rootGroups = @(Invoke-Api GET "$BaseUrl/admin/realms/$Realm/groups?search=$($segments[0])&exact=true")
    $rootMatches = @($rootGroups | ForEach-Object { $_ } | Where-Object { $null -ne $_ -and $_.PSObject.Properties.Name -contains 'path' -and $_.path -eq "/$($segments[0])" })
    $parent = Find-One $rootMatches "grupo $($segments[0])"
    for ($index = 1; $index -lt $segments.Count; $index++) {
        $childGroups = @(Invoke-Api GET "$BaseUrl/admin/realms/$Realm/groups/$($parent.id)/children?search=$($segments[$index])&exact=true")
        $childMatches = @($childGroups | ForEach-Object { $_ } | Where-Object { $null -ne $_ -and $_.PSObject.Properties.Name -contains 'path' -and $_.path -eq "/$($segments[0..$index] -join '/')" })
        $parent = Find-One $childMatches "grupo $GroupPath"
    }
    $policyName = "environment-$EnvironmentId-group-$($segments[-1])"
    $policyType = 'group'
    $policyBody = @{ name = $policyName; type = 'group'; logic = 'POSITIVE'; decisionStrategy = 'UNANIMOUS'; groupsClaim = 'groups'; groups = @(@{ id = $parent.id; path = $GroupPath }) }
}

$policyResponse = @(Invoke-Api GET "$authzBase/policy")
$policies = @($policyResponse.Where({ $null -ne $_ -and $_.PSObject.Properties.Name -contains 'name' -and $_.name -eq $policyName }))
if ($policies.Count -eq 0) {
    try { $policy = Invoke-Api POST "$authzBase/policy/$policyType" $policyBody; Write-Output "Policy UMA criada: $policyName" }
    catch {
        if ($_.Exception.Message -notmatch '409') { throw }
        $policy = Find-One @((Invoke-Api GET "$authzBase/policy").Where({ $_.name -eq $policyName })) "policy $policyName"
        Write-Output "Policy UMA já existe: $policyName"
    }
}
else { $policy = Find-One $policies "policy $policyName"; Write-Output "Policy UMA já existe: $policyName" }

$permissionName = "environment-$EnvironmentId-$($Scope.Replace(':','-'))"
$encodedPermissionName = [Uri]::EscapeDataString($permissionName)
$permissionResponse = @(Invoke-Api GET "$authzBase/permission?name=$encodedPermissionName&exact=true")
$permissions = @($permissionResponse.Where({ $null -ne $_ -and $_.PSObject.Properties.Name -contains 'name' -and $_.name -eq $permissionName }))
$permissionBody = @{
    name = $permissionName; type = 'resource'; logic = 'POSITIVE'; decisionStrategy = 'UNANIMOUS'
    resources = @([string]$resourceId); scopes = @([string]$scopeRecord.id); policies = @([string]$policy.id)
}
function Update-Permission {
    param([Parameter(Mandatory)][object] $Permission)

    $permissionId = if ($Permission.PSObject.Properties.Name -contains 'id') { $Permission.id } else { $Permission._id }
    $currentPolicies = @(@(Invoke-Api GET "$authzBase/policy/$permissionId/associatedPolicies") | ForEach-Object { $_ })
    $currentResources = @(@(Invoke-Api GET "$authzBase/permission/$permissionId/resources") | ForEach-Object { $_ })
    $currentScopes = @(@(Invoke-Api GET "$authzBase/permission/$permissionId/scopes") | ForEach-Object { $_ })
    $currentPolicyIds = @($currentPolicies | ForEach-Object { [string]$_.id })
    $currentResourceIds = @($currentResources | ForEach-Object {
        if ($_.PSObject.Properties.Name -contains 'id' -and $_.id) { [string]$_.id } else { [string]$_._id }
    })
    $currentScopeIds = @($currentScopes | ForEach-Object { [string]$_.id })
    $permissionBody.resources = @($currentResourceIds + @([string]$resourceId) | Select-Object -Unique)
    $permissionBody.scopes = @($currentScopeIds + @([string]$scopeRecord.id) | Select-Object -Unique)
    $permissionBody.policies = @($currentPolicyIds + @([string]$policy.id) | Select-Object -Unique)
    Invoke-Api PUT "$authzBase/permission/resource/$permissionId" $permissionBody | Out-Null
    Write-Output "Permission UMA atualizada: $permissionName"
}
if ($permissions.Count -eq 0) {
    try {
        Invoke-Api POST "$authzBase/permission/resource" $permissionBody | Out-Null
        Write-Output "Permission UMA criada: $permissionName"
    }
    catch {
        if ($_.Exception.Message -notmatch '409') { throw }
        $existingPermissions = @(Invoke-Api GET "$authzBase/permission?name=$encodedPermissionName&exact=true")
        $permission = Find-One @($existingPermissions.Where({ $_.name -eq $permissionName })) "permission $permissionName"
        Update-Permission -Permission $permission
    }
}
else {
    $permission = Find-One $permissions "permission $permissionName"
    Update-Permission -Permission $permission
}

Remove-Variable adminPassword, accessToken -Scope Script -ErrorAction SilentlyContinue
Write-Output 'Provisionamento UMA concluído.'
