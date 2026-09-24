#Requires -Version 7.0
#Requires -PSEdition Core
param([string]$Root='', [Parameter(Mandatory)][string]$Assembly, [Parameter(Mandatory)][string]$Pack)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$Root) { $Root=Join-Path $repo 'build/naraku-event/runtime' }
$Root=[IO.Path]::GetFullPath($Root)
if (!$Root.StartsWith($repo+[IO.Path]::DirectorySeparatorChar)) { throw 'Preview must stay inside the repository' }
$stage=Join-Path $Root 'isolated-game'
$game='C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
$ritsu='C:\Program Files (x86)\Steam\steamapps\workshop\content\2868840\3747602295'
function Link([string]$from,[string]$to) {
 New-Item -ItemType Directory -Path (Split-Path $to) -Force | Out-Null
 if (!(Test-Path -LiteralPath $to)) {New-Item -ItemType HardLink -Path $to -Target $from | Out-Null}
}
foreach($f in Get-ChildItem -LiteralPath $game -File) {
 $to=Join-Path $stage $f.Name
 if($f.Extension -eq '.exe') {New-Item -ItemType Directory -Path $stage -Force | Out-Null;Copy-Item -LiteralPath $f.FullName -Destination $to -Force}
 else {Link $f.FullName $to}
}
foreach($folder in @('data_sts2_windows_x86_64','controller_config')) {
 foreach($f in Get-ChildItem -LiteralPath (Join-Path $game $folder) -Recurse -File) {Link $f.FullName (Join-Path $stage ([IO.Path]::GetRelativePath($game,$f.FullName)))}
}
foreach($f in Get-ChildItem -LiteralPath $ritsu -Recurse -File) {Link $f.FullName (Join-Path $stage ('mods\STS2-RitsuLib\'+[IO.Path]::GetRelativePath($ritsu,$f.FullName)))}
$mod=Join-Path $stage 'mods/NinjaSlayer';New-Item -ItemType Directory -Path $mod -Force | Out-Null
Copy-Item -LiteralPath $Assembly -Destination $mod -Force
Copy-Item -LiteralPath $Pack -Destination $mod -Force
$manifest=Get-Content -LiteralPath (Join-Path $game 'mods/NinjaSlayer/NinjaSlayer.json') -Raw | ConvertFrom-Json
$manifest.dependencies+=@{id='Box2D.NET'}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $mod 'NinjaSlayer.json') -Encoding utf8
$box=Join-Path $stage 'mods/Box2D.NET';New-Item -ItemType Directory -Path $box -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $game 'mods/NinjaSlayer/Box2D.NET.dll') -Destination $box -Force
@{id='Box2D.NET';name='Isolated dependency preload';author='Local QA';version='1.0.0';has_dll=$true;has_pck=$false;affects_gameplay=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $box 'Box2D.NET.json') -Encoding utf8
$probe=Join-Path $stage 'mods/NarakuEventProbe';New-Item -ItemType Directory -Path $probe -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tools/smoke-harness/NinjaSlayer.NarakuEventProbe/bin/Release/net9.0/NarakuEventProbe.dll') -Destination $probe -Force
@{id='NarakuEventProbe';name='Isolated Naraku event QA';author='Local QA';version='1.0.0';has_dll=$true;has_pck=$false;affects_gameplay=$false;dependencies=@(@{id='NinjaSlayer'})} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $probe 'NarakuEventProbe.json') -Encoding utf8
$output=Join-Path $Root ('qa-'+(Get-Date -Format 'yyyyMMdd-HHmmss'));New-Item -ItemType Directory -Path $output -Force | Out-Null
$oldApp=$env:APPDATA;$oldLocal=$env:LOCALAPPDATA
$env:APPDATA=Join-Path $Root 'appdata';$env:LOCALAPPDATA=Join-Path $Root 'localappdata'
$settings=Join-Path $env:APPDATA 'SlayTheSpire2/default/1'
New-Item -ItemType Directory -Path (Join-Path $settings 'profile1/saves'),$env:LOCALAPPDATA -Force | Out-Null
@{schema_version=5;fps_limit=60;aspect_ratio='sixteen_by_nine';language='zhs';fullscreen=$false;target_display=0;window_position=@{X=-5000;Y=-5000};window_size=@{X=1920;Y=1080};skip_intro_logo=$true;seen_ea_disclaimer=$true;limit_fps_in_background=$false;volume_master=0.0;mod_settings=@{mods_enabled=$true;mod_list=@(@{id='STS2-RitsuLib';is_enabled=$true;source='mods_directory'},@{id='Box2D.NET';is_enabled=$true;source='mods_directory'},@{id='NinjaSlayer';is_enabled=$true;source='mods_directory'},@{id='NarakuEventProbe';is_enabled=$true;source='mods_directory'})}} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $settings 'settings.save') -Encoding utf8
@{schema_version=2;mute_in_background=$false;upload_data=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $settings 'profile1/saves/prefs.save') -Encoding utf8
$exe=Join-Path $stage 'SlayTheSpire2.exe'
& ssh localadmin "if (!(Get-NetFirewallRule -DisplayName 'NinjaSlayer-NarakuEventProbe' -ErrorAction SilentlyContinue)) { New-NetFirewallRule -DisplayName 'NinjaSlayer-NarakuEventProbe' -Direction Outbound -Action Block -Program '$exe' | Out-Null }"
if ($LASTEXITCODE -ne 0) {throw 'Preview network isolation failed'}
try {
 $p=Start-Process -FilePath $exe -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'stdout.log') -RedirectStandardError (Join-Path $output 'stderr.log') -ArgumentList @('--force-steam=off','--windowed','--resolution','1920x1080','--rendering-method','gl_compatibility','--rendering-driver','opengl3',('--naraku-output='+$output.Replace('\','/')))
 Write-Output ('Preview PID '+$p.Id+' output '+$output)
 if($p.WaitForExit(50000)){Write-Output ('Exit '+$p.ExitCode)}
}finally{$env:APPDATA=$oldApp;$env:LOCALAPPDATA=$oldLocal}
