param(
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$syncScript = Join-Path $scriptDir "_sync_tutorials.py"

$candidates = @()
$localPython = Join-Path $env:LOCALAPPDATA "Python\bin\python.exe"
if (Test-Path -LiteralPath $localPython) {
    $candidates += $localPython
}

$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if ($pythonCmd) {
    $candidates += $pythonCmd.Source
}

$pyCmd = Get-Command py -ErrorAction SilentlyContinue
if ($pyCmd) {
    $candidates += $pyCmd.Source
}

$python = $null
foreach ($candidate in $candidates | Select-Object -Unique) {
    try {
        $version = & $candidate --version 2>$null
        if ($LASTEXITCODE -eq 0 -and $version -match "Python") {
            $python = $candidate
            break
        }
    }
    catch {
        continue
    }
}

if (-not $python) {
    throw "Python was not found. Install Python 3 or update Docs/Sync-Sts2Tutorials.ps1 with the local python.exe path."
}

Push-Location $repoRoot
try {
    $args = @($syncScript)
    if ($DryRun) {
        $args += "--dry-run"
    }

    & $python @args
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
