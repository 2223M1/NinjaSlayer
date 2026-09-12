#Requires -Version 7.0
#Requires -PSEdition Core

param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$worker = Join-Path $repo 'Infrastructure/telemetry-worker'
$node = (Get-Command node -ErrorAction Stop).Source
$port = if ($env:NINJASLAYER_DASHBOARD_PORT) { [int]$env:NINJASLAYER_DASHBOARD_PORT } else { 4178 }
$url = "http://127.0.0.1:$port"
$running = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if (!$running) {
    if (!(Test-Path -LiteralPath (Join-Path $worker 'node_modules/wrangler/bin/wrangler.js'))) {
        throw "请先在 $worker 运行 npm ci。"
    }
    $logs = Join-Path $repo 'build/dashboard'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    Start-Process -FilePath $node -ArgumentList 'dashboard/server.mjs' -WorkingDirectory $worker -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logs 'server.log') -RedirectStandardError (Join-Path $logs 'server-error.log') | Out-Null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        if (Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { break }
    }
}
$view = Invoke-RestMethod "$url/api/view" -TimeoutSec 5
if ($view.application -ne 'NinjaSlayerDashboard') { throw "端口 $port 已被其他应用占用。" }
if (!$NoBrowser) { Start-Process $url }
Write-Output $url
