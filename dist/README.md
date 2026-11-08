# Built mod DLLs

`HeyJace-1.0.0-Windows-x64.zip` is the complete offline Windows voice-assistant package. Install its whole `BepInEx/plugins/HeyJace` folder, including `Speech/HeyJace.Speech.exe`. The self-contained helper needs no .NET installation. See [Hey Jace commands and setup](../docs/HEY_JACE.md). Release builds and 777 parser/protocol checks plus 12 live pipe assertions passed; Windows speech, headset UI and actual game actions still need an in-game test.

`PiglessBunny.dll` is the standalone local bunny companion, version 1.0.0. It uses an original low-poly, pixel-textured model with grip pickup and shoulder perching. Its Release build and 36 behavior cases (154 assertions) passed, and player/controller APIs were inspected against the supplied game metadata. Rendering and VR interaction still need an in-game check. See [Pigless Bunny instructions](../docs/PIGLESS_BUNNY.md).

`StickyHoldables.dll` is the standalone Sticky Holdables 1.0.1 plugin. It mutes repeated hover vibration from carried local holdables. Its Release build passed with zero warnings/errors, and 25 synthetic API-contract cases (201 assertions) passed. It is not game-tested. See [Sticky Holdables instructions](../docs/STICKY_HOLDABLES.md). Replace the older StickyHoldables copy rather than installing both versions.

To download Sticky Holdables with PowerShell, the source includes [Download-StickyHoldables.ps1](../scripts/Download-StickyHoldables.ps1). It saves the DLL to Downloads, checks its SHA-256 before keeping it, and never runs the DLL, replaces an existing file, or changes the game installation.

These are the newly compiled Camera 1.4.0 and Sheesh 2.3.0 outputs from the source in this repository. They are not the original uploaded DLLs. No game, SteamVR, Unity, or BepInEx dependency assemblies are included.

Both builds completed with zero warnings/errors and 55 input/control/API metadata assertions passed. Unity, VR, multiplayer, lighting, sound, photos, and prop physics still require in-game validation. The pillow needs the exact user image installed as BepInEx/plugins/Pigless/assets/jacesus.png; its texture is not included.

Back up old mod DLLs outside the game's plugins directory before replacing them. Use each mod only once in BepInEx/plugins.
