[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$GameDirectory,

    [Parameter(Mandatory, Position = 1)]
    [ValidateSet('stable', 'preview')]
    [string]$Channel,

    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$arguments = @(
    'run',
    '--project', (Join-Path $PSScriptRoot 'host-contract/NinjaSlayer.HostContractCapture.csproj'),
    '--configuration', 'Release',
    '--no-launch-profile',
    '--',
    (Resolve-Path -LiteralPath $GameDirectory).Path,
    $Channel,
    '--repository-root', $repositoryRoot
)
if ($Apply) {
    $arguments += '--apply'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Host contract capture failed with exit code $LASTEXITCODE."
}
