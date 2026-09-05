#Requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$Channel,
    [Parameter(Mandatory)][string]$NinjaSlayerAssemblyPath,
    [Parameter(Mandatory)][string]$Sts2DataDir,
    [Parameter(Mandatory)][string]$HostPack,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceRevision,
    [Parameter(Mandatory)][string]$GodotPath,
    [Parameter(Mandatory)][string]$DotnetRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1024, 65535)][int]$Port = 19481
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
. "$repository/.github/scripts/compatibility.ps1"
$manifest = Read-NinjaSlayerCompatibility -Path "$repository/eng/compatibility.json"
$hostMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath "$Sts2DataDir/sts2.dll"
$resolvedHost = Resolve-NinjaSlayerCompatibilityHost -Manifest $manifest -ModuleMvid $hostMvid
if ($resolvedHost.Channel -cne $Channel) { throw "Host belongs to $($resolvedHost.Channel), not $Channel." }
$product = (Resolve-Path -LiteralPath $NinjaSlayerAssemblyPath).Path
$pack = (Resolve-Path -LiteralPath $HostPack).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$buildArguments = @('build', "$PSScriptRoot/NinjaSlayer.OrbContractTests.csproj", '-c', 'Debug', '-v:minimal',
    "-p:NinjaSlayerHostChannel=$Channel", "-p:Sts2DataDir=$Sts2DataDir", "-p:NinjaSlayerAssemblyPath=$product")
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer contract build failed.' }
$environment = @{
    DOTNET_ROOT = $DotnetRoot
    DOTNET_ROOT_X64 = $DotnetRoot
    NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY = $product
    NINJASLAYER_CONTRACT_EXPECTED_SOURCE_REVISION = $SourceRevision
    NINJASLAYER_CONTRACT_HOST_MVID = $hostMvid
    NINJASLAYER_CONTRACT_HOST_PACK = $pack
    NINJASLAYER_MULTIPLAYER_DIRECTORY = $output
    NINJASLAYER_MULTIPLAYER_PORT = [string]$Port
}
$processes = @()
try {
    foreach ($role in @('host', 'client')) {
        $start = [Diagnostics.ProcessStartInfo]::new($GodotPath)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @('--headless', '--path', $PSScriptRoot, '--quit-after', '10000')) {
            $start.ArgumentList.Add($argument)
        }
        foreach ($key in $environment.Keys) { $start.Environment[$key] = $environment[$key] }
        $start.Environment['NINJASLAYER_MULTIPLAYER_ROLE'] = $role
        $start.Environment['APPDATA'] = Join-Path $output "$role-profile"
        $start.Environment['LOCALAPPDATA'] = $start.Environment['APPDATA']
        $process = [Diagnostics.Process]::Start($start)
        $processes += [pscustomobject]@{ Role = $role; Process = $process
            Stdout = $process.StandardOutput.ReadToEndAsync(); Stderr = $process.StandardError.ReadToEndAsync() }
    }
    foreach ($entry in $processes) {
        if (!$entry.Process.WaitForExit(60000)) { throw "$($entry.Role) multiplayer runner timed out." }
        $text = $entry.Stdout.GetAwaiter().GetResult()
        if ($entry.Process.ExitCode -ne 0 -or !$text.Contains('NinjaSlayer multiplayer product contracts passed.')) {
            throw "$($entry.Role) multiplayer contracts failed; inspect $output."
        }
    }
    Write-Output "Both native ENet clients passed. Evidence: $output"
}
finally {
    foreach ($entry in $processes) {
        if (!$entry.Process.HasExited) { $entry.Process.Kill($true); $entry.Process.WaitForExit() }
        [IO.File]::WriteAllText((Join-Path $output "$($entry.Role).log"), $entry.Stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $output "$($entry.Role)-errors.log"), $entry.Stderr.GetAwaiter().GetResult())
        $entry.Process.Dispose()
    }
}
