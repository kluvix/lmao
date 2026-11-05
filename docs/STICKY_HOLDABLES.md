# Sticky Holdables

A BepInEx 5 plugin that keeps eligible equipped Gorilla Tag holdables in your hands without holding the grip buttons.

## Install

Finish malware cleanup first. Install into a fresh, trusted BepInEx installation; do not restore the previously infected plugin DLLs or compiler dependencies.

1. Close Gorilla Tag.
2. Copy `StickyHoldables.dll` into `Gorilla Tag/BepInEx/plugins/StickyHoldables/`.
3. Start the game and equip your holdable cosmetics normally.

The release package places the DLL at `BepInEx/plugins/StickyHoldables/StickyHoldables.dll`. Keep only one copy of this plugin installed. The package does not include BepInEx, Harmony, Unity, or game reference assemblies.

## Download with PowerShell

The repository includes `scripts/Download-StickyHoldables.ps1`. After reviewing it, run it with PowerShell. It downloads the published DLL over HTTPS into your Downloads folder, verifies the exact SHA-256 for version 1.0.0, and refuses to replace an existing file. It never executes the downloaded DLL or installs it into the game. Use `-Destination 'C:\path\StickyHoldables.dll'` for a different location. No administrator access or antivirus exclusions are needed.

## Behavior and controls

- Eligible local cosmetics equipped on the arms automatically attach to the opposite hands, following the game's arm-to-hand dock mapping.
- Releasing grip away from a docking target keeps a registered local cosmetic held. Releasing over a docking target still follows the game's normal docking behavior.
- Press **F8** on the PC keyboard to toggle the behavior. Disabling reverts items held by the mod to their previous docks.
- Normal cosmetic changes remain supported. The mod checks both local ownership and the matching hand/equipment registration before suppressing a grip release. It does not unlock cosmetics or take ownership of other players' items.
- Slingshots, throwable bugs, throwable set dressing, decorative items, and scene objects are excluded.

F8 uses BepInEx's keyboard input support. The toggle key and initial enabled state can also be changed in `BepInEx/config/dev.local.stickyholdables.cfg`. If keyboard input is unavailable in your game version, change the config's `Enabled` setting and restart the game.

## Validation limits

The Release build completed with zero warnings and errors. The synthetic API-contract suite passed 21 cases with 143 assertions, including ownership/hand identity, intentional docking, excluded objects, disabled startup, cosmetic-change failures, and state restoration. The resulting DLL has no embedded resources or native imports; static inspection found no network/process API references or the previously identified infection payload. This is not an antivirus certification.

The project uses reflection to bind the game's holdable APIs. Its design was checked against public game API source, not the exact game build installed on your PC. If required APIs are missing, it disables the hooks and records an error in the BepInEx log. No Gorilla Tag or VR runtime is available in this development environment. Compilation and automated checks validate the code and simulated API behavior; they do not prove compatibility, hand alignment, docking, or synchronization in the actual game.

For an in-game check, equip one supported cosmetic on each arm, confirm hand attachment without grip, intentionally dock and re-grab it, change cosmetics, and toggle F8 off and on. Check that remote players' items and excluded throwables remain unaffected. Stop using the plugin if these checks fail.

## Build on Windows

Install the **.NET 8 SDK**, then reopen PowerShell:

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact
```

From the source folder containing `BuildHoldables.ps1`, use your actual game path:

```powershell
.\BuildHoldables.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag'
```

For a custom layout, pass `-ManagedPath` for the game's managed Unity assemblies and `-BepInExCorePath` for a clean `BepInEx/core` folder. Both overrides can be used without `-GamePath`. The build needs BepInEx 5, `0Harmony.dll`, and Unity references; it does not require `Assembly-CSharp.dll` or SteamVR at compile time. It compiles the plugin, runs `StickyHoldablesChecks`, and stages the package without modifying the game installation.

## Build in the cloud

The supplied environment uses .NET SDK 8 and public Unity 2021.3.33 references:

```bash
bash scripts/setup.sh
python3 - <<'PY'
from pathlib import Path
import zipfile
root = Path('/workspace/tools/references')
with zipfile.ZipFile(root / 'bepinex.zip') as archive:
    (root / '0Harmony.dll').write_bytes(archive.read('BepInEx/core/0Harmony.dll'))
PY
bash scripts/build-holdables.sh
```

The setup script downloads official build tools and reference packages. The Harmony extraction uses the same official BepInEx archive; no uploaded mod DLL is executed. If using a custom `PIGLESS_TOOL_ROOT`, adjust the extraction path to its `references` directory. Set `PIGLESS_UNITY_REFERENCE_DIR` to compile against Unity assemblies from your matching game installation instead of the public reference package.

Output:

```text
artifacts/StickyHoldables/
  BepInEx/plugins/StickyHoldables/StickyHoldables.dll
  README.md
  OldHoldables-MIT.txt
```

## Attribution

The conceptual arm-to-hand dock mapping was informed by [OldHoldables](https://github.com/lunakittyyy/OldHoldables), copyright 2025 lunakittyyy. Its complete MIT license is included as `OldHoldables-MIT.txt` in the package and at `docs/licenses/OldHoldables-MIT.txt` in the source tree.
