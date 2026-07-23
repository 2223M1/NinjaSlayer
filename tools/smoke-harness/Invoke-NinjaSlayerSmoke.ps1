[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory)][string]$CandidateRoot,
    [Parameter(Mandatory)][string]$TrustedRoot,
    [Parameter(Mandatory)][string]$GameRootDirectory,
    [Parameter(Mandatory)][string]$RitsuLibModDirectory,
    [Parameter(Mandatory)][string]$GodotExecutable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('FirstCombatRestart', 'FullAutoSlay')][string]$Mode = 'FirstCombatRestart',
    [ValidateRange(0, 7200)][int]$PhaseTimeoutSeconds = 0,
    [string]$Seed = 'NINJASLAYER_SMOKE_01',
    [string]$Repository = 'local',
    [string]$RunId = 'local'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run the real-game smoke launcher from elevated PowerShell so outbound traffic can be blocked.'
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
        $relative = [IO.Path]::GetRelativePath($Source, $directory.FullName)
        New-Item -ItemType Directory -Path (Join-Path $Destination $relative) -Force | Out-Null
    }
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse -Force) {
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
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
        [Parameter(Mandatory)][ValidateSet('Fresh', 'Resume', 'FullAutoSlay')][string]$Phase,
        [Parameter(Mandatory)][int]$ExpectedExitCode
    )

    $configuration = [ordered]@{
        CandidateSha = $CandidateSha.ToLowerInvariant()
        Seed = $Seed
        Phase = switch ($Phase) {
            'Fresh' { 0 }
            'Resume' { 1 }
            'FullAutoSlay' { 2 }
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
        @($TrustedRoot, '<TRUSTED>'),
        @($GameRootDirectory, '<GAME_ROOT>'),
        @($env:USERPROFILE, '<USER_PROFILE>')
    )) {
        $content = $content.Replace($replacement[0], $replacement[1], [StringComparison]::OrdinalIgnoreCase)
    }
    Set-Content -LiteralPath $Destination -Value $content -Encoding utf8
}

$CandidateRoot = Resolve-RequiredPath $CandidateRoot
$TrustedRoot = Resolve-RequiredPath $TrustedRoot
$GameRootDirectory = Resolve-RequiredPath $GameRootDirectory
$RitsuLibModDirectory = Resolve-RequiredPath $RitsuLibModDirectory
$GodotExecutable = Resolve-RequiredPath $GodotExecutable -Leaf
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

foreach ($required in @(
    (Join-Path $GameRootDirectory 'SlayTheSpire2.exe'),
    (Join-Path $GameRootDirectory 'SlayTheSpire2.pck'),
    (Join-Path $GameRootDirectory 'data_sts2_windows_x86_64\sts2.dll'),
    (Join-Path $RitsuLibModDirectory 'STS2-RitsuLib.dll')
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
$sessionRoot = Join-Path $temporaryRoot "NinjaSlayer-Smoke-$($CandidateSha.Substring(0, 12))-$([Guid]::NewGuid().ToString('N'))"
$isolatedGameRoot = Join-Path $sessionRoot 'game'
$appDataDirectory = Join-Path $sessionRoot 'appdata'
$localAppDataDirectory = Join-Path $sessionRoot 'localappdata'
$packageDirectory = Join-Path $sessionRoot 'package\NinjaSlayer'
$driverOutput = Join-Path $sessionRoot 'driver'
$configurationPath = Join-Path $sessionRoot 'smoke-config.json'
$checkpointPath = Join-Path $OutputDirectory 'checkpoints.jsonl'
$firewallRules = [Collections.Generic.List[string]]::new()
$succeeded = $false
$effectivePhaseTimeoutSeconds = if ($PhaseTimeoutSeconds -gt 0) {
    $PhaseTimeoutSeconds
} elseif ($Mode -eq 'FullAutoSlay') {
    3600
} else {
    300
}

try {
    New-Item -ItemType Directory -Path $sessionRoot, $appDataDirectory, $localAppDataDirectory, $packageDirectory, $driverOutput -Force | Out-Null

    & dotnet build (Join-Path $CandidateRoot 'NinjaSlayer.csproj') -c Release -t:PackageMod -v:minimal `
        -p:Sts2Dir=$GameRootDirectory `
        -p:Sts2DataDir=(Join-Path $GameRootDirectory 'data_sts2_windows_x86_64') `
        -p:GodotExe=$GodotExecutable `
        -p:PostBuildModDir="$packageDirectory\"
    if ($LASTEXITCODE -ne 0) { throw 'Candidate PackageMod failed.' }

    $candidateAssembly = Join-Path $packageDirectory 'NinjaSlayer.dll'
    & dotnet build (Join-Path $TrustedRoot 'tools\smoke-harness\NinjaSlayer.SmokeDriver\NinjaSlayer.SmokeDriver.csproj') `
        -c Release -v:minimal -o $driverOutput `
        -p:Sts2DataDir=(Join-Path $GameRootDirectory 'data_sts2_windows_x86_64') `
        -p:NinjaSlayerAssemblyPath=$candidateAssembly `
        -p:RitsuLibAssemblyPath=(Join-Path $RitsuLibModDirectory 'STS2-RitsuLib.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Trusted SmokeDriver build failed.' }

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
    Copy-Item -LiteralPath $packageDirectory -Destination (Join-Path $modsDirectory 'NinjaSlayer') -Recurse
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
    foreach ($program in @($gameExecutable, (Join-Path $isolatedGameRoot 'crashpad_handler.exe'))) {
        if (Test-Path -LiteralPath $program -PathType Leaf) {
            $rule = "NinjaSlayer-Smoke-$([Guid]::NewGuid().ToString('N'))"
            New-NetFirewallRule -DisplayName $rule -Direction Outbound -Action Block -Program $program | Out-Null
            $firewallRules.Add($rule)
        }
    }

    if ($Mode -eq 'FullAutoSlay') {
        Invoke-SmokePhase -Phase FullAutoSlay -ExpectedExitCode 0
    }
    else {
        Invoke-SmokePhase -Phase Fresh -ExpectedExitCode 20
        Invoke-SmokePhase -Phase Resume -ExpectedExitCode 0
    }

    $checkpoints = @(Get-Content -LiteralPath $checkpointPath | ForEach-Object { $_ | ConvertFrom-Json })
    $requiredCheckpoints = if ($Mode -eq 'FullAutoSlay') {
        @('full-autoslay.starting', 'full-autoslay.runtime-idle', 'full-autoslay.completed')
    }
    else {
        @('prepared.created', 'prepared.lifecycle-cleared', 'x-attack.nonlethal-completed', 'finisher.completed', 'fresh.saved', 'resume.loaded', 'resume.completed')
    }
    $missing = @($requiredCheckpoints | Where-Object { $_ -notin $checkpoints.Name })
    if ($missing.Count -gt 0 -or @($checkpoints | Where-Object Status -ne 'passed').Count -gt 0) {
        throw "Smoke checkpoints were incomplete or failed: $($missing -join ', ')"
    }

    $gameVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $GameRootDirectory 'data_sts2_windows_x86_64\sts2.dll')).Version.ToString()
    [ordered]@{
        schemaVersion = 1
        candidateSha = $CandidateSha.ToLowerInvariant()
        result = 'passed'
        gameAssemblyVersion = $gameVersion
        ritsuLibVersion = '0.4.62'
        mode = if ($Mode -eq 'FullAutoSlay') { 'singleplayer-full-autoslay' } else { 'singleplayer-first-combat-restart' }
        repository = $Repository
        runId = $RunId
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'attestation.json') -Encoding utf8
    $succeeded = $true
}
finally {
    Stop-SmokeProcesses -Root $isolatedGameRoot
    foreach ($rule in $firewallRules) {
        Remove-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
    }
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
    Remove-Item -LiteralPath $sessionRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $succeeded) { throw 'NinjaSlayer smoke did not complete.' }
