[CmdletBinding()]
param(
    [uri]$DownloadUrl = 'https://raw.githubusercontent.com/kluvix/lmao/codex/hey-jace-download/dist/HeyJace-1.2.0-Windows-x64.zip',
    [string]$Destination = (Join-Path (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads') 'HeyJace-1.2.0-Windows-x64.zip')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedSha256 = '30ff04da261015429995de69504f73a9030667a50d1f2a2b3022389952db7fca'

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
        throw 'Download verification failed. The downloaded file does not match Hey Jace 1.2.0.'
    }
    # File.Move refuses an existing target, including one created during download.
    [IO.File]::Move($temporaryDownload, $downloadTarget)
    Write-Host "Verified HeyJace-1.2.0-Windows-x64.zip saved to: $downloadTarget"
    Write-Host 'This downloads the package without running or installing its files.'
    Write-Host 'Close the game, extract the ZIP, and copy the whole BepInEx/plugins/HeyJace folder into your game.'
}
finally {
    if (Test-Path -LiteralPath $temporaryDownload) {
        Remove-Item -LiteralPath $temporaryDownload -Force
    }
}
