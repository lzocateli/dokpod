<#
.SYNOPSIS
Reconcilia a configuração do realm Dokpod pela Keycloak Admin REST API.

.DESCRIPTION
Obtém um token administrativo no realm master da instância compartilhada de
Keycloak e cria ou atualiza o realm Dokpod, clients, audience, roles, grupos,
usuário inicial, SMTP e provedores sociais habilitados por variáveis de ambiente.
Secrets são lidos apenas do ambiente ou dos arquivos externos de UserSecrets e
nunca são exibidos.

.PARAMETER BaseUrl
URL externa do Keycloak, sem barra final. Padrão: http://localhost:8080.

.PARAMETER Realm
Realm administrado. Padrão: dokpod.

.PARAMETER AdminUsername
Usuário administrativo da instância compartilhada. Padrão: DOKPOD_KEYCLOAK_ADMIN_USERNAME,
ALTIVY_KEYCLOAK_ADMIN_USERNAME ou admin.

.PARAMETER AdminPassword
SecureString administrativo. Quando omitido, lê DOKPOD_KEYCLOAK_ADMIN_PASSWORD ou
ALTIVY_KEYCLOAK_ADMIN_PASSWORD.

.PARAMETER InitialUserEmail
E-mail do usuário inicial do laboratório. Quando omitido, usa DOKPOD_ADMIN_EMAIL;
se nenhum valor existir, não cria usuário.

.PARAMETER BffBaseUrl
Origem externa do BFF. Padrão: https://localhost:7443.

.PARAMETER SkipUser
Não cria nem atualiza o usuário inicial, mesmo quando DOKPOD_ADMIN_EMAIL estiver definido.

.PARAMETER DryRun
Exibe o plano sem acessar a rede, solicitar credenciais ou alterar o Keycloak.

.PARAMETER RemainingArguments
Argumentos literais remanescentes. Aceita somente --help.

.EXAMPLE
./tools/scripts/configure-keycloak.ps1 --help

.EXAMPLE
$env:DOKPOD_KEYCLOAK_ADMIN_PASSWORD = '<senha-no-cofre>'
./tools/scripts/configure-keycloak.ps1 -InitialUserEmail admin@example.test

.EXAMPLE
./tools/scripts/configure-keycloak.ps1 `
  -BaseUrl https://credential.zocate.li `
  -BffBaseUrl https://dokpod.local `
  -DryRun

.INPUTS
Nenhum.

.OUTPUTS
Resumo das operações aplicadas, sem credenciais ou tokens.

.NOTES
Requer PowerShell 7.4 ou superior e Keycloak 26.7.0. O fluxo funcional requer
acesso HTTPS ao Keycloak e uma conta com permissão para administrar o realm.
Use um cofre para injetar DOKPOD_KEYCLOAK_ADMIN_PASSWORD, DOKPOD_BFF_CLIENT_SECRET,
DOKPOD_PROVISIONER_CLIENT_SECRET, DOKPOD_SMTP_PASSWORD e DOKPOD_IDP_<PROVEDOR>_CLIENT_SECRET.
Em laboratório compartilhado, as credenciais administrativas ALTIVY_KEYCLOAK_ADMIN_USERNAME
e ALTIVY_KEYCLOAK_ADMIN_PASSWORD também são aceitas para operar a mesma instância.

.LINK
../../docs/configuracao-keycloak.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidatePattern('^https?://')]
    [string] $BaseUrl = 'http://localhost:8080',

    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Realm = 'dokpod',

    [string] $AdminUsername,

    [SecureString] $AdminPassword,

    [ValidatePattern('^[^@\s]+@[^@\s]+\.[^@\s]+$')]
    [string] $InitialUserEmail,

    [ValidatePattern('^https?://')]
    [string] $BffBaseUrl = 'https://localhost:7443',

    [switch] $SkipUser,

    [switch] $DryRun,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RemainingArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$BaseUrl = $BaseUrl.TrimEnd('/')
$BffBaseUrl = $BffBaseUrl.TrimEnd('/')
$baseUri = [Uri]$BaseUrl
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
    throw 'O realm administrativo master não pode ser gerenciado por este script. Use um realm dedicado.'
}

if ($baseUri.Scheme -ne 'https' -and -not $baseUri.IsLoopback) {
    throw 'BaseUrl deve usar HTTPS fora do ambiente local.'
}

function Import-EnvironmentFile {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }

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
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
        return
    }

    Import-EnvironmentFile -Path (Join-Path $env:APPDATA 'Microsoft/UserSecrets/Dokpod/.env')
    Import-EnvironmentFile -Path (Join-Path $env:APPDATA 'Microsoft/UserSecrets/Altivy.Notes/.env')
}

function Get-EnvironmentSecret {
    param([Parameter(Mandatory)][string[]] $Names)

    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return $null
}

Import-LocalEnvironment

if (-not $PSBoundParameters.ContainsKey('Realm')) {
    $configuredRealm = Get-EnvironmentSecret @('ALTIVY_KEYCLOAK_REALM')
    if ($configuredRealm -eq 'dokpod') {
        $Realm = $configuredRealm
    }
    elseif ($configuredRealm -and $configuredRealm -ne 'dokpod') {
        Write-Warning "Ignorando ALTIVY_KEYCLOAK_REALM=$configuredRealm no script do Dokpod; use -Realm dokpod para alterar explicitamente."
    }
}

if (-not $PSBoundParameters.ContainsKey('AdminUsername')) {
    $AdminUsername = Get-EnvironmentSecret @('DOKPOD_KEYCLOAK_ADMIN_USERNAME', 'ALTIVY_KEYCLOAK_ADMIN_USERNAME')
    if (-not $AdminUsername) { $AdminUsername = 'admin' }
}
if (-not $SkipUser -and -not $PSBoundParameters.ContainsKey('InitialUserEmail')) {
    $configuredInitialUserEmail = Get-EnvironmentSecret @('DOKPOD_ADMIN_EMAIL')
    if ($configuredInitialUserEmail) {
        $InitialUserEmail = $configuredInitialUserEmail
    }
}

$plannedResources = @(
    "realm $Realm e políticas de segurança",
    'clients dokpod-api, dokpod-provisioner, dokpod-bff e dokpod-authorization-spike',
    'client scope e mapper de audience dokpod-api',
    'roles Administrator, Operator, Auditor e Reader',
    'grupos /dokpod/administrators, /dokpod/operators, /dokpod/auditors e /dokpod/readers',
    'SMTP, quando DOKPOD_SMTP_HOST estiver definido',
    'provedores sociais com client ID e secret definidos'
)
if ($InitialUserEmail -and -not $SkipUser) {
    $plannedResources += "usuário dokpod-admin ($InitialUserEmail) no grupo administrators"
}

if ($DryRun) {
    Write-Output "Plano para $BaseUrl/admin/realms/$Realm`:"
    $plannedResources | ForEach-Object { Write-Output "- $_" }
    exit 0
}

if (-not $AdminPassword) {
    $adminPasswordValue = Get-EnvironmentSecret @('DOKPOD_KEYCLOAK_ADMIN_PASSWORD', 'ALTIVY_KEYCLOAK_ADMIN_PASSWORD')
    if (-not $adminPasswordValue) {
        throw 'Defina DOKPOD_KEYCLOAK_ADMIN_PASSWORD, ALTIVY_KEYCLOAK_ADMIN_PASSWORD ou informe -AdminPassword. Use --help para detalhes.'
    }
    $AdminPassword = ConvertTo-SecureString $adminPasswordValue -AsPlainText -Force
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

function Invoke-KeycloakApi {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PUT', 'DELETE')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        [object] $Body
    )

    $parameters = @{
        Method      = $Method
        Uri         = "$BaseUrl$Path"
        Headers     = @{ Authorization = "Bearer $script:accessToken" }
        ErrorAction = 'Stop'
    }
    if ($useNoProxy) { $parameters.NoProxy = $true }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 30 -Compress
    }
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        try {
            $response = Invoke-RestMethod @parameters -ResponseHeadersVariable responseHeaders
            $script:lastResponseHeaders = $responseHeaders
            return $response
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 401 -and $attempt -eq 0 -and $script:adminPasswordPlain) {
                Request-MasterAccessToken
                $parameters.Headers.Authorization = "Bearer $script:accessToken"
                continue
            }

            $keycloakError = $_.ErrorDetails.Message
            if ($keycloakError) {
                try {
                    $errorRepresentation = $keycloakError | ConvertFrom-Json
                    $keycloakError = @($errorRepresentation.error, $errorRepresentation.error_description) |
                    Where-Object { $_ } | Select-Object -First 1
                }
                catch {
                    $keycloakError = $null
                }
            }
            $detail = if ($keycloakError) { " Motivo informado pelo Keycloak: $keycloakError" } else { '' }
            throw "Falha na Admin API ($Method $Path): $($_.Exception.Message)$detail"
        }
    }
}

function Get-CreatedResourceId {
    $location = $script:lastResponseHeaders.Location | Select-Object -First 1
    if (-not $location) { throw 'A Admin API não retornou o header Location após a criação.' }
    ([Uri]$location).AbsolutePath.TrimEnd('/').Split('/')[-1]
}

function Assert-TemporaryPasswordPolicy {
    param([Parameter(Mandatory)][string] $Value)

    if ($Value.Length -lt 14) {
        throw 'DOKPOD_ADMIN_TEMPORARY_PASSWORD deve ter pelo menos 14 caracteres para atender à política do realm.'
    }
}

function Request-MasterAccessToken {
    $tokenParameters = @{
        Method      = 'POST'
        Uri         = "$BaseUrl/realms/master/protocol/openid-connect/token"
        ContentType = 'application/x-www-form-urlencoded'
        Body        = @{
            grant_type = 'password'
            client_id  = 'admin-cli'
            username   = $AdminUsername
            password   = $script:adminPasswordPlain
        }
        ErrorAction = 'Stop'
    }
    if ($useNoProxy) { $tokenParameters.NoProxy = $true }
    $tokenResponse = Invoke-RestMethod @tokenParameters
    if (-not $tokenResponse.access_token) { throw 'O Keycloak não retornou token administrativo.' }
    $script:accessToken = $tokenResponse.access_token
}

function Ensure-Client {
    param([Parameter(Mandatory)][hashtable] $Representation)

    $encodedClientId = [Uri]::EscapeDataString($Representation.clientId)
    $existing = @(Invoke-KeycloakApi GET "/admin/realms/$Realm/clients?clientId=$encodedClientId") |
    Where-Object clientId -eq $Representation.clientId | Select-Object -First 1
    if ($existing) {
        Invoke-KeycloakApi PUT "/admin/realms/$Realm/clients/$($existing.id)" $Representation | Out-Null
        Write-Output "Atualizado client $($Representation.clientId)."
        return $existing.id
    }

    Invoke-KeycloakApi POST "/admin/realms/$Realm/clients" $Representation | Out-Null
    $createdId = Get-CreatedResourceId
    Write-Output "Criado client $($Representation.clientId)."
    $createdId
}

function Ensure-ClientRole {
    param(
        [Parameter(Mandatory)][string] $ClientUuid,
        [Parameter(Mandatory)][string] $RoleName,
        [Parameter(Mandatory)][string] $Description
    )

    $encodedRole = [Uri]::EscapeDataString($RoleName)
    try {
        $existing = Invoke-KeycloakApi GET "/admin/realms/$Realm/clients/$ClientUuid/roles/$encodedRole"
        $representation = @{ id = $existing.id; name = $RoleName; description = $Description }
        Invoke-KeycloakApi PUT "/admin/realms/$Realm/clients/$ClientUuid/roles/$encodedRole" $representation | Out-Null
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
        Invoke-KeycloakApi POST "/admin/realms/$Realm/clients/$ClientUuid/roles" @{
            name = $RoleName; description = $Description
        } | Out-Null
    }
}

function Ensure-AudienceClientScope {
    param([Parameter(Mandatory)][string] $ClientUuid)

    $scopeRepresentation = @{
        name        = 'dokpod-api-audience'
        description = 'Inclui dokpod-api como audience dos access tokens dos clientes autorizados.'
        protocol    = 'openid-connect'
        attributes  = @{
            'display.on.consent.screen' = 'false'
            'include.in.token.scope'    = 'false'
        }
    }
    $scope = @(Invoke-KeycloakApi GET "/admin/realms/$Realm/client-scopes") |
    Where-Object name -eq $scopeRepresentation.name | Select-Object -First 1
    if ($scope) {
        Invoke-KeycloakApi PUT "/admin/realms/$Realm/client-scopes/$($scope.id)" $scopeRepresentation | Out-Null
        $scopeId = $scope.id
    }
    else {
        Invoke-KeycloakApi POST "/admin/realms/$Realm/client-scopes" $scopeRepresentation | Out-Null
        $scopeId = Get-CreatedResourceId
    }

    $mapperRepresentation = @{
        name            = 'dokpod-api-audience'
        protocol        = 'openid-connect'
        protocolMapper  = 'oidc-audience-mapper'
        consentRequired = $false
        config          = @{
            'included.client.audience'  = 'dokpod-api'
            'id.token.claim'            = 'false'
            'access.token.claim'        = 'true'
            'lightweight.claim'         = 'false'
            'introspection.token.claim' = 'true'
        }
    }
    $mapper = @(Invoke-KeycloakApi GET "/admin/realms/$Realm/client-scopes/$scopeId/protocol-mappers/models") |
    Where-Object name -eq $mapperRepresentation.name | Select-Object -First 1
    if (-not $mapper) {
        Invoke-KeycloakApi POST "/admin/realms/$Realm/client-scopes/$scopeId/protocol-mappers/models" $mapperRepresentation | Out-Null
    }

    $defaultScopes = @(Invoke-KeycloakApi GET "/admin/realms/$Realm/clients/$ClientUuid/default-client-scopes")
    if ($defaultScopes.id -notcontains $scopeId) {
        Invoke-KeycloakApi PUT "/admin/realms/$Realm/clients/$ClientUuid/default-client-scopes/$scopeId" $null | Out-Null
    }
    Write-Output "Reconciliado client scope dokpod-api-audience em $ClientUuid."
}

function Ensure-GroupPath {
    param([Parameter(Mandatory)][string[]] $Segments)

    $parentId = $null
    $currentPath = ''
    foreach ($segment in $Segments) {
        $currentPath += "/$segment"
        $encodedSearch = [Uri]::EscapeDataString($segment)
        $matchingGroups = if ($parentId) {
            @(Invoke-KeycloakApi GET "/admin/realms/$Realm/groups/$parentId/children?search=$encodedSearch&exact=true")
        }
        else {
            @(Invoke-KeycloakApi GET "/admin/realms/$Realm/groups?search=$encodedSearch&exact=true")
        }
        $group = $matchingGroups | Where-Object path -eq $currentPath | Select-Object -First 1
        if (-not $group) {
            $path = if ($parentId) { "/admin/realms/$Realm/groups/$parentId/children" } else { "/admin/realms/$Realm/groups" }
            Invoke-KeycloakApi POST $path @{ name = $segment } | Out-Null
            $group = [pscustomobject]@{ id = Get-CreatedResourceId; path = $currentPath }
        }
        if (-not $group) { throw "Grupo $currentPath não foi localizado após a criação." }
        $parentId = $group.id
    }
    $parentId
}

function Ensure-IdentityProvider {
    param([Parameter(Mandatory)][hashtable] $Provider)

    $clientId = Get-EnvironmentSecret @("DOKPOD_IDP_$($Provider.prefix)_CLIENT_ID")
    $clientSecret = Get-EnvironmentSecret @("DOKPOD_IDP_$($Provider.prefix)_CLIENT_SECRET")
    if (-not $clientId -and -not $clientSecret) { return }
    if (-not $clientId -or -not $clientSecret) {
        throw "Defina client ID e secret para o provedor $($Provider.alias)."
    }

    $providerIdOverride = Get-EnvironmentSecret @("DOKPOD_IDP_$($Provider.prefix)_PROVIDER_ID")
    $representation = @{
        alias                     = $Provider.alias
        providerId                = $(if ($providerIdOverride) { $providerIdOverride } else { $Provider.providerId })
        enabled                   = $true
        trustEmail                = $false
        storeToken                = $false
        addReadTokenRoleOnCreate  = $false
        authenticateByDefault     = $false
        linkOnly                  = $false
        firstBrokerLoginFlowAlias = 'first broker login'
        config                    = @{
            clientId        = $clientId
            clientSecret    = $clientSecret
            syncMode        = 'IMPORT'
            hideOnLoginPage = 'false'
        }
    }
    foreach ($key in $Provider.config.Keys) { $representation.config[$key] = $Provider.config[$key] }

    $encodedAlias = [Uri]::EscapeDataString($Provider.alias)
    try {
        Invoke-KeycloakApi GET "/admin/realms/$Realm/identity-provider/instances/$encodedAlias" | Out-Null
        Invoke-KeycloakApi PUT "/admin/realms/$Realm/identity-provider/instances/$encodedAlias" $representation | Out-Null
        Write-Output "Atualizado IdP $($Provider.alias)."
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
        Invoke-KeycloakApi POST "/admin/realms/$Realm/identity-provider/instances" $representation | Out-Null
        Write-Output "Criado IdP $($Provider.alias)."
    }
}

$script:accessToken = $null
$script:lastResponseHeaders = $null
$script:adminPasswordPlain = ConvertFrom-SecureValue $AdminPassword
try {
    Request-MasterAccessToken
}
finally {
    $script:adminPasswordPlain = $script:adminPasswordPlain
}

try {
    $realmRepresentation = Invoke-KeycloakApi GET "/admin/realms/$Realm"
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
    Invoke-KeycloakApi POST '/admin/realms' @{ realm = $Realm; enabled = $true; displayName = 'Dokpod' } | Out-Null
    $realmRepresentation = Invoke-KeycloakApi GET "/admin/realms/$Realm"
    Write-Output "Criado realm $Realm."
}

$smtpHost = Get-EnvironmentSecret @('DOKPOD_SMTP_HOST')
$realmSettings = @{
    displayName = 'Dokpod'; enabled = $true; sslRequired = 'external'; loginTheme = 'dokpod'
    registrationAllowed = $false; rememberMe = $false; verifyEmail = $true
    loginWithEmailAllowed = $true; duplicateEmailsAllowed = $false; resetPasswordAllowed = [bool]$smtpHost
    editUsernameAllowed = $false; bruteForceProtected = $true; permanentLockout = $false
    failureFactor = 5; waitIncrementSeconds = 60; quickLoginCheckMilliSeconds = 1000
    minimumQuickLoginWaitSeconds = 60; maxFailureWaitSeconds = 900; maxDeltaTimeSeconds = 43200
    passwordPolicy = 'length(14) and notUsername(undefined) and notEmail(undefined) and passwordHistory(5)'
    ssoSessionIdleTimeout = 1800; ssoSessionMaxLifespan = 36000
    clientSessionIdleTimeout = 900; clientSessionMaxLifespan = 28800
    accessTokenLifespan = 300; accessCodeLifespan = 60; accessCodeLifespanLogin = 300
    accessCodeLifespanUserAction = 300; actionTokenGeneratedByAdminLifespan = 43200
    revokeRefreshToken = $false; eventsEnabled = $true; eventsExpiration = 2592000
    eventsListeners = @('jboss-logging'); adminEventsEnabled = $true; adminEventsDetailsEnabled = $false
}
foreach ($key in $realmSettings.Keys) { $realmRepresentation | Add-Member -NotePropertyName $key -NotePropertyValue $realmSettings[$key] -Force }

if ($smtpHost) {
    $smtpUser = Get-EnvironmentSecret @('DOKPOD_SMTP_USERNAME')
    $smtpFrom = Get-EnvironmentSecret @('DOKPOD_SMTP_FROM')
    if (-not $smtpFrom) { throw 'Defina DOKPOD_SMTP_FROM quando DOKPOD_SMTP_HOST estiver configurado.' }
    $realmRepresentation | Add-Member -NotePropertyName smtpServer -NotePropertyValue @{
        host            = $smtpHost
        port            = $(if ($env:DOKPOD_SMTP_PORT) { $env:DOKPOD_SMTP_PORT } else { '587' })
        from            = $smtpFrom
        fromDisplayName = 'Dokpod'
        auth            = [string][bool]$smtpUser
        user            = $smtpUser
        password        = Get-EnvironmentSecret @('DOKPOD_SMTP_PASSWORD')
        starttls        = 'true'
        ssl             = 'false'
    } -Force
}
Invoke-KeycloakApi PUT "/admin/realms/$Realm" $realmRepresentation | Out-Null
Write-Output "Atualizado baseline do realm $Realm."

$apiClient = @{
    clientId = 'dokpod-api'; name = 'Dokpod API'; enabled = $true; publicClient = $false
    standardFlowEnabled = $false; implicitFlowEnabled = $false; directAccessGrantsEnabled = $false
    serviceAccountsEnabled = $true; authorizationServicesEnabled = $true; protocol = 'openid-connect'
}
$apiClientUuid = Ensure-Client $apiClient

$provisionerClient = @{
    clientId = 'dokpod-provisioner'; name = 'Dokpod Provisioner'; enabled = $true; publicClient = $false
    standardFlowEnabled = $false; implicitFlowEnabled = $false; directAccessGrantsEnabled = $false
    serviceAccountsEnabled = $true; protocol = 'openid-connect'
}
$provisionerSecret = Get-EnvironmentSecret @('DOKPOD_PROVISIONER_CLIENT_SECRET')
if ($provisionerSecret) { $provisionerClient.secret = $provisionerSecret }
$provisionerClientUuid = Ensure-Client $provisionerClient

$bffClient = @{
    clientId = 'dokpod-bff'; name = 'Dokpod BFF'; enabled = $true; publicClient = $false
    standardFlowEnabled = $true; implicitFlowEnabled = $false; directAccessGrantsEnabled = $false
    serviceAccountsEnabled = $false; protocol = 'openid-connect'; rootUrl = $BffBaseUrl; baseUrl = "$BffBaseUrl/"
    redirectUris = @("$BffBaseUrl/signin-oidc"); webOrigins = @()
    attributes = @{ 'pkce.code.challenge.method' = 'S256'; 'post.logout.redirect.uris' = "$BffBaseUrl/signout-callback-oidc" }
}
$bffSecret = Get-EnvironmentSecret @('DOKPOD_BFF_CLIENT_SECRET')
if ($bffSecret) { $bffClient.secret = $bffSecret }
$bffClientUuid = Ensure-Client $bffClient
Ensure-AudienceClientScope -ClientUuid $bffClientUuid

$authorizationSpikeClient = @{
    clientId = 'dokpod-authorization-spike'; name = 'Dokpod Authorization Spike'; enabled = $true
    publicClient = $true; standardFlowEnabled = $true; implicitFlowEnabled = $false
    directAccessGrantsEnabled = $false; serviceAccountsEnabled = $false; protocol = 'openid-connect'
    redirectUris = @('http://127.0.0.1:8765/callback/'); webOrigins = @()
    attributes = @{ 'pkce.code.challenge.method' = 'S256'; 'oauth2.device.authorization.grant.enabled' = 'false' }
}
$authorizationSpikeClientUuid = Ensure-Client $authorizationSpikeClient
Ensure-AudienceClientScope -ClientUuid $authorizationSpikeClientUuid

$roles = @(
    @{ name = 'Administrator'; description = 'Administra ambientes, auditoria e recursos Dokpod explicitamente autorizados.'; group = 'administrators' },
    @{ name = 'Operator'; description = 'Le ambientes e opera containers Dokpod explicitamente autorizados.'; group = 'operators' },
    @{ name = 'Auditor'; description = 'Consulta auditoria Dokpod explicitamente autorizada.'; group = 'auditors' },
    @{ name = 'Reader'; description = 'Le ambientes e containers Dokpod explicitamente autorizados.'; group = 'readers' }
)
foreach ($role in $roles) {
    Ensure-ClientRole -ClientUuid $apiClientUuid -RoleName $role.name -Description $role.description
    $groupId = Ensure-GroupPath @('dokpod', $role.group)
    $encodedRole = [Uri]::EscapeDataString($role.name)
    $roleRepresentation = Invoke-KeycloakApi GET "/admin/realms/$Realm/clients/$apiClientUuid/roles/$encodedRole"
    $currentMappings = @(Invoke-KeycloakApi GET "/admin/realms/$Realm/groups/$groupId/role-mappings/clients/$apiClientUuid")
    if ($currentMappings.name -notcontains $role.name) {
        Invoke-KeycloakApi POST "/admin/realms/$Realm/groups/$groupId/role-mappings/clients/$apiClientUuid" @($roleRepresentation) | Out-Null
    }
}
Write-Output 'Reconciliadas roles e associações de grupos.'

if ($InitialUserEmail -and -not $SkipUser) {
    $users = @(Invoke-KeycloakApi GET "/admin/realms/$Realm/users?username=dokpod-admin&exact=true")
    $initialUser = $users | Where-Object username -eq 'dokpod-admin' | Select-Object -First 1
    $requiredActions = @('UPDATE_PASSWORD', 'CONFIGURE_TOTP')
    if ($smtpHost) { $requiredActions += 'VERIFY_EMAIL' }
    $userRepresentation = @{
        username = 'dokpod-admin'; email = $InitialUserEmail; firstName = 'Dokpod'; lastName = 'Admin'
        enabled = $true; emailVerified = $false
        requiredActions = $requiredActions
    }
    if ($initialUser) {
        Invoke-KeycloakApi PUT "/admin/realms/$Realm/users/$($initialUser.id)" $userRepresentation | Out-Null
    }
    else {
        Invoke-KeycloakApi POST "/admin/realms/$Realm/users" $userRepresentation | Out-Null
        $initialUser = [pscustomobject]@{ id = Get-CreatedResourceId; username = 'dokpod-admin' }
    }
    if (-not $initialUser) { throw 'Usuário dokpod-admin não foi localizado após a criação.' }

    $temporaryPassword = Get-EnvironmentSecret @('DOKPOD_ADMIN_TEMPORARY_PASSWORD')
    if ($temporaryPassword) {
        Assert-TemporaryPasswordPolicy $temporaryPassword
        Invoke-KeycloakApi PUT "/admin/realms/$Realm/users/$($initialUser.id)/reset-password" @{
            type = 'password'; value = $temporaryPassword; temporary = $true
        } | Out-Null
    }
    $administratorGroupId = Ensure-GroupPath @('dokpod', 'administrators')
    Invoke-KeycloakApi PUT "/admin/realms/$Realm/users/$($initialUser.id)/groups/$administratorGroupId" $null | Out-Null
    Write-Output 'Reconciliado usuário dokpod-admin no grupo administrators.'
}

$providers = @(
    @{ prefix = 'FACEBOOK'; alias = 'facebook'; providerId = 'facebook'; config = @{ defaultScope = 'email' } },
    @{ prefix = 'GITHUB'; alias = 'github'; providerId = 'github'; config = @{ defaultScope = 'read:user user:email' } },
    @{ prefix = 'GOOGLE'; alias = 'google'; providerId = 'google'; config = @{ defaultScope = 'openid profile email' } },
    @{ prefix = 'MICROSOFT'; alias = 'microsoft'; providerId = 'microsoft'; config = @{ defaultScope = 'openid profile email' } },
    @{ prefix = 'LINKEDIN'; alias = 'linkedin'; providerId = 'linkedin-openid-connect'; config = @{ defaultScope = 'openid profile email' } },
    @{ prefix = 'X'; alias = 'x'; providerId = 'oauth2'; config = @{
            defaultScope = 'users.read tweet.read'; authorizationUrl = 'https://x.com/i/oauth2/authorize'
            tokenUrl = 'https://api.x.com/2/oauth2/token'; userInfoUrl = 'https://api.x.com/2/users/me?user.fields=confirmed_email'
            pkceEnabled = 'true'; pkceMethod = 'S256'; clientAuthMethod = 'client_secret_basic'
        }
    }
)
foreach ($provider in $providers) { Ensure-IdentityProvider $provider }

$script:accessToken = $null
$script:adminPasswordPlain = $null
Write-Output 'Configuração do Keycloak Dokpod concluída.'