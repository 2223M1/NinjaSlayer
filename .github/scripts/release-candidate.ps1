#Requires -Version 7.0
#Requires -PSEdition Core

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'spine-extension.ps1')

if ($null -eq ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

function Get-NinjaSlayerGitText {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    Push-Location $RepositoryRoot
    try {
        $output = & git @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
        return ($output | Out-String).Trim()
    }
    finally {
        Pop-Location
    }
}

function Assert-NinjaSlayerCandidateChildPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$CandidateRoot
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($CandidateRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release candidate path must remain under $resolvedRoot`: $resolvedPath"
    }
    return $resolvedPath
}

function Expand-NinjaSlayerGitArchive {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $resolvedDestination = [IO.Path]::GetFullPath($DestinationPath)
    $destinationPrefix = $resolvedDestination.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    [IO.Directory]::CreateDirectory($resolvedDestination) | Out-Null

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryName = $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $destination = [IO.Path]::GetFullPath((Join-Path $resolvedDestination $entryName))
            if (-not $destination.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not $seen.Add($destination)) {
                throw "Git archive contains an unsafe or duplicate entry: $($entry.FullName)"
            }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                [IO.Directory]::CreateDirectory($destination) | Out-Null
                continue
            }
            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-NinjaSlayerReleaseCandidate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CandidateSha,
        [Parameter(Mandatory)][string]$CandidateRoot,
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][string]$SpineExtensionDirectory,
        [Parameter(Mandatory)][string]$ReleaseNoteRelativePath,
        [string]$WorkshopMetadataRelativePath = 'Workshop/workshop.json'
    )

    $resolvedRepository = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
    $resolvedCandidateRoot = [IO.Path]::GetFullPath($CandidateRoot)
    [IO.Directory]::CreateDirectory($resolvedCandidateRoot) | Out-Null
    $normalizedSha = $CandidateSha.ToLowerInvariant()
    $resolvedCommit = (Get-NinjaSlayerGitText $resolvedRepository @(
        'rev-parse', '--verify', "$normalizedSha^{commit}"
    )).ToLowerInvariant()
    if ($resolvedCommit -ne $normalizedSha) {
        throw "Release candidate $normalizedSha did not resolve to the requested commit."
    }
    $treeSha = (Get-NinjaSlayerGitText $resolvedRepository @(
        'rev-parse', '--verify', "$normalizedSha^{tree}"
    )).ToLowerInvariant()

    $session = [Guid]::NewGuid().ToString('N')
    $destination = Assert-NinjaSlayerCandidateChildPath `
        -Path (Join-Path $resolvedCandidateRoot "$normalizedSha-$session") `
        -CandidateRoot $resolvedCandidateRoot
    $archivePath = Assert-NinjaSlayerCandidateChildPath `
        -Path (Join-Path $resolvedCandidateRoot "$normalizedSha-$session.zip") `
        -CandidateRoot $resolvedCandidateRoot
    try {
        Push-Location $resolvedRepository
        try {
            & git archive --format=zip --output=$archivePath $normalizedSha
            if ($LASTEXITCODE -ne 0) {
                throw "git archive failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
        Expand-NinjaSlayerGitArchive -ArchivePath $archivePath -DestinationPath $destination

        $spineFiles = @(Copy-NinjaSlayerVerifiedSpineExtension `
            -Compatibility $Compatibility `
            -SourceDirectory $SpineExtensionDirectory `
            -DestinationDirectory (Join-Path $destination 'addons\spine\windows'))
        $releaseNotePath = Join-Path $destination $ReleaseNoteRelativePath
        $workshopMetadataPath = Join-Path $destination $WorkshopMetadataRelativePath
        foreach ($required in @($releaseNotePath, $workshopMetadataPath)) {
            if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
                throw "Release candidate is missing tracked input: $required"
            }
        }

        return [pscustomobject]@{
            Root = $destination
            CandidateSha = $normalizedSha
            TreeSha = $treeSha
            ReleaseNotePath = $releaseNotePath
            WorkshopMetadataPath = $workshopMetadataPath
            SpineFiles = $spineFiles
        }
    }
    catch {
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Recurse -Force
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }
}

function Remove-NinjaSlayerReleaseCandidate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$CandidateRoot
    )

    $resolved = Assert-NinjaSlayerCandidateChildPath -Path $Path -CandidateRoot $CandidateRoot
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Test-NinjaSlayerFrozenReleaseInputs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$CandidateSha,
        [Parameter(Mandatory)][string]$CandidateTree,
        [Parameter(Mandatory)][string]$CompatibilitySha,
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][string]$ReleaseNoteRelativePath,
        [Parameter(Mandatory)][string]$ReleaseNoteBlob,
        [Parameter(Mandatory)][string]$ReleaseNotePath,
        [Parameter(Mandatory)][string]$WorkshopMetadataRelativePath,
        [Parameter(Mandatory)][string]$WorkshopMetadataBlob,
        [Parameter(Mandatory)][string]$WorkshopMetadataPath
    )

    try {
        if ([int]$State.schemaVersion -ne 2 -or
            $State.reusable -ne $true -or
            [string]$State.version -ne $Version -or
            [string]$State.candidateSha -ne $CandidateSha -or
            [string]$State.candidateTree -ne $CandidateTree -or
            [string]$State.compatibilityManifestSha256 -ne $CompatibilitySha) {
            return $false
        }
        if ([string]$State.frozenInputs.releaseNote.relativePath -cne $ReleaseNoteRelativePath -or
            [string]$State.frozenInputs.releaseNote.gitBlob -ne $ReleaseNoteBlob -or
            -not (Test-Path -LiteralPath $ReleaseNotePath -PathType Leaf) -or
            [string]$State.frozenInputs.releaseNote.sha256 -ne
                (Get-NinjaSlayerFileSha256 -Path $ReleaseNotePath) -or
            [string]$State.frozenInputs.workshopMetadata.relativePath -cne
                $WorkshopMetadataRelativePath -or
            [string]$State.frozenInputs.workshopMetadata.gitBlob -ne $WorkshopMetadataBlob -or
            -not (Test-Path -LiteralPath $WorkshopMetadataPath -PathType Leaf) -or
            [string]$State.frozenInputs.workshopMetadata.sha256 -ne
                (Get-NinjaSlayerFileSha256 -Path $WorkshopMetadataPath)) {
            return $false
        }
        $expectedSpineFiles = @($Compatibility.spineExtension.windowsFiles)
        $stateSpineFiles = @($State.frozenInputs.spineFiles)
        if ($stateSpineFiles.Count -ne $expectedSpineFiles.Count) {
            return $false
        }
        for ($index = 0; $index -lt $expectedSpineFiles.Count; $index++) {
            if ([string]$stateSpineFiles[$index].name -cne
                    [string]$expectedSpineFiles[$index].name -or
                [string]$stateSpineFiles[$index].sha256 -ne
                    [string]$expectedSpineFiles[$index].sha256) {
                return $false
            }
        }
        return $true
    }
    catch {
        return $false
    }
}
