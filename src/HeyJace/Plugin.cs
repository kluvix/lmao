using System;
using BepInEx;
using BepInEx.Configuration;
using HeyJace.Core;
using UnityEngine;

namespace HeyJace;

[BepInPlugin("dev.local.heyjace", "Hey Jace", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> listening = null!, sound = null!;
    private ConfigEntry<KeyboardShortcut> toggle = null!;
    private SpeechBridge? speech;
    private GameActions? actions;
    private JaceHud? hud;
    private bool previousBackground, changedBackground;
    private int lastCommandId;
    private float nextAction, nextHudRetry;

    private void Awake()
    {
        listening = Config.Bind("Speech", "Enabled", true, "Listen for Hey Jace using the local Windows default microphone. F10 pauses/restarts the helper.");
        sound = Config.Bind("Feedback", "ActivationSound", true, "Play the generated two-note activation chime.");
        toggle = Config.Bind("Controls", "ToggleListeningKey", new KeyboardShortcut(KeyCode.F10), "Pause or restart background voice recognition.");
        listening.SettingChanged += ListeningChanged;
    }

    private void OnEnable()
    {
        previousBackground = Application.runInBackground;
        changedBackground = !previousBackground;
        Application.runInBackground = true;
        actions = new GameActions(); hud = new JaceHud();
        if (listening.Value) StartSpeech();
        Logger.LogInfo("Hey Jace 1.0.0 loaded. F10 pauses/restarts listening. Game commands run while the game remains running, including unfocused/minimized.");
    }

    private void StartSpeech()
    {
        speech?.Dispose(); speech = new SpeechBridge(); lastCommandId = 0;
        speech.Start(); hud?.Show("HEY JACE", "Starting offline Windows speech...", 8);
    }

    private void ListeningChanged(object sender, EventArgs e)
    {
        if (!isActiveAndEnabled) return;
        if (listening.Value) StartSpeech();
        else { speech?.Dispose(); speech = null; hud?.Show("JACE • PAUSED", "Microphone listening stopped. F10 resumes."); }
    }

    private void Update()
    {
        try
        {
            if (toggle.Value.IsDown())
            {
                if (listening.Value && speech?.Running != true) StartSpeech();
                else listening.Value = !listening.Value;
            }
        }
        catch (Exception e) { Logger.LogWarning("Listening shortcut unavailable: " + e.Message); }
        try { actions?.Tick(); }
        catch (Exception e) { Logger.LogWarning("Jace time update: " + e.GetBaseException().Message); }
        for (int n = 0; n < 8 && speech != null && speech.TryRead(out WireMessage? incoming); n++)
        {
            if (incoming == null) continue;
            switch (incoming.Kind)
            {
                case WireKind.Wake:
                    try { hud?.Wake(sound.Value); } catch (Exception e) { Logger.LogWarning("Jace chime: " + e.Message); }
                    break;
                case WireKind.Status:
                    hud?.Show("HEY JACE", incoming.Text, 8); Logger.LogInfo(incoming.Text);
                    break;
                case WireKind.Command:
                    if (incoming.Command == null || incoming.Id <= lastCommandId) break;
                    lastCommandId = incoming.Id;
                    ActionResult result;
                    if (Time.unscaledTime < nextAction) result = new ActionResult(false, "One command at a time, please.");
                    else
                    {
                        nextAction = Time.unscaledTime + .35f;
                        try { result = actions?.Execute(incoming.Command) ?? new ActionResult(false, "The local player is not ready yet."); }
                        catch (Exception e) { result = new ActionResult(false, "Game command could not complete: " + e.GetBaseException().Message); }
                    }
                    hud?.Show(result.Success ? "JACE • DONE" : "JACE • CHECK", result.Message, 8);
                    speech.SendResult(incoming.Id, result); Logger.LogInfo(result.Message);
                    break;
            }
        }
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextHudRetry) return;
        try { hud?.Tick(); }
        catch (Exception e) { nextHudRetry = Time.unscaledTime + 3; Logger.LogWarning("Jace popup: " + e.GetBaseException().Message); }
    }
    private void OnGUI() { try { hud?.DrawDesktop(); } catch { } }

    private void OnDisable()
    {
        try { speech?.Dispose(); } catch (Exception e) { Logger.LogWarning("Speech cleanup: " + e.Message); }
        finally { speech = null; }
        try { actions?.Dispose(); } catch (Exception e) { Logger.LogWarning("Time/action cleanup: " + e.Message); }
        finally { actions = null; }
        try { hud?.Dispose(); } catch (Exception e) { Logger.LogWarning("Popup cleanup: " + e.Message); }
        finally { hud = null; }
        if (changedBackground) Application.runInBackground = previousBackground;
        changedBackground = false;
    }
    private void OnDestroy() { listening.SettingChanged -= ListeningChanged; OnDisable(); }
}
