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

    [string]$GameDataDirectoryStable,

    [string]$GameDataDirectoryPreview,

    [string]$GameRootDirectoryStable,

    [string]$GameRootDirectoryPreview,

    [string]$RitsuLibModDirectory,

    [string]$GodotExecutable = 'C:\Program Files\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe',

    [string]$SpineExtensionDirectory = (Join-Path $PSScriptRoot '..\..\addons\spine\windows')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$compatibilityScript = Join-Path $repositoryRoot '.github\scripts\compatibility.ps1'
. $compatibilityScript
$compatibility = Read-NinjaSlayerCompatibility -Path (Join-Path $repositoryRoot 'eng\compatibility.json')
$channelNames = @($compatibility.channels.PSObject.Properties.Name)

if ($RunnerPurpose -in @('Contract', 'Smoke') -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session so the $RunnerPurpose runner can enforce outbound firewall isolation."
}

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot 4.5.1 Mono was not found at $GodotExecutable"
}

$dataDirectories = @{
    stable = $GameDataDirectoryStable
    preview = $GameDataDirectoryPreview
}
$gameRoots = @{
    stable = $GameRootDirectoryStable
    preview = $GameRootDirectoryPreview
}
$baseRequiredReferences = @('sts2.dll', '0Harmony.dll', 'GodotSharp.dll')
$hostInputs = foreach ($channelName in $channelNames) {
    $profile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel $channelName
    [pscustomobject]@{
        Channel = $channelName
        GameApiVersion = [string]$profile.gameApiVersion
        PackageId = [string]$profile.ritsuLibPackageId
        DataDirectory = $dataDirectories[$channelName]
        GameRootDirectory = $gameRoots[$channelName]
        RuntimeReferences = @($profile.runtimeAssemblies | ForEach-Object { [string]$_ })
        ExpectedMvid = [string]$profile.hostContract.moduleMvid
    }
}

if ($RunnerPurpose -in @('Contract', 'Release')) {
    foreach ($hostInput in $hostInputs) {
        if ([string]::IsNullOrWhiteSpace($hostInput.DataDirectory)) {
            throw "GameDataDirectory$([char]::ToUpperInvariant($hostInput.Channel[0]))$($hostInput.Channel.Substring(1)) is required for $RunnerPurpose."
        }
        $hostInput.DataDirectory = (Resolve-Path -LiteralPath $hostInput.DataDirectory -ErrorAction Stop).Path
        foreach ($fileName in @($baseRequiredReferences + $hostInput.RuntimeReferences)) {
            $source = Join-Path $hostInput.DataDirectory $fileName
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Missing $($hostInput.Channel) $RunnerPurpose reference: $source"
            }
        }
        $actualMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath (Join-Path $hostInput.DataDirectory 'sts2.dll')
        if ($actualMvid -ne $hostInput.ExpectedMvid) {
            throw "$($hostInput.Channel) sts2.dll MVID $actualMvid does not match compatibility.json."
        }
    }
}

if ($RunnerPurpose -eq 'Smoke') {
    if ([string]::IsNullOrWhiteSpace($RitsuLibModDirectory)) {
        throw 'RitsuLibModDirectory is required for Smoke and must point to the current Workshop RitsuLib installation.'
    }
    $RitsuLibModDirectory = (Resolve-Path -LiteralPath $RitsuLibModDirectory -ErrorAction Stop).Path
    foreach ($hostInput in $hostInputs) {
        if ([string]::IsNullOrWhiteSpace($hostInput.GameRootDirectory)) {
            throw "GameRootDirectory$([char]::ToUpperInvariant($hostInput.Channel[0]))$($hostInput.Channel.Substring(1)) is required for Smoke."
        }
        $hostInput.GameRootDirectory = (Resolve-Path -LiteralPath $hostInput.GameRootDirectory -ErrorAction Stop).Path
        foreach ($relativePath in @(
            'SlayTheSpire2.exe',
            'SlayTheSpire2.pck',
            'data_sts2_windows_x86_64\sts2.dll'
        )) {
            $path = Join-Path $hostInput.GameRootDirectory $relativePath
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Missing $($hostInput.Channel) protected smoke input: $path"
            }
        }
        $assemblyPath = Join-Path $hostInput.GameRootDirectory 'data_sts2_windows_x86_64\sts2.dll'
        $actualMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath $assemblyPath
        if ($actualMvid -ne $hostInput.ExpectedMvid) {
            throw "$($hostInput.Channel) smoke host MVID $actualMvid does not match compatibility.json."
        }
    }
    foreach ($relativePath in @('STS2-RitsuLib.dll', 'mod_manifest.json')) {
        $path = Join-Path $RitsuLibModDirectory $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing protected RitsuLib smoke input: $path"
        }
    }
}

$requiredSpineFiles = @(
    'libspine_godot.windows.editor.x86_64.dll',
    'libspine_godot.windows.template_debug.x86_64.dll',
    'libspine_godot.windows.template_release.x86_64.dll'
)
if ($RunnerPurpose -in @('Release', 'Smoke')) {
    foreach ($fileName in $requiredSpineFiles) {
        $source = Join-Path $SpineExtensionDirectory $fileName
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Missing Spine $RunnerPurpose input: $source"
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
$managedEnvironmentVariables = @(
    'STS2_DATA_DIR',
    'GODOT_EXE',
    'NINJASLAYER_CONTRACT_DOTNET_ROOT',
    'NINJASLAYER_SPINE_DIR',
    'NINJASLAYER_RITSULIB_MOD_DIR',
    'NINJASLAYER_STS2_STABLE_DATA_DIR',
    'NINJASLAYER_STS2_PREVIEW_DATA_DIR',
    'NINJASLAYER_SMOKE_STABLE_GAME_ROOT',
    'NINJASLAYER_SMOKE_PREVIEW_GAME_ROOT'
)
$previousEnvironment = @{}
foreach ($name in $managedEnvironmentVariables) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Set-RunnerEnvironment {
    param([Parameter(Mandatory)][string]$Name, [AllowNull()][string]$Value)

    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function ConvertTo-SemVerCore {
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Field)

    if ($Value -notmatch '^(?<Core>\d+\.\d+\.\d+)(?:[-+].*)?$') {
        throw "$Field '$Value' is not a semantic version."
    }
    return [Version]$Matches.Core
}

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
        Copy-Item -LiteralPath $RitsuLibModDirectory -Destination $ritsuLibSmokeDirectory -Recurse
        $nested = Join-Path $ritsuLibSmokeDirectory (Split-Path -Leaf $RitsuLibModDirectory)
        if (Test-Path -LiteralPath $nested -PathType Container) {
            Get-ChildItem -LiteralPath $nested -Force | Move-Item -Destination $ritsuLibSmokeDirectory
            Remove-Item -LiteralPath $nested -Force
        }

        $minimumRitsuVersion = ConvertTo-SemVerCore `
            -Value ([string]$compatibility.ritsuLibVersion) `
            -Field 'compatibility.ritsuLibVersion'
        $ritsuManifest = Get-Content -LiteralPath (Join-Path $ritsuLibSmokeDirectory 'mod_manifest.json') `
            -Raw -Encoding utf8 | ConvertFrom-Json
        if ([string]$ritsuManifest.id -ne 'STS2-RitsuLib') {
            throw 'The protected smoke runner requires the STS2-RitsuLib Workshop mod.'
        }
        $runtimeManifestVersion = ConvertTo-SemVerCore `
            -Value ([string]$ritsuManifest.version) `
            -Field 'RitsuLib manifest version'
        $ritsuAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName(
            (Join-Path $ritsuLibSmokeDirectory 'STS2-RitsuLib.dll')).Version
        $runtimeAssemblyVersion = [Version]::new(
            $ritsuAssemblyVersion.Major,
            $ritsuAssemblyVersion.Minor,
            [Math]::Max(0, $ritsuAssemblyVersion.Build))
        if ($runtimeManifestVersion -lt $minimumRitsuVersion -or $runtimeAssemblyVersion -lt $minimumRitsuVersion) {
            throw "The Workshop RitsuLib runtime must be at least $minimumRitsuVersion; found manifest $runtimeManifestVersion and assembly $runtimeAssemblyVersion."
        }
    }

    if ($RunnerPurpose -eq 'Contract') {
        Copy-IsolatedDotnet9Runtime -Destination $dotnetRuntimeDirectory
    }

    if ($RunnerPurpose -in @('Contract', 'Release')) {
        foreach ($hostInput in $hostInputs) {
            $hostReferenceDirectory = Join-Path $referenceDirectory $hostInput.Channel
            New-Item -ItemType Directory -Path $hostReferenceDirectory -Force | Out-Null
            foreach ($fileName in @($baseRequiredReferences + $hostInput.RuntimeReferences)) {
                $destination = Join-Path $hostReferenceDirectory $fileName
                Copy-Item -LiteralPath (Join-Path $hostInput.DataDirectory $fileName) -Destination $destination
                (Get-Item -LiteralPath $destination).IsReadOnly = $true
            }
        }
    }

    if ($RunnerPurpose -in @('Release', 'Smoke')) {
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

    $isolatedDataDirectories = @{}
    if ($RunnerPurpose -in @('Contract', 'Release')) {
        foreach ($hostInput in $hostInputs) {
            $isolatedDataDirectories[$hostInput.Channel] = Join-Path $referenceDirectory $hostInput.Channel
        }
    }
    $stableHost = $hostInputs | Where-Object Channel -eq 'stable' | Select-Object -First 1
    $previewHost = $hostInputs | Where-Object Channel -eq 'preview' | Select-Object -First 1

    Set-RunnerEnvironment -Name 'GODOT_EXE' -Value $GodotExecutable
    Set-RunnerEnvironment -Name 'NINJASLAYER_CONTRACT_DOTNET_ROOT' `
        -Value $(if ($RunnerPurpose -eq 'Contract') { $dotnetRuntimeDirectory } else { $null })
    Set-RunnerEnvironment -Name 'NINJASLAYER_SPINE_DIR' `
        -Value $(if ($RunnerPurpose -in @('Release', 'Smoke')) { $spineDirectory } else { $null })
    Set-RunnerEnvironment -Name 'NINJASLAYER_RITSULIB_MOD_DIR' `
        -Value $(if ($RunnerPurpose -eq 'Smoke') { $ritsuLibSmokeDirectory } else { $null })
    Set-RunnerEnvironment -Name 'NINJASLAYER_STS2_STABLE_DATA_DIR' `
        -Value $(if ($RunnerPurpose -in @('Contract', 'Release')) { $isolatedDataDirectories.stable } else { $null })
    Set-RunnerEnvironment -Name 'NINJASLAYER_STS2_PREVIEW_DATA_DIR' `
        -Value $(if ($RunnerPurpose -in @('Contract', 'Release')) { $isolatedDataDirectories.preview } else { $null })
    Set-RunnerEnvironment -Name 'NINJASLAYER_SMOKE_STABLE_GAME_ROOT' `
        -Value $(if ($RunnerPurpose -eq 'Smoke') { $stableHost.GameRootDirectory } else { $null })
    Set-RunnerEnvironment -Name 'NINJASLAYER_SMOKE_PREVIEW_GAME_ROOT' `
        -Value $(if ($RunnerPurpose -eq 'Smoke') { $previewHost.GameRootDirectory } else { $null })
    Set-RunnerEnvironment -Name 'STS2_DATA_DIR' -Value $(if ($RunnerPurpose -eq 'Smoke') {
        Join-Path $previewHost.GameRootDirectory 'data_sts2_windows_x86_64'
    } else {
        $isolatedDataDirectories.preview
    })

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
    foreach ($name in $managedEnvironmentVariables) {
        Set-RunnerEnvironment -Name $name -Value $previousEnvironment[$name]
    }
    Remove-SessionDirectory -Path $sessionRoot
}
