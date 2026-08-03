#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [string]$GameExe = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $GameExe)) {
    throw "Slay the Spire 2 executable not found: $GameExe"
}

$logPath = Join-Path $env:TEMP 'ninjaslayer-softbody-gpu-sentinel.log'
$arguments = @(
    '--path', $root,
    '--scene', 'res://tools/gpu-sentinel/BossSoftBodyGpuSentinel.tscn',
    '--audio-driver', 'Dummy',
    '--rendering-method', 'mobile',
    '--resolution', '160x160',
    '--position=-10000,-10000',
    '--disable-vsync',
    '--fixed-fps', '60',
    '--log-file', $logPath
)
$process = Start-Process -FilePath $GameExe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Boss soft-body GPU sentinel failed with exit code $($process.ExitCode)."
}

$result = Get-Content -LiteralPath $logPath | Select-String -Pattern 'GPU_SENTINEL_(PASS|FAIL)' | Select-Object -Last 1
if ($null -eq $result -or $result.Line -notmatch 'GPU_SENTINEL_PASS') {
    throw "Boss soft-body GPU sentinel did not report a passing pixel comparison. See $logPath."
}

$result.Line
