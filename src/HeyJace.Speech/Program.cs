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
    private sealed record Heard(string Text, float Confidence, long Timestamp) : HostEvent;
    private sealed record FromParent(WireMessage Message) : HostEvent;
    private sealed record SpeechFailed(string Message) : HostEvent;

    private static readonly string[] Greetings =
    {
        "what's up",
        "what do you want chud",
        "elite is better than you and what do you want"
    };
    private readonly PipeConnection _connection;
    private readonly CancellationTokenSource _stop;
    private readonly Channel<HostEvent> _events = Channel.CreateBounded<HostEvent>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly VoiceSession _session = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<int> _pending = new();
    private SpeechRecognitionEngine? _recognizer;
    private SpeechSynthesizer? _speaker;
    private TaskCompletionSource<bool>? _recognitionEnded;
    private volatile bool _paused = true;
    private volatile bool _disposed;
    private long _acceptAfter;
    private long _speechPausedTicks;
    private int _greetingIndex = Random.Shared.Next(Greetings.Length);
    private int _commandId;

    internal SpeechHost(PipeConnection connection, CancellationTokenSource stop)
    { _connection = connection; _stop = stop; }

    internal async Task<int> RunAsync()
    {
        CancellationToken cancellationToken = _stop.Token;
        await _connection.SendAsync(new WireMessage(WireKind.Hello, text: "HeyJace/1"), cancellationToken).ConfigureAwait(false);
        Task readParent = ReadParentAsync();
        try
        {
            if (!await InitializeSpeechAsync().ConfigureAwait(false)) return 1;
            await foreach (HostEvent item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (item)
                {
                    case Heard heard when heard.Timestamp >= Interlocked.Read(ref _acceptAfter):
                        foreach (VoiceEvent voiceEvent in _session.Process(heard.Text, heard.Confidence,
                            (double)(heard.Timestamp - _speechPausedTicks) / Stopwatch.Frequency))
                        {
                            if (voiceEvent.Kind == VoiceEventKind.Wake)
                            {
                                string greeting = Greetings[_greetingIndex++ % Greetings.Length];
                                await SendAsync(new WireMessage(WireKind.Wake, text: greeting)).ConfigureAwait(false);
                                await SpeakAsync(greeting).ConfigureAwait(false);
                            }
                            else if (voiceEvent.Kind == VoiceEventKind.Prompt)
                            {
                                await StatusAsync(voiceEvent.Text).ConfigureAwait(false);
                                await SpeakAsync(voiceEvent.Text).ConfigureAwait(false);
                            }
                            else if (voiceEvent.Kind == VoiceEventKind.Command && voiceEvent.Command != null)
                            {
                                if (_pending.Count >= 32)
                                {
                                    await StatusAsync("Waiting for the game to finish earlier commands.", false).ConfigureAwait(false);
                                    continue;
                                }
                                int id = _commandId = _commandId == int.MaxValue ? 1 : _commandId + 1;
                                _pending.Add(id);
                                await SendAsync(new WireMessage(WireKind.Command, id, command: voiceEvent.Command)).ConfigureAwait(false);
                            }
                        }
                        break;
                    case FromParent { Message.Kind: WireKind.Result } reply when _pending.Remove(reply.Message.Id):
                        Console.WriteLine(reply.Message.Text);
                        await SpeakAsync(reply.Message.Text).ConfigureAwait(false);
                        break;
                    case FromParent { Message.Kind: WireKind.Status } status:
                        Console.WriteLine(status.Message.Text);
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
            LoadGrammars(_recognizer, info.Culture);
            _recognizer.SpeechRecognized += OnSpeechRecognized;
            _recognizer.RecognizeCompleted += OnRecognizeCompleted;
            _recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(0);
            _recognizer.BabbleTimeout = TimeSpan.FromSeconds(0);
            _recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(350);
            _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(600);
            _recognizer.SetInputToDefaultAudioDevice();
            try
            {
                _speaker = new SpeechSynthesizer();
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

    private static void LoadGrammars(SpeechRecognitionEngine recognizer, CultureInfo culture)
    {
        // Explicit phrases improve the wake word; dictation supplies arbitrary
        // room codes, item names, and the next utterance of a follow-up question.
        string[] commands =
        {
            "join room", "join the room", "join code", "join room code", "set color", "set my color to", "change color", "change my color to", "change my colour to",
            "set time", "set the time to", "change the time to", "equip", "put on", "wear", "unequip", "take off", "remove",
            "red", "green", "blue", "black", "white", "pink", "purple", "orange", "yellow", "cyan",
            "dawn", "day", "noon", "afternoon", "sunset", "evening", "night", "midnight", "auto", "cancel"
        };
        var phrases = new Choices(new[] { "hey jace" }
            .Concat(commands).Concat(commands.Select(command => "hey jace " + command)).ToArray());
        var builder = new GrammarBuilder { Culture = culture };
        builder.Append(phrases);
        recognizer.LoadGrammar(new Grammar(builder) { Name = "HeyJace commands", Weight = 1.0f });
        recognizer.LoadGrammar(new DictationGrammar { Name = "HeyJace room codes and item names", Weight = 0.65f });
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs args)
    {
        if (_disposed || _paused || _stop.IsCancellationRequested) return;
        long timestamp = _clock.ElapsedTicks;
        if (timestamp < Interlocked.Read(ref _acceptAfter)) return;
        _events.Writer.TryWrite(new Heard(args.Result.Text, args.Result.Confidence, timestamp));
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs args)
    {
        _recognitionEnded?.TrySetResult(true);
        if (_disposed || _paused || _stop.IsCancellationRequested) return;
        _events.Writer.TryWrite(new SpeechFailed("Microphone recognition stopped. Check the default recording device and restart Gorilla Tag. " +
            (args.Error?.Message ?? "The audio input ended.")));
    }

    private void StartRecognition()
    {
        if (_recognizer == null || _stop.IsCancellationRequested || _disposed) return;
        _recognitionEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _acceptAfter, _clock.ElapsedTicks);
        _paused = false;
        _recognizer.RecognizeAsync(RecognizeMode.Multiple);
    }

    private async Task SpeakAsync(string text)
    {
        if (_speaker == null || string.IsNullOrWhiteSpace(text)) return;
        CancellationToken cancellationToken = _stop.Token;
        long pauseStarted = _clock.ElapsedTicks;
        _paused = true;
        if (_recognizer != null && _recognitionEnded != null && !_recognitionEnded.Task.IsCompleted)
        {
            _recognizer.RecognizeAsyncCancel();
            await _recognitionEnded.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(object? sender, SpeakCompletedEventArgs args)
        {
            if (args.Error != null) finished.TrySetException(args.Error);
            else finished.TrySetResult(true);
        }
        _speaker.SpeakCompleted += OnFinished;
        try
        {
            _speaker.SpeakAsync(text);
            await finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            // Keep this helper's recognizer stopped until speaker echo has faded.
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _speaker.SpeakCompleted -= OnFinished;
            // A follow-up question keeps its full answer window after speech
            // finishes, because recognition cannot hear while the helper speaks.
            _speechPausedTicks += _clock.ElapsedTicks - pauseStarted;
        }
        StartRecognition();
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
            _recognizer.RecognizeCompleted -= OnRecognizeCompleted;
            _recognizer.Dispose();
        }
        _speaker?.Dispose();
        _session.Reset();
    }
}
