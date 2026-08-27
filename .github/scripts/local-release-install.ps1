#Requires -Version 7.0
#Requires -PSEdition Core

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'compatibility.ps1')
. (Join-Path $PSScriptRoot 'release-artifact.ps1')

function Invoke-NinjaSlayerInstallCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $output = @(& $Command @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = ($output | Out-String).Trim()
        throw "$Command failed with exit code $exitCode. $details"
    }
    $output | Out-Host
}

function Assert-NinjaSlayerInstallDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)]$ArchiveContract
    )

    $resolved = (Resolve-Path -LiteralPath $Directory -ErrorAction Stop).Path
    $expectedFiles = @($ArchiveContract.files)
    $actualFiles = @(Get-ChildItem -LiteralPath $resolved -File)
    $actualDirectories = @(Get-ChildItem -LiteralPath $resolved -Directory)
    $difference = Compare-Object `
        -ReferenceObject @($expectedFiles.path | Sort-Object) `
        -DifferenceObject @($actualFiles.Name | Sort-Object) `
        -CaseSensitive
    if ($actualDirectories.Count -ne 0 -or
        $actualFiles.Count -ne $expectedFiles.Count -or
        $null -ne $difference) {
        throw "NinjaSlayer install directory must contain exactly the four package files: $resolved"
    }

    foreach ($file in $expectedFiles) {
        $actualHash = Get-NinjaSlayerSha256 -Path (Join-Path $resolved ([string]$file.path))
        if ($actualHash -cne [string]$file.sha256) {
            throw "Installed NinjaSlayer file hash mismatch: $($file.path)"
        }
    }
}

function Assert-NinjaSlayerReleaseArchiveForHost {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$StagingPath,
        [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$Channel,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)]$Profile,
        [Parameter(Mandatory)][string]$CompatibilityRitsuLibVersion,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceRevision,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
    $expectedArchiveName = "NinjaSlayer-v$Version-$Channel-sts2-$($Profile.gameApiVersion).zip"
    if ((Split-Path -Leaf $resolvedArchive) -cne $expectedArchiveName) {
        throw "Expected the $Channel archive '$expectedArchiveName', received '$(Split-Path -Leaf $resolvedArchive)'."
    }

    $contract = Read-NinjaSlayerPackageArchive -Path $resolvedArchive
    Expand-NinjaSlayerExactZip `
        -ArchivePath $resolvedArchive `
        -DestinationPath $StagingPath `
        -ExpectedFileNames $script:NinjaSlayerPackageFiles

    $artifactProject = Join-Path $RepositoryRoot 'tools\artifact-contract\NinjaSlayer.ArtifactContract.csproj'
    Invoke-NinjaSlayerInstallCommand -Command 'dotnet' -Arguments @(
        'run',
        '--project', $artifactProject,
        '--configuration', 'Release',
        '--no-launch-profile',
        '--',
        'validate-assembly',
        '--assembly', (Join-Path $StagingPath 'NinjaSlayer.dll'),
        '--channel', $Channel,
        '--game-api-version', [string]$Profile.gameApiVersion,
        '--ritsulib-package-id', [string]$Profile.ritsuLibPackageId,
        '--ritsulib-version', $CompatibilityRitsuLibVersion,
        '--source-revision', $SourceRevision.ToLowerInvariant(),
        '--forbidden-path-root', $RepositoryRoot
    )
    Invoke-NinjaSlayerInstallCommand -Command 'node' -Arguments @(
        (Join-Path $RepositoryRoot 'tools\package-contract.mjs'),
        'validate-manifest',
        '--manifest', (Join-Path $StagingPath 'NinjaSlayer.json'),
        '--version', $Version,
        '--min-game-version', [string]$Profile.gameApiVersion,
        '--ritsulib-version', $CompatibilityRitsuLibVersion
    )
    Assert-NinjaSlayerInstallDirectory -Directory $StagingPath -ArchiveContract $contract
    return $contract
}

function Remove-NinjaSlayerInstallWorkDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ModsRoot
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($ModsRoot)
    $prefix = $resolvedRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $leaf = [IO.Path]::GetFileName($resolvedPath)
    if (-not $resolvedPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        $leaf -notmatch '^\.NinjaSlayer\.(install|backup|failed)-[0-9a-f]{32}$') {
        throw "Refusing to remove an install work directory outside the NinjaSlayer session: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Install-NinjaSlayerReleaseArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$Channel,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceRevision,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [scriptblock]$PromoteDirectory
    )

    $resolvedDestination = [IO.Path]::GetFullPath($DestinationPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ([IO.Path]::GetFileName($resolvedDestination) -cne 'NinjaSlayer') {
        throw "The local install destination must be a NinjaSlayer directory: $resolvedDestination"
    }
    if (Test-Path -LiteralPath $resolvedDestination -PathType Leaf) {
        throw "The local install destination is a file: $resolvedDestination"
    }

    $modsRoot = Split-Path -Parent $resolvedDestination
    [IO.Directory]::CreateDirectory($modsRoot) | Out-Null
    $sessionId = [Guid]::NewGuid().ToString('N')
    $stagingPath = Join-Path $modsRoot ".NinjaSlayer.install-$sessionId"
    $backupPath = Join-Path $modsRoot ".NinjaSlayer.backup-$sessionId"
    $failedPath = Join-Path $modsRoot ".NinjaSlayer.failed-$sessionId"
    $profile = Get-NinjaSlayerCompatibilityChannel -Manifest $Compatibility -Channel $Channel
    $archiveContract = $null
    $preserveBackup = $false

    try {
        $archiveContract = Assert-NinjaSlayerReleaseArchiveForHost `
            -ArchivePath $ArchivePath `
            -StagingPath $stagingPath `
            -Channel $Channel `
            -Version $Version `
            -Profile $profile `
            -CompatibilityRitsuLibVersion ([string]$Compatibility.ritsuLibVersion) `
            -SourceRevision $SourceRevision `
            -RepositoryRoot $RepositoryRoot

        $hadExistingInstall = Test-Path -LiteralPath $resolvedDestination -PathType Container
        if ($hadExistingInstall) {
            [IO.Directory]::Move($resolvedDestination, $backupPath)
        }

        try {
            if ($null -eq $PromoteDirectory) {
                [IO.Directory]::Move($stagingPath, $resolvedDestination)
            }
            else {
                & $PromoteDirectory $stagingPath $resolvedDestination
            }
            Assert-NinjaSlayerInstallDirectory `
                -Directory $resolvedDestination `
                -ArchiveContract $archiveContract
        }
        catch {
            $installError = $_
            try {
                if (Test-Path -LiteralPath $resolvedDestination -PathType Container) {
                    [IO.Directory]::Move($resolvedDestination, $failedPath)
                }
                if (Test-Path -LiteralPath $backupPath -PathType Container) {
                    [IO.Directory]::Move($backupPath, $resolvedDestination)
                }
            }
            catch {
                $preserveBackup = Test-Path -LiteralPath $backupPath -PathType Container
                throw "NinjaSlayer installation failed and rollback also failed. Backup retained at $backupPath. Install error: $($installError.Exception.Message) Rollback error: $($_.Exception.Message)"
            }
            throw $installError
        }

        if (Test-Path -LiteralPath $backupPath -PathType Container) {
            Remove-NinjaSlayerInstallWorkDirectory -Path $backupPath -ModsRoot $modsRoot
        }
        return [pscustomobject]@{
            Channel = $Channel
            Version = $Version
            Destination = $resolvedDestination
            ArchiveSha256 = [string]$archiveContract.sha256
        }
    }
    finally {
        foreach ($path in @($stagingPath, $failedPath)) {
            if (Test-Path -LiteralPath $path) {
                Remove-NinjaSlayerInstallWorkDirectory -Path $path -ModsRoot $modsRoot
            }
        }
        if (-not $preserveBackup -and (Test-Path -LiteralPath $backupPath)) {
            Remove-NinjaSlayerInstallWorkDirectory -Path $backupPath -ModsRoot $modsRoot
        }
    }
}
