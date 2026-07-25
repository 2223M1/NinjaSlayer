[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

try {
    $publisher = Join-Path $PSScriptRoot 'Publish-WorkshopQuickRelease.ps1'
    & $publisher -Confirm
}
catch {
    Write-Host ''
    Write-Host 'WORKSHOP-ONLY RELEASE FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ''
    Write-Host 'No GitHub operation was attempted. The window remains open for troubleshooting.' `
        -ForegroundColor Yellow
}
