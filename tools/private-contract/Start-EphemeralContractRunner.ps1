[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$RegistrationToken,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$RunnerVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$RunnerArchiveSha256,

    [ValidateSet('Contract', 'Release', 'Smoke')]
    [string]$RunnerPurpose = 'Contract',

    [string]$RunnerArchivePath,

    [string]$RepositoryUrl = 'https://github.com/2223M1/NinjaSlayer',

    [string]$GameDataDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64',

    [string]$GameRootDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',

    [string]$RitsuLibModDirectory,

    [string]$RitsuLibPackageDirectory = (Join-Path $env:USERPROFILE '.nuget\packages\sts2.ritsulib\0.4.62'),

    [string]$GodotExecutable = 'C:\Program Files\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe',

    [string]$SpineExtensionDirectory = (Join-Path $PSScriptRoot '..\..\addons\spine\windows')
)

$ErrorActionPreference = 'Stop'

if ($RunnerPurpose -in @('Contract', 'Smoke') -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session so the $RunnerPurpose runner can enforce outbound firewall isolation."
}

if ($RunnerPurpose -eq 'Smoke') {
    $smokeInputs = @(
        (Join-Path $GameRootDirectory 'SlayTheSpire2.exe'),
        (Join-Path $GameRootDirectory 'SlayTheSpire2.pck')
    )
    if ([string]::IsNullOrWhiteSpace($RitsuLibModDirectory)) {
        $smokeInputs += (Join-Path $RitsuLibPackageDirectory 'lib\net9.0\STS2-RitsuLib.dll')
        $smokeInputs += (Join-Path $RitsuLibPackageDirectory 'contentFiles\any\any\mod_manifest.json')
    }
    else {
        $smokeInputs += (Join-Path $RitsuLibModDirectory 'STS2-RitsuLib.dll')
        $smokeInputs += (Join-Path $RitsuLibModDirectory 'mod_manifest.json')
    }
    foreach ($path in $smokeInputs) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing protected smoke input: $path"
        }
    }
}

$requiredReferences = @('sts2.dll', '0Harmony.dll', 'GodotSharp.dll')
foreach ($fileName in $requiredReferences) {
    $source = Join-Path $GameDataDirectory $fileName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing private contract reference: $source"
    }
}
if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot 4.5.1 Mono was not found at $GodotExecutable"
}

$requiredSpineFiles = @(
    'libspine_godot.windows.editor.x86_64.dll',
    'libspine_godot.windows.template_debug.x86_64.dll',
    'libspine_godot.windows.template_release.x86_64.dll'
)
if ($RunnerPurpose -eq 'Release') {
    foreach ($fileName in $requiredSpineFiles) {
        $source = Join-Path $SpineExtensionDirectory $fileName
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Missing Spine release input: $source"
        }
    }
}

$sessionId = [Guid]::NewGuid().ToString('N')
$purposeName = $RunnerPurpose.ToLowerInvariant()
$sessionRoot = Join-Path $env:TEMP "NinjaSlayer-${RunnerPurpose}Runner-$sessionId"
$runnerDirectory = Join-Path $sessionRoot 'runner'
$referenceDirectory = Join-Path $sessionRoot 'references'
$spineDirectory = Join-Path $sessionRoot 'spine'
$dotnetRuntimeDirectory = Join-Path $sessionRoot 'dotnet-runtime'
$workDirectory = Join-Path $sessionRoot 'work'
$ritsuLibSmokeDirectory = Join-Path $sessionRoot 'ritsulib-mod'
$archive = Join-Path $sessionRoot 'actions-runner.zip'
$runnerName = "ninjaslayer-$purposeName-$env:COMPUTERNAME-$($sessionId.Substring(0, 8))"
$runnerLabel = switch ($RunnerPurpose) {
    'Contract' { 'ninjaslayer-contract' }
    'Release' { 'ninjaslayer-release' }
    'Smoke' { 'ninjaslayer-smoke' }
}
$downloadUrl = "https://github.com/actions/runner/releases/download/v$RunnerVersion/actions-runner-win-x64-$RunnerVersion.zip"
$previousSts2DataDirectory = $env:STS2_DATA_DIR
$previousGodotExecutable = $env:GODOT_EXE
$previousContractDotnetRoot = $env:NINJASLAYER_CONTRACT_DOTNET_ROOT
$previousSpineDirectory = $env:NINJASLAYER_SPINE_DIR
$previousSmokeGameRoot = $env:NINJASLAYER_SMOKE_GAME_ROOT
$previousRitsuLibModDirectory = $env:NINJASLAYER_RITSULIB_MOD_DIR

function Copy-IsolatedDotnet9Runtime {
    param([Parameter(Mandatory)][string]$Destination)

    $dotnetExecutable = (Get-Command dotnet -ErrorAction Stop).Source
    $dotnetRoot = Split-Path -Parent $dotnetExecutable
    $fxr = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -Directory |
        Where-Object { $_.Name -match '^9\.' } |
        Sort-Object { [Version]$_.Name } -Descending |
        Select-Object -First 1
    $runtime = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory |
        Where-Object { $_.Name -match '^9\.' } |
        Sort-Object { [Version]$_.Name } -Descending |
        Select-Object -First 1
    if ($null -eq $fxr -or $null -eq $runtime) {
        throw 'The protected contract runner requires an installed .NET 9 runtime and hostfxr.'
    }

    $fxrRoot = Join-Path $Destination 'host\fxr'
    $sharedRoot = Join-Path $Destination 'shared\Microsoft.NETCore.App'
    New-Item -ItemType Directory -Path $fxrRoot, $sharedRoot -Force | Out-Null
    Copy-Item -LiteralPath $dotnetExecutable -Destination $Destination
    Copy-Item -LiteralPath $fxr.FullName -Destination $fxrRoot -Recurse
    Copy-Item -LiteralPath $runtime.FullName -Destination $sharedRoot -Recurse
}

function Remove-SessionDirectory {
    param([Parameter(Mandatory)][string]$Path)

    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            if (-not (Test-Path -LiteralPath $Path)) {
                return
            }

            Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
                ForEach-Object { $_.IsReadOnly = $false }
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 6) {
                throw "Could not remove ephemeral runner directory after $attempt attempts: $Path. $($_.Exception.Message)"
            }

            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

try {
    New-Item -ItemType Directory -Path $runnerDirectory, $referenceDirectory, $workDirectory -Force | Out-Null
    if ($RunnerPurpose -eq 'Smoke') {
        New-Item -ItemType Directory -Path $ritsuLibSmokeDirectory -Force | Out-Null
        if ([string]::IsNullOrWhiteSpace($RitsuLibModDirectory)) {
            Copy-Item -LiteralPath (Join-Path $RitsuLibPackageDirectory 'lib\net9.0\STS2-RitsuLib.dll') -Destination $ritsuLibSmokeDirectory
            Copy-Item -LiteralPath (Join-Path $RitsuLibPackageDirectory 'contentFiles\any\any\mod_manifest.json') -Destination $ritsuLibSmokeDirectory
            $viewer = Join-Path $RitsuLibPackageDirectory 'contentFiles\any\any\viewer'
            if (Test-Path -LiteralPath $viewer -PathType Container) {
                Copy-Item -LiteralPath $viewer -Destination $ritsuLibSmokeDirectory -Recurse
            }
        }
        else {
            Copy-Item -LiteralPath (Resolve-Path -LiteralPath $RitsuLibModDirectory).Path -Destination $ritsuLibSmokeDirectory -Recurse
            $nested = Join-Path $ritsuLibSmokeDirectory (Split-Path -Leaf $RitsuLibModDirectory)
            if (Test-Path -LiteralPath $nested -PathType Container) {
                Get-ChildItem -LiteralPath $nested -Force | Move-Item -Destination $ritsuLibSmokeDirectory
                Remove-Item -LiteralPath $nested -Force
            }
        }
        $ritsuAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $ritsuLibSmokeDirectory 'STS2-RitsuLib.dll')).Version
        $ritsuManifest = Get-Content -LiteralPath (Join-Path $ritsuLibSmokeDirectory 'mod_manifest.json') -Raw | ConvertFrom-Json
        if ($ritsuAssemblyVersion.Major -ne 0 -or $ritsuAssemblyVersion.Minor -ne 4 -or $ritsuAssemblyVersion.Build -ne 62 -or
            [string]$ritsuManifest.version -ne '0.4.62') {
            throw 'The protected smoke runner requires a complete RitsuLib 0.4.62 mod package.'
        }
    }
    if ($RunnerPurpose -eq 'Contract') {
        Copy-IsolatedDotnet9Runtime -Destination $dotnetRuntimeDirectory
    }
    foreach ($fileName in $requiredReferences) {
        $destination = Join-Path $referenceDirectory $fileName
        Copy-Item -LiteralPath (Join-Path $GameDataDirectory $fileName) -Destination $destination
        (Get-Item -LiteralPath $destination).IsReadOnly = $true
    }
    if ($RunnerPurpose -eq 'Release') {
        New-Item -ItemType Directory -Path $spineDirectory -Force | Out-Null
        foreach ($fileName in $requiredSpineFiles) {
            $destination = Join-Path $spineDirectory $fileName
            Copy-Item -LiteralPath (Join-Path $SpineExtensionDirectory $fileName) -Destination $destination
            (Get-Item -LiteralPath $destination).IsReadOnly = $true
        }
    }

    if ([string]::IsNullOrWhiteSpace($RunnerArchivePath)) {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archive
    }
    else {
        $resolvedArchive = (Resolve-Path -LiteralPath $RunnerArchivePath -ErrorAction Stop).Path
        Copy-Item -LiteralPath $resolvedArchive -Destination $archive
    }
    $actualArchiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($actualArchiveHash -ne $RunnerArchiveSha256) {
        throw "GitHub Actions runner archive SHA-256 mismatch: expected $RunnerArchiveSha256, got $actualArchiveHash."
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $runnerDirectory -Force

    Push-Location $runnerDirectory
    try {
        & .\config.cmd --unattended --ephemeral --replace `
            --url $RepositoryUrl `
            --token $RegistrationToken `
            --name $runnerName `
            --labels $runnerLabel `
            --work $workDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub Actions runner registration failed with exit code $LASTEXITCODE."
        }

        $env:STS2_DATA_DIR = $referenceDirectory
        $env:GODOT_EXE = $GodotExecutable
        if ($RunnerPurpose -eq 'Contract') {
            $env:NINJASLAYER_CONTRACT_DOTNET_ROOT = $dotnetRuntimeDirectory
            $env:NINJASLAYER_SPINE_DIR = $null
        }
        else {
            $env:NINJASLAYER_CONTRACT_DOTNET_ROOT = $null
            $env:NINJASLAYER_SPINE_DIR = if ($RunnerPurpose -eq 'Release') { $spineDirectory } else { $null }
        }
        if ($RunnerPurpose -eq 'Smoke') {
            $env:NINJASLAYER_SMOKE_GAME_ROOT = (Resolve-Path -LiteralPath $GameRootDirectory).Path
            $env:NINJASLAYER_RITSULIB_MOD_DIR = $ritsuLibSmokeDirectory
        }
        else {
            $env:NINJASLAYER_SMOKE_GAME_ROOT = $null
            $env:NINJASLAYER_RITSULIB_MOD_DIR = $null
        }
        & .\run.cmd
        if ($LASTEXITCODE -ne 0) {
            throw "The ephemeral GitHub Actions runner exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:STS2_DATA_DIR = $previousSts2DataDirectory
    $env:GODOT_EXE = $previousGodotExecutable
    $env:NINJASLAYER_CONTRACT_DOTNET_ROOT = $previousContractDotnetRoot
    $env:NINJASLAYER_SPINE_DIR = $previousSpineDirectory
    $env:NINJASLAYER_SMOKE_GAME_ROOT = $previousSmokeGameRoot
    $env:NINJASLAYER_RITSULIB_MOD_DIR = $previousRitsuLibModDirectory
    Remove-SessionDirectory -Path $sessionRoot
}
