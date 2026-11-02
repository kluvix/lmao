# Validation record

## Completed in the cloud

- Original mod uploads copied byte-for-byte, SHA-256 hashes recorded; source recovered with ILSpy.
- .NET SDK 8.0.419 and ILSpy 9.1.0.7988 installed.
- Both edited netstandard2.1 projects build in Release with zero warnings/errors against uploaded game/SteamVR assemblies, official BepInEx 5.4.23.5, and public Unity 2021.3.33 reference assemblies.
- 55 executable assertions passed, covering double-press activation/timeout, held-button behavior, contact debounce/re-entry, exactly two seconds before photo capture, duplicate-photo suppression/cancellation, camera grabs, tablet action hit regions/settings occlusion, removal of CLIP, photo resolution, and required tag/time-of-day API metadata in the supplied game DLL.
- Successful build checks are compiler/API evidence, not Unity runtime, multiplayer, audio, rendering, or physics evidence. Tests inspect game metadata without executing game code.
- Reusable setup and build scripts exercised in their documented directory; source recovery script exercised against a new output directory.
- Build.ps1 executed with PowerShell 7.4.13 on Linux against a simulated game directory layout (including spaces) backed by the real supplied/reference DLLs. Both builds and all 55 assertions passed. Windows PowerShell 5.1 execution and builds against the actual Windows game installation remain untested.

## Still required in-game

1. Load each mod alone and both together. Confirm BepInEx log versions 1.4.0/2.3.0 and no missing-method/type errors. Public Unity references are older than the Unity 6 API indicated by the original mods; actual Unity modules/runtime remain the compatibility authority.
2. Double X toggles Sheesh once; a single or held X does not. Double Y toggles the camera. Opening one panel closes the other as before.
3. Every TAGS tab/button responds with sound/haptics and a visible status. Enable TAG GUN: menu closes, grip aims, a new trigger press fires. Exercise no-room, out-of-range, cooldown, non-host TAG ALL, wrong mode, host infection, and empty eligible-player cases. Confirm game acceptance and remote state; these outcomes cannot be established by compilation. Supplied API signatures match the original reflection bindings, so an API-version mismatch was not confirmed as the original reported fault.
4. Camera has no pointer beam or CLIP button. Touch every button with either free hand, including both settings pages, at different tablet scales. Hold contact and slide: no repeated toggles. Withdraw and retouch: exactly one action. Check the 2.5 cm contact tolerance against controller/palm pose alignment on the actual headset.
5. Verify click sounds in both mods, appropriate volume/spatial position, and no audio objects left after unload. Verify #8E5500 and white text in rendered UI.
6. Touch PHOTO and press B: countdown 2 → 1 → one PNG at/after two seconds. Spam does not reset it. Check mirror, selected camera pose, image encoding, filename collisions, disk permissions, hiding the tablet during countdown, and disabling the plugin. No AVI or rolling capture remains.
7. Cycle every TOOLS/TIME OF DAY preset and back to default, reset, disable, and change maps. Confirm lighting, skybox, shadows, and restoration. Map/server scripts or other mods may override local lighting.
8. Supply the exact PNG at `BepInEx/plugins/Pigless/assets/jacesus.png`. Spawn JACESUS and confirm image orientation/quality on both sides, hand reach, stable grip offsets, release direction/speed/spin, world collision, and gravity. Confirm it doesn't push the player or interfere with locomotion. Test loss of controller input and repeated spawn/despawn/reset. It is intentionally a local prop; other players will not see it.

## Outstanding input

The exact PNG shown in chat is unavailable as a downloadable attachment. Pillow code compiles, but the requested appearance is incomplete until that file is supplied. No replacement texture was invented.
