#Requires -Version 7.0
#Requires -PSEdition Core
param([ValidateSet('baseline','ritsu','ninja','full','no-ninja')][string]$Group='baseline', [string]$Attempt='01')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$evidence = "$repo/build/save-attribution-20260916"
$outDir = "$evidence/$Group-$Attempt"
if (Test-Path -LiteralPath $outDir) { throw "Output already exists: $outDir" }
$gameRoot = "$outDir/game"
$sourceGame = 'C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2'
New-Item -ItemType Directory -Force $gameRoot | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $sourceGame -File) {
    if ($file.Extension -eq '.exe') { Copy-Item -LiteralPath $file.FullName -Destination "$gameRoot/$($file.Name)" }
    else { New-Item -ItemType HardLink -Path "$gameRoot/$($file.Name)" -Target $file.FullName | Out-Null }
}
foreach ($subdir in @('data_sts2_windows_x86_64','controller_config')) {
    foreach ($file in Get-ChildItem -LiteralPath "$sourceGame/$subdir" -Recurse -File) {
        $target = Join-Path $gameRoot ([IO.Path]::GetRelativePath($sourceGame,$file.FullName))
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null
    }
}
if (!(Test-Path "$gameRoot/steam_appid.txt")) { '2868840' | Set-Content "$gameRoot/steam_appid.txt" }
$probeDir = "$gameRoot/mods/SaveProbe"
New-Item -ItemType Directory -Force $probeDir | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net9.0/SaveProbe.dll","$PSScriptRoot/SaveProbe.json" -Destination $probeDir
$manifests = Get-Content "$evidence/installed-manifests.json" -Raw | ConvertFrom-Json
$ids = @('SaveProbe')
if ($Group -ne 'baseline') { $ids += 'STS2-RitsuLib' }
if ($Group -in @('ninja','full')) { $ids += 'NinjaSlayer' }
if ($Group -in @('full','no-ninja')) { $ids += @('BaseLib','Flagellant','LexNinja2','Narrator','LieRenTVmod') }
foreach ($id in $ids | Where-Object { $_ -ne 'SaveProbe' }) {
    $manifest = $manifests | Where-Object id -EQ $id | Select-Object -First 1
    if ($id -eq 'Narrator') { $manifest = $manifests | Where-Object { $_.id -eq $id -and $_.path -like '*3748718202*' } | Select-Object -First 1 }
    if (!$manifest) { throw "Missing manifest: $id" }
    Copy-Item -LiteralPath (Split-Path $manifest.path) -Destination "$gameRoot/mods/$id" -Recurse
}
$steamUser32 = (Get-ItemProperty 'HKCU:/Software/Valve/Steam/ActiveProcess').ActiveUser
if (!$steamUser32) { throw 'No active Steam user; real callback experiment unavailable.' }
$steamUser64 = [uint64]76561197960265728 + [uint64]$steamUser32
$env:APPDATA = "$outDir/appdata"
$env:LOCALAPPDATA = "$outDir/localappdata"
$settingsDir = "$env:APPDATA/SlayTheSpire2/steam/$steamUser64"
New-Item -ItemType Directory -Force $settingsDir,$env:LOCALAPPDATA | Out-Null
$modList = @(@{id='SaveProbe';is_enabled=$true;source='mods_directory'})
foreach ($id in $ids | Where-Object { $_ -ne 'SaveProbe' }) { $modList += @{id=$id;is_enabled=$true;source='mods_directory'} }
foreach ($id in ($manifests.id + 'ModLaunchManager' | Sort-Object -Unique)) { $modList += @{id=$id;is_enabled=$false;source='steam_workshop'} }
@{
 schema_version=5; fps_limit=60;language='zhs';fullscreen=$false;target_display=0
 window_position=@{X=-3000;Y=100};window_size=@{X=1280;Y=720};skip_intro_logo=$true;seen_ea_disclaimer=$true
 limit_fps_in_background=$false;volume_master=0;mod_settings=@{mods_enabled=$true;mod_list=$modList}
} | ConvertTo-Json -Depth 7 | Set-Content "$settingsDir/settings.save" -Encoding utf8
$scope = "$Group-$([Guid]::NewGuid().ToString('N'))"
@{group=$Group;scope="ninjaslayer-diagnostics/$scope/";mods=$ids;sourceRevision=(git -C $repo rev-parse HEAD).Trim();started=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content "$outDir/run.json"
Get-ChildItem "$gameRoot/mods" -Recurse -File -Filter '*.dll' | ForEach-Object {
 @{path=[IO.Path]::GetRelativePath($gameRoot,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
} | ConvertTo-Json | Set-Content "$outDir/assemblies.json"
$arguments = @('--verbose','--force-steam=on','--windowed','--resolution','1280x720','--position','-3000,100','--rendering-method','forward_plus','--rendering-driver','vulkan',"--save-probe-output=$outDir","--save-probe-scope=$scope")
if ($Group -in @('ninja','full')) { $arguments += '--save-probe-ninja' }
$game = Start-Process -FilePath "$gameRoot/SlayTheSpire2.exe" -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$outDir/stdout.log" -RedirectStandardError "$outDir/stderr.log"
try {
 $deadline = [DateTime]::UtcNow.AddMinutes(6)
 while (!$game.WaitForExit(500) -and [DateTime]::UtcNow -lt $deadline) { }
 if (!$game.HasExited) { throw 'Probe exceeded six minutes.' }
 @{exitCode=$game.ExitCode;finished=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content "$outDir/exit.json"
 if ($game.ExitCode -ne 0) { throw "Probe failed: $($game.ExitCode)" }
 Write-Output "Probe finished: $outDir"
} finally { if (!$game.HasExited) { $game.Kill($true) } }
