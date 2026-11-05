[CmdletBinding()]
param(
    [uri]$DownloadUrl = 'https://raw.githubusercontent.com/kluvix/lmao/codex/sticky-holdables-download/dist/StickyHoldables.dll',
    [string]$Destination = (Join-Path (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads') 'StickyHoldables-1.0.1.dll')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedSha256 = '4bb6eeff381710a5499b7fb449449ae90cd651ad355ef3403cae6fcfddd10ad9'

if (-not $DownloadUrl.IsAbsoluteUri -or $DownloadUrl.Scheme -ne 'https') {
    throw 'The download URL must use HTTPS.'
}
$downloadTarget = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $downloadTarget) {
    throw "A file already exists at $downloadTarget. Choose another -Destination; existing files are never replaced."
}
$destinationDirectory = [IO.Path]::GetDirectoryName($downloadTarget)
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
$temporaryDownload = Join-Path $destinationDirectory ([IO.Path]::GetRandomFileName())

try {
    # Windows PowerShell 5.1 can otherwise default to obsolete TLS protocols.
    if ($PSVersionTable.PSVersion.Major -lt 6) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    Invoke-WebRequest -Uri $DownloadUrl.AbsoluteUri -OutFile $temporaryDownload -UseBasicParsing
    $actualSha256 = (Get-FileHash -LiteralPath $temporaryDownload -Algorithm SHA256).Hash
    if ($actualSha256 -ne $expectedSha256) {
        throw 'Download verification failed. The downloaded file does not match Sticky Holdables 1.0.1.'
    }
    # File.Move refuses an existing target, including one created during download.
    [IO.File]::Move($temporaryDownload, $downloadTarget)
    Write-Host "Verified StickyHoldables.dll saved to: $downloadTarget"
    Write-Host 'This only downloads the DLL; it does not run it or change your game.'
    Write-Host 'After malware cleanup, copy it into a clean BepInEx/plugins/StickyHoldables folder.'
}
finally {
    if (Test-Path -LiteralPath $temporaryDownload) {
        Remove-Item -LiteralPath $temporaryDownload -Force
    }
}
