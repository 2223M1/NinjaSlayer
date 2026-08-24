#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$BundleVersion,
    [Parameter(Mandatory)][string]$CandidateRoot,
    [Parameter(Mandatory)][string]$BundleDirectory,
    [Parameter(Mandatory)][string]$TrustedRoot,
    [Parameter(Mandatory)][string]$GameRootDirectory,
    [Parameter(Mandatory)][string]$RitsuLibModDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$Channel,
    [ValidateSet('FirstCombatRestart', 'FullAutoSlay', 'SawatariSameCombat')]
    [string]$Mode = 'FirstCombatRestart',
    [ValidateRange(0, 7200)][int]$PhaseTimeoutSeconds = 0,
    [string]$Seed = 'NINJASLAYER_SMOKE_01',
    [string]$Repository = 'local',
    [string]$RunId = 'local'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\..\.github\scripts\compatibility.ps1')

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run the real-game smoke launcher from elevated PowerShell so outbound traffic can be blocked.'
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $changeDirectory = -not [string]::IsNullOrWhiteSpace($WorkingDirectory)
    $exitCode = 0
    try {
        if ($changeDirectory) {
            Push-Location -LiteralPath $WorkingDirectory
        }
        & $Command @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($changeDirectory) {
            Pop-Location
        }
    }
    if ($exitCode -ne 0) {
        throw "$Command failed with exit code $exitCode."
    }
}

function Add-MsBuildProperty {
    param(
        [Parameter(Mandatory)][Collections.Generic.List[string]]$Arguments,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    $Arguments.Add("-p:$Name=$Value")
}

function Get-RelativeChildPath {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Child
    )

    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $sourcePrefix = "$sourceRoot$([IO.Path]::DirectorySeparatorChar)"
    $childPath = [IO.Path]::GetFullPath($Child)
    if (-not $childPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Smoke mirror path is not a child of $sourceRoot`: $childPath"
    }

    return [IO.Path]::GetRelativePath($sourceRoot, $childPath)
}

function Resolve-RequiredPath {
    param([Parameter(Mandatory)][string]$Path, [switch]$Leaf)

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    if ($Leaf -and -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Required file was not found: $resolved"
    }
    if (-not $Leaf -and -not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "Required directory was not found: $resolved"
    }
    return [IO.Path]::GetFullPath($resolved)
}

function New-HardLinkedTree {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    if ([IO.Path]::GetPathRoot($Source) -ne [IO.Path]::GetPathRoot($Destination)) {
        throw 'The smoke game mirror must be on the same volume as the golden game root.'
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($directory in Get-ChildItem -LiteralPath $Source -Directory -Recurse -Force) {
        $relative = Get-RelativeChildPath -Source $Source -Child $directory.FullName
        New-Item -ItemType Directory -Path (Join-Path $Destination $relative) -Force | Out-Null
    }
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse -Force) {
        $relative = Get-RelativeChildPath -Source $Source -Child $file.FullName
        New-Item -ItemType HardLink -Path (Join-Path $Destination $relative) -Target $file.FullName | Out-Null
    }
}

function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)

    if ($null -ne $Process -and -not $Process.HasExited) {
        & taskkill.exe /PID $Process.Id /T /F | Out-Null
    }
}

function Stop-SmokeProcesses {
    param([Parameter(Mandatory)][string]$Root)

    foreach ($process in Get-CimInstance Win32_Process -ErrorAction SilentlyContinue) {
        if (-not [string]::IsNullOrWhiteSpace($process.ExecutablePath) -and
            $process.ExecutablePath.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
            & taskkill.exe /PID $process.ProcessId /T /F | Out-Null
        }
    }
}

function Invoke-SmokePhase {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Fresh', 'Resume', 'FullAutoSlay', 'SawatariSameCombat')]
        [string]$Phase,
        [Parameter(Mandatory)][int]$ExpectedExitCode
    )

    $configuration = [ordered]@{
        CandidateSha = $CandidateSha.ToLowerInvariant()
        Seed = $Seed
        Phase = switch ($Phase) {
            'Fresh' { 0 }
            'Resume' { 1 }
            'FullAutoSlay' { 2 }
            'SawatariSameCombat' { 3 }
        }
        CheckpointPath = $checkpointPath
        AutoSlayLogPath = (Join-Path $OutputDirectory "autoslay-$($Phase.ToLowerInvariant()).log")
        FailureScreenshotPath = (Join-Path $OutputDirectory "failure-$($Phase.ToLowerInvariant()).png")
    }
    $configuration | ConvertTo-Json | Set-Content -LiteralPath $configurationPath -Encoding utf8

    $previousAppData = $env:APPDATA
    $previousLocalAppData = $env:LOCALAPPDATA
    $process = $null
    try {
        $env:APPDATA = $appDataDirectory
        $env:LOCALAPPDATA = $localAppDataDirectory
        $arguments = @(
            '--force-steam=off',
            '--windowed',
            '--resolution', '1280x720',
            '--audio-driver', 'Dummy',
            "--ninjaslayer-smoke-config=$configurationPath"
        )
        $process = Start-Process -FilePath $gameExecutable -ArgumentList $arguments `
            -WorkingDirectory $isolatedGameRoot -PassThru
        if (-not $process.WaitForExit($effectivePhaseTimeoutSeconds * 1000)) {
            Stop-ProcessTree -Process $process
            throw "$Phase smoke phase exceeded $effectivePhaseTimeoutSeconds seconds."
        }
        if ($process.ExitCode -ne $ExpectedExitCode) {
            throw "$Phase smoke phase returned $($process.ExitCode); expected $ExpectedExitCode."
        }
    }
    finally {
        Stop-ProcessTree -Process $process
        $env:APPDATA = $previousAppData
        $env:LOCALAPPDATA = $previousLocalAppData
    }
}

function Copy-SanitizedTextArtifact {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { return }
    $content = Get-Content -LiteralPath $Source -Raw
    foreach ($replacement in @(
        @($sessionRoot, '<SMOKE_SESSION>'),
        @($CandidateRoot, '<CANDIDATE>'),
        @($BundleDirectory, '<BUNDLE>'),
        @($TrustedRoot, '<TRUSTED>'),
        @($GameRootDirectory, '<GAME_ROOT>'),
        @($env:USERPROFILE, '<USER_PROFILE>')
    )) {
        $content = $content.Replace(
            [string]$replacement[0],
            [string]$replacement[1],
            [StringComparison]::OrdinalIgnoreCase)
    }
    Set-Content -LiteralPath $Destination -Value $content -Encoding utf8
}

function Get-RitsuLibRuntimeVersion {
    param(
        [Parameter(Mandatory)][string]$ModDirectory,
        [Parameter(Mandatory)][string]$MinimumVersion
    )

    $manifestPath = Join-Path $ModDirectory 'mod_manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "RitsuLib Workshop manifest was not found: $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    $runtimeVersion = [string]$manifest.version
    if ([string]$manifest.id -cne 'STS2-RitsuLib' -or
        $runtimeVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'RitsuLib Workshop manifest has an invalid id or version.'
    }
    if ([version]$runtimeVersion -lt [version]$MinimumVersion) {
        throw "RitsuLib Workshop version $runtimeVersion is older than required $MinimumVersion."
    }

    return $runtimeVersion
}

function Assert-SmokeLogsClean {
    param(
        [Parameter(Mandatory)][string]$GameLogsDirectory,
        [Parameter(Mandatory)][string]$HarnessLogsDirectory
    )

    $logs = [Collections.Generic.List[IO.FileInfo]]::new()
    if (Test-Path -LiteralPath $GameLogsDirectory -PathType Container) {
        $logs.AddRange([IO.FileInfo[]]@(Get-ChildItem -LiteralPath $GameLogsDirectory -File -Force))
    }
    $logs.AddRange([IO.FileInfo[]]@(Get-ChildItem -LiteralPath $HarnessLogsDirectory `
        -File -Filter 'autoslay-*.log' -ErrorAction SilentlyContinue))

    $forbidden = [ordered]@{
        'NinjaSlayer loader failure' = '(?i)\[NinjaSlayer\.Loader\].*initialization failed|NinjaSlayer does not support STS2 host MVID|Variant SHA-256 mismatch'
        'managed assembly load failure' = '(?i)Could not load file or assembly|Could not resolve assembly|FileLoadException|BadImageFormatException'
        'native library load failure' = '(?i)DllNotFoundException|Unable to load shared library|cannot open shared object file|Library not loaded'
        'NinjaSlayer resource or Spine failure' = '(?im)^(?:ERROR|SCRIPT ERROR):.*(?:res://NinjaSlayer|addons/spine|SpineSprite|spine_godot)'
    }
    foreach ($log in $logs) {
        $content = Get-Content -LiteralPath $log.FullName -Raw
        foreach ($entry in $forbidden.GetEnumerator()) {
            if ($content -match $entry.Value) {
                throw "Smoke log $($log.Name) contains $($entry.Key)."
            }
        }
    }
}

$CandidateRoot = Resolve-RequiredPath $CandidateRoot
$BundleDirectory = Resolve-RequiredPath $BundleDirectory
$TrustedRoot = Resolve-RequiredPath $TrustedRoot
$GameRootDirectory = Resolve-RequiredPath $GameRootDirectory
$RitsuLibModDirectory = Resolve-RequiredPath $RitsuLibModDirectory
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$networkIsolationScript = Join-Path $TrustedRoot '.github\scripts\process-network-isolation.ps1'
if (-not (Test-Path -LiteralPath $networkIsolationScript -PathType Leaf)) {
    throw "Trusted process network isolation helper was not found: $networkIsolationScript"
}
. $networkIsolationScript
$compatibilityManifestPath = Join-Path $CandidateRoot 'eng\compatibility.json'
$compatibility = Read-NinjaSlayerCompatibility -Path $compatibilityManifestPath
$hostCompatibility = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel $Channel
$GameApiVersion = [string]$hostCompatibility.gameApiVersion
$RitsuLibPackageId = [string]$hostCompatibility.ritsuLibPackageId
$RitsuLibVersion = [string]$compatibility.ritsuLibVersion
$RitsuLibRuntimeVersion = Get-RitsuLibRuntimeVersion `
    -ModDirectory $RitsuLibModDirectory `
    -MinimumVersion $RitsuLibVersion
$compatibilityManifestSha256 = Get-NinjaSlayerCompatibilitySha256 -Path $compatibilityManifestPath
$bundleSha256 = Get-NinjaSlayerFileSha256 -Path (Join-Path $BundleDirectory 'SHA256SUMS')

foreach ($required in @(
    (Join-Path $GameRootDirectory 'SlayTheSpire2.exe'),
    (Join-Path $GameRootDirectory 'SlayTheSpire2.pck'),
    (Join-Path $GameRootDirectory 'data_sts2_windows_x86_64\sts2.dll'),
    (Join-Path $RitsuLibModDirectory 'STS2-RitsuLib.dll'),
    (Join-Path $RitsuLibModDirectory 'mod_manifest.json'),
    (Join-Path $BundleDirectory 'NinjaSlayer.dll'),
    (Join-Path $BundleDirectory 'NinjaSlayer.json'),
    (Join-Path $BundleDirectory 'NinjaSlayer.pck'),
    (Join-Path $BundleDirectory 'ninjaslayer-variants.manifest'),
    (Join-Path $BundleDirectory "lib\$GameApiVersion\NinjaSlayer.dll")
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing smoke input: $required" }
}

if (Test-Path -LiteralPath $OutputDirectory -PathType Container) {
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
        throw 'Smoke OutputDirectory must be empty so stale evidence cannot be mistaken for this run.'
    }
}
else {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}
$temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:TEMP } else { $env:RUNNER_TEMP }
$sessionRoot = Join-Path $temporaryRoot "NinjaSlayer-Smoke-$Channel-$($CandidateSha.Substring(0, 12))-$([Guid]::NewGuid().ToString('N'))"
$isolatedGameRoot = Join-Path $sessionRoot 'game'
$appDataDirectory = Join-Path $sessionRoot 'appdata'
$localAppDataDirectory = Join-Path $sessionRoot 'localappdata'
$driverOutput = Join-Path $sessionRoot 'driver'
$configurationPath = Join-Path $sessionRoot 'smoke-config.json'
$checkpointPath = Join-Path $OutputDirectory 'checkpoints.jsonl'
$firewallLease = $null
$succeeded = $false
$effectivePhaseTimeoutSeconds = if ($PhaseTimeoutSeconds -gt 0) {
    $PhaseTimeoutSeconds
    } elseif ($Mode -in @('FullAutoSlay', 'SawatariSameCombat')) {
    3600
} else {
    300
}

try {
    New-Item -ItemType Directory -Path $sessionRoot, $appDataDirectory, $localAppDataDirectory, $driverOutput -Force | Out-Null

    $gameDataDirectory = Join-Path $GameRootDirectory 'data_sts2_windows_x86_64'
    Invoke-Native -Command dotnet -Arguments @(
        'run',
        '--project', (Join-Path $CandidateRoot 'tools\artifact-contract\NinjaSlayer.ArtifactContract.csproj'),
        '--configuration', 'Release',
        '--no-launch-profile',
        '--',
        'validate-workshop-bundle',
        '--directory', $BundleDirectory,
        '--compatibility', $compatibilityManifestPath,
        '--version', $BundleVersion,
        '--ritsulib-version', $RitsuLibVersion,
        '--forbidden-path-root', $CandidateRoot
    ) -WorkingDirectory $CandidateRoot

    $candidateAssembly = Join-Path $BundleDirectory "lib\$GameApiVersion\NinjaSlayer.dll"
    $driverArguments = [Collections.Generic.List[string]]::new()
    foreach ($argument in @(
            'build',
            (Join-Path $TrustedRoot 'tools\smoke-harness\NinjaSlayer.SmokeDriver\NinjaSlayer.SmokeDriver.csproj'),
            '-c',
            'Release',
            '-v:minimal',
            '-o',
            $driverOutput
        )) {
        $driverArguments.Add($argument)
    }
    Add-MsBuildProperty $driverArguments 'Sts2DataDir' $gameDataDirectory
    Add-MsBuildProperty $driverArguments 'NinjaSlayerHostChannel' $Channel
    Add-MsBuildProperty $driverArguments 'NinjaSlayerAssemblyPath' $candidateAssembly
    try {
        Invoke-Native -Command dotnet -Arguments $driverArguments.ToArray() -WorkingDirectory $TrustedRoot
    }
    catch {
        throw "Trusted SmokeDriver build failed. $($_.Exception.Message)"
    }

    New-Item -ItemType Directory -Path $isolatedGameRoot -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $GameRootDirectory -File -Force) {
        New-Item -ItemType HardLink -Path (Join-Path $isolatedGameRoot $file.Name) -Target $file.FullName | Out-Null
    }
    foreach ($directoryName in @('data_sts2_windows_x86_64', 'controller_config')) {
        $source = Join-Path $GameRootDirectory $directoryName
        if (Test-Path -LiteralPath $source -PathType Container) {
            New-HardLinkedTree -Source $source -Destination (Join-Path $isolatedGameRoot $directoryName)
        }
    }

    $modsDirectory = Join-Path $isolatedGameRoot 'mods'
    New-Item -ItemType Directory -Path $modsDirectory -Force | Out-Null
    Copy-Item -LiteralPath $BundleDirectory -Destination (Join-Path $modsDirectory 'NinjaSlayer') -Recurse
    Copy-Item -LiteralPath $RitsuLibModDirectory -Destination (Join-Path $modsDirectory 'STS2-RitsuLib') -Recurse
    $smokeModDirectory = Join-Path $modsDirectory 'NinjaSlayer-SmokeDriver'
    New-Item -ItemType Directory -Path $smokeModDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $driverOutput 'NinjaSlayer-SmokeDriver.dll') -Destination $smokeModDirectory
    Copy-Item -LiteralPath (Join-Path $TrustedRoot 'tools\smoke-harness\NinjaSlayer.SmokeDriver\NinjaSlayer-SmokeDriver.json') -Destination $smokeModDirectory

    $settingsDirectory = Join-Path $appDataDirectory 'SlayTheSpire2\default\1'
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $settings = @{
        schema_version = 6; fps_limit = 60; language = 'eng'; fullscreen = $false
        window_position = @{ X = 0; Y = 0 }; window_size = @{ X = 1280; Y = 720 }
        skip_intro_logo = $true; seen_ea_disclaimer = $true; volume_master = 0
        mod_settings = @{
            mods_enabled = $true
            mod_list = @(
                @{ id = 'STS2-RitsuLib'; is_enabled = $true; source = 'mods_directory' },
                @{ id = 'NinjaSlayer'; is_enabled = $true; source = 'mods_directory' },
                @{ id = 'NinjaSlayer-SmokeDriver'; is_enabled = $true; source = 'mods_directory' }
            )
        }
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $settingsDirectory 'settings.save') -Encoding utf8

    $gameExecutable = Join-Path $isolatedGameRoot 'SlayTheSpire2.exe'
    $protectedPrograms = @($gameExecutable, (Join-Path $isolatedGameRoot 'crashpad_handler.exe')) |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    $firewallLease = New-NinjaSlayerProcessFirewallLease `
        -ProgramPath $protectedPrograms `
        -RemoteScope All `
        -RulePrefix "NinjaSlayer-Smoke-$Channel-$($CandidateSha.Substring(0, 12))" `
        -ForbiddenRoot @($CandidateRoot, $TrustedRoot, $BundleDirectory)

    if ($Mode -eq 'FullAutoSlay') {
        Invoke-SmokePhase -Phase FullAutoSlay -ExpectedExitCode 0
    }
    elseif ($Mode -eq 'SawatariSameCombat') {
        Invoke-SmokePhase -Phase SawatariSameCombat -ExpectedExitCode 0
    }
    else {
        Invoke-SmokePhase -Phase Fresh -ExpectedExitCode 20
        Invoke-SmokePhase -Phase Resume -ExpectedExitCode 0
    }

    $gameLogsDirectory = Join-Path $appDataDirectory 'SlayTheSpire2\logs'
    Assert-SmokeLogsClean `
        -GameLogsDirectory $gameLogsDirectory `
        -HarnessLogsDirectory $OutputDirectory

    $checkpoints = @(Get-Content -LiteralPath $checkpointPath | ForEach-Object { $_ | ConvertFrom-Json })
    $requiredCheckpoints = if ($Mode -eq 'FullAutoSlay') {
        @('full-autoslay.starting', 'full-autoslay.completed')
    }
    elseif ($Mode -eq 'SawatariSameCombat') {
        @('sawatari.starting', 'sawatari.same-combat-completed', 'sawatari.completed')
    }
    else {
        @('prepared.created', 'prepared.lifecycle-cleared', 'x-attack.nonlethal-completed', 'spine.platform-extension-completed', 'dark-strike.completed', 'finisher.completed', 'fresh.saved', 'fresh.restart-requested', 'resume.loaded', 'resume.completed')
    }
    $missing = @($requiredCheckpoints | Where-Object { $_ -notin $checkpoints.Name })
    if ($missing.Count -gt 0 -or @($checkpoints | Where-Object Status -ne 'passed').Count -gt 0) {
        throw "Smoke checkpoints were incomplete or failed: $($missing -join ', ')"
    }

    $gameAssemblyPath = Join-Path $GameRootDirectory 'data_sts2_windows_x86_64\sts2.dll'
    $gameVersion = [Reflection.AssemblyName]::GetAssemblyName($gameAssemblyPath).Version.ToString()
    [ordered]@{
        schemaVersion = 5
        candidateSha = $CandidateSha.ToLowerInvariant()
        bundleVersion = $BundleVersion
        result = 'passed'
        channel = $Channel
        gameApiVersion = $GameApiVersion
        gameAssemblyVersion = $gameVersion
        gameModuleMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath $gameAssemblyPath
        ritsuLibPackageId = $RitsuLibPackageId
        ritsuLibVersion = $RitsuLibVersion
        ritsuLibRuntimeVersion = $RitsuLibRuntimeVersion
        bundleSha256 = $bundleSha256
        compatibilityManifestSha256 = $compatibilityManifestSha256
        mode = switch ($Mode) {
            'FullAutoSlay' { 'singleplayer-full-autoslay' }
            'SawatariSameCombat' { 'singleplayer-sawatari-same-combat' }
            default { 'singleplayer-first-combat-restart' }
        }
        repository = $Repository
        workflowRunId = $RunId
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'attestation.json') -Encoding utf8
    $succeeded = $true
}
finally {
    $cleanupFailures = [Collections.Generic.List[string]]::new()
    try {
        Stop-SmokeProcesses -Root $isolatedGameRoot
    }
    catch {
        $cleanupFailures.Add("Failed to stop isolated smoke processes: $($_.Exception.Message)")
    }
    try {
        if ($null -ne $firewallLease) {
            Remove-NinjaSlayerProcessFirewallLease -Lease $firewallLease
        }
    }
    catch {
        $cleanupFailures.Add($_.Exception.Message)
    }
    try {
        $gameLogs = Join-Path $appDataDirectory 'SlayTheSpire2\logs'
        if (Test-Path -LiteralPath $gameLogs -PathType Container) {
            foreach ($log in Get-ChildItem -LiteralPath $gameLogs -File | Select-Object -Last 3) {
                Copy-SanitizedTextArtifact -Source $log.FullName -Destination (Join-Path $OutputDirectory "game-$($log.Name)")
            }
        }
        foreach ($log in Get-ChildItem -LiteralPath $OutputDirectory -Filter 'autoslay-*.log' -File -ErrorAction SilentlyContinue) {
            Copy-SanitizedTextArtifact -Source $log.FullName -Destination "$($log.FullName).sanitized"
            Move-Item -LiteralPath "$($log.FullName).sanitized" -Destination $log.FullName -Force
        }
        if (Test-Path -LiteralPath $checkpointPath -PathType Leaf) {
            Copy-SanitizedTextArtifact -Source $checkpointPath -Destination "$checkpointPath.sanitized"
            Move-Item -LiteralPath "$checkpointPath.sanitized" -Destination $checkpointPath -Force
        }
    }
    finally {
        Remove-Item -LiteralPath $sessionRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($cleanupFailures.Count -gt 0) {
        throw "NinjaSlayer smoke cleanup failed. $($cleanupFailures -join ' | ')"
    }
}

if (-not $succeeded) { throw 'NinjaSlayer smoke did not complete.' }
