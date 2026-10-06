#Requires -Version 7.0
#Requires -PSEdition Core
[CmdletBinding()]
param(
    [string]$GameRoot = 'C:\Program Files\Steam\steamapps\common\Slay the Spire 2',
    [string]$PublishedMod = 'C:\Program Files\Steam\steamapps\workshop\content\2868840\3776911445',
    [string]$HostRoot = 'C:\Users\theon\Documents\STS2 MOD\Tools\STS2\hosts',
    [string]$Godot = 'C:\Users\theon\Documents\STS2 MOD\Tools\STS2\godot-4.5.1\Godot_v4.5.1-stable_mono_win64_console.exe',
    [ValidateSet('stable','preview')][string[]]$Channels = @('stable','preview')
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
Push-Location -LiteralPath $repository
try {
    & node tools/validate-localization.mjs
    if ($LASTEXITCODE) { throw 'Locale validation failed.' }
    $release = Get-Content -LiteralPath Website/content/current.json -Raw | ConvertFrom-Json
    $compatibility = Get-Content -LiteralPath eng/compatibility.json -Raw | ConvertFrom-Json
    $output = Join-Path $repository 'build/localization-validation'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $translationInputs = @(Get-ChildItem -LiteralPath "$repository/NinjaSlayer/localization" -Recurse -File -Filter '*.json' |
        Sort-Object FullName | ForEach-Object { "$($_.FullName):$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" })
    # Import and export current resources, never modify a daily game or use a stale pack.
    & dotnet build NinjaSlayer.csproj -c Debug -v:minimal "-p:Sts2DataDir=$HostRoot/preview" *> "$output/editor-build.log"
    if ($LASTEXITCODE) { throw 'Editor build failed.' }
    & $Godot --headless --editor --path $repository --import *> "$output/import.log"
    if ($LASTEXITCODE) { throw 'Godot import failed.' }
    $pack = Join-Path $output 'NinjaSlayer.pck'
    & $Godot --headless --path $repository --export-pack 'Windows Desktop' $pack *> "$output/export.log"
    if ($LASTEXITCODE) { throw 'Godot pack export failed.' }
    $previousOutput = $env:NINJASLAYER_CONTRACT_CATALOG_OUTPUT
    $previousVersion = $env:NINJASLAYER_CATALOG_VERSION
    $previousHostLocalization = $env:NINJASLAYER_CONTRACT_HOST_LOCALIZATION
    try {
        $env:NINJASLAYER_CATALOG_VERSION = $release.version
        foreach ($channel in $Channels) {
            $version = $compatibility.channels.$channel.gameApiVersion
            # The available art pack is the installed preview client. Overlay the
            # exact channel's extracted native text before loading current mod text;
            # this validates formatting, not a complete stable game installation.
            $env:NINJASLAYER_CONTRACT_HOST_LOCALIZATION = (Resolve-Path -LiteralPath (
                Join-Path $repository "../Slay the Spire 2/Slay the Spire 2 v$version/localization")).Path
            $env:NINJASLAYER_CONTRACT_CATALOG_OUTPUT = Join-Path $output "$channel/content"
            New-Item -ItemType Directory -Path "$output/$channel" -Force | Out-Null
            & Tests/NinjaSlayer.OrbContractTests/Run-Contracts.ps1 -Channel $channel `
                -NinjaSlayerAssemblyPath "$PublishedMod/lib/$version/NinjaSlayer.dll" `
                -Sts2DataDir "$HostRoot/$channel" -HostPack "$GameRoot/SlayTheSpire2.pck" `
                -ProductPack $pack -SourceRevision $release.sourceRevision `
                -GodotPath $Godot -DotnetRoot 'C:\Program Files\dotnet' -LogPath "$output/$channel/contracts.log"
        }
        $currentTranslationInputs = @(Get-ChildItem -LiteralPath "$repository/NinjaSlayer/localization" -Recurse -File -Filter '*.json' |
            Sort-Object FullName | ForEach-Object { "$($_.FullName):$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" })
        if (Compare-Object $translationInputs $currentTranslationInputs) {
            throw 'Translation sources changed during pack/export validation. Rerun before importing these catalogs.'
        }
    } finally {
        $env:NINJASLAYER_CONTRACT_CATALOG_OUTPUT = $previousOutput
        $env:NINJASLAYER_CATALOG_VERSION = $previousVersion
        $env:NINJASLAYER_CONTRACT_HOST_LOCALIZATION = $previousHostLocalization
    }
    Write-Host "Offline locale exports ready under $output; published gameplay DLLs unchanged."
} finally { Pop-Location }
