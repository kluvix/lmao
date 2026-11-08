# Hey Jace 1.2.0

An offline voice assistant for **Windows 64-bit Gorilla Tag with BepInEx 5**. Say **"hey jace"** to hear a generated activation chime, see a popup, and get one of these spoken greetings:

- "what's up"
- "what do you want chud"
- "whats goody jacemcgames"

The popup uses a cyan **[Jace]** tag followed by bold white text with a dark shadow. It is inset farther right in the lower part of the VR view so the text is easier to see through both eyes. There is no separate PC/desktop overlay; a monitor mirroring the headset view can still show the same in-game message. Wake greetings, short listening prompts, and results use this style. The long generic "Try..." help message has been removed. Command failures stay visible so you can read the reason. Spoken replies use Windows' default audio output at a moderately faster rate. Recognition uses Windows' default recording device, so select your headset microphone there before starting the game.

This update improves mixed room codes such as **JACE34**, spoken outfit numbers, and Gorilla Tag RGB numbers. Room spelling and numeric commands have stronger recognition grammars. Unrecognized wording keeps the active reply window open for another attempt, with a short retry message. The helper can consider a bounded alternative transcription when the top result cannot form a valid command. These changes improve the supported recognition paths; microphone accuracy still depends on the Windows speech engine and needs a Windows play test.

## Install

1. Close Gorilla Tag and use a clean, trusted BepInEx 5 installation. When updating, replace the entire old `BepInEx/plugins/HeyJace` folder with this version so the plugin, core DLL, and speech helper match. Do not leave another HeyJace DLL elsewhere under `plugins`.
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

Say the command in the same utterance as "hey jace", or wait until the greeting finishes and the popup says it is listening before speaking. Each wake allows one command; follow-up answers have a 10-second window that pauses during spoken replies. Argument and retry hints are text-only, so Jace keeps listening while you finish your answer. Greetings and actual command results are spoken. Invalid answers prompt a retry within a fresh answer window. Say "cancel" to discard a pending command.

| Action | Examples after "hey jace" |
| --- | --- |
| Join a room | "join code jace three four", "join code jace thirty four", or "join code" then "BANANA" |
| Spell a code | "join code bravo alpha november alpha november alpha" |
| Change color | "change my color to nine zero five", or "change my color to" then "nine zero five" |
| Set local time | "change the time to morning", "change the time to night", "change the time to six AM", "change the time to 2" |
| Restore normal time | "change the time to auto" or "change the time to normal" |
| Equip an owned cosmetic | "equip" followed by its wardrobe display name |
| Remove a worn cosmetic | "unequip" followed by its wardrobe display name |
| Equip a saved outfit | "equip outfit two", or "equip outfit" then "two" |

Colors use exactly **three numbers from 0 to 9: red, green, blue**, matching Gorilla Tag's computer. Say each number separately, such as "nine zero five" for 905. Supported whole-number transcriptions are normalized too. Named colors and hex colors are not accepted. Room codes accept 1–10 letters or digits, including spelled letters, NATO words, and a word followed by digits such as "jace three four" or "jace thirty four" for JACE34. Time supports dawn/morning/sunrise, day, noon/midday, afternoon, sunset, evening, night, midnight, clock hours, and zero-based preset indices available in the current map.

Use the cosmetic's **full actual shop/wardrobe name**, rather than an internal item ID or a shortened description. Jace teaches its offline recognizer the names from your loaded owned inventory. Matching ignores case, spacing, and punctuation; ambiguous names report an error. Cosmetics must already belong to your wardrobe; this does not unlock items.

Outfit numbers start at **1** and use the game's existing saved wardrobe outfits, in the same order as the wardrobe. Save the desired outfits in the game first. This command switches an existing slot through the normal wardrobe controls; it respects the number of slots available to your account.

Time overrides affect your local view and restore when the plugin unloads, the map changes, you join a room, or the game's time controls take over. Available presets and game commands depend on the installed game version.

## Troubleshooting

If startup reports missing speech or microphone support, check the English Speech language option, desktop microphone permission, and default recording device, then focus the game and press F10 to restart listening. If recognition hears the chime or replies through speakers, use headphones. Missing voices affect spoken replies; status messages still appear in the game and `BepInEx/LogOutput.log`.

If the helper is missing, copy the whole plugin folder again. A game update can change reflected APIs; a failed action reports an error instead of claiming success. Windows microphone capture, installed voices, VR rendering, background execution, and live game actions require a Windows play test.

If waking works but a command does nothing, wait for Jace to finish speaking, then try a complete phrase such as "hey jace change my color to nine zero five". The popup shows short prompts and specific game errors. Recognition diagnostics stay in `BepInEx/LogOutput.log` rather than filling the VR view: look for `Heard:`, `Recognition:`, and `Voice command` entries. If recognition still fails, those entries show what the Windows engine actually heard, its selected grammar, and bounded alternatives. If a command reaches `Voice command` but fails, include the exact error text when reporting it. Only activated command text is logged; this mod does not save microphone audio or transcribe idle conversation to its log.

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

Both build scripts compile the plugin, run the core behavior checks, real local named-pipe checks, game boundary checks against fake API fixtures, and managed speech-host regression checks, publish the Windows helper, then stage `artifacts/HeyJace/`. Only the two project DLLs, speech executable, and this README enter that package. Reference/game DLLs are never shipped. The scripts do not install into the game or execute the speech helper or game. Publishing needs access to official NuGet packages unless cached.

Core and pipe checks can run on Linux. Builds and game assembly metadata inspection do not verify Windows audio, voices, VR presentation, or in-game behavior; those need the Windows play test described above.

## Validation record

- Full plugin/core Release build: zero warnings and errors.
- Parser, mixed word/numeric room codes, spoken numbers, all 1,000 RGB codes, outfits, command windows, confidence/retry handling, aliases, cancellation and wire format **1,942 automated checks passed**, including malformed-message cases.
- Actual local named-pipe bridge: **20 assertions passed**, including duplex delivery, owned-name vocabulary transport, invalid handshakes, truncated/oversized frames, and a nonreading helper that cannot freeze main-thread replies or shutdown.
- Game boundary: **35 assertions passed**, exercising the real adapter against fake APIs for owned-name equip/unequip, saved-outfit indexing, slot limits, RGB scaling, room joins, local time restoration, thread checks, and disposal. These fixtures do not establish that the current live game accepts every command.
- Managed speech host: **196 checks passed**, including bounded owned-name vocabulary, spoken outfit choices, room grammar/parser alignment, alternate-transcript selection, deferred greeting guards, and the recognition-completion race handler without starting native speech.
- The Windows x64 speech helper was successfully published as a self-contained executable. It includes the runtime; it does not need a separate .NET installation.
- Room, color, time and wardrobe APIs were inspected in the supplied game assembly as metadata/decompiled text without running that assembly.
- Windows microphone capture, spoken recognition/replies, headset popup appearance, and actual game actions have **not** been run here. This is an in-game test build.
