[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GamePath,

    # Optional overrides for installations with a nonstandard directory layout.
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
        throw "dotnet failed with exit code $LASTEXITCODE. No new DLLs were staged."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK (not just the runtime): winget install --id Microsoft.DotNet.SDK.8 --exact . Then reopen PowerShell.'
}

$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
if (-not $ManagedPath) {
    $candidates = @(Get-ChildItem -LiteralPath $GamePath -Directory | Where-Object {
        $_.Name -like '*_Data' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Managed/Assembly-CSharp.dll') -PathType Leaf)
    })
    if ($candidates.Count -ne 1) {
        throw 'Could not identify one game Managed folder. Pass -ManagedPath pointing to the folder containing Assembly-CSharp.dll and UnityEngine.CoreModule.dll.'
    }
    $ManagedPath = Join-Path $candidates[0].FullName 'Managed'
}
if (-not $BepInExCorePath) {
    $BepInExCorePath = Join-Path $GamePath 'BepInEx/core'
}
$ManagedPath = (Resolve-Path -LiteralPath $ManagedPath).Path
$BepInExCorePath = (Resolve-Path -LiteralPath $BepInExCorePath).Path

foreach ($name in @('Assembly-CSharp.dll', 'SteamVR.dll', 'SteamVR_Actions.dll',
                    'UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll',
                    'UnityEngine.AudioModule.dll', 'UnityEngine.ImageConversionModule.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ManagedPath $name) -PathType Leaf)) {
        throw "Missing reference: $ManagedPath/$name. Use assemblies from your matching game installation."
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $BepInExCorePath 'BepInEx.dll') -PathType Leaf)) {
    throw "Missing BepInEx.dll in $BepInExCorePath. These mods require BepInEx 5."
}

$hadManagedVariable = Test-Path Env:PIGLESS_GAME_MANAGED_DIR
$previousManaged = $env:PIGLESS_GAME_MANAGED_DIR
Push-Location -LiteralPath $repoPath
try {
    # global.json selects a compatible .NET 8 SDK; fail before building if unavailable.
    Invoke-DotNet -Arguments @('--version')
    foreach ($project in @('PiglessCamera', 'PiglessSheesh')) {
        Invoke-DotNet -Arguments @(
            'build', "src/$project/$project.csproj", '--configuration', 'Release', '--nologo',
            "-p:GameManagedDir=$ManagedPath",
            "-p:ToolReferenceDir=$BepInExCorePath",
            "-p:UnityReferenceDir=$ManagedPath"
        )
    }
    $env:PIGLESS_GAME_MANAGED_DIR = $ManagedPath
    Invoke-DotNet -Arguments @('run', '--project', 'tests/CoreChecks', '--configuration', 'Release')

    # Stage only after BOTH builds and the checks succeed. Never modify the game installation.
    $output = Join-Path $repoPath 'artifacts/BepInEx/plugins/Pigless'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    foreach ($project in @('PiglessCamera', 'PiglessSheesh')) {
        Copy-Item -LiteralPath (Join-Path $repoPath "src/$project/bin/Release/netstandard2.1/$project.dll") -Destination $output -Force
    }
    $image = Join-Path $repoPath 'assets/jacesus.png'
    if (Test-Path -LiteralPath $image -PathType Leaf) {
        $assetOutput = Join-Path $output 'assets'
        New-Item -ItemType Directory -Path $assetOutput -Force | Out-Null
        Copy-Item -LiteralPath $image -Destination $assetOutput -Force
    }
    else {
        Write-Warning 'Pillow texture is missing. Add assets/jacesus.png and rebuild to include your image.'
    }
    Write-Host "DLLs built successfully: $output"
    Write-Host 'Back up/remove old mod copies before manually copying these DLLs into the game plugins directory.'
}
finally {
    Pop-Location
    if ($hadManagedVariable) { $env:PIGLESS_GAME_MANAGED_DIR = $previousManaged }
    else { Remove-Item Env:PIGLESS_GAME_MANAGED_DIR -ErrorAction SilentlyContinue }
}
