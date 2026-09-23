<#
.SYNOPSIS
Gerencia o Windows Service do agente Dokpod.

.DESCRIPTION
Instala, inicia, para, reinicia, consulta e remove o serviço do agente
Windows self-contained. A instalação grava somente configuração operacional
não secreta no ambiente específico do serviço. Certificados, identidade,
journal e binários nunca são removidos por padrão.

.PARAMETER Action
Install, Start, Stop, Restart, Status ou Remove.

.PARAMETER BinaryPath
Caminho absoluto do executável self-contained Dokpod.Agent.exe.

.PARAMETER ServiceName
Nome interno do serviço. Padrão: DokpodAgent.

.PARAMETER DisplayName
Nome exibido no SCM. Padrão: Dokpod Agent.

.PARAMETER Description
Descrição do serviço.

.PARAMETER FailureRestartDelaySeconds
Intervalo, em segundos, entre tentativas de recuperação. Padrão: 60.

.PARAMETER ControlPlaneEndpoint
Endpoint HTTPS/gRPC do plano de controle.

.PARAMETER EnvironmentId
UUID do ambiente associado ao agente.

.PARAMETER DockerSocket
Named pipe Docker. Padrão: docker_engine.

.PARAMETER ClientCertificatePath
Caminho absoluto do PFX do agente.

.PARAMETER ServerCaCertificatePath
Caminho absoluto da CA confiável do plano de controle.

.PARAMETER DataDirectory
Diretório persistente de identidade e journal.

.PARAMETER RemoveData
Remove o diretório de dados somente com Remove. Não remove certificados por
padrão e exige confirmação explícita via -Confirm.

.PARAMETER DryRun
Mostra as ações sem modificar o SCM ou o registro.

.EXAMPLE
./manage-windows-service.ps1 -Action Install -BinaryPath C:\Program Files\Dokpod\Agent\Dokpod.Agent.exe -ControlPlaneEndpoint https://controlplane.example:7443 -EnvironmentId 00000000-0000-0000-0000-000000000001 -ClientCertificatePath C:\ProgramData\Dokpod\Agent\identity\agent.pfx -ServerCaCertificatePath C:\ProgramData\Dokpod\Agent\identity\ca.crt -DataDirectory C:\ProgramData\Dokpod\Agent

.EXAMPLE
./manage-windows-service.ps1 -Action Restart

.EXAMPLE
./manage-windows-service.ps1 -Action Remove -Confirm

.NOTES
Requer PowerShell 7.4 e execução elevada para Install e Remove.
O serviço usa a conta LocalSystem apenas como padrão de laboratório; produção
deve usar conta dedicada com ACL mínima para o named pipe e os dados.

.LINK
../../docs/distribuicao.md
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High', PositionalBinding = $false)]
param(
    [ValidateSet('Install', 'Start', 'Stop', 'Restart', 'Status', 'Remove')]
    [string] $Action = 'Status',

    [string] $BinaryPath,
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string] $ServiceName = 'DokpodAgent',
    [string] $DisplayName = 'Dokpod Agent',
    [string] $Description = 'Agente Dokpod para Docker local.',
    [ValidateRange(5, 3600)]
    [int] $FailureRestartDelaySeconds = 60,
    [string] $ControlPlaneEndpoint,
    [Guid] $EnvironmentId,
    [string] $DockerSocket = 'docker_engine',
    [string] $ClientCertificatePath,
    [string] $ServerCaCertificatePath,
    [string] $DataDirectory,
    [switch] $RemoveData,
    [switch] $DryRun,

    [Parameter(ValueFromRemainingArguments)]
    [string[]] $RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-UsageError {
    param([Parameter(Mandatory)][string] $Message)

    [Console]::Error.WriteLine("$Message Consulte --help.")
    exit 2
}

$RemainingArguments = @($RemainingArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($RemainingArguments -contains '--help') {
    Get-Help $PSCommandPath -Full
    exit 0
}
if ($RemainingArguments.Count -gt 0) {
    Write-UsageError "Argumentos não reconhecidos: $($RemainingArguments -join ', ')."
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Administrator {
    if (-not $DryRun -and -not (Test-Administrator)) {
        Write-UsageError 'Install e Remove exigem PowerShell executado como Administrador.'
    }
}

function Resolve-AbsolutePath {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Name)

    if (-not [System.IO.Path]::IsPathFullyQualified($Path)) {
        Write-UsageError "$Name deve ser um caminho absoluto."
    }
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-ServiceObject {
    return Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
}

function Invoke-Sc {
    param([Parameter(Mandatory)][string[]] $Arguments)

    if ($DryRun) {
        Write-Host "sc.exe $($Arguments -join ' ')"
        return
    }

    & sc.exe @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe falhou com código $LASTEXITCODE."
    }
}

function Set-ServiceEnvironment {
    param([Parameter(Mandatory)][hashtable] $Values)

    $keyPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if ($DryRun) {
        Write-Host "Atualizaria o ambiente específico do serviço $ServiceName."
        return
    }

    New-Item -Path $keyPath -Force | Out-Null
    New-ItemProperty -Path $keyPath -Name Environment -PropertyType MultiString -Value @(
        $Values.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
    ) -Force | Out-Null
}

function Test-ShouldProcess {
    param([Parameter(Mandatory)][string] $Target, [Parameter(Mandatory)][string] $ActionName)

    return $DryRun -or $PSCmdlet.ShouldProcess($Target, $ActionName)
}

function Assert-InstallArguments {
    $required = @{
        BinaryPath = $BinaryPath
        ControlPlaneEndpoint = $ControlPlaneEndpoint
        EnvironmentId = $EnvironmentId
        ClientCertificatePath = $ClientCertificatePath
        ServerCaCertificatePath = $ServerCaCertificatePath
        DataDirectory = $DataDirectory
    }
    foreach ($item in $required.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace([string]$item.Value) -or ($item.Key -eq 'EnvironmentId' -and $item.Value -eq [Guid]::Empty)) {
            Write-UsageError "-$($item.Key) é obrigatório para Install."
        }
    }

    $script:BinaryPath = Resolve-AbsolutePath $BinaryPath 'BinaryPath'
    $script:ClientCertificatePath = Resolve-AbsolutePath $ClientCertificatePath 'ClientCertificatePath'
    $script:ServerCaCertificatePath = Resolve-AbsolutePath $ServerCaCertificatePath 'ServerCaCertificatePath'
    $script:DataDirectory = Resolve-AbsolutePath $DataDirectory 'DataDirectory'
    if (-not (Test-Path -LiteralPath $BinaryPath -PathType Leaf)) {
        Write-UsageError "Executável não encontrado: $BinaryPath."
    }
    if (-not (Test-Path -LiteralPath $ClientCertificatePath -PathType Leaf)) {
        Write-UsageError "Certificado do agente não encontrado: $ClientCertificatePath."
    }
    if (-not (Test-Path -LiteralPath $ServerCaCertificatePath -PathType Leaf)) {
        Write-UsageError "CA do plano de controle não encontrada: $ServerCaCertificatePath."
    }
}

function Install-AgentService {
    Assert-Administrator
    Assert-InstallArguments
    if (Get-ServiceObject) {
        Write-UsageError "O serviço $ServiceName já existe. Use Remove antes de instalar novamente."
    }
    if (-not $DryRun) {
        New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
    }

    $quotedBinaryPath = '"{0}"' -f $BinaryPath
    if (Test-ShouldProcess $ServiceName 'Instalar Windows Service') {
        if ($DryRun) {
            Write-Host "New-Service -Name $ServiceName -BinaryPathName $quotedBinaryPath"
            Write-Host "sc.exe config $ServiceName start= delayed-auto"
            Write-Host "sc.exe failure $ServiceName reset= 86400 actions= restart/$FailureRestartDelaySeconds/restart/$FailureRestartDelaySeconds/none/0"
        }
        else {
            New-Service -Name $ServiceName -BinaryPathName $quotedBinaryPath -DisplayName $DisplayName -Description $Description -StartupType Automatic | Out-Null
            Invoke-Sc @('config', $ServiceName, 'start=', 'delayed-auto')
            Invoke-Sc @('failure', $ServiceName, 'reset=', '86400', 'actions=', "restart/$FailureRestartDelaySeconds/restart/$FailureRestartDelaySeconds/none/0")
        }
    }

    Set-ServiceEnvironment @{
        DOKPOD_AGENT_CONTROL_PLANE_ENDPOINT = $ControlPlaneEndpoint
        DOKPOD_AGENT_ENVIRONMENT_ID = $EnvironmentId.ToString('D')
        DOKPOD_AGENT_DOCKER_SOCKET = $DockerSocket
        DOKPOD_AGENT_CLIENT_CERTIFICATE_PATH = $ClientCertificatePath
        DOKPOD_AGENT_SERVER_CA_CERTIFICATE_PATH = $ServerCaCertificatePath
        DOKPOD_AGENT_DATA_DIRECTORY = $DataDirectory
    }
}

switch ($Action) {
    'Install' { Install-AgentService }
    'Start' {
        if ($DryRun) { Write-Host "Start-Service -Name $ServiceName" }
        else { Start-Service -Name $ServiceName -ErrorAction Stop }
    }
    'Stop' {
        if ($DryRun) { Write-Host "Stop-Service -Name $ServiceName" }
        else { Stop-Service -Name $ServiceName -ErrorAction Stop }
    }
    'Restart' {
        if ($DryRun) { Write-Host "Restart-Service -Name $ServiceName" }
        else { Restart-Service -Name $ServiceName -ErrorAction Stop }
    }
    'Status' {
        $service = Get-ServiceObject
        if (-not $service) { Write-UsageError "O serviço $ServiceName não existe." }
        $service | Select-Object Name, DisplayName, Status, StartType
    }
    'Remove' {
        Assert-Administrator
        if ($RemoveData -and [string]::IsNullOrWhiteSpace($DataDirectory)) {
            Write-UsageError '-DataDirectory é obrigatório com -RemoveData.'
        }
        if ($RemoveData -and -not (Test-ShouldProcess $DataDirectory 'Remover dados do agente')) {
            exit 0
        }
        if (Get-ServiceObject) {
            if (Test-ShouldProcess $ServiceName 'Parar e remover Windows Service') {
                Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
                Invoke-Sc @('delete', $ServiceName)
            }
        }
        if ($RemoveData) {
            $resolvedDataDirectory = Resolve-AbsolutePath $DataDirectory 'DataDirectory'
            if (Test-ShouldProcess $resolvedDataDirectory 'Remover diretório de dados') {
                Remove-Item -LiteralPath $resolvedDataDirectory -Recurse -Force
            }
        }
    }
}