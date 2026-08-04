#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,

    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    [string]$ArchivePath,
    [ValidatePattern('^$|^[0-9a-fA-F]{64}$')]
    [string]$ExpectedArchiveSha256,
    [ValidatePattern('^[^/]+/[^/]+$')]
    [string]$Repository = '2223M1/NinjaSlayer'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-GitHubReleaseAsset([string]$Tag, [string]$AssetName, [string]$RepositoryName) {
    $json = & gh release view $Tag --repo $RepositoryName --json tagName,isDraft,assets
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect GitHub Release $Tag."
    }
    $release = ($json | Out-String) | ConvertFrom-Json
    if ([string]$release.tagName -cne $Tag -or $release.isDraft -ne $false) {
        throw "GitHub Release $Tag is missing or still a draft."
    }
    $matches = @($release.assets | Where-Object { [string]$_.name -ceq $AssetName })
    if ($matches.Count -ne 1) {
        throw "GitHub Release $Tag must contain exactly one $AssetName asset."
    }
    if ([string]$matches[0].digest -notmatch '^sha256:([0-9a-fA-F]{64})$') {
        throw "GitHub Release asset $AssetName does not expose a SHA-256 digest."
    }
    return [pscustomobject]@{
        Name = $AssetName
        Sha256 = $Matches[1].ToLowerInvariant()
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')
. (Join-Path $repositoryRoot '.github\scripts\local-release-install.ps1')

$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $resolvedGameRoot -PathType Container)) {
    throw "STS2 game root does not exist: $resolvedGameRoot"
}
$gameExecutable = Join-Path $resolvedGameRoot 'SlayTheSpire2.exe'
if (-not (Test-Path -LiteralPath $gameExecutable -PathType Leaf)) {
    throw "STS2 executable is missing: $gameExecutable"
}
$runningGame = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($gameExecutable)) -ErrorAction SilentlyContinue)
if ($runningGame.Count -gt 0) {
    throw 'Close Slay the Spire 2 before replacing the local NinjaSlayer package.'
}

$compatibility = Read-NinjaSlayerCompatibility -Path (Join-Path $repositoryRoot 'eng\compatibility.json')
$gameAssembly = Join-Path $resolvedGameRoot 'data_sts2_windows_x86_64\sts2.dll'
$moduleMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath $gameAssembly
$selectedHost = Resolve-NinjaSlayerCompatibilityHost -Manifest $compatibility -ModuleMvid $moduleMvid
$expectedArchiveName = "NinjaSlayer-v$Version-$($selectedHost.Channel)-sts2-$($selectedHost.Profile.gameApiVersion).zip"
$temporaryRoot = $null

try {
    if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
        $asset = Get-GitHubReleaseAsset -Tag "v$Version" -AssetName $expectedArchiveName -RepositoryName $Repository
        $ExpectedArchiveSha256 = $asset.Sha256
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
            "NinjaSlayer-install-$([Guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
        $downloadArguments = @(
            'release', 'download', "v$Version",
            '--repo', $Repository,
            '--pattern', $expectedArchiveName,
            '--dir', $temporaryRoot
        )
        & gh @downloadArguments
        $downloadExitCode = $LASTEXITCODE
        if ($downloadExitCode -ne 0) {
            throw "gh release download failed with exit code $downloadExitCode."
        }
        $ArchivePath = Join-Path $temporaryRoot $expectedArchiveName
    }
    else {
        $ArchivePath = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
        if ((Split-Path -Leaf $ArchivePath) -cne $expectedArchiveName) {
            throw "Current host requires $expectedArchiveName, not $(Split-Path -Leaf $ArchivePath)."
        }
        if ([string]::IsNullOrWhiteSpace($ExpectedArchiveSha256)) {
            $asset = Get-GitHubReleaseAsset -Tag "v$Version" -AssetName $expectedArchiveName -RepositoryName $Repository
            $ExpectedArchiveSha256 = $asset.Sha256
        }
    }

    $actualArchiveSha = Get-NinjaSlayerSha256 -Path $ArchivePath
    if ($actualArchiveSha -cne $ExpectedArchiveSha256.ToLowerInvariant()) {
        throw "Archive SHA-256 mismatch: expected $ExpectedArchiveSha256, received $actualArchiveSha."
    }

    $result = Install-NinjaSlayerReleaseArchive `
        -ArchivePath $ArchivePath `
        -DestinationPath (Join-Path $resolvedGameRoot 'mods\NinjaSlayer') `
        -Channel $selectedHost.Channel `
        -Version $Version `
        -Compatibility $compatibility `
        -RepositoryRoot $repositoryRoot
    Write-Host "Installed NinjaSlayer $Version for $($selectedHost.Channel) STS2 $($selectedHost.Profile.gameApiVersion)." `
        -ForegroundColor Green
    Write-Output $result
}
finally {
    if ($null -ne $temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
