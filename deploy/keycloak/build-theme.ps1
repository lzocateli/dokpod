<#
.SYNOPSIS
Gera o tema de login do Keycloak do Dokpod a partir da fonte versionada.

.DESCRIPTION
Empacota o tema de Keycloak versionado em deploy/keycloak em um diretório de
saída limpo. O diretório canônico da marca é opcional e serve apenas para
verificar os assets versionados. Não são necessários rede, credenciais,
usuários, exports de realm ou secrets.

.PARAMETER BrandRoot
Diretório canônico opcional da marca Dokpod usado para comparar checksums locais.

.PARAMETER OutputPath
Diretório que receberá o artefato de tema gerado. O conteúdo existente é removido.

.EXAMPLE
./deploy/keycloak/build-theme.ps1 --help

.EXAMPLE
./deploy/keycloak/build-theme.ps1 -OutputPath ./deploy/keycloak/.build/keycloak-theme

.NOTES
Requer PowerShell 7. Quando informado, o diretório canônico da marca deve conter
os assets aprovados do Dokpod.

.LINK
https://www.keycloak.org/ui-customization/themes
#>
[CmdletBinding()]
param(
    [Parameter()]
    [Alias('h')]
    [switch] $Help,

    [Parameter()]
    [string] $BrandRoot = '',

    [Parameter()]
    [string] $OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Help -or $BrandRoot -in @('--help', '-h')) {
    Get-Help $PSCommandPath -Full
    exit 0
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot 'deploy/keycloak/.build/keycloak-theme'
}

if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $outputRoot = [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    $outputRoot = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
}
$themeSource = Join-Path $repositoryRoot 'deploy/keycloak/themes/dokpod/login'
$themeOutput = Join-Path $outputRoot 'dokpod/login'

$themeFiles = @(
    'theme.properties',
    'resources/css/login.css',
    'resources/img/logo.svg',
    'resources/img/logo-dark.svg',
    'resources/js/theme-toggle.js'
)

foreach ($themeFile in $themeFiles) {
    $sourceFile = Join-Path $themeSource $themeFile
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
        throw "Arquivo versionado do tema não encontrado: $sourceFile"
    }
}

if (-not [string]::IsNullOrWhiteSpace($BrandRoot)) {
    $BrandRoot = (Resolve-Path $BrandRoot).Path
    $canonicalBanner = Join-Path $BrandRoot 'dokpod-banner.svg'
    if (-not (Test-Path -LiteralPath $canonicalBanner -PathType Leaf)) {
        throw "Asset canônico da marca não encontrado: $canonicalBanner"
    }
}

foreach ($themeFile in $themeFiles) {
    $sourceFile = Join-Path $themeSource $themeFile
    if ((Get-Item -LiteralPath $sourceFile).LinkType) {
        throw "Links simbólicos não são permitidos no tema versionado: $sourceFile"
    }
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $themeOutput -Force | Out-Null
Get-ChildItem -LiteralPath $themeSource -Force | Copy-Item -Destination $themeOutput -Recurse -Force

$checksums = @()
foreach ($file in Get-ChildItem -LiteralPath $themeOutput -File -Recurse | Sort-Object FullName) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $relativePath = $file.FullName.Substring($outputRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    $checksums += "$hash  $relativePath"
}
$checksums | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS') -Encoding ascii

Write-Output "Artefato do tema criado em $outputRoot"
Write-Output "Arquivos: $($checksums.Count)"