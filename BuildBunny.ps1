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
        throw "dotnet failed with exit code $LASTEXITCODE. No new DLL was staged."
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

foreach ($name in @('UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ManagedPath $name) -PathType Leaf)) {
        throw "Missing Unity reference: $ManagedPath/$name. Use references from your matching game installation."
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $BepInExCorePath 'BepInEx.dll') -PathType Leaf)) {
    throw "Missing BepInEx.dll in $BepInExCorePath. Use a clean BepInEx 5 installation."
}

Push-Location -LiteralPath $repoPath
try {
    Invoke-DotNet -Arguments @('--version')
    Invoke-DotNet -Arguments @(
        'build', 'src/PiglessBunny/PiglessBunny.csproj', '--configuration', 'Release', '--nologo',
        "-p:ToolReferenceDir=$BepInExCorePath",
        "-p:UnityReferenceDir=$ManagedPath"
    )
    Invoke-DotNet -Arguments @('run', '--project', 'tests/PiglessBunnyChecks', '--configuration', 'Release')

    # Stage only after the build and checks succeed; never modify the game.
    $output = Join-Path $repoPath 'artifacts/PiglessBunny'
    $pluginOutput = Join-Path $output 'BepInEx/plugins/PiglessBunny'
    New-Item -ItemType Directory -Path $pluginOutput -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoPath 'src/PiglessBunny/bin/Release/netstandard2.1/PiglessBunny.dll') -Destination (Join-Path $pluginOutput 'PiglessBunny.dll') -Force
    Copy-Item -LiteralPath (Join-Path $repoPath 'docs/PIGLESS_BUNNY.md') -Destination (Join-Path $output 'README.md') -Force
    Write-Host "Build and checks passed. Package staged at $output"
    Write-Host 'Install only PiglessBunny.dll in BepInEx/plugins/PiglessBunny after finishing malware cleanup.'
}
finally {
    Pop-Location
}
