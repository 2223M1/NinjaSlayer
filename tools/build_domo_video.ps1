param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$FfmpegPath,

    [string]$OutputPath = "$PSScriptRoot\..\NinjaSlayer\videos\ninja_slayer_domo.ogv"
)

$ErrorActionPreference = "Stop"
$frameRate = 24
$frameCount = 260
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$ffmpeg = (Resolve-Path -LiteralPath $FfmpegPath).Path
$output = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($output)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$indexedFrames = @{}
foreach ($file in Get-ChildItem -LiteralPath $source -Filter "Ninja-Slayer_*.png") {
    if ($file.Name -notmatch '^Ninja-Slayer_(\d{4})_.*-(\d+)\.png$') {
        continue
    }

    $sourceIndex = [int]$Matches[1]
    $layer = [int]$Matches[2]
    if ($layer -ne 260 - $sourceIndex) {
        throw "Greeting frame index mismatch: $($file.Name)"
    }
    $indexedFrames[$sourceIndex] = $file.FullName
}

$frames = for ($sourceIndex = 259; $sourceIndex -ge 0; $sourceIndex--) {
    if (-not $indexedFrames.ContainsKey($sourceIndex)) {
        throw "Missing greeting source index: $sourceIndex"
    }
    $indexedFrames[$sourceIndex]
}

if ($frames.Count -ne $frameCount) {
    throw "Expected $frameCount greeting frames, found $($frames.Count)."
}
if ([System.IO.Path]::GetFileName($frames[0]) -notmatch '^Ninja-Slayer_0259_.*-1\.png$') {
    throw "Unexpected first frame: $($frames[0])"
}
if ([System.IO.Path]::GetFileName($frames[-1]) -notmatch '^Ninja-Slayer_0000_.*-260\.png$') {
    throw "Unexpected final frame: $($frames[-1])"
}

$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "ninja-slayer-domo-$([guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
try {
    for ($frameIndex = 0; $frameIndex -lt $frames.Count; $frameIndex++) {
        $stagedPath = Join-Path $stagingDirectory ("frame{0:D4}.png" -f $frameIndex)
        try {
            New-Item -ItemType HardLink -Path $stagedPath -Target $frames[$frameIndex] -ErrorAction Stop | Out-Null
        }
        catch {
            Copy-Item -LiteralPath $frames[$frameIndex] -Destination $stagedPath
        }
    }

    & $ffmpeg -hide_banner -loglevel warning -y `
        -framerate $frameRate -start_number 0 -i (Join-Path $stagingDirectory "frame%04d.png") `
        -vf "format=yuv420p" `
        -frames:v $frameCount -an -c:v libtheora -q:v 8 $output
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

$result = Get-Item -LiteralPath $output
Write-Host "Created $($result.FullName) ($($result.Length) bytes, $frameCount frames at $frameRate FPS)."
