# Built mod DLLs

`StickyHoldables.dll` is the standalone Sticky Holdables 1.0.0 plugin. Its Release build passed with zero warnings/errors, and 21 synthetic API-contract cases (143 assertions) passed. It is not game-tested. See [Sticky Holdables instructions](../docs/STICKY_HOLDABLES.md). Finish malware cleanup before installing any plugin.

To download Sticky Holdables with PowerShell, the source includes [Download-StickyHoldables.ps1](../scripts/Download-StickyHoldables.ps1). It saves the DLL to Downloads, checks its SHA-256 before keeping it, and never runs the DLL, replaces an existing file, or changes the game installation.

These are the newly compiled Camera 1.4.0 and Sheesh 2.3.0 outputs from the source in this repository. They are not the original uploaded DLLs. No game, SteamVR, Unity, or BepInEx dependency assemblies are included.

Both builds completed with zero warnings/errors and 55 input/control/API metadata assertions passed. Unity, VR, multiplayer, lighting, sound, photos, and prop physics still require in-game validation. The pillow needs the exact user image installed as BepInEx/plugins/Pigless/assets/jacesus.png; its texture is not included.

Back up old mod DLLs outside the game's plugins directory before replacing them. Use each mod only once in BepInEx/plugins.
