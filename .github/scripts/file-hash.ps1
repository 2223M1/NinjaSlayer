#Requires -Version 7.0
#Requires -PSEdition Core

Set-StrictMode -Version Latest

function Get-NinjaSlayerFileSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $stream = [IO.File]::OpenRead($resolved)
    try {
        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $hash = $hasher.ComputeHash($stream)
        }
        finally {
            $hasher.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return [BitConverter]::ToString($hash).Replace('-', '').ToLowerInvariant()
}
