#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,

    [string]$StableDataDir,
    [string]$PreviewDataDir,
    [string]$GodotExe,
    [string]$WorkshopUploadRoot,
    [string]$LocalGameRoot,
    [string]$ReleaseNoteFile = 'Workshop\change-note.md',
    [string]$SettingsFile = 'build\fast-release\settings.json',

    [ValidateRange(60, 3600)]
    [int]$BudgetSeconds = 300,

    [switch]$Confirm,
    [switch]$DryRun,
    [switch]$Resume,
    [switch]$SaveSettings,
    [switch]$SkipGitHub,
    [switch]$SkipWorkshop,
    [switch]$SkipLocalInstall,
    [switch]$CleanBuildCache
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:PhaseTimings = [Collections.Generic.List[object]]::new()
$script:TotalTimer = [Diagnostics.Stopwatch]::StartNew()
$script:PackageFiles = @(
    'NinjaSlayer.dll',
    'NinjaSlayer.json',
    'NinjaSlayer.pck',
    'SHA256SUMS'
)

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Get-NativeText {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
    )

    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
    return ($output | Out-String).Trim()
}

function Invoke-TimedStep([string]$Name, [scriptblock]$Action) {
    Write-Host "[$Name]" -ForegroundColor Cyan
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
    }
    finally {
        $timer.Stop()
        $script:PhaseTimings.Add([pscustomobject]@{
            Phase = $Name
            Seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
        })
        Write-Host ("  {0:N2}s" -f $timer.Elapsed.TotalSeconds) -ForegroundColor DarkGray
    }
}

function Resolve-FilePath([string]$Path, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description is required."
    }
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$Description does not exist: $resolved"
    }
    return $resolved
}

function Resolve-DirectoryPath([string]$Path, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description is required."
    }
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "$Description does not exist: $resolved"
    }
    return $resolved
}

function Assert-ChildPath([string]$Path, [string]$AllowedRoot, [string]$Description) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($AllowedRoot)
    $prefix = $resolvedRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description must remain under $resolvedRoot`: $resolvedPath"
    }
    return $resolvedPath
}

function Get-RepositoryRelativePath(
    [string]$Path,
    [string]$RepositoryRoot,
    [string]$Description) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($RepositoryRoot)
    $rootPrefix = $resolvedRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description must be stored inside the repository: $resolvedPath"
    }
    return $resolvedPath.Substring($rootPrefix.Length).Replace('\', '/')
}

function Get-TrackedTextAtRevision(
    [string]$Revision,
    [string]$RelativePath,
    [switch]$AllowMissing) {
    $output = @(& git show "$Revision`:$RelativePath" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        if ($AllowMissing) {
            return $null
        }
        throw "Unable to read $RelativePath from $Revision."
    }
    return (($output -join "`n").Trim())
}

function Get-TrackedObjectIdAtRevision(
    [string]$Revision,
    [string]$RelativePath) {
    $objectId = Get-NativeText git @('rev-parse', "$Revision`:$RelativePath")
    if ($objectId -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Unable to resolve the tracked object id for $RelativePath at $Revision."
    }
    return $objectId.ToLowerInvariant()
}

function Assert-ReleaseNoteIsFresh(
    [string]$ReleaseNotePath,
    [string]$RepositoryRoot,
    [string]$PreviousTag) {
    $relativePath = Get-RepositoryRelativePath `
        -Path $ReleaseNotePath `
        -RepositoryRoot $RepositoryRoot `
        -Description 'Release note'

    $null = & git ls-files --error-unmatch -- $relativePath 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Release note must be tracked and committed before publishing: $relativePath"
    }
    $null = & git diff --quiet -- $relativePath
    if ($LASTEXITCODE -ne 0) {
        throw "Release note must be committed before publishing: $relativePath"
    }
    $null = & git diff --cached --quiet HEAD -- $relativePath
    if ($LASTEXITCODE -ne 0) {
        throw "Release note must be committed before publishing: $relativePath"
    }

    $currentText = Get-TrackedTextAtRevision -Revision HEAD -RelativePath $relativePath
    if ([string]::IsNullOrWhiteSpace($currentText)) {
        throw 'The committed release note must contain at least one sentence.'
    }
    if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
        return
    }

    $previousText = Get-TrackedTextAtRevision `
        -Revision $PreviousTag `
        -RelativePath $relativePath `
        -AllowMissing
    if ($null -ne $previousText -and $currentText -ceq $previousText) {
        throw "Release note matches the previous release $PreviousTag; update it before publishing."
    }
}

function Get-SettingValue($Settings, [string]$Name) {
    if ($null -eq $Settings) {
        return $null
    }
    $property = $Settings.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return $null
    }
    return [string]$property.Value
}

function Select-ConfiguredValue(
    [string]$ExplicitValue,
    [string]$EnvironmentName,
    $Settings,
    [string]$SettingsName,
    [string]$Fallback) {
    foreach ($candidate in @(
        $ExplicitValue,
        [Environment]::GetEnvironmentVariable($EnvironmentName),
        (Get-SettingValue -Settings $Settings -Name $SettingsName),
        $Fallback)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    }
    return $null
}

function Resolve-HostDataDirectory(
    [string]$Channel,
    [string]$ConfiguredPath,
    $Profile,
    [string]$DefaultSteamDataDirectory) {
    $candidate = $ConfiguredPath
    if ([string]::IsNullOrWhiteSpace($candidate) -and
        (Test-Path -LiteralPath (Join-Path $DefaultSteamDataDirectory 'sts2.dll') -PathType Leaf)) {
        $defaultMvid = Get-NinjaSlayerGameModuleMvid `
            -AssemblyPath (Join-Path $DefaultSteamDataDirectory 'sts2.dll')
        if ($defaultMvid -eq [string]$Profile.hostContract.moduleMvid) {
            $candidate = $DefaultSteamDataDirectory
        }
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw "No $Channel host was configured. Pass -$([char]::ToUpperInvariant($Channel[0]))$($Channel.Substring(1))DataDir once with -SaveSettings."
    }

    $resolved = Resolve-DirectoryPath $candidate "$Channel STS2 data directory"
    foreach ($required in @('sts2.dll', '0Harmony.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolved $required) -PathType Leaf)) {
            throw "$Channel STS2 data directory is missing $required`: $resolved"
        }
    }
    $actualMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath (Join-Path $resolved 'sts2.dll')
    $expectedMvid = [string]$Profile.hostContract.moduleMvid
    if ($actualMvid -ne $expectedMvid) {
        throw "$Channel sts2.dll MVID is $actualMvid; compatibility.json requires $expectedMvid."
    }
    return $resolved
}

function Get-DisallowedWorktreeChanges {
    $changes = @(& git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git worktree.'
    }
    return @($changes | Where-Object {
        if ($_.Length -lt 4) {
            return $true
        }
        $path = $_.Substring(3).Replace('\\', '/')
        return $path -ne 'AGENTS.md' -and -not $path.StartsWith('.agents/', [StringComparison]::Ordinal)
    })
}

function Get-TagCommit([string]$Tag) {
    $output = & git rev-parse -q --verify "refs/tags/$Tag^{commit}" 2>$null
    if ($LASTEXITCODE -eq 0) {
        return ($output | Out-String).Trim().ToLowerInvariant()
    }
    return $null
}

function Get-RemoteTagCommit([string]$Tag) {
    $lines = @(& git ls-remote --tags origin "refs/tags/$Tag" "refs/tags/$Tag^{}")
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect remote release tags.'
    }
    if ($lines.Count -eq 0) {
        return $null
    }
    $peeled = @($lines | Where-Object { $_ -match '\^\{\}$' })
    $selected = if ($peeled.Count -gt 0) { $peeled[0] } else { $lines[0] }
    return ($selected -split '\s+')[0].ToLowerInvariant()
}

function Test-GitHubRelease([string]$Tag, [string]$Repository) {
    $null = & gh release view $Tag --repo $Repository --json tagName 2>$null
    return $LASTEXITCODE -eq 0
}

function New-ExactPackageArchive([string]$PackageDirectory, [string]$ArchivePath) {
    foreach ($name in $script:PackageFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory $name) -PathType Leaf)) {
            throw "Package artifact is missing: $PackageDirectory\$name"
        }
    }
    $extraFiles = @(Get-ChildItem -LiteralPath $PackageDirectory -File | Where-Object {
        $_.Name -cnotin $script:PackageFiles
    })
    if ($extraFiles.Count -gt 0) {
        throw "Package contains unexpected files: $($extraFiles.Name -join ', ')"
    }

    if (Test-Path -LiteralPath $ArchivePath) {
        Remove-Item -LiteralPath $ArchivePath -Force
    }
    $zip = [IO.Compression.ZipFile]::Open($ArchivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $script:PackageFiles) {
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $source = [IO.File]::OpenRead((Join-Path $PackageDirectory $name))
            try {
                $destination = $entry.Open()
                try {
                    $source.CopyTo($destination)
                }
                finally {
                    $destination.Dispose()
                }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Test-ResumeArtifacts(
    [string]$StatePath,
    [string]$CandidateSha,
    [string]$CandidateTree,
    [string]$CompatibilitySha,
    $Compatibility,
    [string]$ReleaseNoteRelativePath,
    [string]$ReleaseNoteBlob,
    [string]$ReleaseNotePath,
    [string]$WorkshopMetadataRelativePath,
    [string]$WorkshopMetadataBlob,
    [string]$WorkshopMetadataPath,
    [hashtable]$ArchivePaths) {
    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        return $false
    }
    try {
        $state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not (Test-NinjaSlayerFrozenReleaseInputs `
            -State $state `
            -Version $Version `
            -CandidateSha $CandidateSha `
            -CandidateTree $CandidateTree `
            -CompatibilitySha $CompatibilitySha `
            -Compatibility $Compatibility `
            -ReleaseNoteRelativePath $ReleaseNoteRelativePath `
            -ReleaseNoteBlob $ReleaseNoteBlob `
            -ReleaseNotePath $ReleaseNotePath `
            -WorkshopMetadataRelativePath $WorkshopMetadataRelativePath `
            -WorkshopMetadataBlob $WorkshopMetadataBlob `
            -WorkshopMetadataPath $WorkshopMetadataPath)) {
            return $false
        }
        foreach ($channel in @('stable', 'preview')) {
            $archivePath = $ArchivePaths[$channel]
            if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
                return $false
            }
            $archive = Read-NinjaSlayerPackageArchive -Path $archivePath
            if ([string]$state.archives.$channel.path -cne (Split-Path -Leaf $archivePath) -or
                [string]$state.archives.$channel.sha256 -ne [string]$archive.sha256) {
                return $false
            }
        }
        $workshopArchivePath = $ArchivePaths.workshop
        if (-not (Test-Path -LiteralPath $workshopArchivePath -PathType Leaf)) {
            return $false
        }
        $workshopArchive = Read-NinjaSlayerWorkshopBundleArchive `
            -Path $workshopArchivePath `
            -Compatibility $Compatibility
        if ([string]$state.archives.workshop.path -cne (Split-Path -Leaf $workshopArchivePath) -or
            [string]$state.archives.workshop.sha256 -ne [string]$workshopArchive.sha256) {
            return $false
        }
        return $true
    }
    catch {
        Write-Warning "Ignoring unusable fast-release state: $($_.Exception.Message)"
        return $false
    }
}

function Assert-GitHubReleaseAssets(
    [string]$Tag,
    [string]$Repository,
    [hashtable]$ArchivePaths) {
    $releaseJson = Get-NativeText gh @(
        'release', 'view', $Tag,
        '--repo', $Repository,
        '--json', 'assets,isDraft,tagName,url'
    ) | ConvertFrom-Json
    if ($releaseJson.isDraft -or [string]$releaseJson.tagName -ne $Tag) {
        throw "GitHub Release $Tag is missing or still a draft."
    }
    $expectedNames = @($ArchivePaths.Values | ForEach-Object { Split-Path -Leaf $_ } | Sort-Object)
    $actualNames = @($releaseJson.assets | ForEach-Object name | Sort-Object)
    if ($null -ne (Compare-Object $expectedNames $actualNames -CaseSensitive)) {
        throw "GitHub Release $Tag does not contain exactly the stable, preview, and universal Workshop archives."
    }
    foreach ($archiveName in @('stable', 'preview', 'workshop')) {
        $path = $ArchivePaths[$archiveName]
        $asset = @($releaseJson.assets | Where-Object name -CEQ (Split-Path -Leaf $path))[0]
        $file = Get-Item -LiteralPath $path
        if ([long]$asset.size -ne $file.Length) {
            throw "GitHub Release asset size mismatch for $($file.Name)."
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$asset.digest)) {
            $expectedDigest = "sha256:$(Get-NinjaSlayerSha256 -Path $path)"
            if ([string]$asset.digest -cne $expectedDigest) {
                throw "GitHub Release asset digest mismatch for $($file.Name)."
            }
        }
    }
    return [string]$releaseJson.url
}

if (-not $DryRun -and -not $Confirm) {
    throw 'Fast official release is disabled until -Confirm is supplied.'
}
if ($DryRun -and ($SkipGitHub -or $SkipWorkshop)) {
    throw 'DryRun already disables publication; SkipGitHub and SkipWorkshop are unnecessary.'
}
if (-not $DryRun -and $SkipGitHub -and $SkipWorkshop) {
    throw 'SkipGitHub and SkipWorkshop cannot both be selected.'
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location $repositoryRoot
$compatibilityPath = Join-Path $repositoryRoot 'eng\compatibility.json'
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')
. (Join-Path $repositoryRoot '.github\scripts\release-artifact.ps1')
. (Join-Path $repositoryRoot '.github\scripts\release-candidate.ps1')

$compatibility = Read-NinjaSlayerCompatibility -Path $compatibilityPath
$compatibilitySha = Get-NinjaSlayerCompatibilitySha256 -Path $compatibilityPath
$stableProfile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel stable
$previewProfile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel preview

$resolvedSettingsPath = if ([IO.Path]::IsPathRooted($SettingsFile)) {
    [IO.Path]::GetFullPath($SettingsFile)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $SettingsFile))
}
$settings = if (Test-Path -LiteralPath $resolvedSettingsPath -PathType Leaf) {
    Get-Content -LiteralPath $resolvedSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
else {
    $null
}

$defaultSteamDataDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
$stableConfigured = Select-ConfiguredValue `
    $StableDataDir 'NINJASLAYER_STS2_STABLE_DATA_DIR' $settings 'stableDataDir' $null
$previewConfigured = Select-ConfiguredValue `
    $PreviewDataDir 'NINJASLAYER_STS2_PREVIEW_DATA_DIR' $settings 'previewDataDir' $null
$resolvedStableDataDir = Resolve-HostDataDirectory `
    stable $stableConfigured $stableProfile $defaultSteamDataDirectory
$resolvedPreviewDataDir = Resolve-HostDataDirectory `
    preview $previewConfigured $previewProfile $defaultSteamDataDirectory

$defaultGodot = 'C:\Program Files\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe'
$resolvedGodot = Resolve-FilePath `
    (Select-ConfiguredValue $GodotExe 'GODOT_EXE' $settings 'godotExe' $defaultGodot) `
    'Godot executable'

$resolvedWorkshopRoot = $null
if (-not $DryRun -and -not $SkipWorkshop -or $SaveSettings) {
    $configuredWorkshopRoot = Select-ConfiguredValue `
        $WorkshopUploadRoot 'NINJASLAYER_WORKSHOP_UPLOAD_ROOT' $settings 'workshopUploadRoot' $null
    if ([string]::IsNullOrWhiteSpace($configuredWorkshopRoot)) {
        $workspaceRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..'))
        $uploadRoots = @(Get-ChildItem -LiteralPath $workspaceRoot -Directory | Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName 'ModUploader.exe') -PathType Leaf
        })
        if ($uploadRoots.Count -eq 1) {
            $configuredWorkshopRoot = $uploadRoots[0].FullName
        }
    }
    $resolvedWorkshopRoot = Resolve-DirectoryPath $configuredWorkshopRoot 'Workshop upload root'
    $null = Resolve-FilePath (Join-Path $resolvedWorkshopRoot 'ModUploader.exe') 'Workshop uploader'
}

if ($SaveSettings) {
    $settingsObject = [ordered]@{
        schemaVersion = 1
        stableDataDir = $resolvedStableDataDir
        previewDataDir = $resolvedPreviewDataDir
        godotExe = $resolvedGodot
        workshopUploadRoot = $resolvedWorkshopRoot
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedSettingsPath)) | Out-Null
    [IO.File]::WriteAllText(
        $resolvedSettingsPath,
        ($settingsObject | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))
    Write-Host "Saved local fast-release settings to $resolvedSettingsPath"
}

$releaseNotePath = if ([IO.Path]::IsPathRooted($ReleaseNoteFile)) {
    [IO.Path]::GetFullPath($ReleaseNoteFile)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $ReleaseNoteFile))
}
$releaseNotePath = Resolve-FilePath $releaseNotePath 'Release note file'
if ([string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $releaseNotePath -Raw -Encoding UTF8))) {
    throw 'Release note must contain at least one sentence.'
}
$releaseNoteRelativePath = Get-RepositoryRelativePath `
    -Path $releaseNotePath `
    -RepositoryRoot $repositoryRoot `
    -Description 'Release note'
$workshopMetadataRelativePath = 'Workshop/workshop.json'

$tag = "v$Version"
$buildRoot = Join-Path $repositoryRoot 'build\fast-release\cache'
$candidateRoot = Join-Path $repositoryRoot 'build\fast-release\candidate'
$releaseRoot = Join-Path $repositoryRoot "build\fast-release\$tag"
$releaseRoot = Assert-ChildPath $releaseRoot (Join-Path $repositoryRoot 'build\fast-release') 'Release output'
[IO.Directory]::CreateDirectory($releaseRoot) | Out-Null
$archivePaths = @{
    stable = Join-Path $releaseRoot "NinjaSlayer-$tag-stable-sts2-$($stableProfile.gameApiVersion).zip"
    preview = Join-Path $releaseRoot "NinjaSlayer-$tag-preview-sts2-$($previewProfile.gameApiVersion).zip"
    workshop = Join-Path $releaseRoot "NinjaSlayer-$tag-workshop-universal.zip"
}
$statePath = Join-Path $releaseRoot 'fast-release-state.json'
$frozenInputRoot = Join-Path $releaseRoot 'frozen-inputs'
$frozenReleaseNotePath = Join-Path $frozenInputRoot 'change-note.md'
$frozenWorkshopMetadataPath = Join-Path $frozenInputRoot 'workshop.json'
$spineExtensionDirectory = Join-Path $repositoryRoot 'addons\spine\windows'

$repository = $null
$head = $null
$candidateTree = $null
$releaseNoteBlob = $null
$workshopMetadataBlob = $null
$candidate = $null
$existingRelease = $false
try {
    Invoke-TimedStep 'Preflight' {
        Invoke-Native git @('fetch', 'origin', 'main', '--tags', '--prune')
        $branch = Get-NativeText git @('branch', '--show-current')
        if ($branch -ne 'main') {
            throw "Fast official release must run from main, not $branch."
        }
        $script:head = (Get-NativeText git @('rev-parse', 'HEAD')).ToLowerInvariant()
        $originMain = (Get-NativeText git @('rev-parse', 'origin/main')).ToLowerInvariant()
        if ($script:head -ne $originMain) {
            throw 'Fast official release requires HEAD to match origin/main exactly.'
        }

        $dirty = @(Get-DisallowedWorktreeChanges)
        if ($dirty.Count -gt 0) {
            throw "Fast official release found uncommitted shipping changes:`n$($dirty -join [Environment]::NewLine)"
        }
        Invoke-Native git @('diff', '--check')

        $localTagCommit = Get-TagCommit $tag
        $remoteTagCommit = Get-RemoteTagCommit $tag
        foreach ($existingCommit in @($localTagCommit, $remoteTagCommit)) {
            if (-not [string]::IsNullOrWhiteSpace($existingCommit) -and $existingCommit -ne $script:head) {
                throw "$tag already points to $existingCommit instead of HEAD $($script:head)."
            }
        }
        if (($localTagCommit -or $remoteTagCommit) -and -not $Resume -and -not $DryRun) {
            throw "$tag already exists. Use -Resume only when continuing this exact release."
        }

        $releaseTags = @(& git tag --list 'v*' | ForEach-Object {
            if ($_ -match '^v(\d+)\.(\d+)\.(\d+)$') {
                [pscustomobject]@{
                    Name = $_
                    Version = [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
                }
            }
        })
        $requestedVersion = [version]$Version
        if (-not $localTagCommit -and $releaseTags.Count -gt 0) {
            $latest = $releaseTags | Sort-Object Version -Descending | Select-Object -First 1
            if ($requestedVersion -le $latest.Version) {
                throw "$tag must be newer than the latest release $($latest.Name)."
            }
        }

        $previousRelease = $releaseTags |
            Where-Object { $_.Version -lt $requestedVersion } |
            Sort-Object Version -Descending |
            Select-Object -First 1
        $previousReleaseTag = if ($null -eq $previousRelease) { $null } else { $previousRelease.Name }
        Assert-ReleaseNoteIsFresh -ReleaseNotePath $releaseNotePath -RepositoryRoot $repositoryRoot -PreviousTag $previousReleaseTag
        $script:candidateTree = (Get-NativeText git @(
            'rev-parse', "$($script:head)^{tree}"
        )).ToLowerInvariant()
        $script:releaseNoteBlob = Get-TrackedObjectIdAtRevision `
            -Revision $script:head `
            -RelativePath $releaseNoteRelativePath
        $script:workshopMetadataBlob = Get-TrackedObjectIdAtRevision `
            -Revision $script:head `
            -RelativePath $workshopMetadataRelativePath

        if (-not $DryRun -and -not $SkipGitHub) {
            Invoke-Native gh @('auth', 'status')
            $script:repository = Get-NativeText gh @('repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner')
            $script:existingRelease = Test-GitHubRelease $tag $script:repository
            if ($script:existingRelease -and -not $Resume) {
                throw "GitHub Release $tag already exists. Use -Resume only to finish its Workshop upload."
            }
        }
    }

    Invoke-TimedStep 'Fast repository checks' {
        Invoke-Native node @('tools/validate-repository.mjs')
    }

    $reuseArtifacts = $Resume -and (Test-ResumeArtifacts `
        -StatePath $statePath `
        -CandidateSha $head `
        -CandidateTree $candidateTree `
        -CompatibilitySha $compatibilitySha `
        -Compatibility $compatibility `
        -ReleaseNoteRelativePath $releaseNoteRelativePath `
        -ReleaseNoteBlob $releaseNoteBlob `
        -ReleaseNotePath $frozenReleaseNotePath `
        -WorkshopMetadataRelativePath $workshopMetadataRelativePath `
        -WorkshopMetadataBlob $workshopMetadataBlob `
        -WorkshopMetadataPath $frozenWorkshopMetadataPath `
        -ArchivePaths $archivePaths)
    if ($reuseArtifacts) {
        Write-Host 'Reusing locally verified stable, preview, and universal Workshop archives.' -ForegroundColor Green
    }
    else {
        Invoke-TimedStep 'Freeze release candidate' {
            $script:candidate = New-NinjaSlayerReleaseCandidate `
                -RepositoryRoot $repositoryRoot `
                -CandidateSha $head `
                -CandidateRoot $candidateRoot `
                -Compatibility $compatibility `
                -SpineExtensionDirectory $spineExtensionDirectory `
                -ReleaseNoteRelativePath $releaseNoteRelativePath `
                -WorkshopMetadataRelativePath $workshopMetadataRelativePath
            if ([string]$script:candidate.TreeSha -ne $candidateTree) {
                throw 'The frozen release candidate tree does not match the preflight tree.'
            }
            if (Test-Path -LiteralPath $frozenInputRoot) {
                Remove-Item -LiteralPath $frozenInputRoot -Recurse -Force
            }
            [IO.Directory]::CreateDirectory($frozenInputRoot) | Out-Null
            Copy-Item -LiteralPath $script:candidate.ReleaseNotePath `
                -Destination $frozenReleaseNotePath
            Copy-Item -LiteralPath $script:candidate.WorkshopMetadataPath `
                -Destination $frozenWorkshopMetadataPath
        }
        foreach ($channel in @('stable', 'preview')) {
            $dataDirectory = if ($channel -eq 'stable') {
                $resolvedStableDataDir
            }
            else {
                $resolvedPreviewDataDir
            }
            Invoke-TimedStep "Build $channel" {
                $parameters = @{
                    Channel = $channel
                    Version = $Version
                    Sts2DataDir = $dataDirectory
                    Target = 'PackageMod'
                    GodotExe = $resolvedGodot
                    BuildRoot = $buildRoot
                    ReuseCache = -not $CleanBuildCache
                }
                $parameters.SourceRevision = $head
                & (Join-Path $candidate.Root 'tools\release\Invoke-NinjaSlayerChannelBuild.ps1') `
                    @parameters
            }

            Invoke-TimedStep "Archive $channel" {
                $packageDirectory = Join-Path $buildRoot "$channel\package\NinjaSlayer"
                New-ExactPackageArchive $packageDirectory $archivePaths[$channel]
                $null = Read-NinjaSlayerPackageArchive -Path $archivePaths[$channel]
            }
        }

        Invoke-TimedStep 'Bundle Workshop package' {
            $bundleDirectory = Join-Path $buildRoot 'workshop\package\NinjaSlayer'
            & (Join-Path $candidate.Root 'tools\release\New-NinjaSlayerWorkshopBundle.ps1') `
                -StablePackageDirectory (Join-Path $buildRoot 'stable\package\NinjaSlayer') `
                -PreviewPackageDirectory (Join-Path $buildRoot 'preview\package\NinjaSlayer') `
                -StableSts2DataDir $resolvedStableDataDir `
                -OutputDirectory $bundleDirectory `
                -BuildRoot (Join-Path $buildRoot 'workshop\build') `
                -CompatibilityManifestPath (Join-Path $candidate.Root 'eng\compatibility.json') `
                -Version $Version `
                -SourceRevision $head
            $bundleFiles = Get-NinjaSlayerWorkshopBundleFiles -Compatibility $compatibility
            New-NinjaSlayerExactZip `
                -SourceDirectory $bundleDirectory `
                -ArchivePath $archivePaths.workshop `
                -ExpectedFileNames $bundleFiles
            $null = Read-NinjaSlayerWorkshopBundleArchive `
                -Path $archivePaths.workshop `
                -Compatibility $compatibility
        }

        $state = [ordered]@{
            schemaVersion = 2
            version = $Version
            tag = $tag
            candidateSha = $head
            candidateTree = $candidateTree
            compatibilityManifestSha256 = $compatibilitySha
            reusable = $true
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            frozenInputs = [ordered]@{
                releaseNote = [ordered]@{
                    relativePath = $releaseNoteRelativePath
                    gitBlob = $releaseNoteBlob
                    sha256 = Get-NinjaSlayerSha256 -Path $frozenReleaseNotePath
                }
                workshopMetadata = [ordered]@{
                    relativePath = $workshopMetadataRelativePath
                    gitBlob = $workshopMetadataBlob
                    sha256 = Get-NinjaSlayerSha256 -Path $frozenWorkshopMetadataPath
                }
                spineFiles = @($candidate.SpineFiles)
            }
            archives = [ordered]@{
                stable = [ordered]@{
                    path = Split-Path -Leaf $archivePaths.stable
                    sha256 = Get-NinjaSlayerSha256 -Path $archivePaths.stable
                }
                preview = [ordered]@{
                    path = Split-Path -Leaf $archivePaths.preview
                    sha256 = Get-NinjaSlayerSha256 -Path $archivePaths.preview
                }
                workshop = [ordered]@{
                    path = Split-Path -Leaf $archivePaths.workshop
                    sha256 = Get-NinjaSlayerSha256 -Path $archivePaths.workshop
                }
            }
        }
        [IO.File]::WriteAllText(
            $statePath,
            ($state | ConvertTo-Json -Depth 6),
            [Text.UTF8Encoding]::new($false))
    }

    if ($DryRun) {
        Write-Host 'Dry run complete. No tag, GitHub Release, or Workshop upload was created.' `
            -ForegroundColor Green
        return
    }
    if ($script:TotalTimer.Elapsed.TotalSeconds -ge $BudgetSeconds) {
        throw "Preparation exceeded the $BudgetSeconds-second budget before publication; no tag was created."
    }

    if (-not $SkipLocalInstall) {
        Invoke-TimedStep 'Local host install' {
            $activeGameRoot = if ([string]::IsNullOrWhiteSpace($LocalGameRoot)) {
                Split-Path -Parent $defaultSteamDataDirectory
            }
            else {
                [IO.Path]::GetFullPath($LocalGameRoot)
            }
            $activeGameAssembly = Join-Path $activeGameRoot 'data_sts2_windows_x86_64\sts2.dll'
            $activeMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath $activeGameAssembly
            $activeHost = Resolve-NinjaSlayerCompatibilityHost `
                -Manifest $compatibility `
                -ModuleMvid $activeMvid
            $activeArchive = $archivePaths[$activeHost.Channel]
            & (Join-Path $repositoryRoot 'tools\release\Install-CurrentHostRelease.ps1') `
                -Version $Version `
                -SourceRevision $head `
                -GameRoot $activeGameRoot `
                -ArchivePath $activeArchive `
                -ExpectedArchiveSha256 (Get-NinjaSlayerSha256 -Path $activeArchive)
        }
    }

    Invoke-TimedStep 'Tag and push' {
        Invoke-Native git @('fetch', 'origin', 'main', '--tags', '--prune')
        $currentHead = (Get-NativeText git @('rev-parse', 'HEAD')).ToLowerInvariant()
        $currentOriginMain = (Get-NativeText git @('rev-parse', 'origin/main')).ToLowerInvariant()
        if ($currentHead -ne $head -or $currentOriginMain -ne $head) {
            throw "Release inputs moved after freezing candidate $head."
        }
        $dirty = @(Get-DisallowedWorktreeChanges)
        if ($dirty.Count -gt 0) {
            throw "Shipping changes appeared after candidate freeze:`n$($dirty -join [Environment]::NewLine)"
        }
        Invoke-Native git @('diff', '--check')
        $localTagCommit = Get-TagCommit $tag
        if (-not [string]::IsNullOrWhiteSpace($localTagCommit) -and $localTagCommit -ne $head) {
            throw "$tag changed locally during packaging."
        }
        if ([string]::IsNullOrWhiteSpace($localTagCommit)) {
            Invoke-Native git @('tag', '-a', $tag, '-m', "NinjaSlayer $tag")
        }
        $remoteTagCommit = Get-RemoteTagCommit $tag
        if (-not [string]::IsNullOrWhiteSpace($remoteTagCommit) -and $remoteTagCommit -ne $head) {
            throw "$tag appeared remotely on a different commit during packaging."
        }
        if ([string]::IsNullOrWhiteSpace($remoteTagCommit)) {
            Invoke-Native git @('push', 'origin', $tag)
        }
        $remoteTagCommit = Get-RemoteTagCommit $tag
        if ($remoteTagCommit -ne $head) {
            throw "Remote $tag does not resolve to packaged commit $head after push."
        }
    }

    $releaseUrl = $null
    if (-not $SkipGitHub) {
        Invoke-TimedStep 'GitHub Release' {
            if (-not $existingRelease) {
                Invoke-Native gh @(
                    'release', 'create', $tag,
                    $archivePaths.stable,
                    $archivePaths.preview,
                    $archivePaths.workshop,
                    '--repo', $repository,
                    '--verify-tag',
                    '--notes-file', $frozenReleaseNotePath,
                    '--title', "NinjaSlayer $tag",
                    '--latest'
                )
            }
            $script:releaseUrl = Assert-GitHubReleaseAssets $tag $repository $archivePaths
        }
    }

    if (-not $SkipWorkshop) {
        Invoke-TimedStep 'Steam Workshop' {
            $workshopDirectory = Assert-ChildPath `
                (Join-Path $resolvedWorkshopRoot 'NinjaSlayer') `
                $resolvedWorkshopRoot `
                'Workshop staging directory'
            $contentDirectory = Assert-ChildPath `
                (Join-Path $workshopDirectory 'content') `
                $workshopDirectory `
                'Workshop content directory'
            [IO.Directory]::CreateDirectory($workshopDirectory) | Out-Null
            Expand-NinjaSlayerExactZip `
                -ArchivePath $archivePaths.workshop `
                -DestinationPath $contentDirectory `
                -ExpectedFileNames (Get-NinjaSlayerWorkshopBundleFiles -Compatibility $compatibility)

            $metadata = Get-Content -LiteralPath $frozenWorkshopMetadataPath `
                -Raw -Encoding UTF8 | ConvertFrom-Json
            $metadata.changeNote = (Get-Content -LiteralPath $frozenReleaseNotePath -Raw -Encoding UTF8).Trim()
            [IO.File]::WriteAllText(
                (Join-Path $workshopDirectory 'workshop.json'),
                ($metadata | ConvertTo-Json -Depth 10),
                [Text.UTF8Encoding]::new($false))

            Push-Location $resolvedWorkshopRoot
            try {
                Invoke-Native (Join-Path $resolvedWorkshopRoot 'ModUploader.exe') @(
                    'upload', '-w', 'NinjaSlayer'
                )
            }
            finally {
                Pop-Location
            }
        }
    }

    $summary = [ordered]@{
        schemaVersion = 1
        version = $Version
        tag = $tag
        candidateSha = $head
        candidateTree = $candidateTree
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        elapsedSeconds = [Math]::Round($script:TotalTimer.Elapsed.TotalSeconds, 3)
        budgetSeconds = $BudgetSeconds
        githubRelease = $releaseUrl
        workshopUploaded = -not $SkipWorkshop
        localInstallSkipped = [bool]$SkipLocalInstall
        phases = $script:PhaseTimings.ToArray()
    }
    [IO.File]::WriteAllText(
        (Join-Path $releaseRoot 'fast-release-summary.json'),
        ($summary | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))

    Write-Host "NinjaSlayer $tag release completed." -ForegroundColor Green
}
finally {
    if ($null -ne $candidate -and -not [string]::IsNullOrWhiteSpace([string]$candidate.Root)) {
        try {
            Remove-NinjaSlayerReleaseCandidate -Path $candidate.Root -CandidateRoot $candidateRoot
        }
        catch {
            Write-Warning "Unable to remove frozen release candidate: $($_.Exception.Message)"
        }
    }
    $script:TotalTimer.Stop()
    Write-Host ''
    $script:PhaseTimings | Format-Table Phase, Seconds -AutoSize | Out-Host
    $budgetColor = if ($script:TotalTimer.Elapsed.TotalSeconds -le $BudgetSeconds) { 'Green' } else { 'Yellow' }
    Write-Host ("Total: {0:N2}s / {1}s budget" -f $script:TotalTimer.Elapsed.TotalSeconds, $BudgetSeconds) `
        -ForegroundColor $budgetColor
}
