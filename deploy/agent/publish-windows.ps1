#Requires -Version 7.0

<#
.SYNOPSIS
Publica o agente Dokpod self-contained para Windows.

.DESCRIPTION
Executa dotnet publish no SDK containerizado e grava somente binários no
diretório de artefatos. O pacote não inclui configuração, certificados, chaves
ou journal do agente.

.PARAMETER RuntimeIdentifier
RID Windows homologado. O padrão inicial é win-x64.

.PARAMETER OutputDirectory
Diretório absoluto ou relativo ao repositório para os binários publicados.

.PARAMETER Help
Exibe esta ajuda sem criar artefatos. Também aceita --help.

.EXAMPLE
./deploy/agent/publish-windows.ps1

Publica o agente Release self-contained para win-x64 em artifacts/agent/windows/win-x64.

.EXAMPLE
./deploy/agent/publish-windows.ps1 -RuntimeIdentifier win-x64 -OutputDirectory artifacts/agent/windows/win-x64

Publica para o RID e diretório informados.

.NOTES
Requer PowerShell 7 e Docker com acesso à imagem lzocateli/dotnet-sdk:10.0.400-noble.

.LINK
../../docs/distribuicao.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter()]
    [ValidatePattern('^win-(x64|arm64)$')]
    [string]$RuntimeIdentifier = 'win-x64',

    [Parameter()]
    [string]$OutputDirectory,

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

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 ou superior é obrigatório.'
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker não encontrado. Instale ou disponibilize o comando e consulte --help.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot "agent/windows/$RuntimeIdentifier"
}
elseif (-not [System.IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith("$artifactsRoot$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory deve estar dentro de $artifactsRoot."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

& docker run --rm `
    --volume "${repositoryRoot}:/workspace" `
    --volume "${OutputDirectory}:/output" `
    --workdir /workspace/backend `
    lzocateli/dotnet-sdk:10.0.400-noble `
    dotnet publish apps/Dokpod.Agent/Dokpod.Agent.csproj `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output /output `
    /p:PublishSingleFile=false `
    /p:UseAppHost=true

if ($LASTEXITCODE -ne 0) {
    throw "A publicação do agente para $RuntimeIdentifier falhou com código $LASTEXITCODE."
}

Write-Output "Agente publicado em $OutputDirectory"