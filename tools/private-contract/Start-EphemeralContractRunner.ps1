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

    [string]$RepositoryUrl = 'https://github.com/2223M1/NinjaSlayer',

    [string]$GameDataDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64',

    [string]$GodotExecutable = 'C:\Program Files\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session so the contract can enforce its outbound firewall rule.'
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

$sessionId = [Guid]::NewGuid().ToString('N')
$sessionRoot = Join-Path $env:TEMP "NinjaSlayer-ContractRunner-$sessionId"
$runnerDirectory = Join-Path $sessionRoot 'runner'
$referenceDirectory = Join-Path $sessionRoot 'references'
$workDirectory = Join-Path $sessionRoot 'work'
$archive = Join-Path $sessionRoot 'actions-runner.zip'
$runnerName = "ninjaslayer-contract-$env:COMPUTERNAME-$($sessionId.Substring(0, 8))"
$downloadUrl = "https://github.com/actions/runner/releases/download/v$RunnerVersion/actions-runner-win-x64-$RunnerVersion.zip"
$previousSts2DataDirectory = $env:STS2_DATA_DIR
$previousGodotExecutable = $env:GODOT_EXE

try {
    New-Item -ItemType Directory -Path $runnerDirectory, $referenceDirectory, $workDirectory -Force | Out-Null
    foreach ($fileName in $requiredReferences) {
        $destination = Join-Path $referenceDirectory $fileName
        Copy-Item -LiteralPath (Join-Path $GameDataDirectory $fileName) -Destination $destination
        (Get-Item -LiteralPath $destination).IsReadOnly = $true
    }

    Invoke-WebRequest -Uri $downloadUrl -OutFile $archive
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
            --labels ninjaslayer-contract `
            --work $workDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub Actions runner registration failed with exit code $LASTEXITCODE."
        }

        $env:STS2_DATA_DIR = $referenceDirectory
        $env:GODOT_EXE = $GodotExecutable
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
    if (Test-Path -LiteralPath $sessionRoot) {
        Get-ChildItem -LiteralPath $referenceDirectory -File -ErrorAction SilentlyContinue |
            ForEach-Object { $_.IsReadOnly = $false }
        Remove-Item -LiteralPath $sessionRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
