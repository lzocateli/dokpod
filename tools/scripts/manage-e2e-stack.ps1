<#
.SYNOPSIS
Sobe, recria e encerra a stack E2E do Dokpod ou um serviço específico dela.

.DESCRIPTION
Orquestra o Docker Compose de deploy/e2e/docker-compose-dokpod.yaml usando um
arquivo de variáveis de ambiente externo ao repositório. O arquivo do Dokpod é
repassado ao Docker Compose por --env-file. As credenciais PostgreSQL são lidas
do arquivo externo da plataforma de identidade somente para derivar, em memória,
a connection string interna da API; nenhum valor sensível é exibido ou copiado.

As ações cobrem subir a stack, recriar containers e encerrá-los, com escopo na
stack inteira ou em uma lista de serviços.

.PARAMETER Action
Operação executada. Valores aceitos:
Up, sobe ou atualiza os serviços;
Recreate, recria os containers com --force-recreate;
Down, encerra e remove os containers;
Config, valida a interpolação do Compose sem alterar nada.
Padrão: Up.

.PARAMETER Service
Um ou mais serviços do Compose, por exemplo web, api, bff ou agent. Quando
omitido, a ação vale para a stack inteira.

.PARAMETER EnvFile
Caminho do arquivo de variáveis de ambiente repassado ao Compose. Padrão:
$env:APPDATA\Microsoft\UserSecrets\Dokpod\.env no Windows e
$HOME/.microsoft/usersecrets/Dokpod/.env nas demais plataformas.

.PARAMETER IdentityEnvFile
Caminho do arquivo externo da plataforma de identidade que contém
POSTGRES_ADMIN_USERNAME e POSTGRES_ADMIN_PASSWORD. Padrão:
$env:APPDATA\Microsoft\UserSecrets\Altivy.Identity\.env no Windows e
$HOME/.microsoft/usersecrets/Altivy.Identity/.env nas demais plataformas.

.PARAMETER ComposeFile
Caminho do arquivo Compose. Padrão: deploy/e2e/docker-compose-dokpod.yaml.

.PARAMETER ComposeProfile
Perfis do Compose habilitados, por exemplo agent. O perfil agent monta o socket
Docker do host e deve ser usado somente em laboratório controlado.

.PARAMETER Build
Reconstrói as imagens antes de subir. Válido em Up e Recreate.

.PARAMETER NoWait
Não aguarda os serviços ficarem saudáveis em Up e Recreate.

.PARAMETER NoDeps
Não inicia as dependências dos serviços informados em -Service.

.PARAMETER RemoveVolumes
Remove também os volumes nomeados da stack. Operação destrutiva, exclusiva da
ação Down e sem escopo por serviço.

.PARAMETER DryRun
Exibe o comando docker compose resultante e não executa nada.

.PARAMETER RemainingArguments
Argumentos literais remanescentes. Aceita somente --help.

.EXAMPLE
./tools/scripts/manage-e2e-stack.ps1 --help

.EXAMPLE
./tools/scripts/manage-e2e-stack.ps1 -Action Up -Build

.EXAMPLE
./tools/scripts/manage-e2e-stack.ps1 -Action Recreate -Service api -NoDeps

.EXAMPLE
./tools/scripts/manage-e2e-stack.ps1 -Action Down -Service agent -ComposeProfile agent

.INPUTS
Nenhum.

.OUTPUTS
Comandos executados e saída do Docker Compose, sem conteúdo do arquivo de
variáveis de ambiente.

.NOTES
Requer PowerShell 7.4 ou superior e Docker Compose v2. O arquivo de variáveis
de ambiente permanece fora do repositório e não deve ser versionado.

.LINK
../../deploy/e2e/README.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Up', 'Recreate', 'Down', 'Config')]
    [string] $Action = 'Up',

    [ValidatePattern('^[a-z0-9][a-z0-9_.-]*$')]
    [string[]] $Service,

    [string] $EnvFile,

    [string] $IdentityEnvFile,

    [string] $ComposeFile,

    [ValidatePattern('^[a-z0-9][a-z0-9_.-]*$')]
    [string[]] $ComposeProfile,

    [switch] $Build,

    [switch] $NoWait,

    [switch] $NoDeps,

    [switch] $RemoveVolumes,

    [switch] $DryRun,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RemainingArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}

if ($RemainingArguments.Count -gt 0) {
    [Console]::Error.WriteLine("Argumentos não reconhecidos: $($RemainingArguments -join ', '). Consulte --help.")
    exit 2
}

function Write-UsageError {
    param([Parameter(Mandatory)][string] $Message)

    [Console]::Error.WriteLine("$Message Consulte --help.")
    exit 2
}

function Get-EnvironmentFileValue {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name
    )

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match "^\s*$([regex]::Escape($Name))\s*=\s*(?<value>.*)\s*$") {
            return $Matches.value.Trim().Trim('"', "'")
        }
    }

    return $null
}

function ConvertTo-NpgsqlValue {
    param([Parameter(Mandatory)][string] $Value)

    return "'$($Value.Replace("'", "''"))'"
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

if (-not $ComposeFile) {
    $ComposeFile = Join-Path $repositoryRoot 'deploy' 'e2e' 'docker-compose-dokpod.yaml'
}

if (-not (Test-Path -LiteralPath $ComposeFile -PathType Leaf)) {
    Write-UsageError "Arquivo Compose não encontrado em '$ComposeFile'."
}

$ComposeFile = (Resolve-Path -LiteralPath $ComposeFile).Path

if (-not $EnvFile) {
    $EnvFile = if ($env:APPDATA) {
        Join-Path $env:APPDATA 'Microsoft' 'UserSecrets' 'Dokpod' '.env'
    }
    else {
        Join-Path $HOME '.microsoft' 'usersecrets' 'Dokpod' '.env'
    }
}

if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    Write-UsageError "Arquivo de variáveis de ambiente não encontrado em '$EnvFile'. Informe -EnvFile com o caminho do arquivo externo do Dokpod."
}

$EnvFile = (Resolve-Path -LiteralPath $EnvFile).Path

if ([string]::IsNullOrWhiteSpace($env:DOKPOD_E2E_CERTIFICATE_DIRECTORY)) {
    $certificateRoot = if ($env:APPDATA) {
        Join-Path $env:APPDATA 'Microsoft' 'UserSecrets' 'Dokpod'
    }
    else {
        Join-Path $HOME '.microsoft' 'usersecrets' 'Dokpod'
    }
    $env:DOKPOD_E2E_CERTIFICATE_DIRECTORY = Join-Path $certificateRoot 'certs'
}

if (-not (Test-Path -LiteralPath $env:DOKPOD_E2E_CERTIFICATE_DIRECTORY -PathType Container)) {
    Write-UsageError "Pasta externa de certificados não encontrada em '$env:DOKPOD_E2E_CERTIFICATE_DIRECTORY'. Gere a PKI de laboratório conforme deploy/e2e/README.md."
}

if (-not $IdentityEnvFile) {
    $IdentityEnvFile = if ($env:APPDATA) {
        Join-Path $env:APPDATA 'Microsoft' 'UserSecrets' 'Altivy.Identity' '.env'
    }
    else {
        Join-Path $HOME '.microsoft' 'usersecrets' 'Altivy.Identity' '.env'
    }
}

if (-not (Test-Path -LiteralPath $IdentityEnvFile -PathType Leaf)) {
    Write-UsageError "Arquivo de variáveis da plataforma de identidade não encontrado em '$IdentityEnvFile'. Informe -IdentityEnvFile."
}

$IdentityEnvFile = (Resolve-Path -LiteralPath $IdentityEnvFile).Path
$postgresUsername = Get-EnvironmentFileValue -Path $IdentityEnvFile -Name 'POSTGRES_ADMIN_USERNAME'
$postgresPassword = Get-EnvironmentFileValue -Path $IdentityEnvFile -Name 'POSTGRES_ADMIN_PASSWORD'

if ([string]::IsNullOrWhiteSpace($postgresUsername) -or [string]::IsNullOrWhiteSpace($postgresPassword)) {
    Write-UsageError "POSTGRES_ADMIN_USERNAME e POSTGRES_ADMIN_PASSWORD são obrigatórios no arquivo externo da plataforma de identidade."
}

$previousControlPlaneConnection = [Environment]::GetEnvironmentVariable('DOKPOD_CONTROLPLANE_CONNECTION', 'Process')
$env:DOKPOD_CONTROLPLANE_CONNECTION = "Host=postgres;Port=5432;Database=keycloak;Username=$(ConvertTo-NpgsqlValue $postgresUsername);Password=$(ConvertTo-NpgsqlValue $postgresPassword);Search Path=dokpod"

$services = @($Service | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$profiles = @($ComposeProfile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if ($profiles -contains 'agent' -and [string]::IsNullOrWhiteSpace($env:DOKPOD_E2E_AGENT_ENVIRONMENT_ID)) {
    Write-UsageError "O perfil agent exige DOKPOD_E2E_AGENT_ENVIRONMENT_ID com uma identidade previamente provisionada no PostgreSQL."
}

if ($RemoveVolumes -and $Action -ne 'Down') {
    Write-UsageError "-RemoveVolumes é válido somente com -Action Down."
}

if ($RemoveVolumes -and $services.Count -gt 0) {
    Write-UsageError "-RemoveVolumes remove volumes da stack inteira e não aceita -Service."
}

if ($Build -and $Action -notin @('Up', 'Recreate')) {
    Write-UsageError "-Build é válido somente com -Action Up ou Recreate."
}

if (($NoWait -or $NoDeps) -and $Action -notin @('Up', 'Recreate')) {
    Write-UsageError "-NoWait e -NoDeps são válidos somente com -Action Up ou Recreate."
}

if ($NoDeps -and $services.Count -eq 0) {
    Write-UsageError "-NoDeps exige ao menos um serviço em -Service."
}

$baseArguments = @('compose', '--env-file', $EnvFile, '--file', $ComposeFile)

foreach ($profileName in $profiles) {
    $baseArguments += @('--profile', $profileName)
}

function Invoke-DockerCompose {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $commandLine = "docker $($Arguments -join ' ')"

    if ($DryRun) {
        Write-Host $commandLine
        return
    }

    Write-Host "==> $commandLine"
    & docker @Arguments

    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("O comando falhou com código $LASTEXITCODE.")
        exit $LASTEXITCODE
    }
}

try {
    switch ($Action) {
        'Config' {
            Invoke-DockerCompose -Arguments ($baseArguments + @('config', '--quiet') + $services)
        }
        { $_ -in @('Up', 'Recreate') } {
            $arguments = $baseArguments + @('up', '--detach')

            if ($Action -eq 'Recreate') {
                $arguments += '--force-recreate'
            }

            if ($Build) {
                $arguments += '--build'
            }

            if ($NoDeps) {
                $arguments += '--no-deps'
            }

            if (-not $NoWait) {
                $arguments += '--wait'
            }

            Invoke-DockerCompose -Arguments ($arguments + $services)
        }
        'Down' {
            if ($services.Count -gt 0) {
                Invoke-DockerCompose -Arguments ($baseArguments + @('rm', '--stop', '--force') + $services)
                break
            }

            $arguments = $baseArguments + @('down', '--remove-orphans')

            if ($RemoveVolumes) {
                $arguments += '--volumes'
            }

            Invoke-DockerCompose -Arguments $arguments
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        'DOKPOD_CONTROLPLANE_CONNECTION',
        $previousControlPlaneConnection,
        'Process')
}
