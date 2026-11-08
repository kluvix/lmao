# Pigless mods: recovered source and changes

**Hey Jace 1.2.0** is the separate offline Windows voice-assistant mod, with in-game chat notifications, numeric Gorilla Tag RGB codes, actual owned cosmetic names, and numbered saved outfits. See [its commands, installation, and validation notes](docs/HEY_JACE.md). Build it with `bash scripts/build-jace.sh` or `BuildJace.ps1`; install the complete plugin folder, including its speech helper.

**Pigless Bunny** adds a local low-poly bunny companion with grip pickup, hand hopping, and shoulder perching. See [its controls, build, and validation notes](docs/PIGLESS_BUNNY.md); build it independently with `bash scripts/build-bunny.sh` or `BuildBunny.ps1`.

The separate **Sticky Holdables** plugin keeps equipped local holdables attached without holding grip. Press **F8** to restore normal grip behavior. See [its build, installation, and validation notes](docs/STICKY_HOLDABLES.md); it builds independently with `bash scripts/build-holdables.sh` or `BuildHoldables.ps1`.

Recovered from the supplied Pigless Camera 1.3.0 and Pigless Sheesh 2.2.0 assemblies. The edited versions build as **Camera 1.4.0** and **Sheesh 2.3.0**. This is decompiled source, not the original author's source tree, comments, or project layout.

- `originals/`: untouched original mod DLLs retained locally, with SHA-256 checksums. The DLLs are excluded from GitHub; the checksums are included.
- `recovered/`: unedited ILSpy recovery baseline, including the original video code.
- `src/`: edited, buildable projects. Video recording is removed here.
- `lib/game/`: supplied game reference DLLs, retained locally and excluded from Git. Their hashes are recorded. Do not deploy these copies over game files.
- `scripts/`: repeatable setup, build, and source-recovery commands.
- `docs/VALIDATION.md`: what was checked and what still needs the game.

## Build DLLs with PowerShell on Windows

Install the **.NET 8 SDK** once (BepInEx 5 must already be installed in your game):

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact
```

Reopen PowerShell, open the source folder, and run this command, replacing the game path with yours:

```powershell
.\Build.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag'
```

If Windows marks the downloaded script as blocked, review `Build.ps1`, then unblock that file and allow local scripts for this shell session:

```powershell
Unblock-File -LiteralPath .\Build.ps1
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned
```

Run these commands from the extracted source folder containing `Build.ps1`, not from inside the ZIP. The SDK selection accepts installed .NET 8 SDK feature bands rather than requiring 8.0.419 specifically.

The script locates the game's `*_Data/Managed` folder and `BepInEx/core`, uses the actual game/Unity assemblies, compiles both projects, and runs the checks. The resulting files are:

```text
artifacts/BepInEx/plugins/Pigless/PiglessCamera.dll
artifacts/BepInEx/plugins/Pigless/PiglessSheesh.dll
```

It stages files locally; install the DLLs manually after backing up/removing old mod copies. Add your image as `assets/jacesus.png` before building to include the pillow texture. No Unity editor or decompiler is required to compile the recovered source.

For a custom layout, supply `-ManagedPath 'D:\path\to\Managed'` and/or `-BepInExCorePath 'D:\path\to\BepInEx\core'`. Keep source files under `src/`; `recovered/` is an archival baseline and is not built.

## Build

```bash
cd /workspace/lmao
bash scripts/setup.sh
bash scripts/build.sh
```

Setup installs .NET SDK 8.0.419, ILSpy 9.1.0.7988, BepInEx 5.4.23.5 references, and public Unity 2021.3.33 reference assemblies under `/workspace/tools`. It does not execute the uploaded DLLs or change the recovery baseline. Existing game references are reused.

The required uploaded build references are `Assembly-CSharp.dll`, `SteamVR.dll`, and `SteamVR_Actions.dll`. `Unity.InputSystem.dll` and `Unity.XR.OpenVR.dll` are also retained, though the mod code continues using its existing SteamVR action bindings. No further game assemblies were needed for the successful builds in this environment.

For builds against your exact Unity installation, set `PIGLESS_UNITY_REFERENCE_DIR` to its managed Unity assemblies directory. Set `PIGLESS_GAME_MANAGED_DIR` to use game references elsewhere. The original mods use Unity 6's `linearVelocity`; the edited code uses the compatible `velocity` alias so it can compile against the public reference package. Exact Unity 6 runtime compatibility is still an in-game check. The build never ships reference assemblies.

`bash scripts/recover.sh /workspace/new-recovery` produces another decompilation in a new directory. A source-only GitHub download needs the original mod DLLs placed in `originals/` first; they are preserved in this cloud workspace but are not published. It refuses to overwrite existing output. Source recovery is not part of normal installation or startup.

## Controls and features

- Double **X** opens/closes Sheesh. Double **Y** opens/hides the camera. Double X also hides the camera when returning to Sheesh.
- Brown **#8E5500**, white text, existing tabs and camera modes are retained.
- Sheesh supports its original right-hand pointer + trigger and adds physical touch as an alternative. Button sounds accompany actions. Sound failure does not block actions.
- TAG GUN now closes the menu when enabled so aiming becomes available immediately. Hold right grip and press trigger. Normal tags still use the game's validation and range checks. TAG ALL retains the existing room-host/infection requirements. Missing APIs and rejected actions produce status messages; initialization can retry instead of disabling tags permanently. Host API capability no longer depends on the gun validator.
- Camera buttons activate on free-hand contact, without a pointer or trigger. Withdraw before pressing again; sliding across buttons does not repeat actions. Either grip moves the tablet. When held in the right hand, use the left hand for buttons.
- Photos remain PNGs in Pictures/PiglessCamera (or the BepInEx config directory if Pictures is unavailable). Touch PHOTO or press B to start **two seconds** of countdown. Repeated presses do not restart it. Hiding the panel preserves a pending shot; disabling the plugin cancels it. Video capture, rolling buffers, AVI export, and the CLIP button are removed.
- **TOOLS → TIME OF DAY** cycles the supplied game's lighting presets, then restores the previous time mode/index. Reset/disable restores it too. This changes local lighting, with no room RPC.
- **TOOLS → JACESUS** spawns/removes a local body pillow. Grip near it with either hand to grab; release to throw. It has a curved mesh, full-photo UVs on both sides, gravity, world collisions, and capped throw velocity. It does not synchronize to other players. Reset/disable removes it.

## Pillow image and deployment

The image displayed in chat was not provided as an accessible image file. **Its texture is not included.** Upload the exact PNG or place it at `assets/jacesus.png`, then rerun the build. Until then, JACESUS shows `NEED JACESUS.PNG` instead of spawning a substitute.

Build output is staged under `artifacts/BepInEx/plugins/Pigless/`. Back up old copies of these two mods outside the game plugins directory, then remove them from plugins (including versioned filenames such as PiglessCamera-1.3.0.dll). Copy the staged Pigless directory into the game's BepInEx/plugins directory, so each mod is loaded only once. For the pillow, the image must end up at `BepInEx/plugins/Pigless/assets/jacesus.png`. Keep BepInEx, SteamVR, Unity, and game dependencies from the installed game; do not copy development reference assemblies into the game.

The current release builds are development candidates. No game or VR runtime is available here; see the validation checklist before relying on them in-game.
