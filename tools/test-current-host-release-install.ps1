#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')
. (Join-Path $repositoryRoot '.github\scripts\local-release-install.ps1')

function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "Expected failure matching '$Pattern', received: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected failure matching '$Pattern', but the operation succeeded."
}

function New-InstallFixtureArchive {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$AssemblyChannel,
        [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$ArchiveChannel,
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][string]$Version,
        [switch]$ExtraFile
    )

    $assemblyProfile = Get-NinjaSlayerCompatibilityChannel `
        -Manifest $Compatibility `
        -Channel $AssemblyChannel
    $archiveProfile = Get-NinjaSlayerCompatibilityChannel `
        -Manifest $Compatibility `
        -Channel $ArchiveChannel
    $fixtureRoot = Join-Path $Root "$AssemblyChannel-to-$ArchiveChannel-$([Guid]::NewGuid().ToString('N'))"
    $projectRoot = Join-Path $fixtureRoot 'project'
    $packageRoot = Join-Path $fixtureRoot 'package'
    [IO.Directory]::CreateDirectory($projectRoot) | Out-Null
    [IO.Directory]::CreateDirectory($packageRoot) | Out-Null
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>NinjaSlayer</AssemblyName>
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyMetadata Include="NinjaSlayerHostChannel" Value="$AssemblyChannel" />
    <AssemblyMetadata Include="NinjaSlayerGameApiVersion" Value="$($assemblyProfile.gameApiVersion)" />
    <AssemblyMetadata Include="NinjaSlayerRitsuLibPackageId" Value="$($assemblyProfile.ritsuLibPackageId)" />
    <AssemblyMetadata Include="NinjaSlayerRitsuLibVersion" Value="$($Compatibility.ritsuLibVersion)" />
  </ItemGroup>
</Project>
"@
    [IO.File]::WriteAllText(
        (Join-Path $projectRoot 'Fixture.csproj'),
        $project,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $projectRoot 'Fixture.cs'),
        'public static class Fixture { }',
        [Text.UTF8Encoding]::new($false))
    & dotnet build (Join-Path $projectRoot 'Fixture.csproj') -c Release -v:q | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to build the local-install assembly fixture.'
    }
    Copy-Item `
        -LiteralPath (Join-Path $projectRoot 'bin\Release\net9.0\NinjaSlayer.dll') `
        -Destination (Join-Path $packageRoot 'NinjaSlayer.dll')
    [IO.File]::WriteAllText(
        (Join-Path $packageRoot 'NinjaSlayer.pck'),
        'fixture pck',
        [Text.UTF8Encoding]::new($false))
    $manifest = [ordered]@{
        id = 'NinjaSlayer'
        name = 'NinjaSlayer'
        author = 'fixture'
        description = 'fixture'
        version = $Version
        min_game_version = [string]$archiveProfile.gameApiVersion
        has_pck = $true
        has_dll = $true
        dependencies = @([ordered]@{
            id = 'STS2-RitsuLib'
            min_version = [string]$Compatibility.ritsuLibVersion
        })
        affects_gameplay = $true
    }
    [IO.File]::WriteAllText(
        (Join-Path $packageRoot 'NinjaSlayer.json'),
        ($manifest | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    $checksumLines = foreach ($name in @('NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck')) {
        "$(Get-NinjaSlayerSha256 -Path (Join-Path $packageRoot $name)) *$name"
    }
    [IO.File]::WriteAllLines(
        (Join-Path $packageRoot 'SHA256SUMS'),
        $checksumLines,
        [Text.UTF8Encoding]::new($false))
    if ($ExtraFile) {
        [IO.File]::WriteAllText((Join-Path $packageRoot 'unexpected.txt'), 'unexpected')
    }

    $archiveName = "NinjaSlayer-v$Version-$ArchiveChannel-sts2-$($archiveProfile.gameApiVersion).zip"
    $archivePath = Join-Path $fixtureRoot $archiveName
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $archivePath)
    return $archivePath
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    "ninjaslayer-current-host-install-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $compatibility = Read-NinjaSlayerCompatibility `
        -Path (Join-Path $repositoryRoot 'eng\compatibility.json')
    $version = '9.8.7'
    $archive = New-InstallFixtureArchive `
        -Root $temporaryRoot `
        -AssemblyChannel stable `
        -ArchiveChannel stable `
        -Compatibility $compatibility `
        -Version $version
    $modsRoot = Join-Path $temporaryRoot 'game\mods'
    $destination = Join-Path $modsRoot 'NinjaSlayer'
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    [IO.File]::WriteAllText((Join-Path $destination 'legacy.txt'), 'legacy')

    $first = Install-NinjaSlayerReleaseArchive `
        -ArchivePath $archive `
        -DestinationPath $destination `
        -Channel stable `
        -Version $version `
        -Compatibility $compatibility `
        -RepositoryRoot $repositoryRoot
    if ($first.Channel -cne 'stable' -or
        (Test-Path -LiteralPath (Join-Path $destination 'legacy.txt')) -or
        @(Get-ChildItem -LiteralPath $destination -File).Count -ne 4) {
        throw 'A valid stable archive was not installed as an exact replacement.'
    }

    $null = Install-NinjaSlayerReleaseArchive `
        -ArchivePath $archive `
        -DestinationPath $destination `
        -Channel stable `
        -Version $version `
        -Compatibility $compatibility `
        -RepositoryRoot $repositoryRoot

    $wrongChannelArchive = New-InstallFixtureArchive `
        -Root $temporaryRoot `
        -AssemblyChannel stable `
        -ArchiveChannel preview `
        -Compatibility $compatibility `
        -Version $version
    $beforeWrongChannel = Get-NinjaSlayerSha256 -Path (Join-Path $destination 'NinjaSlayer.dll')
    Assert-Throws {
        Install-NinjaSlayerReleaseArchive `
            -ArchivePath $wrongChannelArchive `
            -DestinationPath $destination `
            -Channel preview `
            -Version $version `
            -Compatibility $compatibility `
            -RepositoryRoot $repositoryRoot
    } "metadata 'NinjaSlayerHostChannel'"
    if ((Get-NinjaSlayerSha256 -Path (Join-Path $destination 'NinjaSlayer.dll')) -cne $beforeWrongChannel) {
        throw 'A rejected cross-channel archive changed the existing install.'
    }

    $extraArchive = New-InstallFixtureArchive `
        -Root $temporaryRoot `
        -AssemblyChannel stable `
        -ArchiveChannel stable `
        -Compatibility $compatibility `
        -Version $version `
        -ExtraFile
    Assert-Throws {
        Install-NinjaSlayerReleaseArchive `
            -ArchivePath $extraArchive `
            -DestinationPath $destination `
            -Channel stable `
            -Version $version `
            -Compatibility $compatibility `
            -RepositoryRoot $repositoryRoot
    } 'exactly the four'

    $markerPath = Join-Path $destination 'rollback-marker.txt'
    [IO.File]::WriteAllText($markerPath, 'restore me')
    Assert-Throws {
        Install-NinjaSlayerReleaseArchive `
            -ArchivePath $archive `
            -DestinationPath $destination `
            -Channel stable `
            -Version $version `
            -Compatibility $compatibility `
            -RepositoryRoot $repositoryRoot `
            -PromoteDirectory { throw 'injected promotion failure' }
    } 'injected promotion failure'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw 'Atomic install rollback did not restore the previous directory.'
    }
    if (@(Get-ChildItem -LiteralPath $modsRoot -Directory -Force | Where-Object {
        $_.Name -match '^\.NinjaSlayer\.(install|backup|failed)-'
    }).Count -ne 0) {
        throw 'Atomic install left a work directory after rollback.'
    }

    Write-Output 'Current-host release install tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
