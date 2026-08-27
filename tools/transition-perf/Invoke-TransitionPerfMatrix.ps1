#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('stable', 'preview')]
    [string]$Channel,

    [Parameter(Mandatory)]
    [string]$GameRoot,

    [Parameter(Mandatory)]
    [string]$RitsuLibRoot,

    [Parameter(Mandatory)]
    [string]$SpineExtensionDirectory,

    [Parameter(Mandatory)]
    [string]$InputSnapshotRoot,

    [Parameter(Mandatory)]
    [string]$EvidenceRoot,

    [string]$DotNetRoot,

    [ValidateRange(5, 30)]
    [int]$Runs = 5,

    [ValidateRange(60, 300)]
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gameRoot = [IO.Path]::GetFullPath($GameRoot)
$ritsuLibRoot = [IO.Path]::GetFullPath($RitsuLibRoot)
$spineSource = [IO.Path]::GetFullPath($SpineExtensionDirectory)
$snapshotRoot = [IO.Path]::GetFullPath($InputSnapshotRoot)
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$gameData = Join-Path $gameRoot 'data_sts2_windows_x86_64'
$gameExecutable = Join-Path $gameRoot 'SlayTheSpire2.exe'
$releaseScript = Join-Path $repositoryRoot 'tools\release\Invoke-NinjaSlayerChannelBuild.ps1'
$driverProject = Join-Path $repositoryRoot 'tools\smoke-harness\NinjaSlayer.SmokeDriver\NinjaSlayer.SmokeDriver.csproj'
$driverManifest = Join-Path $repositoryRoot 'tools\smoke-harness\NinjaSlayer.SmokeDriver\NinjaSlayer-SmokeDriver.json'
$compatibilityPath = Join-Path $repositoryRoot 'eng\compatibility.json'
$spineScript = Join-Path $repositoryRoot '.github\scripts\spine-extension.ps1'
$spineDestination = Join-Path $repositoryRoot 'addons\spine\windows'
$seed = 'NINJASLAYER_SMOKE_01'
$version = '0.1.43'

function Assert-RequiredPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateSet('Leaf', 'Container')][string]$Type
    )

    if (!(Test-Path -LiteralPath $Path -PathType $Type)) {
        throw "Required $Type path was not found: $Path"
    }
}

function Test-IsChildPath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)

    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    return $resolvedPath.StartsWith(
        $resolvedRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function Remove-ExperimentDirectory {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$AllowedRoot)

    if (!(Test-IsChildPath -Path $Path -Root $AllowedRoot)) {
        throw "Refusing to remove a directory outside $AllowedRoot`: $Path"
    }
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [string]$LogPath
    )

    if ([string]::IsNullOrWhiteSpace($LogPath)) {
        & $Executable @ArgumentList
    }
    else {
        & $Executable @ArgumentList *> $LogPath
    }
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Executable failed with exit code $exitCode$(if ($LogPath) { ". See $LogPath" })."
    }
}

function Write-Utf8Json {
    param([Parameter(Mandatory)]$Value, [Parameter(Mandatory)][string]$Path, [int]$Depth = 12)

    [IO.File]::WriteAllText(
        [IO.Path]::GetFullPath($Path),
        (($Value | ConvertTo-Json -Depth $Depth) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

function Copy-DirectoryContents {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

function Get-TreeManifestLines {
    param([Parameter(Mandatory)][string]$Root)

    return @(
        Get-ChildItem -LiteralPath $Root -File -Recurse -Force |
            Sort-Object FullName |
            ForEach-Object {
                $relative = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
                $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                "$hash *$relative"
            })
}

function Get-LinesSha256 {
    param([Parameter(Mandatory)][string[]]$Lines)

    $bytes = [Text.Encoding]::UTF8.GetBytes(($Lines -join "`n") + "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-Median {
    param([Parameter(Mandatory)][double[]]$Values)

    $sorted = @($Values | Sort-Object)
    $middle = [Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) {
        return [double]$sorted[$middle]
    }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}

function Set-ProcessEnvironment {
    param([Parameter(Mandatory)][string]$Name, [AllowNull()][string]$Value)

    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Build-Variant {
    param(
        [Parameter(Mandatory)]$Variant,
        [Parameter(Mandatory)][string]$CandidateSha
    )

    $variantRoot = Join-Path $evidenceRoot "build\$($Variant.Name)"
    $buildLog = Join-Path $variantRoot 'build.log'
    [IO.Directory]::CreateDirectory($variantRoot) | Out-Null
    $previousLoadLimit = [Environment]::GetEnvironmentVariable(
        'NinjaSlayerTransitionLoadLimitEnabled',
        'Process')
    $previousFinalize = [Environment]::GetEnvironmentVariable(
        'NinjaSlayerTransitionFinalizeBatchingEnabled',
        'Process')
    try {
        Set-ProcessEnvironment 'NinjaSlayerTransitionLoadLimitEnabled' $Variant.LoadLimit.ToString().ToLowerInvariant()
        Set-ProcessEnvironment 'NinjaSlayerTransitionFinalizeBatchingEnabled' $Variant.Finalize.ToString().ToLowerInvariant()
        $nativeArgs = @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $releaseScript,
            '-Channel', $Channel,
            '-Version', $version,
            '-Sts2DataDir', $gameData,
            '-Target', 'PackageMod',
            '-BuildRoot', $variantRoot,
            '-SourceRevision', $CandidateSha
        )
        Invoke-Native -Executable 'pwsh.exe' -ArgumentList $nativeArgs -LogPath $buildLog
    }
    finally {
        Set-ProcessEnvironment 'NinjaSlayerTransitionLoadLimitEnabled' $previousLoadLimit
        Set-ProcessEnvironment 'NinjaSlayerTransitionFinalizeBatchingEnabled' $previousFinalize
    }

    $package = Join-Path $variantRoot "$Channel\package\NinjaSlayer"
    foreach ($name in @('NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck', 'SHA256SUMS')) {
        Assert-RequiredPath -Path (Join-Path $package $name) -Type Leaf
    }
    $assemblyPath = Join-Path $package 'NinjaSlayer.dll'
    $artifact = [ordered]@{
        variant = $Variant.Name
        sourceRevision = $CandidateSha
        channel = $Channel
        loadLimitEnabled = $Variant.LoadLimit
        finalizeBatchingEnabled = $Variant.Finalize
        assemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        pckSha256 = (Get-FileHash -LiteralPath (Join-Path $package 'NinjaSlayer.pck') -Algorithm SHA256).Hash.ToLowerInvariant()
        packageDirectory = $package
    }
    Write-Utf8Json -Value $artifact -Path (Join-Path $variantRoot 'artifact.json')
    return [pscustomobject]@{ Matrix = $Variant; Package = $package; Artifact = $artifact }
}

function Build-SmokeDriver {
    param([Parameter(Mandatory)][string]$ProductAssembly)

    $driverRoot = Join-Path $evidenceRoot 'driver'
    $intermediate = Join-Path $driverRoot 'obj'
    $output = Join-Path $driverRoot 'bin'
    [IO.Directory]::CreateDirectory($intermediate) | Out-Null
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $common = @(
        $driverProject,
        ('-p:NinjaSlayerHostChannel=' + $Channel),
        ('-p:Sts2DataDir=' + $gameData),
        ('-p:NinjaSlayerAssemblyPath=' + $ProductAssembly),
        ('-p:BaseIntermediateOutputPath=' + $intermediate + [IO.Path]::DirectorySeparatorChar),
        ('-p:MSBuildProjectExtensionsPath=' + $intermediate + [IO.Path]::DirectorySeparatorChar)
    )
    Invoke-Native -Executable 'dotnet' -ArgumentList (@('restore') + $common) -LogPath (Join-Path $driverRoot 'restore.log')
    Invoke-Native -Executable 'dotnet' -ArgumentList (@(
        'build', '--no-restore', '-c', 'Release', '-v:minimal', '-o', $output
    ) + $common) -LogPath (Join-Path $driverRoot 'build.log')
    $driverAssembly = Join-Path $output 'NinjaSlayer-SmokeDriver.dll'
    Assert-RequiredPath -Path $driverAssembly -Type Leaf
    return $driverAssembly
}

function Stage-Mod {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Name)

    $modsRoot = Join-Path $gameRoot 'mods'
    $target = Join-Path $modsRoot $Name
    [IO.Directory]::CreateDirectory($modsRoot) | Out-Null
    Remove-ExperimentDirectory -Path $target -AllowedRoot $gameRoot
    Copy-Item -LiteralPath $Source -Destination $target -Recurse
}

function Start-Game {
    param(
        [Parameter(Mandatory)][string]$AppData,
        [Parameter(Mandatory)][string]$LocalAppData,
        [Parameter(Mandatory)][string]$ConfigurationPath
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $gameExecutable
    $startInfo.WorkingDirectory = $gameRoot
    $startInfo.UseShellExecute = $false
    foreach ($argument in @(
            '--force-steam=off',
            '--windowed',
            '--resolution', '1280x720',
            '--max-fps', '60',
            '--audio-driver', 'Dummy',
            "--ninjaslayer-smoke-config=$ConfigurationPath")) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.Environment['APPDATA'] = $AppData
    $startInfo.Environment['LOCALAPPDATA'] = $LocalAppData
    if (![string]::IsNullOrWhiteSpace($DotNetRoot)) {
        $startInfo.Environment['DOTNET_ROOT'] = $DotNetRoot
        $startInfo.Environment['DOTNET_ROOT_X64'] = $DotNetRoot
        $startInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    }

    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Failed to start $gameExecutable"
    }
    return $process
}

function Assert-GameLogsClean {
    param([Parameter(Mandatory)][string]$AppData)

    $logsRoot = Join-Path $AppData 'SlayTheSpire2\logs'
    if (!(Test-Path -LiteralPath $logsRoot)) {
        return
    }
    $forbidden = @(
        '(?i)NinjaSlayer.*initialization failed|Critical NinjaSlayer patch installation failed',
        '(?i)Could not load file or assembly|FileLoadException|BadImageFormatException',
        '(?i)ObjectDisposedException|transition watchdog failed',
        '(?i)Resource loaded as null|Failed to load resource synchronously|Error requesting load for path'
    )
    foreach ($log in Get-ChildItem -LiteralPath $logsRoot -File -Force) {
        $content = Get-Content -Raw -LiteralPath $log.FullName
        foreach ($pattern in $forbidden) {
            if ($content -match $pattern) {
                throw "Game log contains a forbidden TransitionPerf failure: $($log.FullName)"
            }
        }
    }
}

function Invoke-TransitionRun {
    param(
        [Parameter(Mandatory)]$Build,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][bool]$Warmup,
        [Parameter(Mandatory)][string]$CandidateSha,
        [Parameter(Mandatory)][string[]]$SnapshotManifest,
        [Parameter(Mandatory)][string]$SnapshotSha256,
        [Parameter(Mandatory)][string]$HostMvid
    )

    Stage-Mod -Source $Build.Package -Name 'NinjaSlayer'
    $runRoot = Join-Path $evidenceRoot "runs\$($Build.Matrix.Name)\$Label"
    if (Test-Path -LiteralPath $runRoot) {
        throw "Run output already exists: $runRoot"
    }
    [IO.Directory]::CreateDirectory($runRoot) | Out-Null
    $appData = Join-Path $runRoot 'profile\appdata'
    $localAppData = Join-Path $runRoot 'profile\localappdata'
    Copy-DirectoryContents -Source (Join-Path $snapshotRoot 'appdata') -Destination $appData
    Copy-DirectoryContents -Source (Join-Path $snapshotRoot 'localappdata') -Destination $localAppData
    $clonedManifest = @(
        Get-TreeManifestLines -Root $appData | ForEach-Object { "appdata/$_" }
        Get-TreeManifestLines -Root $localAppData | ForEach-Object { "localappdata/$_" }
    )
    if (($SnapshotManifest -join "`n") -cne ($clonedManifest -join "`n")) {
        throw "Run $Label did not receive a byte-identical input snapshot."
    }

    $configurationPath = Join-Path $runRoot 'smoke-config.json'
    $checkpointPath = Join-Path $runRoot 'checkpoints.jsonl'
    $perfPath = Join-Path $runRoot 'transition-perf.json'
    Write-Utf8Json -Value ([ordered]@{
            CandidateSha = $CandidateSha
            Seed = $seed
            Phase = 5
            CheckpointPath = $checkpointPath
            AutoSlayLogPath = (Join-Path $runRoot 'autoslay.log')
            FailureScreenshotPath = (Join-Path $runRoot 'failure.png')
            TransitionPerfOutputPath = $perfPath
            TransitionVariant = $Build.Matrix.Name
            TransitionLoadLimitEnabled = $Build.Matrix.LoadLimit
            TransitionFinalizeBatchingEnabled = $Build.Matrix.Finalize
            TransitionPerfWarmup = $Warmup
        }) -Path $configurationPath

    $startedAt = [DateTimeOffset]::UtcNow
    $game = Start-Game -AppData $appData -LocalAppData $localAppData -ConfigurationPath $configurationPath
    try {
        if (!$game.WaitForExit($TimeoutSeconds * 1000)) {
            $game.Kill($true)
            throw "TransitionPerf game exceeded $TimeoutSeconds seconds."
        }
        if ($game.ExitCode -ne 0) {
            throw "TransitionPerf game returned $($game.ExitCode); see $checkpointPath"
        }
    }
    finally {
        if (!$game.HasExited) {
            $game.Kill($true)
            $game.WaitForExit()
        }
        $game.Dispose()
    }

    Assert-RequiredPath -Path $checkpointPath -Type Leaf
    Assert-RequiredPath -Path $perfPath -Type Leaf
    $checkpoints = @(Get-Content -LiteralPath $checkpointPath | ForEach-Object { $_ | ConvertFrom-Json })
    foreach ($required in @(
            'driver.started',
            'mods.loaded',
            'transition-perf.started',
            'transition-perf.run-loading-started',
            'character.selected',
            'transition-perf.neow-held',
            'transition-perf.revealed',
            'transition-perf.completed')) {
        if ($required -notin $checkpoints.Name) {
            throw "TransitionPerf checkpoint was missing: $required"
        }
    }
    if (@($checkpoints | Where-Object Status -ne 'passed').Count -ne 0) {
        throw "TransitionPerf run $Label recorded a failed checkpoint."
    }
    $transitionStart = $checkpoints | Where-Object Name -eq 'transition-perf.started' | Select-Object -First 1
    $runLoadingStart = $checkpoints | Where-Object Name -eq 'transition-perf.run-loading-started' | Select-Object -First 1
    if ([bool]$transitionStart.Data.nonInteractiveMode -or
        -not [bool]$runLoadingStart.Data.nonInteractiveMode -or
        [double]$transitionStart.Data.timeScale -ne 1d -or
        [string]$transitionStart.Data.fastMode -eq 'Instant') {
        throw "TransitionPerf did not isolate one interactive, real-time embark wait."
    }

    $perf = Get-Content -Raw -LiteralPath $perfPath | ConvertFrom-Json
    if ([string]$perf.candidateSha -cne $CandidateSha -or
        [string]$perf.variant -cne $Build.Matrix.Name -or
        [bool]$perf.warmup -ne $Warmup -or
        [bool]$perf.loadLimitEnabled -ne [bool]$Build.Matrix.LoadLimit -or
        [bool]$perf.finalizeBatchingEnabled -ne [bool]$Build.Matrix.Finalize -or
        -not [bool]$perf.runLoadingOverlappedAnimation -or
        -not [bool]$perf.cacheComplete -or
        [int]$perf.frameCount -lt 2) {
        throw "TransitionPerf result did not match the requested run identity or postconditions."
    }
    Assert-GameLogsClean -AppData $appData

    $metadata = [ordered]@{
        candidateSha = $CandidateSha
        channel = $Channel
        hostMvid = $HostMvid
        hostAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $gameData 'sts2.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        gameExecutableSha256 = (Get-FileHash -LiteralPath $gameExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        snapshotSha256 = $SnapshotSha256
        seed = $seed
        resolution = '1280x720'
        fpsLimit = 60
        vsync = 'adaptive'
        variant = $Build.Matrix.Name
        loadLimitEnabled = $Build.Matrix.LoadLimit
        finalizeBatchingEnabled = $Build.Matrix.Finalize
        warmup = $Warmup
        assemblySha256 = $Build.Artifact.assemblySha256
        startedAtUtc = $startedAt.ToString('O')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        resultPath = $perfPath
    }
    Write-Utf8Json -Value $metadata -Path (Join-Path $runRoot 'run-metadata.json')
    return [pscustomobject]@{
        Variant = $Build.Matrix.Name
        Label = $Label
        Warmup = $Warmup
        FrameCount = [int]$perf.frameCount
        P99 = [double]$perf.p99Milliseconds
        RunLoadStart = [double]$perf.runLoadStartMilliseconds
        AnimationEnd = [double]$perf.animationEndMilliseconds
        Reveal = [double]$perf.revealMilliseconds
        Drain = [double]$perf.queueDrainMilliseconds
        BlackScreen = [double]$perf.blackScreenHoldMilliseconds
        FirstVisible = [double]$perf.firstVisibleGameplayFrameMilliseconds
        ResultPath = $perfPath
    }
}

foreach ($path in @($gameRoot, $ritsuLibRoot, $spineSource, $snapshotRoot, (Join-Path $snapshotRoot 'appdata'), (Join-Path $snapshotRoot 'localappdata'))) {
    Assert-RequiredPath -Path $path -Type Container
}
foreach ($path in @($gameExecutable, (Join-Path $gameData 'sts2.dll'), (Join-Path $ritsuLibRoot 'STS2-RitsuLib.dll'), $releaseScript, $driverProject, $driverManifest, $spineScript)) {
    Assert-RequiredPath -Path $path -Type Leaf
}
if (![string]::IsNullOrWhiteSpace($DotNetRoot)) {
    Assert-RequiredPath -Path ([IO.Path]::GetFullPath($DotNetRoot)) -Type Container
}
if ((Test-IsChildPath -Path $evidenceRoot -Root $repositoryRoot) -or
    $evidenceRoot.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Transition performance evidence must be outside the repository.'
}
if ((Test-IsChildPath -Path $spineSource -Root $repositoryRoot) -or
    $spineSource.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Transition performance Spine inputs must remain outside the repository.'
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Evidence root already exists; refusing to overwrite it: $evidenceRoot"
}
if (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue) {
    throw 'Close the running SlayTheSpire2 process before starting the matrix.'
}

$spineInstallStarted = $false
$spineFiles = @()
Push-Location $repositoryRoot
try {
    $status = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
        throw 'Transition performance builds require a clean worktree.'
    }
    $candidateSha = (git rev-parse HEAD).Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $candidateSha -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve the candidate SHA.'
    }

    . (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')
    $compatibility = Read-NinjaSlayerCompatibility -Path $compatibilityPath
    $profile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel $Channel
    $hostMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath (Join-Path $gameData 'sts2.dll')
    if ($hostMvid -cne [string]$profile.hostContract.moduleMvid) {
        throw "$Channel host MVID is $hostMvid; expected $($profile.hostContract.moduleMvid)."
    }
    if (Test-Path -LiteralPath $spineDestination) {
        throw "Refusing to overwrite an existing Spine extension directory: $spineDestination"
    }

    . $spineScript
    $spineInstallStarted = $true
    $spineFiles = @(Copy-NinjaSlayerVerifiedSpineExtension `
        -Compatibility $compatibility `
        -SourceDirectory $spineSource `
        -DestinationDirectory $spineDestination)

    [IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
    $variants = @(
        [pscustomobject]@{ Name = 'baseline'; LoadLimit = $true; Finalize = $true },
        [pscustomobject]@{ Name = 'load-limit-off'; LoadLimit = $false; Finalize = $true },
        [pscustomobject]@{ Name = 'finalize-off'; LoadLimit = $true; Finalize = $false }
    )
    $builds = @{}
    foreach ($variant in $variants) {
        $builds[$variant.Name] = Build-Variant -Variant $variant -CandidateSha $candidateSha
    }
    $pckHashes = @($builds.Values | ForEach-Object { $_.Artifact.pckSha256 } | Select-Object -Unique)
    if ($pckHashes.Count -ne 1) {
        throw 'The three same-source variants produced different resource PCK files.'
    }

    $driverAssembly = Build-SmokeDriver -ProductAssembly (Join-Path $builds.baseline.Package 'NinjaSlayer.dll')
    Stage-Mod -Source $ritsuLibRoot -Name 'STS2-RitsuLib'
    $driverStaging = Join-Path $evidenceRoot 'driver-mod'
    [IO.Directory]::CreateDirectory($driverStaging) | Out-Null
    Copy-Item -LiteralPath $driverAssembly -Destination $driverStaging
    Copy-Item -LiteralPath $driverManifest -Destination $driverStaging
    Stage-Mod -Source $driverStaging -Name 'NinjaSlayer-SmokeDriver'

    $snapshotManifest = @(
        Get-TreeManifestLines -Root (Join-Path $snapshotRoot 'appdata') | ForEach-Object { "appdata/$_" }
        Get-TreeManifestLines -Root (Join-Path $snapshotRoot 'localappdata') | ForEach-Object { "localappdata/$_" }
    )
    $snapshotSha256 = Get-LinesSha256 -Lines $snapshotManifest
    $results = [Collections.Generic.List[object]]::new()
    foreach ($variant in $variants) {
        $results.Add((Invoke-TransitionRun -Build $builds[$variant.Name] -Label 'warmup' -Warmup $true -CandidateSha $candidateSha -SnapshotManifest $snapshotManifest -SnapshotSha256 $snapshotSha256 -HostMvid $hostMvid))
    }
    for ($cycle = 0; $cycle -lt $Runs; $cycle++) {
        for ($offset = 0; $offset -lt $variants.Count; $offset++) {
            $variant = $variants[($cycle + $offset) % $variants.Count]
            $label = 'run-{0:d2}' -f ($cycle + 1)
            $results.Add((Invoke-TransitionRun -Build $builds[$variant.Name] -Label $label -Warmup $false -CandidateSha $candidateSha -SnapshotManifest $snapshotManifest -SnapshotSha256 $snapshotSha256 -HostMvid $hostMvid))
        }
    }

    $summaries = [Collections.Generic.List[object]]::new()
    foreach ($variant in $variants) {
        $formal = @($results | Where-Object { $_.Variant -eq $variant.Name -and !$_.Warmup })
        $summaries.Add([ordered]@{
                variant = $variant.Name
                runs = $formal.Count
                medianP99Milliseconds = Get-Median -Values @($formal.P99)
                loadLimitEnabled = $variant.LoadLimit
                finalizeBatchingEnabled = $variant.Finalize
                results = @($formal | ForEach-Object {
                    [ordered]@{
                        label = $_.Label
                        frameCount = $_.FrameCount
                        p99Milliseconds = $_.P99
                        runLoadStartMilliseconds = $_.RunLoadStart
                        animationEndMilliseconds = $_.AnimationEnd
                        runLoadingOverlappedAnimation = $true
                        revealMilliseconds = $_.Reveal
                        queueDrainMilliseconds = $_.Drain
                        blackScreenHoldMilliseconds = $_.BlackScreen
                        firstVisibleGameplayFrameMilliseconds = $_.FirstVisible
                        rawDataPath = $_.ResultPath
                    }
                })
            })
    }
    $matrixSummary = [ordered]@{
        schemaVersion = 1
        candidateSha = $candidateSha
        channel = $Channel
        hostMvid = $hostMvid
        seed = $seed
        resolution = '1280x720'
        fpsLimit = 60
        frameSource = 'Godot SceneTree.ProcessFrame QPC'
        p99Algorithm = 'nearest-rank over consecutive ProcessFrame QPC deltas'
        snapshotSha256 = $snapshotSha256
        spineExtension = $spineFiles
        variants = $summaries.ToArray()
    }
    Write-Utf8Json -Value $matrixSummary -Path (Join-Path $evidenceRoot 'matrix-summary.json')
    $matrixSummary | ConvertTo-Json -Depth 5
}
finally {
    if ($spineInstallStarted -and (Test-Path -LiteralPath $spineDestination)) {
        Remove-ExperimentDirectory -Path $spineDestination -AllowedRoot $repositoryRoot
    }
    Pop-Location
}
