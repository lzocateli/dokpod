<#
.SYNOPSIS
Constrói e valida as imagens Linux publicáveis do Dokpod.

.DESCRIPTION
Constrói API, BFF, web e agente, gera relatório Trivy HIGH/CRITICAL, SBOM
CycloneDX e falha quando houver vulnerabilidade CRITICAL com correção disponível.
Todos os artifacts ficam em artifacts/security-local.

.PARAMETER Image
Imagem a validar: all, api, bff, web ou agent. Padrão: all.

.PARAMETER Tag
Tag local aplicada às imagens. Padrão: release-check.

.PARAMETER SkipBuild
Usa imagens locais existentes sem reconstruí-las.

.PARAMETER TrivyImage
Imagem fixada do Trivy. Padrão: aquasec/trivy:0.72.0.

.PARAMETER Timeout
Timeout de cada análise Trivy. Padrão: 20m.

.PARAMETER RemainingArguments
Aceita somente --help.

.EXAMPLE
./tools/scripts/validate-release-images.ps1 --help

.EXAMPLE
./tools/scripts/validate-release-images.ps1 -Image api -Tag e2e -SkipBuild

.EXAMPLE
./tools/scripts/validate-release-images.ps1 -Image all -Tag ci

.NOTES
Requer PowerShell 7+, Docker Engine ativo e acesso ao socket local. Não publica
imagens e não usa credenciais de registry.

.LINK
../../docs/release-readiness.md
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('all', 'api', 'bff', 'web', 'agent')]
    [string] $Image = 'all',

    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]*$')]
    [string] $Tag = 'release-check',

    [switch] $SkipBuild,

    [string] $TrivyImage = 'aquasec/trivy:0.72.0',

    [ValidatePattern('^[0-9]+[smh]$')]
    [string] $Timeout = '20m',

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
    [Console]::Error.WriteLine("Argumento desconhecido: $($RemainingArguments -join ' '). Use --help.")
    exit 2
}

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $previousErrorActionPreference = $ErrorActionPreference
    $previousNativePreference = $PSNativeCommandUseErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $PSNativeCommandUseErrorActionPreference = $false
        & docker @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "docker $($Arguments -join ' ') falhou com código $LASTEXITCODE."
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $previousNativePreference
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$artifactDirectory = Join-Path $repositoryRoot 'artifacts' 'security-local'
$cacheDirectory = Join-Path $artifactDirectory 'trivy-cache'
New-Item -ItemType Directory -Force $artifactDirectory, $cacheDirectory | Out-Null

$definitions = [ordered]@{
    api = @{
        Name = 'dokpod-api'
        Dockerfile = 'backend/apps/Dokpod.ControlPlane.Api/Dockerfile'
        PackageTypes = $null
    }
    bff = @{
        Name = 'dokpod-bff'
        Dockerfile = 'backend/apps/Dokpod.Bff/Dockerfile'
        PackageTypes = $null
    }
    web = @{
        Name = 'dokpod-web'
        Dockerfile = 'frontend/web/Dockerfile'
        PackageTypes = 'os'
    }
    agent = @{
        Name = 'dokpod-agent'
        Dockerfile = 'deploy/agent/Dockerfile'
        PackageTypes = $null
    }
}

$selected = if ($Image -eq 'all') { @($definitions.Keys) } else { @($Image) }
$artifactMount = "${artifactDirectory}:/artifacts"
$cacheMount = "${cacheDirectory}:/root/.cache/trivy"

Push-Location $repositoryRoot
try {
    foreach ($imageId in $selected) {
        $definition = $definitions[$imageId]
        $imageReference = "$($definition.Name):$Tag"
        if (-not $SkipBuild) {
            Invoke-Docker @(
                'build',
                '--file', [string]$definition.Dockerfile,
                '--tag', $imageReference,
                '.'
            )
        }

        $packageArguments = if ($definition.PackageTypes) {
            @('--pkg-types', [string]$definition.PackageTypes)
        }
        else {
            @()
        }
        $commonArguments = @(
            'run', '--rm',
            '--volume', '/var/run/docker.sock:/var/run/docker.sock',
            '--volume', $artifactMount,
            '--volume', $cacheMount,
            $TrivyImage,
            'image',
            '--timeout', $Timeout,
            '--skip-version-check'
        ) + $packageArguments

        Invoke-Docker ($commonArguments + @(
            '--scanners', 'vuln',
            '--severity', 'HIGH,CRITICAL',
            '--ignore-unfixed',
            '--format', 'json',
            '--output', "/artifacts/trivy-$imageId.json",
            $imageReference
        ))
        Invoke-Docker ($commonArguments + @(
            '--format', 'cyclonedx',
            '--output', "/artifacts/sbom-$imageId.cdx.json",
            $imageReference
        ))
        Invoke-Docker ($commonArguments + @(
            '--scanners', 'vuln',
            '--severity', 'CRITICAL',
            '--ignore-unfixed',
            '--exit-code', '1',
            '--quiet',
            $imageReference
        ))

        Write-Output "Imagem validada: $imageReference"
    }
}
finally {
    Pop-Location
}

Write-Output "Artifacts de segurança: $artifactDirectory"