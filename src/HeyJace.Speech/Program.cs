using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Threading.Channels;
using HeyJace.Core;

namespace HeyJace.Speech;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!TryReadArguments(args, out string pipeName, out int parentId))
        {
            Console.Error.WriteLine("Launch HeyJace.Speech through the HeyJace game plugin (--pipe <name> --parent <PID>).");
            return 2;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("HeyJace.Speech requires Windows desktop speech recognition and a microphone.");
            return 2;
        }

        using var stop = new CancellationTokenSource();
        try
        {
            using Process parent = Process.GetProcessById(parentId);
            if (parent.HasExited) return 0;
            Task parentWatch = WatchParentAsync(parent, stop);
            try
            {
                // The modern Windows helper enforces the current-user ACL. Unity's
                // older Mono runtime connects as a regular client to this local pipe.
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                using (var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    connectionTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await pipe.WaitForConnectionAsync(connectionTimeout.Token).ConfigureAwait(false);
                }
                using var connection = new PipeConnection(pipe);
                using var host = new SpeechHost(connection, stop);
                return await host.RunAsync().ConfigureAwait(false);
            }
            finally
            {
                stop.Cancel();
                await parentWatch.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
            or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("HeyJace.Speech: " + error.Message);
            return 1;
        }
    }

    private static async Task WatchParentAsync(Process parent, CancellationTokenSource stop)
    {
        try { await parent.WaitForExitAsync(stop.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Console.Error.WriteLine("The game process could no longer be monitored: " + error.Message); }
        stop.Cancel();
    }

    private static bool TryReadArguments(string[] args, out string pipeName, out int parentId)
    {
        pipeName = "";
        parentId = 0;
        if (args.Length != 4) return false;
        for (int index = 0; index < args.Length; index += 2)
        {
            if (args[index] == "--pipe" && pipeName.Length == 0) pipeName = args[index + 1];
            else if (args[index] == "--parent" && parentId == 0 &&
                int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)) parentId = parsed;
            else return false;
        }
        return parentId > 0 && parentId != Environment.ProcessId && pipeName.Length is > 0 and <= 160 &&
            pipeName.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
    }
}

internal sealed class SpeechHost : IDisposable
{
    private abstract record HostEvent;
    private sealed record Heard(string Text, float Confidence, long Timestamp, string Grammar, RecognitionCandidate[] Candidates) : HostEvent;
    private sealed record Rejected(long Timestamp) : HostEvent;
    private sealed record FromParent(WireMessage Message) : HostEvent;
    private sealed record SpeechFailed(string Message) : HostEvent;
    private sealed record ClockTick : HostEvent;
    private sealed record ResumeRecognition : HostEvent;
    private sealed record ResponseDue(string Text, int Version, CommandKind? Pending) : HostEvent;

    private static readonly string[] Greetings =
    {
        "what's up",
        "what do you want chud",
        "whats goody jacemcgames"
    };
    private readonly PipeConnection _connection;
    private readonly CancellationTokenSource _stop;
    private readonly Channel<HostEvent> _events = Channel.CreateBounded<HostEvent>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly VoiceSession _session = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<int> _pending = new();
    private readonly RecognitionVocabulary _vocabulary = new();
    private SpeechRecognitionEngine? _recognizer;
    private RecognitionGrammars? _grammars;
    private SpeechSynthesizer? _speaker;
    private TaskCompletionSource<bool>? _recognitionEnded;
    private volatile bool _paused = true;
    private volatile bool _disposed;
    private long _acceptAfter;
    private long _speechPausedTicks;
    private int _greetingIndex = Random.Shared.Next(Greetings.Length);
    private int _commandId;
    private bool _vocabularyDirty;
    private volatile bool _resumeWhenStopped;
    private int _resumeQueued;
    private int _responseVersion;
    private string? _unspokenGreeting;

    internal SpeechHost(PipeConnection connection, CancellationTokenSource stop)
    { _connection = connection; _stop = stop; }

    internal async Task<int> RunAsync()
    {
        CancellationToken cancellationToken = _stop.Token;
        await _connection.SendAsync(new WireMessage(WireKind.Hello, text: PipeProtocol.Handshake), cancellationToken).ConfigureAwait(false);
        Task readParent = ReadParentAsync();
        Task clockTicks = TickAsync();
        try
        {
            if (!await InitializeSpeechAsync().ConfigureAwait(false)) return 1;
            await foreach (HostEvent item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (item)
                {
                    case Heard heard when heard.Timestamp >= Interlocked.Read(ref _acceptAfter):
                        double eventTime = (double)(heard.Timestamp - _speechPausedTicks) / Stopwatch.Frequency;
                        bool wasArmed = _session.IsAwaitingCommand(eventTime);
                        RecognitionCandidate selected = RecognitionCandidates.Select(_session, heard.Candidates, eventTime);
                        string original = heard.Text;
                        heard = heard with { Text = selected.Text, Confidence = selected.Confidence };
                        IReadOnlyList<VoiceEvent> voiceEvents = _session.Process(heard.Text, heard.Confidence, eventTime);
                        if (voiceEvents.Count > 0) _responseVersion++;
                        bool woke = voiceEvents.Any(item => item.Kind == VoiceEventKind.Wake);
                        // Never send transcripts of unrelated ambient speech to
                        // the game/log. Diagnostics belong to a trusted wake.
                        if (wasArmed || woke)
                            await StatusAsync("Heard: " + heard.Text + " (" + heard.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + ")").ConfigureAwait(false);
                        if ((wasArmed || woke) && !voiceEvents.Any(value => value.Kind == VoiceEventKind.Command))
                            await StatusAsync("Recognition: grammar=" + heard.Grammar + "; best=" + original + "; alternatives=" +
                                string.Join(" / ", heard.Candidates.Take(5).Select(value => value.Text + " (" + value.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + ")"))).ConfigureAwait(false);
                        if (wasArmed && voiceEvents.Count == 0 && heard.Confidence < VoiceSession.MinimumFollowUpConfidence)
                            await StatusAsync("Didn't catch that confidently. Please say the command or answer again.").ConfigureAwait(false);
                        UpdateGrammarState();
                        string? greeting = null;
                        if (woke)
                        {
                            greeting = Greetings[_greetingIndex++ % Greetings.Length];
                            _unspokenGreeting = greeting;
                            await SendAsync(new WireMessage(WireKind.Wake, text: greeting)).ConfigureAwait(false);
                        }
                        // Dispatch a complete same-utterance command before
                        // spending time speaking the greeting. Audio failure
                        // must never prevent an already parsed game action.
                        foreach (VoiceEvent voiceEvent in voiceEvents.Where(item => item.Kind == VoiceEventKind.Command))
                            await DispatchAsync(voiceEvent.Command).ConfigureAwait(false);
                        foreach (VoiceEvent voiceEvent in voiceEvents.Where(item => item.Kind == VoiceEventKind.Prompt))
                        {
                            await StatusAsync(voiceEvent.Text).ConfigureAwait(false);
                        }
                        // Argument/retry hints stay on screen. Speaking them
                        // cancels microphone capture just as the user answers.
                        // Only a wake greeting and completed action use TTS.
                        if (voiceEvents.Count > 0) greeting ??= _unspokenGreeting;
                        string response = greeting ?? "";
                        if (response.Length > 0)
                        {
                            if (voiceEvents.Any(value => value.Kind == VoiceEventKind.Command))
                            {
                                _unspokenGreeting = null;
                                await SpeakAsync(response).ConfigureAwait(false);
                            }
                            else
                                _ = DeferResponseAsync(response, _responseVersion, _session.PendingKind);
                        }
                        break;
                    case ResponseDue due when IsCurrentResponse(due):
                        _responseVersion++;
                        _unspokenGreeting = null;
                        await SpeakAsync(due.Text).ConfigureAwait(false);
                        break;
                    case FromParent { Message.Kind: WireKind.Result } reply when _pending.Remove(reply.Message.Id):
                        Console.WriteLine(reply.Message.Text);
                        await SpeakAsync(reply.Message.Text).ConfigureAwait(false);
                        break;
                    case FromParent { Message.Kind: WireKind.Status } status:
                        Console.WriteLine(status.Message.Text);
                        break;
                    case FromParent { Message.Kind: WireKind.Vocabulary } vocabulary:
                        if (_vocabulary.Accept(vocabulary.Message)) _vocabularyDirty = true;
                        await ApplyVocabularyIfIdleAsync().ConfigureAwait(false);
                        break;
                    case Rejected rejected when rejected.Timestamp >= Interlocked.Read(ref _acceptAfter) &&
                        _session.IsAwaitingCommand((double)(rejected.Timestamp - _speechPausedTicks) / Stopwatch.Frequency):
                        await StatusAsync("Didn't catch that. Wait for the reply to finish, then say the command or answer again.").ConfigureAwait(false);
                        break;
                    case ClockTick:
                        UpdateGrammarState();
                        await ApplyVocabularyIfIdleAsync().ConfigureAwait(false);
                        break;
                    case ResumeRecognition:
                        Interlocked.Exchange(ref _resumeQueued, 0);
                        _resumeWhenStopped = false;
                        if (!_paused) break;
                        StartRecognition();
                        await ListeningAsync().ConfigureAwait(false);
                        break;
                    case SpeechFailed failure:
                        await StatusAsync(failure.Message, false).ConfigureAwait(false);
                        return 1;
                }
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException or TimeoutException)
        {
            try { await StatusAsync("Speech assistant stopped: " + error.Message, false).ConfigureAwait(false); }
            catch (Exception sendError) when (sendError is IOException or OperationCanceledException or ObjectDisposedException) { }
            return 1;
        }
        finally
        {
            _paused = true;
            _stop.Cancel();
            StopSpeech();
            await readParent.ConfigureAwait(false);
            await clockTicks.ConfigureAwait(false);
        }
    }

    private async Task<bool> InitializeSpeechAsync()
    {
        try
        {
            RecognizerInfo? info = SpeechRecognitionEngine.InstalledRecognizers()
                .OrderBy(recognizer => recognizer.Culture.Name == "en-US" ? 0 : 1)
                .FirstOrDefault(recognizer => recognizer.Culture.TwoLetterISOLanguageName == "en");
            if (info == null)
            {
                await StatusAsync("No offline English speech recognizer is installed. In Windows Settings > Time & language > Language & region, add English and install its Speech language option, then restart Gorilla Tag.", false).ConfigureAwait(false);
                return false;
            }
            _recognizer = new SpeechRecognitionEngine(info);
            _grammars = new RecognitionGrammars(_recognizer, info.Culture);
            _grammars.Load();
            _recognizer.SpeechRecognized += OnSpeechRecognized;
            _recognizer.SpeechRecognitionRejected += OnSpeechRejected;
            _recognizer.RecognizeCompleted += OnRecognizeCompleted;
            _recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(0);
            _recognizer.BabbleTimeout = TimeSpan.FromSeconds(0);
            _recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(350);
            _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(600);
            _recognizer.SetInputToDefaultAudioDevice();
            try
            {
                _speaker = new SpeechSynthesizer();
                _speaker.Rate = 2;
                _speaker.SetOutputToDefaultAudioDevice();
            }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                _speaker?.Dispose();
                _speaker = null;
                await StatusAsync("Voice replies are unavailable. Check Windows speech voices and the default playback device. " + error.Message, false).ConfigureAwait(false);
            }
            _stop.Token.ThrowIfCancellationRequested();
            StartRecognition();
            await StatusAsync("Listening offline for 'hey jace' (" + info.Culture.Name + "). Works while Gorilla Tag is unfocused or minimized. Use headphones to reduce game audio pickup.").ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException or System.PlatformNotSupportedException)
        {
            await StatusAsync("Microphone or offline speech recognition could not start. Select a working default recording device, allow desktop microphone access in Windows Settings > Privacy & security > Microphone, and install an English Speech language option. " + error.Message, false).ConfigureAwait(false);
            return false;
        }
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs args)
    {
        if (_disposed || _paused || _stop.IsCancellationRequested) return;
        long timestamp = _clock.ElapsedTicks;
        if (timestamp < Interlocked.Read(ref _acceptAfter)) return;
        var candidates = new List<RecognitionCandidate> { new(args.Result.Text, args.Result.Confidence) };
        try
        {
            foreach (RecognizedPhrase alternate in args.Result.Alternates.Take(5))
                if (!candidates.Any(value => string.Equals(value.Text, alternate.Text, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(new RecognitionCandidate(alternate.Text, alternate.Confidence));
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        _events.Writer.TryWrite(new Heard(args.Result.Text, args.Result.Confidence, timestamp, args.Result.Grammar?.Name ?? "unknown", candidates.Take(5).ToArray()));
    }

    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs args)
    {
        if (_disposed || _paused || _stop.IsCancellationRequested) return;
        _events.Writer.TryWrite(new Rejected(_clock.ElapsedTicks));
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs args)
    {
        // Capture stop intent before releasing the waiter. Its continuation can
        // restart recognition immediately and change _paused on another thread.
        bool intentionalStop = _paused;
        bool resumeWhenStopped = _resumeWhenStopped;
        _recognitionEnded?.TrySetResult(true);
        if (_disposed || _stop.IsCancellationRequested) return;
        if (resumeWhenStopped)
        {
            QueueResume();
            return;
        }
        if (intentionalStop) return;
        _events.Writer.TryWrite(new SpeechFailed("Microphone recognition stopped. Check the default recording device and restart Gorilla Tag. " +
            (args.Error?.Message ?? "The audio input ended.")));
    }

    private double SessionNow => (double)(_clock.ElapsedTicks - _speechPausedTicks) / Stopwatch.Frequency;

    private void UpdateGrammarState() => _grammars?.SetState(_session.IsAwaitingCommand(SessionNow), _session.PendingKind);

    private async Task TickAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                _events.Writer.TryWrite(new ClockTick());
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task DispatchAsync(JaceCommand? command)
    {
        if (command == null) return;
        if (_pending.Count >= 32)
        {
            await StatusAsync("Waiting for the game to finish earlier commands.", false).ConfigureAwait(false);
            return;
        }
        int id = _commandId = _commandId == int.MaxValue ? 1 : _commandId + 1;
        _pending.Add(id);
        await SendAsync(new WireMessage(WireKind.Command, id, command: command)).ConfigureAwait(false);
    }

    private async Task DeferResponseAsync(string text, int version, CommandKind? pending)
    {
        try
        {
            // A short gap after "join code" can belong to the same spoken
            // command. Keep recording briefly instead of immediately speaking
            // over (and cancelling recognition of) the user's room code.
            await Task.Delay(300, _stop.Token).ConfigureAwait(false);
            await _events.Writer.WriteAsync(new ResponseDue(text, version, pending), _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private bool IsCurrentResponse(ResponseDue response) => response.Version == _responseVersion &&
        response.Pending == _session.PendingKind && _session.IsAwaitingCommand(SessionNow);

    private Task ListeningAsync(bool includeStandby = false)
    {
        if (_paused || _disposed || _stop.IsCancellationRequested) return Task.CompletedTask;
        bool active = _session.IsAwaitingCommand(SessionNow);
        if (!active && !includeStandby) return Task.CompletedTask;
        string text = !active ? "Listening for 'hey jace'." : _session.PendingKind switch
        {
            CommandKind.SetColor => "Listening for three color digits.",
            CommandKind.EquipOutfit => "Listening for the outfit number.",
            CommandKind.Equip or CommandKind.Unequip => "Listening for the cosmetic name.",
            CommandKind.JoinRoom => "Listening for the room code.",
            CommandKind.SetTime => "Listening for the time.",
            _ => "Listening for your command."
        };
        return StatusAsync(text);
    }

    private void StartRecognition()
    {
        if (_recognizer == null || _stop.IsCancellationRequested || _disposed) return;
        UpdateGrammarState();
        _recognitionEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _acceptAfter, _clock.ElapsedTicks);
        _paused = false;
        try { _recognizer.RecognizeAsync(RecognizeMode.Multiple); }
        catch { _paused = true; throw; }
    }

    private async Task<bool> PauseRecognitionAsync()
    {
        _paused = true;
        Task? ended = _recognitionEnded?.Task;
        if (_recognizer == null || ended == null || ended.IsCompleted) return true;
        _recognizer.RecognizeAsyncCancel();
        try
        {
            await ended.WaitAsync(TimeSpan.FromSeconds(5), _stop.Token).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            // A delayed device cancellation is not a reason to kill the helper.
            // Its completion event resumes recognition once it is safe to do so.
            _resumeWhenStopped = true;
            if (ended.IsCompleted) QueueResume();
            return false;
        }
    }

    private void QueueResume()
    {
        if (Interlocked.Exchange(ref _resumeQueued, 1) == 0 && !_events.Writer.TryWrite(new ResumeRecognition()))
            Interlocked.Exchange(ref _resumeQueued, 0);
    }

    private async Task ApplyVocabularyIfIdleAsync()
    {
        if (!_vocabularyDirty || _paused || _grammars == null || _session.IsAwaitingCommand(SessionNow)) return;
        long pauseStarted = _clock.ElapsedTicks;
        bool stopped = false;
        try
        {
            stopped = await PauseRecognitionAsync().ConfigureAwait(false);
            if (!stopped) return;
            _grammars.ReplaceCosmetics(_vocabulary.Names);
            _vocabularyDirty = false;
            Console.WriteLine("Loaded " + _vocabulary.Names.Length + " owned cosmetic names for offline recognition.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _vocabularyDirty = false;
            await StatusAsync("Cosmetic speech hints could not refresh; normal commands still work. " + error.Message, false).ConfigureAwait(false);
        }
        finally
        {
            _speechPausedTicks += _clock.ElapsedTicks - pauseStarted;
            if (stopped && !_stop.IsCancellationRequested)
            {
                StartRecognition();
                await ListeningAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task SpeakAsync(string text)
    {
        if (_speaker == null || string.IsNullOrWhiteSpace(text))
        {
            await ListeningAsync().ConfigureAwait(false);
            return;
        }
        CancellationToken cancellationToken = _stop.Token;
        long pauseStarted = _clock.ElapsedTicks;
        bool stopped = false, played = false;
        string? warning = null;
        SpeechSynthesizer speaker = _speaker;
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(object? sender, SpeakCompletedEventArgs args)
        {
            if (args.Error != null) finished.TrySetException(args.Error);
            else finished.TrySetResult(true);
        }
        speaker.SpeakCompleted += OnFinished;
        try
        {
            stopped = await PauseRecognitionAsync().ConfigureAwait(false);
            if (!stopped)
            {
                warning = "Voice replies paused while the microphone finishes resetting. Listening resumes automatically; F10 restarts if needed.";
                return;
            }
            speaker.SpeakAsync(text);
            played = true;
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Voice output is optional. A missing voice, failed playback device,
            // or stuck synthesizer must not take down microphone commands.
            warning = "Voice replies are unavailable; microphone commands remain enabled. Check the Windows playback device/voice. " + error.Message;
            try { speaker.SpeakAsyncCancelAll(); } catch (Exception) { }
            _speaker = null;
        }
        finally
        {
            speaker.SpeakCompleted -= OnFinished;
            if (_speaker == null) { try { speaker.Dispose(); } catch (Exception) { } }
            if (played && !cancellationToken.IsCancellationRequested)
                await Task.Delay(180, cancellationToken).ConfigureAwait(false);
            _speechPausedTicks += _clock.ElapsedTicks - pauseStarted;
            if (stopped && !cancellationToken.IsCancellationRequested)
            {
                StartRecognition();
                await ListeningAsync().ConfigureAwait(false);
            }
            if (warning != null && !cancellationToken.IsCancellationRequested)
                await StatusAsync(warning, false).ConfigureAwait(false);
        }
    }

    private async Task ReadParentAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                string? line = await _connection.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                if (line == null) break;
                if (!PipeProtocol.TryDecode(line, out WireMessage? message) || message == null)
                    throw new InvalidDataException("The game sent an invalid assistant pipe message.");
                if (message.Kind == WireKind.Quit) break;
                await _events.Writer.WriteAsync(new FromParent(message), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or ObjectDisposedException or System.Text.DecoderFallbackException)
        { Console.Error.WriteLine("Assistant connection closed: " + error.Message); }
        finally { _stop.Cancel(); }
    }

    private Task StatusAsync(string message, bool success = true)
    {
        message = new string(message.Select(character => char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character).Take(512).ToArray());
        Console.WriteLine(message);
        return SendAsync(new WireMessage(WireKind.Status, success: success, text: message));
    }

    private Task SendAsync(WireMessage message) => _connection.SendAsync(message, _stop.Token);

    private void StopSpeech()
    {
        try { _recognizer?.RecognizeAsyncCancel(); } catch (InvalidOperationException) { }
        try { _speaker?.SpeakAsyncCancelAll(); } catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _paused = true;
        StopSpeech();
        if (_recognizer != null)
        {
            _recognizer.SpeechRecognized -= OnSpeechRecognized;
            _recognizer.SpeechRecognitionRejected -= OnSpeechRejected;
            _recognizer.RecognizeCompleted -= OnRecognizeCompleted;
            _recognizer.Dispose();
        }
        _speaker?.Dispose();
        _session.Reset();
    }
}
