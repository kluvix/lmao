[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$ManagedPath,
    [string]$BepInExCorePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoPath = $PSScriptRoot

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE. No new package was staged."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK: winget install --id Microsoft.DotNet.SDK.8 --exact . Then reopen PowerShell.'
}
if ($GamePath) {
    $GamePath = (Resolve-Path -LiteralPath $GamePath).Path
}
if (-not $ManagedPath) {
    if (-not $GamePath) {
        throw 'Pass -GamePath pointing to Gorilla Tag, or provide both -ManagedPath and -BepInExCorePath.'
    }
    $candidates = @(Get-ChildItem -LiteralPath $GamePath -Directory | Where-Object {
        $_.Name -like '*_Data' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Managed/UnityEngine.CoreModule.dll') -PathType Leaf)
    })
    if ($candidates.Count -ne 1) {
        throw 'Could not identify one game Managed folder. Pass -ManagedPath pointing to the folder containing UnityEngine.CoreModule.dll.'
    }
    $ManagedPath = Join-Path $candidates[0].FullName 'Managed'
}
if (-not $BepInExCorePath) {
    if (-not $GamePath) {
        throw 'Provide -BepInExCorePath when -GamePath is omitted.'
    }
    $BepInExCorePath = Join-Path $GamePath 'BepInEx/core'
}
$ManagedPath = (Resolve-Path -LiteralPath $ManagedPath).Path
$BepInExCorePath = (Resolve-Path -LiteralPath $BepInExCorePath).Path

foreach ($name in @('UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.AudioModule.dll', 'UnityEngine.IMGUIModule.dll', 'UnityEngine.TextRenderingModule.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ManagedPath $name) -PathType Leaf)) {
        throw "Missing Unity reference: $ManagedPath/$name. Use references from your matching game installation."
    }
}
foreach ($name in @('BepInEx.dll', '0Harmony.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $BepInExCorePath $name) -PathType Leaf)) {
        throw "Missing $name in $BepInExCorePath. Use a clean BepInEx 5 installation."
    }
}

Push-Location -LiteralPath $repoPath
$stage = $null
try {
    $sdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^(\d+)\.' -or [int]$Matches[1] -lt 8) {
        throw 'The build needs the .NET 8 SDK or newer. Install Microsoft.DotNet.SDK.8 and reopen PowerShell.'
    }
    Invoke-DotNet -Arguments @(
        'build', 'src/HeyJace/HeyJace.csproj', '--configuration', 'Release', '--nologo',
        "-p:ToolReferenceDir=$BepInExCorePath", "-p:UnityReferenceDir=$ManagedPath"
    )
    Invoke-DotNet -Arguments @('run', '--project', 'tests/HeyJaceChecks', '--configuration', 'Release')
    Invoke-DotNet -Arguments @('run', '--project', 'tests/HeyJacePipeChecks', '--configuration', 'Release')
    Invoke-DotNet -Arguments @(
        'publish', 'src/HeyJace.Speech/HeyJace.Speech.csproj', '--configuration', 'Release',
        '--runtime', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true',
        '--output', (Join-Path $repoPath 'artifacts/heyjace-speech'), '--nologo'
    )

    # Stage only after every build/check succeeds, without modifying the game.
    $output = Join-Path $repoPath 'artifacts/HeyJace'
    $stage = Join-Path $repoPath ('artifacts/.HeyJace-stage.' + [Guid]::NewGuid().ToString('N'))
    $pluginOutput = Join-Path $stage 'BepInEx/plugins/HeyJace'
    New-Item -ItemType Directory -Path (Join-Path $pluginOutput 'Speech') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoPath 'src/HeyJace/bin/Release/netstandard2.1/HeyJace.dll') -Destination (Join-Path $pluginOutput 'HeyJace.dll')
    Copy-Item -LiteralPath (Join-Path $repoPath 'src/HeyJace.Core/bin/Release/netstandard2.1/HeyJace.Core.dll') -Destination (Join-Path $pluginOutput 'HeyJace.Core.dll')
    Copy-Item -LiteralPath (Join-Path $repoPath 'artifacts/heyjace-speech/HeyJace.Speech.exe') -Destination (Join-Path $pluginOutput 'Speech/HeyJace.Speech.exe')
    Copy-Item -LiteralPath (Join-Path $repoPath 'docs/HEY_JACE.md') -Destination (Join-Path $stage 'README.md')
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }
    Move-Item -LiteralPath $stage -Destination $output
    $stage = $null
    Write-Host "Build, checks, and Windows publish passed. Package staged at $output"
    Write-Host 'Copy the whole BepInEx/plugins/HeyJace folder into the game.'
}
finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    Pop-Location
}
