#Requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$Channel,
    [Parameter(Mandatory)][string]$NinjaSlayerAssemblyPath,
    [Parameter(Mandatory)][string]$Sts2DataDir,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceRevision,
    [Parameter(Mandatory)][string]$GodotPath,
    [Parameter(Mandatory)][string]$DotnetRoot,
    [Parameter(Mandatory)][string]$LogPath
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
. "$repository/.github/scripts/compatibility.ps1"
$manifest = Read-NinjaSlayerCompatibility -Path "$repository/eng/compatibility.json"
$hostMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath "$Sts2DataDir/sts2.dll"
$resolvedHost = Resolve-NinjaSlayerCompatibilityHost -Manifest $manifest -ModuleMvid $hostMvid
if ($resolvedHost.Channel -cne $Channel) { throw "Host belongs to $($resolvedHost.Channel), not $Channel." }
$product = (Resolve-Path -LiteralPath $NinjaSlayerAssemblyPath).Path
dotnet build "$PSScriptRoot/NinjaSlayer.OrbContractTests.csproj" -c Debug -v:minimal `
    "-p:NinjaSlayerHostChannel=$Channel" "-p:Sts2DataDir=$Sts2DataDir" "-p:NinjaSlayerAssemblyPath=$product"
if ($LASTEXITCODE -ne 0) { throw "Contract build failed: $LASTEXITCODE" }
$previous = @{}
$environment = @{
    DOTNET_ROOT = $DotnetRoot
    DOTNET_ROOT_X64 = $DotnetRoot
    NINJASLAYER_CONTRACT_EXPECTED_SOURCE_REVISION = $SourceRevision
    NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY = $product
    NINJASLAYER_CONTRACT_HOST_MVID = $hostMvid
}
try {
    foreach ($name in $environment.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
    }
    & $GodotPath --headless --path $PSScriptRoot --quit-after 1500 *> $LogPath
    $contractExit = $LASTEXITCODE
    Get-Content -LiteralPath $LogPath
    if ($contractExit -ne 0 -or !(Select-String -LiteralPath $LogPath -SimpleMatch 'NinjaSlayer orb product contracts passed.')) {
        throw "Orb contracts failed or timed out. Exit: $contractExit. Log: $LogPath"
    }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
