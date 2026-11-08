# Pigless Bunny

A local Gorilla Tag pet with an original low-poly bunny model: chestnut fur, cream accents, pink ears, and a small pixel texture. It follows you, hops onto your hand, and rests on your shoulder. Only you can see it.

## Install

Finish malware cleanup first and use a fresh, trusted BepInEx 5 installation. Do not restore previously infected mod or compiler DLLs.

1. Close Gorilla Tag.
2. Copy `PiglessBunny.dll` into `Gorilla Tag/BepInEx/plugins/PiglessBunny/`.
3. Start the game. The bunny starts in follow mode once your local player is ready.

Keep only one copy of the plugin installed. The package contains the new plugin DLL and this README; it does not include BepInEx, Unity, game DLLs, or compiler dependencies.

## Controls

- **F6:** switch between following and sitting on your right shoulder.
- **F7:** hide or show the bunny.
- **Pick up:** press grip with a hand near the bunny. Release grip to set it down, or place it on your shoulder by releasing nearby.
- **Offer a hand:** hold a nearby open hand still for about **0.65 seconds** and the bunny hops onto it. Grip and release that hand to set it down.
- **Hand to shoulder:** hold the perched bunny near your shoulder for about **0.55 seconds** to move it there.
- **Idle shoulder perch:** stand still for about **6 seconds** and the bunny joins your shoulder automatically.

The bunny is a local cosmetic companion. It has no colliders or rigidbodies and does not synchronize to other players, send gameplay network messages, or trigger controller haptics. It uses environment raycasts for following and ground placement.

The BepInEx config lets you change size, keyboard shortcuts, visibility, and automatic hand/shoulder perching. No additional asset download is required.

## Build on Windows

Install the **.NET 8 SDK**, then reopen PowerShell:

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact
```

From the source folder containing `BuildBunny.ps1`, run this with your game path:

```powershell
.\BuildBunny.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag'
```

For a custom installation, pass `-ManagedPath` for the game's managed Unity assemblies and `-BepInExCorePath` for clean `BepInEx/core` references. Both overrides can be supplied without `-GamePath`.

The build needs BepInEx 5 and the Unity facade, CoreModule, and PhysicsModule assemblies. It uses reflection for local player binding, so `Assembly-CSharp.dll`, SteamVR, XRModule, and Harmony are not compile dependencies. The script compiles the plugin, runs the pure behavior checks, then stages the package without modifying the game installation.

## Build in the cloud

Use the existing official tool/reference setup if needed, then build:

```bash
bash scripts/setup.sh
bash scripts/build-bunny.sh
```

The setup script uses official .NET, BepInEx, and public Unity reference packages. Builds use .NET 8 and public Unity 2021.3.33 references by default; set `PIGLESS_UNITY_REFERENCE_DIR` to matching game Unity assemblies for an exact-reference build. `PIGLESS_TOOL_ROOT` can select a custom tools directory. No uploaded game or compiler DLL is executed.

Output:

```text
artifacts/PiglessBunny/
  BepInEx/plugins/PiglessBunny/PiglessBunny.dll
  README.md
```

## Validation

The Release build passed with zero warnings and errors. The behavior suite passed **36 cases and 154 assertions**, including both-hand pickup/release, missing input, single-controller tracking recovery, shoulder placement, perching timers, and invalid data. The local head/hand/grip/scale APIs and torso shoulder dock were checked against the user's uploaded `Assembly-CSharp.dll` metadata without running that DLL. SteamVR references were inspected but are not required by this plugin.

Automated checks exercise the bunny's behavior decisions as pure .NET logic. They do not run Gorilla Tag, Unity's native engine, or a headset. No in-game run is available in this development environment, so compilation and tests do not establish rendering, hand alignment, map traversal, or exact game-version compatibility.

The generated bunny uses 26 renderers with shared faceted meshes and owned pixel textures. All geometry/materials are created locally and cleaned up when the plugin is disabled. Static output inspection found no embedded resources, native imports, or network/process references.

The optional `scripts/Download-PiglessBunny.ps1` downloads a published copy over HTTPS and verifies the exact release SHA-256. It refuses an existing output file and never installs or executes the downloaded DLL. It requires a published download URL; use the chat's direct DLL/ZIP download until GitHub publication is confirmed.

In the game, check follow mode on floors and slopes, both-hand pickup and release, open-hand hopping, shoulder transfers, idle perching, F6, and F7. Confirm the bunny does not block movement. Check the BepInEx log if local player binding is unavailable, and disable/remove the plugin if it behaves incorrectly.
