using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HeyJace.Core;
using UnityEngine;

namespace HeyJace;

[BepInPlugin("dev.local.heyjace", "Hey Jace", "1.3.0")]
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
    private float nextVocabularyCheck;
    private string vocabularyFingerprint = "";
    private string? speechFailure;
    private bool commandFailure;
    private float holdResultUntil;
    private readonly Queue<WireMessage> vocabulary = new();

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
        Logger.LogInfo("Hey Jace 1.3.0 loaded. F10 pauses/restarts listening. Outfit, cosmetic, name, mic mode and local time commands are available.");
    }

    private void StartSpeech()
    {
        speech?.Dispose(); speech = new SpeechBridge(); lastCommandId = 0;
        vocabulary.Clear(); vocabularyFingerprint = ""; nextVocabularyCheck = 0; speechFailure = null; commandFailure = false; holdResultUntil = 0;
        speech.Start(); hud?.Show("HEY JACE", "Starting...", 8);
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
                    speechFailure = null; commandFailure = false; holdResultUntil = 0;
                    try { hud?.Wake(sound.Value, incoming.Text); } catch (Exception e) { Logger.LogWarning("Jace chime: " + e.Message); }
                    break;
                case WireKind.Status:
                    if (!incoming.Success)
                    {
                        bool genericExit = incoming.Text.StartsWith("Speech helper stopped", StringComparison.Ordinal) ||
                            incoming.Text.StartsWith("Speech connection closed", StringComparison.Ordinal);
                        if (speechFailure == null || !genericExit) speechFailure = incoming.Text;
                        hud?.ShowPersistent("JACE • ATTENTION", speechFailure);
                        Logger.LogWarning(incoming.Text);
                    }
                    else
                    {
                        bool ready = incoming.Text.StartsWith("Listening", StringComparison.Ordinal);
                        bool heard = incoming.Text.StartsWith("Heard:", StringComparison.Ordinal);
                        if (ready) speechFailure = null;
                        if (heard) { commandFailure = false; holdResultUntil = 0; }
                        bool diagnostic = heard || incoming.Text.StartsWith("Recognition:", StringComparison.Ordinal);
                        if (!diagnostic && speechFailure == null && !(ready && (commandFailure || Time.unscaledTime < holdResultUntil)))
                            hud?.Show("HEY JACE", ready ? "Listening." : incoming.Text, 8);
                        Logger.LogInfo(incoming.Text);
                    }
                    break;
                case WireKind.Command:
                    if (incoming.Command == null || incoming.Id <= lastCommandId) break;
                    lastCommandId = incoming.Id;
                    commandFailure = false;
                    Logger.LogInfo("Voice command " + incoming.Command.Kind + ": " + incoming.Command.Argument);
                    ActionResult result;
                    if (Time.unscaledTime < nextAction) result = new ActionResult(false, "One command at a time, please.");
                    else
                    {
                        nextAction = Time.unscaledTime + .35f;
                        try { result = actions?.Execute(incoming.Command) ?? new ActionResult(false, "The local player is not ready yet."); }
                        catch (Exception e) { result = new ActionResult(false, "Game command could not complete: " + e.GetBaseException().Message); }
                    }
                    holdResultUntil = Time.unscaledTime + 6;
                    if (result.Success) hud?.Show("JACE • DONE", result.Message, 8);
                    else { commandFailure = true; hud?.ShowPersistent("JACE • CHECK", result.Message); }
                    speech.SendResult(incoming.Id, result);
                    if (result.Success) Logger.LogInfo(result.Message); else Logger.LogWarning(result.Message);
                    break;
            }
        }
        try { SendCosmeticVocabulary(); }
        catch (Exception e)
        {
            nextVocabularyCheck = Time.unscaledTime + 30;
            vocabulary.Clear();
            Logger.LogWarning("Cosmetic voice names not ready: " + e.GetBaseException().Message);
        }
    }

    private void SendCosmeticVocabulary()
    {
        if (speech?.Connected != true || actions == null) return;
        if (vocabulary.Count == 0 && Time.unscaledTime >= nextVocabularyCheck)
        {
            nextVocabularyCheck = Time.unscaledTime + 15;
            IReadOnlyList<string> names = actions.GetOwnedCosmeticNames();
            if (names.Count > 0)
            {
                string fingerprint = string.Join("|", names);
                if (fingerprint != vocabularyFingerprint)
                {
                    vocabularyFingerprint = fingerprint;
                    vocabulary.Enqueue(new WireMessage(WireKind.Vocabulary, 0));
                    var chunk = new StringBuilder();
                    foreach (string name in names)
                    {
                        if (chunk.Length + name.Length + 1 > 500)
                        {
                            vocabulary.Enqueue(new WireMessage(WireKind.Vocabulary, 1, text: chunk.ToString()));
                            chunk.Clear();
                        }
                        if (chunk.Length > 0) chunk.Append('|');
                        chunk.Append(name);
                    }
                    if (chunk.Length > 0) vocabulary.Enqueue(new WireMessage(WireKind.Vocabulary, 1, text: chunk.ToString()));
                    vocabulary.Enqueue(new WireMessage(WireKind.Vocabulary, 2));
                    Logger.LogInfo("Teaching offline speech " + names.Count + " actual owned cosmetic names.");
                }
            }
        }
        // Leave capacity for command results; never block the Unity thread.
        for (int n = 0; n < 2 && vocabulary.Count > 0; n++)
        {
            WireMessage frame = vocabulary.Peek();
            if (!speech.TrySendVocabulary(frame.Id, frame.Text)) break;
            vocabulary.Dequeue();
        }
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextHudRetry) return;
        try { hud?.Tick(); }
        catch (Exception e) { nextHudRetry = Time.unscaledTime + 3; Logger.LogWarning("Jace popup: " + e.GetBaseException().Message); }
    }

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
