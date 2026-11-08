# Hey Jace

An offline voice assistant for **Windows 64-bit Gorilla Tag with BepInEx 5**. Say **"hey jace"** to hear a generated activation chime, see a popup, and get one of these spoken greetings:

- "what's up"
- "what do you want chud"
- "elite is better than you and what do you want"

The popup appears in the desktop game view and near the upper-left of the VR view. Spoken replies use Windows' default audio output. Recognition uses Windows' default recording device, so select your headset microphone there before starting the game.

## Install

1. Close Gorilla Tag and use a clean, trusted BepInEx 5 installation.
2. Copy the **whole `BepInEx/plugins/HeyJace` folder** from the package into the game's `BepInEx/plugins/` folder. Keep this layout:

   ```text
   BepInEx/plugins/HeyJace/
     HeyJace.dll
     HeyJace.Core.dll
     Speech/
       HeyJace.Speech.exe
   ```

3. In Windows Settings, enable microphone access for desktop apps and select a working default recording device. Install an **English Speech language option** under Time & language > Language & region, plus an English text-to-speech voice if one is unavailable.
4. Start the game. The plugin starts its speech helper automatically; do not launch the helper yourself. Keep only one copy of the plugin installed.

The packaged helper includes its .NET runtime; installing the .NET SDK or a separate .NET runtime is unnecessary for playing. Recognition and speech run locally: no API key, cloud voice service, or voice upload is used. Gorilla Tag must remain running, but it can be unfocused or minimized. The plugin enables background game updates and restores the previous setting when unloaded.

**F10 pauses or restarts listening.** Keyboard shortcuts use the game's input and should be pressed with the game focused. The helper exits when the plugin unloads or the game closes. Settings are in `BepInEx/config/dev.local.heyjace.cfg`.

## Commands

Say the command in the same utterance as "hey jace", or wait for the greeting and then speak it. Each wake allows one command; follow-up answers have a 10-second window that pauses during spoken replies. Say "cancel" to discard a pending command.

| Action | Examples after "hey jace" |
| --- | --- |
| Join a room | "join code BANANA", or "join code" then "BANANA" |
| Spell a code | "join code bravo alpha november alpha november alpha" |
| Change color | "change my color to red", "change my color to 905", "change my color to nine zero five" |
| Set local time | "change the time to morning", "change the time to night", "change the time to six AM", "change the time to 2" |
| Restore normal time | "change the time to auto" or "change the time to normal" |
| Equip an owned cosmetic | "equip" followed by its wardrobe display name |
| Remove a worn cosmetic | "unequip" followed by its wardrobe display name |

Color parsing also accepts six-digit `#RRGGBB` values when recognized as text; spoken color names or three digits are easier to dictate. The three digits use the game's 0–9 RGB scale. Room codes accept 1–10 letters or digits, including spelled letters and NATO words. Time supports dawn/morning/sunrise, day, noon/midday, afternoon, sunset, evening, night, midnight, clock hours, and zero-based preset indices available in the current map.

Cosmetics must already belong to your wardrobe; this does not unlock items. Use the full display name if several items match. Time overrides affect your local view and restore when the plugin unloads, the map changes, you join a room, or the game's time controls take over. Available presets and game commands depend on the installed game version.

## Troubleshooting

If startup reports missing speech or microphone support, check the English Speech language option, desktop microphone permission, and default recording device, then focus the game and press F10 to restart listening. If recognition hears the chime or replies through speakers, use headphones. Missing voices affect spoken replies; status messages still appear in the game and `BepInEx/LogOutput.log`.

If the helper is missing, copy the whole plugin folder again. A game update can change reflected APIs; a failed action reports an error instead of claiming success. Windows microphone capture, installed voices, VR rendering, background execution, and live game actions require a Windows play test.

## Build

Building requires the **.NET 8 SDK** and clean BepInEx 5/Unity references. Install the SDK on Windows, reopen PowerShell, then run from the source folder:

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact
.\BuildJace.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag'
```

Alternatively, pass both `-ManagedPath` (the game's managed Unity assemblies) and `-BepInExCorePath` (`BepInEx/core`). The build verifies `BepInEx.dll`, `0Harmony.dll`, `UnityEngine.dll`, and Unity's CoreModule, AudioModule, IMGUIModule, and TextRenderingModule. It does not need `Assembly-CSharp.dll` to compile.

In the configured cloud workspace:

```bash
bash scripts/setup.sh
bash scripts/build-jace.sh
```

The shell script sources `scripts/env.sh`; `PIGLESS_TOOL_ROOT` selects the tools directory and `PIGLESS_UNITY_REFERENCE_DIR` can select matching game Unity assemblies. The existing setup obtains official tool/reference dependencies. The speech build restores Microsoft's `System.Speech` 8.0.0 from NuGet and publishes a self-contained `win-x64` single executable.

Both build scripts compile the plugin, run the core behavior checks and real local named-pipe checks, publish the Windows helper, then stage `artifacts/HeyJace/`. Only the two project DLLs, speech executable, and this README enter that package. Reference/game DLLs are never shipped. The scripts do not install into the game or execute the speech helper or game. Publishing needs access to official NuGet packages unless cached.

Core and pipe checks can run on Linux. Builds and game assembly metadata inspection do not verify Windows audio, voices, VR presentation, or in-game behavior; those need the Windows play test described above.

## Validation record

- Full plugin/core Release build: zero warnings and errors.
- Parser, colors, command windows, aliases, cancellation and wire format: **777 checks passed**, including malformed-message cases.
- Actual local named-pipe bridge: **12 assertions passed**, including duplex delivery, invalid handshakes, truncated/oversized frames, and a nonreading helper that cannot freeze main-thread replies or shutdown.
- The Windows x64 speech helper was successfully published as a self-contained executable. It includes the runtime; it does not need a separate .NET installation.
- Room, color, time and wardrobe APIs were inspected in the supplied game assembly as metadata/decompiled text without running that assembly.
- Windows microphone capture, spoken recognition/replies, headset/desktop popup appearance, and actual game actions have **not** been run here. This is an in-game test build.
