#Requires -Version 7.0
#Requires -PSEdition Core
[CmdletBinding()]
param(
    [string]$Script = (Join-Path $PSScriptRoot 'theater/promo-fight.json'),
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$FromCue,
    [string]$ToCue,
    [ValidateRange(1, 10)][int]$Repeat = 1,
    [switch]$Rehearsal,
    [switch]$SkipBuild,
    [switch]$FullMix,
    [string]$GameRoot = 'C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2',
    [string]$RitsuLibDirectory = 'C:/Program Files (x86)/Steam/steamapps/workshop/content/2868840/3747602295',
    [string]$StableDataDirectory,
    [string]$PreviewDataDirectory = 'C:/Users/theon/Documents/NinjaSlayer/NinjaSlayer/build/aim-validation/reference/preview',
    [string]$ResourcePack,
    [string]$DebugAudioDirectory,
    [ValidateSet('forward_plus', 'gl_compatibility')][string]$Renderer = 'forward_plus',
    [string]$Python = 'C:/Users/theon/AppData/Local/Python/bin/python.exe'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$scriptPath = (Resolve-Path -LiteralPath $Script).Path
$story = Get-Content -LiteralPath $scriptPath -Raw | ConvertFrom-Json
$revision = (git -C $repo rev-parse HEAD).Trim()
. (Join-Path $repo '.github/scripts/compatibility.ps1')
$manifest = Read-NinjaSlayerCompatibility -Path (Join-Path $repo 'eng/compatibility.json')
if (!$StableDataDirectory) {
    $StableDataDirectory = Join-Path (Split-Path $repo) ('.sts2build/hosts/stable-' + $manifest.channels.stable.gameApiVersion)
}
$data = Join-Path $GameRoot 'data_sts2_windows_x86_64'
$mvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath (Join-Path $data 'sts2.dll')
$channel = @($manifest.channels.PSObject.Properties | Where-Object { $_.Value.hostContract.moduleMvid -eq $mvid })
if ($channel.Count -ne 1) { throw "Unsupported recording host: $mvid" }
$channelName = $channel[0].Name
$build = Join-Path $repo "build/theater/compile/$channelName"
$assembly = Join-Path $build 'Debug/NinjaSlayer.dll'
if (!$ResourcePack) { $ResourcePack = Join-Path $repo 'build/balance-v0216/NinjaSlayer.pck' }
$ResourcePack = (Resolve-Path -LiteralPath $ResourcePack).Path
function Invoke-Checked([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}
if (!$SkipBuild) {
    foreach ($variant in @('stable', 'preview')) {
        $references = if ($variant -eq 'stable') { $StableDataDirectory } else { $PreviewDataDirectory }
        if ((Get-NinjaSlayerGameModuleMvid -AssemblyPath "$references/sts2.dll") -ne $manifest.channels.$variant.hostContract.moduleMvid) {
            throw "Wrong $variant references: $references"
        }
        Invoke-Checked dotnet @('build', "$repo/NinjaSlayer.csproj", '-c', 'Debug', '--nologo', '-v:quiet',
            "-p:NinjaSlayerHostChannel=$variant", "-p:Sts2DataDir=$references", "-p:RepositoryCommit=$revision", "-p:NinjaSlayerIsolatedOutputRoot=$repo/build/theater/compile/$variant/")
    }
    Invoke-Checked dotnet @('build', "$repo/tools/loader/NinjaSlayer.Loader.csproj", '-c', 'Release', '--nologo', '-v:quiet',
        "-p:Sts2DataDir=$StableDataDirectory", '-o', "$repo/build/theater/loader")
    Invoke-Checked dotnet @('build', "$PSScriptRoot/NinjaSlayer.SmokeDriver/NinjaSlayer.SmokeDriver.csproj", '-c', 'Debug', '--nologo', '-v:quiet',
        "-p:NinjaSlayerHostChannel=$channelName", "-p:Sts2DataDir=$data", "-p:NinjaSlayerAssemblyPath=$assembly")
    Invoke-Checked dotnet @('build', "$PSScriptRoot/NinjaSlayer.BackgroundGame/NinjaSlayer.BackgroundGame.csproj", '-c', 'Release', '--nologo', '-v:quiet')
    Invoke-Checked dotnet @('build', "$PSScriptRoot/NinjaSlayer.AudioCapture/NinjaSlayer.AudioCapture.csproj", '-c', 'Release', '--nologo', '-v:quiet')
}
if (!(Test-Path -LiteralPath $assembly)) { throw "Build is missing: $assembly" }
$game = Join-Path $repo "build/theater/game-$channelName"
if (!(Test-Path -LiteralPath "$game/SlayTheSpire2.exe")) {
    New-Item -ItemType Directory -Path $game -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $GameRoot -File) {
        if ($file.Extension -eq '.exe') { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $game $file.Name) }
        else { New-Item -ItemType HardLink -Path (Join-Path $game $file.Name) -Target $file.FullName | Out-Null }
    }
    foreach ($folder in @('data_sts2_windows_x86_64', 'controller_config')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $GameRoot $folder) -Recurse -File) {
            $destination = Join-Path $game ([IO.Path]::GetRelativePath($GameRoot, $file.FullName))
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            New-Item -ItemType HardLink -Path $destination -Target $file.FullName | Out-Null
        }
    }
}
$mods = Join-Path $game 'mods'
New-Item -ItemType Directory -Path "$mods/NinjaSlayer", "$mods/NinjaSlayer-SmokeDriver", "$mods/STS2-RitsuLib" -Force | Out-Null
Copy-Item -Path "$RitsuLibDirectory/*" -Destination "$mods/STS2-RitsuLib" -Recurse -Force
# Use the production loader so dependency discovery matches the installed mod.
Copy-Item -LiteralPath "$repo/build/theater/loader/NinjaSlayer.Loader.dll" -Destination "$mods/NinjaSlayer/NinjaSlayer.dll" -Force
$variants = foreach ($variant in @('stable', 'preview')) {
    $profile = $manifest.channels.$variant
    $variantFolder = "$mods/NinjaSlayer/lib/$($profile.gameApiVersion)"
    New-Item -ItemType Directory -Path $variantFolder -Force | Out-Null
    Copy-Item -LiteralPath "$repo/build/theater/compile/$variant/Debug/NinjaSlayer.dll" -Destination "$variantFolder/NinjaSlayer.dll" -Force
    $profile.gameApiVersion | Set-Content -LiteralPath "$variantFolder/compat-target.txt" -Encoding utf8
    @{
        channel=$variant; gameApiVersion=$profile.gameApiVersion; moduleMvid=$profile.hostContract.moduleMvid
        directory="lib/$($profile.gameApiVersion)"; assembly='NinjaSlayer.dll'
        sha256=(Get-FileHash -LiteralPath "$variantFolder/NinjaSlayer.dll").Hash.ToLowerInvariant()
    }
}
@{schemaVersion=1; variants=@($variants)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$mods/NinjaSlayer/ninjaslayer-variants.manifest" -Encoding utf8
Copy-Item -LiteralPath $ResourcePack -Destination "$mods/NinjaSlayer/NinjaSlayer.pck" -Force
Copy-Item -LiteralPath "$build/Debug/Box2D.NET.dll", "$repo/LICENSE.Box2D.NET.txt" -Destination "$mods/NinjaSlayer" -Force
Invoke-Checked node @("$repo/tools/package-contract.mjs", 'generate', '--source', "$repo/NinjaSlayer.json",
    '--destination', "$mods/NinjaSlayer/NinjaSlayer.json", '--version', '0.0.1-theater',
    '--min-game-version', $channel[0].Value.gameApiVersion, '--ritsulib-version', $manifest.ritsuLibVersion)
Copy-Item -LiteralPath "$PSScriptRoot/NinjaSlayer.SmokeDriver/bin/Debug/net9.0/NinjaSlayer-SmokeDriver.dll",
    "$PSScriptRoot/NinjaSlayer.SmokeDriver/NinjaSlayer-SmokeDriver.json" -Destination "$mods/NinjaSlayer-SmokeDriver" -Force
$rule = "NinjaSlayer-Theater-$channelName"
$exe = [IO.Path]::GetFullPath("$game/SlayTheSpire2.exe")
& ssh localadmin "if (!(Get-NetFirewallRule -DisplayName '$rule' -ErrorAction SilentlyContinue)) { New-NetFirewallRule -DisplayName '$rule' -Direction Outbound -Action Block -Program '$exe' | Out-Null }"
if ($LASTEXITCODE -ne 0) { throw 'Recording network isolation failed.' }
for ($iteration = 1; $iteration -le $Repeat; $iteration++) {
    $destination = if ($Repeat -eq 1) { $output } else { Join-Path $output ('take-{0:D2}' -f $iteration) }
    if (Test-Path -LiteralPath $destination) { throw "Recording output already exists: $destination" }
    New-Item -ItemType Directory -Path $destination | Out-Null
    $configuration = [ordered]@{
        CandidateSha = ((git -C $repo rev-parse HEAD).Trim()); Seed = $story.seed; Phase = 11
        CheckpointPath = "$destination/checkpoints.jsonl"; AutoSlayLogPath = "$destination/autoslay.log"
        FailureScreenshotPath = "$destination/failure.png"; ActionPreviewDirectory = $destination
        TheaterScriptPath = $scriptPath; TheaterFromCue = $FromCue; TheaterToCue = $ToCue; TheaterRehearsal = [bool]$Rehearsal
    }
    if (!$FromCue) { $configuration.TheaterFromCue = $null }
    if (!$ToCue) { $configuration.TheaterToCue = $null }
    $configuration | ConvertTo-Json | Set-Content -LiteralPath "$destination/config.json" -Encoding utf8
    $oldAppData = $env:APPDATA; $oldLocalAppData = $env:LOCALAPPDATA
    $env:APPDATA = "$destination/appdata"; $env:LOCALAPPDATA = "$destination/localappdata"
    $settings = "$env:APPDATA/SlayTheSpire2/default/1"
    New-Item -ItemType Directory -Path "$settings/profile1/saves", $env:LOCALAPPDATA -Force | Out-Null
    @{
        schema_version=5; fps_limit=60; language='zhs'; fullscreen=$false; target_display=0
        window_position=@{X=0;Y=0}; window_size=@{X=1920;Y=1080}; skip_intro_logo=$true
        seen_ea_disclaimer=$true; limit_fps_in_background=$false
        volume_master=.7; volume_sfx=.7; volume_bgm=$(if ($FullMix) { .7 } else { 0.0 }); volume_ambience=$(if ($FullMix) { .7 } else { 0.0 })
        mod_settings=@{mods_enabled=$true; mod_list=@(
            @{id='STS2-RitsuLib';is_enabled=$true;source='mods_directory'},
            @{id='NinjaSlayer';is_enabled=$true;source='mods_directory'},
            @{id='NinjaSlayer-SmokeDriver';is_enabled=$true;source='mods_directory'})}
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$settings/settings.save" -Encoding utf8
    @{schema_version=2;mute_in_background=$false;upload_data=$false} | ConvertTo-Json | Set-Content -LiteralPath "$settings/profile1/saves/prefs.save" -Encoding utf8
    $capture = $null; $process = $null
    try {
        $capture = Start-Process "$PSScriptRoot/NinjaSlayer.AudioCapture/bin/Release/net9.0-windows/AudioCapture.exe" -ArgumentList @($destination,'process') -WindowStyle Hidden -PassThru -RedirectStandardError "$destination/audio-error.log"
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (!(Test-Path -LiteralPath "$destination/audio-ready") -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (!(Test-Path -LiteralPath "$destination/audio-ready")) { throw 'Audio capture failed to start.' }
        $arguments = @($exe, '--verbose', '--force-steam=off', '--windowed', '--resolution', '1920x1080',
            '--disable-vsync', '--rendering-method', $Renderer, '--rendering-driver',
            $(if ($Renderer -eq 'gl_compatibility') { 'opengl3' } else { 'vulkan' }), "--ninjaslayer-smoke-config=$destination/config.json")
        $process = Start-Process "$PSScriptRoot/NinjaSlayer.BackgroundGame/bin/Release/net9.0-windows/BackgroundGame.exe" -ArgumentList $arguments -WorkingDirectory $game -WindowStyle Hidden -PassThru -RedirectStandardOutput "$destination/stdout.log" -RedirectStandardError "$destination/stderr.log"
        $deadline = [DateTime]::UtcNow.AddMinutes(5)
        while (!$process.WaitForExit(500) -and [DateTime]::UtcNow -lt $deadline) { }
        if (!$process.HasExited) { throw 'Theater exceeded five minutes.' }
        if ($process.ExitCode -ne 0) { throw "Theater failed: $($process.ExitCode). See $destination/checkpoints.jsonl" }
        if (!$capture.WaitForExit(10000)) { throw 'Audio capture did not stop.' }
        $audioArguments = @("$PSScriptRoot/sync_preview_audio.py", $destination, '--output', "$destination/theater.mp4")
        if ($DebugAudioDirectory) { $audioArguments += @('--debug-audio', $DebugAudioDirectory) }
        Invoke-Checked $Python $audioArguments
        $sync = Get-Content -LiteralPath "$destination/audio-sync.json" -Raw | ConvertFrom-Json
        $start = $sync.clockOffsetSeconds + $sync.playbackLatencySeconds
        $duration = (& ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "$destination/theater.mp4").Trim()
        Invoke-Checked ffmpeg @('-y','-v','error','-ss', $start.ToString('F9',[Globalization.CultureInfo]::InvariantCulture),
            '-i', "$destination/audio.wav", '-t', $duration, '-c:a', 'pcm_s24le', "$destination/theater-audio.wav")
        @{
            hostChannel=$channelName; hostMvid=$mvid; renderer=$Renderer; scriptSha256=(Get-FileHash -LiteralPath $scriptPath).Hash
            assemblySha256=(Get-FileHash -LiteralPath $assembly).Hash; resourceSha256=(Get-FileHash -LiteralPath $ResourcePack).Hash
            driverSha256=(Get-FileHash -LiteralPath "$mods/NinjaSlayer-SmokeDriver/NinjaSlayer-SmokeDriver.dll").Hash
        } | ConvertTo-Json | Set-Content -LiteralPath "$destination/recording-build.json"
        Invoke-Checked node @("$PSScriptRoot/theater/verify-theater.mjs", $destination)
        Write-Output "Theater ready: $destination/theater.mp4"
    }
    finally {
        if ($null -ne $process -and !$process.HasExited) { $process.Kill($true) }
        if ($null -ne $capture -and !$capture.HasExited) { $capture.Kill($true) }
        $env:APPDATA = $oldAppData; $env:LOCALAPPDATA = $oldLocalAppData
    }
}
