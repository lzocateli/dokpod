#Requires -Version 7.0

<#
.SYNOPSIS
Audita ou aplica a governança exclusiva do repositório Dokpod no GitHub.

.DESCRIPTION
Configura merges, permissões do GitHub Actions, recursos de segurança e o
ruleset da branch padrão. Também falha se existir colaborador diferente do
proprietário ou deploy key com permissão de escrita.

O modo padrão é Audit e não altera o repositório. Use -Mode Apply de forma
explícita para reconciliar as configurações remotas.

.PARAMETER Repository
Repositório GitHub no formato proprietario/nome. O padrão é lzocateli/dokpod.

.PARAMETER Owner
Única conta autorizada a administrar e promover código. O padrão é lzocateli.

.PARAMETER Mode
Audit somente valida o estado. Apply reconcilia o estado e valida o resultado.

.PARAMETER Help
Exibe esta ajuda sem acessar ou alterar o GitHub. Também aceita --help.

.EXAMPLE
./tools/scripts/configure-github-governance.ps1

Audita a governança de lzocateli/dokpod sem realizar alterações.

.EXAMPLE
./tools/scripts/configure-github-governance.ps1 -Mode Apply

Reaplica a política e valida o estado remoto resultante.

.NOTES
Requer PowerShell 7, GitHub CLI autenticado como administrador do repositório
e os scopes necessários para administrar repositório, Actions e segurança.

.LINK
../../.github/GOVERNANCE.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter()]
    [ValidatePattern('^[^/\s]+/[^/\s]+$')]
    [string]$Repository = 'lzocateli/dokpod',

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$Owner = 'lzocateli',

    [Parameter()]
    [ValidateSet('Audit', 'Apply')]
    [string]$Mode = 'Audit',

    [Parameter()]
    [Alias('?')]
    [switch]$Help,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)

$ErrorActionPreference = 'Stop'

$literalHelp = $RemainingArguments -contains '--help'
$unknownArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne '--help' })
if ($unknownArguments.Count -gt 0) {
    [Console]::Error.WriteLine("Argumento desconhecido: $($unknownArguments -join ', '). Consulte --help.")
    exit 2
}

if ($Help -or $literalHelp) {
    Get-Help $PSCommandPath -Full
    return
}

function Invoke-GitHubApi {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter()]
        [AllowNull()]
        [string]$InputJson
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $hasNativePreference = Test-Path Variable:PSNativeCommandUseErrorActionPreference
    if ($hasNativePreference) {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
    }

    try {
        $ErrorActionPreference = 'Continue'
        if ($hasNativePreference) {
            $PSNativeCommandUseErrorActionPreference = $false
        }

        if ($null -eq $InputJson) {
            $output = ((& gh @Arguments 2>&1) | Out-String).Trim()
        }
        else {
            $output = (($InputJson | & gh @Arguments 2>&1) | Out-String).Trim()
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        if ($hasNativePreference) {
            $PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }

    if ($exitCode -ne 0) {
        throw "GitHub CLI falhou (gh $($Arguments -join ' ')): $output"
    }

    if (-not [string]::IsNullOrWhiteSpace($output)) {
        return $output
    }
}

function ConvertTo-ApiJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [hashtable]$Value
    )

    return $Value | ConvertTo-Json -Depth 12 -Compress
}

function Assert-Equal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter()]
        $Actual,

        [Parameter()]
        $Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Name inválido. Esperado: '$Expected'. Atual: '$Actual'."
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) não encontrado. Instale ou disponibilize o comando e consulte --help.'
}

$rulesetName = 'Protecao exclusiva da main'
$repositorySettings = @{
    allow_merge_commit          = $false
    allow_rebase_merge          = $false
    allow_squash_merge          = $true
    allow_auto_merge            = $false
    delete_branch_on_merge      = $true
    web_commit_signoff_required = $true
    squash_merge_commit_title   = 'PR_TITLE'
    squash_merge_commit_message = 'PR_BODY'
}
$actionsPermissions = @{
    enabled              = $true
    allowed_actions      = 'selected'
    sha_pinning_required = $true
}
$selectedActions = @{
    github_owned_allowed = $true
    verified_allowed     = $false
    patterns_allowed     = @()
}
$workflowPermissions = @{
    default_workflow_permissions    = 'read'
    can_approve_pull_request_reviews = $false
}
$ruleset = @{
    name          = $rulesetName
    target        = 'branch'
    enforcement   = 'active'
    bypass_actors = @(
        @{
            actor_id    = 5
            actor_type  = 'RepositoryRole'
            bypass_mode = 'pull_request'
        }
    )
    conditions    = @{
        ref_name = @{
            include = @('~DEFAULT_BRANCH')
            exclude = @()
        }
    }
    rules         = @(
        @{ type = 'deletion' }
        @{ type = 'non_fast_forward' }
        @{ type = 'required_linear_history' }
        @{ type = 'required_signatures' }
        @{ type = 'update' }
        @{
            type       = 'pull_request'
            parameters = @{
                allowed_merge_methods                   = @('squash')
                automatic_copilot_code_review_enabled   = $false
                dismiss_stale_reviews_on_push           = $true
                require_code_owner_review               = $true
                require_last_push_approval              = $true
                required_approving_review_count         = 1
                required_review_thread_resolution       = $true
            }
        }
    )
}

$viewer = Invoke-GitHubApi -Arguments @('repo', 'view', $Repository, '--json', 'viewerPermission', '--jq', '.viewerPermission')
Assert-Equal -Name 'Permissão da conta autenticada' -Actual $viewer -Expected 'ADMIN'

if ($Mode -eq 'Apply') {
    Invoke-GitHubApi -Arguments @('api', '--method', 'PATCH', "repos/$Repository", '--input', '-', '--silent') -InputJson (ConvertTo-ApiJson $repositorySettings)
    Invoke-GitHubApi -Arguments @('api', '--method', 'PUT', "repos/$Repository/actions/permissions", '--input', '-', '--silent') -InputJson (ConvertTo-ApiJson $actionsPermissions)
    Invoke-GitHubApi -Arguments @('api', '--method', 'PUT', "repos/$Repository/actions/permissions/selected-actions", '--input', '-', '--silent') -InputJson (ConvertTo-ApiJson $selectedActions)
    Invoke-GitHubApi -Arguments @('api', '--method', 'PUT', "repos/$Repository/actions/permissions/workflow", '--input', '-', '--silent') -InputJson (ConvertTo-ApiJson $workflowPermissions)
    Invoke-GitHubApi -Arguments @('api', '--method', 'PUT', "repos/$Repository/vulnerability-alerts", '--silent')
    Invoke-GitHubApi -Arguments @('api', '--method', 'PUT', "repos/$Repository/automated-security-fixes", '--silent')
    Invoke-GitHubApi -Arguments @('api', '--method', 'PUT', "repos/$Repository/private-vulnerability-reporting", '--silent')

    $existingRulesets = Invoke-GitHubApi -Arguments @('api', "repos/$Repository/rulesets") | ConvertFrom-Json
    $existingRuleset = $existingRulesets | Where-Object { $_.target -eq 'branch' -and $_.name -match 'main$' } | Select-Object -First 1
    if ($existingRuleset) {
        Invoke-GitHubApi -Arguments @('api', '--method', 'PUT', "repos/$Repository/rulesets/$($existingRuleset.id)", '--input', '-', '--silent') -InputJson (ConvertTo-ApiJson $ruleset)
    }
    else {
        Invoke-GitHubApi -Arguments @('api', '--method', 'POST', "repos/$Repository/rulesets", '--input', '-', '--silent') -InputJson (ConvertTo-ApiJson $ruleset)
    }
}

$repositoryState = Invoke-GitHubApi -Arguments @('api', "repos/$Repository") | ConvertFrom-Json
foreach ($setting in $repositorySettings.Keys) {
    Assert-Equal -Name "Configuração $setting" -Actual $repositoryState.$setting -Expected $repositorySettings[$setting]
}

$collaborators = @(Invoke-GitHubApi -Arguments @('api', "repos/$Repository/collaborators?affiliation=all") | ConvertFrom-Json)
if ($collaborators.Count -ne 1 -or $collaborators[0].login -ne $Owner -or $collaborators[0].role_name -ne 'admin') {
    throw "A lista de colaboradores deve conter somente '$Owner' como admin."
}

$deployKeys = @(Invoke-GitHubApi -Arguments @('api', "repos/$Repository/keys") | ConvertFrom-Json)
$writeDeployKeys = @($deployKeys | Where-Object { -not $_.read_only })
if ($writeDeployKeys.Count -gt 0) {
    throw 'Existe ao menos uma deploy key com permissão de escrita.'
}

$actionsState = Invoke-GitHubApi -Arguments @('api', "repos/$Repository/actions/permissions") | ConvertFrom-Json
foreach ($setting in $actionsPermissions.Keys) {
    Assert-Equal -Name "Permissão do Actions $setting" -Actual $actionsState.$setting -Expected $actionsPermissions[$setting]
}

$selectedActionsState = Invoke-GitHubApi -Arguments @('api', "repos/$Repository/actions/permissions/selected-actions") | ConvertFrom-Json
Assert-Equal -Name 'Actions oficiais do GitHub' -Actual $selectedActionsState.github_owned_allowed -Expected $true
Assert-Equal -Name 'Actions verificadas' -Actual $selectedActionsState.verified_allowed -Expected $false
if (@($selectedActionsState.patterns_allowed).Count -ne 0) {
    throw 'A lista de padrões de Actions permitidas deve estar vazia.'
}

$workflowState = Invoke-GitHubApi -Arguments @('api', "repos/$Repository/actions/permissions/workflow") | ConvertFrom-Json
foreach ($setting in $workflowPermissions.Keys) {
    Assert-Equal -Name "Permissão de workflow $setting" -Actual $workflowState.$setting -Expected $workflowPermissions[$setting]
}

$rulesets = @(Invoke-GitHubApi -Arguments @('api', "repos/$Repository/rulesets") | ConvertFrom-Json)
$rulesetStateSummary = $rulesets | Where-Object { $_.name -eq $rulesetName } | Select-Object -First 1
if (-not $rulesetStateSummary) {
    throw "Ruleset '$rulesetName' não encontrado. Execute com -Mode Apply."
}

$rulesetState = Invoke-GitHubApi -Arguments @('api', "repos/$Repository/rulesets/$($rulesetStateSummary.id)") | ConvertFrom-Json
Assert-Equal -Name 'Enforcement do ruleset' -Actual $rulesetState.enforcement -Expected 'active'
Assert-Equal -Name 'Alvo do ruleset' -Actual $rulesetState.target -Expected 'branch'
if (@($rulesetState.conditions.ref_name.include).Count -ne 1 -or $rulesetState.conditions.ref_name.include[0] -ne '~DEFAULT_BRANCH') {
    throw 'O ruleset deve selecionar exclusivamente a branch padrão.'
}
if (@($rulesetState.bypass_actors).Count -ne 1 -or $rulesetState.bypass_actors[0].actor_type -ne 'RepositoryRole' -or $rulesetState.bypass_actors[0].actor_id -ne 5 -or $rulesetState.bypass_actors[0].bypass_mode -ne 'pull_request') {
    throw 'O bypass por pull request deve pertencer exclusivamente ao papel Administrator.'
}

$requiredRuleTypes = @('deletion', 'non_fast_forward', 'required_linear_history', 'required_signatures', 'update', 'pull_request')
$actualRuleTypes = @($rulesetState.rules.type)
foreach ($ruleType in $requiredRuleTypes) {
    if ($ruleType -notin $actualRuleTypes) {
        throw "Regra obrigatória ausente: $ruleType."
    }
}

$pullRequestRule = $rulesetState.rules | Where-Object type -eq 'pull_request' | Select-Object -First 1
Assert-Equal -Name 'Revisão de code owner' -Actual $pullRequestRule.parameters.require_code_owner_review -Expected $true
Assert-Equal -Name 'Aprovação da última alteração' -Actual $pullRequestRule.parameters.require_last_push_approval -Expected $true
Assert-Equal -Name 'Invalidação de revisão obsoleta' -Actual $pullRequestRule.parameters.dismiss_stale_reviews_on_push -Expected $true
Assert-Equal -Name 'Resolução de conversas' -Actual $pullRequestRule.parameters.required_review_thread_resolution -Expected $true
Assert-Equal -Name 'Quantidade de aprovações' -Actual $pullRequestRule.parameters.required_approving_review_count -Expected 1
if (@($pullRequestRule.parameters.allowed_merge_methods).Count -ne 1 -or $pullRequestRule.parameters.allowed_merge_methods[0] -ne 'squash') {
    throw 'O ruleset deve permitir somente squash merge.'
}

Invoke-GitHubApi -Arguments @('api', "repos/$Repository/vulnerability-alerts", '--silent')
Invoke-GitHubApi -Arguments @('api', "repos/$Repository/automated-security-fixes", '--silent')
$privateReporting = Invoke-GitHubApi -Arguments @('api', "repos/$Repository/private-vulnerability-reporting") | ConvertFrom-Json
Assert-Equal -Name 'Reporte privado de vulnerabilidade' -Actual $privateReporting.enabled -Expected $true

Write-Output "Governança validada: somente $Owner pode administrar e promover código em $Repository."