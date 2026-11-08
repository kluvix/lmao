using System.Reflection;
using System.Runtime.CompilerServices;
using System.Speech.Recognition;
using HeyJace.Core;
using HeyJace.Speech;

namespace SpeechChecks;

internal static class Entry
{
    private static int _checks;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new InvalidOperationException(name); }

    private static async Task Main()
    {
        Vocabulary();
        OutfitGrammarAlignment();
        CompletionRace();
        Candidates();
        await DeferredResponse();
        Console.WriteLine("PASS: " + _checks + " managed speech-host checks. No microphone, recognizer, synthesizer, or game was started.");
    }

    private static void Candidates()
    {
        var session = new VoiceSession();
        var incorrect = new RecognitionCandidate("joint coat jace thirty four", .85f);
        var correct = new RecognitionCandidate("join code jace thirty four", .8f);
        Check(RecognitionCandidates.Select(session, new[] { incorrect, correct }, 0) == incorrect, "alternates never authorize idle commands");
        session.Process("hey jace", 1, 0);
        Check(RecognitionCandidates.Select(session, new[] { incorrect, correct }, 1) == correct, "bounded alternate rescues misheard room command after wake");
        Check(session.IsAwaitingCommand(1) && session.PendingKind == null, "candidate selection leaves the real session untouched");
        JaceCommand command = session.Process(correct.Text, correct.Confidence, 1).Single(value => value.Kind == VoiceEventKind.Command).Command!;
        Check(command.Kind == CommandKind.JoinRoom && command.Argument == "JACE34", "selected native-style mixed code dispatches once");
        session.Reset(); session.Process("hey jace", 1, 0);
        var understood = new RecognitionCandidate("join code BANANA", .75f);
        Check(RecognitionCandidates.Select(session, new[] { understood, correct }, 1) == understood, "valid best interpretation is never replaced by alternative");
        var weak = new RecognitionCandidate(correct.Text, .5f);
        Check(RecognitionCandidates.Select(session, new[] { incorrect, weak }, 1) == incorrect, "far lower confidence alternatives are ignored");
        session.Process("change my color to", 1, 1);
        Check(RecognitionCandidates.Select(session, new[] { incorrect, correct }, 2) == incorrect, "room alternative cannot replace pending numeric color answer");
        var color = new RecognitionCandidate("nine zero five", .8f);
        Check(RecognitionCandidates.Select(session, new[] { incorrect, correct, color }, 2) == color, "numeric answer alternative respects pending intent");
        var newWake = new RecognitionCandidate("hey jace joint coat jace thirty four", .85f);
        var newRoom = new RecognitionCandidate("hey jace join code jace thirty four", .8f);
        Check(RecognitionCandidates.Select(session, new[] { newWake, newRoom }, 2) == newRoom, "fresh wake overrides the previous pending kind for alternatives");
        Check(RecognitionCandidates.Select(session, new[] { newWake, newRoom }, 30) == newRoom, "expired pending kind cannot block a fresh wake alternative");
        foreach (string symbol in RecognitionGrammars.RoomSymbols())
        {
            var spelling = new VoiceSession();
            Check(spelling.Process("hey jace join code " + symbol, 1, 0).Any(value => value.Kind == VoiceEventKind.Command), "room grammar symbol parser alignment " + symbol);
        }
    }

    private static async Task DeferredResponse()
    {
        var connection = (PipeConnection)RuntimeHelpers.GetUninitializedObject(typeof(PipeConnection));
        using var stop = new CancellationTokenSource();
        using var host = new SpeechHost(connection, stop);
        Type type = typeof(SpeechHost);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var session = (VoiceSession)type.GetField("_session", flags)!.GetValue(host)!;
        session.Process("hey jace join code", 1, 0);
        FieldInfo generation = type.GetField("_responseVersion", flags)!;
        generation.SetValue(host, 7);
        Task deferred = (Task)type.GetMethod("DeferResponseAsync", flags)!.Invoke(host, new object[] { "Say the room code.", 7, CommandKind.JoinRoom })!;
        Check(!deferred.IsCompleted, "prefix prompt leaves a brief continuation window before TTS");
        await deferred;
        object channel = type.GetField("_events", flags)!.GetValue(host)!;
        object reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        object?[] next = { null };
        Check((bool)reader.GetType().GetMethod("TryRead")!.Invoke(reader, next)! && next[0]?.GetType().Name == "ResponseDue", "deferred response returns through serialized host events");
        MethodInfo current = type.GetMethod("IsCurrentResponse", flags)!;
        Check((bool)current.Invoke(host, new[] { next[0] })!, "unchanged pending prefix can speak its deferred prompt");
        generation.SetValue(host, 8);
        Check(!(bool)current.Invoke(host, new[] { next[0] })!, "continued speech cancels older prompt generation");
        generation.SetValue(host, 7);
        double now = (double)type.GetProperty("SessionNow", flags)!.GetValue(host)!;
        Check(session.Process("jace three four", 1, now).Any(value => value.Kind == VoiceEventKind.Command), "room continuation remains recognizable while prompt is deferred");
        Check(!(bool)current.Invoke(host, new[] { next[0] })!, "completed command cannot be interrupted by an obsolete prompt");
    }

    private static WireMessage Frame(int id, string text = "") => new(WireKind.Vocabulary, id, text: text);

    private static void Vocabulary()
    {
        var vocabulary = new RecognitionVocabulary();
        Check(!vocabulary.Accept(Frame(1, "Top Hat")), "ignore chunks outside transaction");
        Check(!vocabulary.Accept(Frame(2)), "ignore finish outside transaction");
        Check(!vocabulary.Accept(Frame(0)), "reset does not publish partial vocabulary");
        Check(!vocabulary.Accept(Frame(1, "Top Hat|top hat|  Bunny Ears  ||!!!|" + new string('x', 65) + "|Bad\nName|Bad\u2028Name")), "chunk remains private");
        Check(vocabulary.Names.Length == 0, "partial transaction does not replace catalog");
        Check(vocabulary.Accept(Frame(2)), "complete transaction publishes");
        Check(vocabulary.Names.SequenceEqual(new[] { "Bunny Ears", "Top Hat" }), "trim, case dedupe, reject invalid names");
        vocabulary.Accept(Frame(0)); vocabulary.Accept(Frame(1, "top hat|bunny ears"));
        Check(!vocabulary.Accept(Frame(2)), "same catalog does not reload native grammars");
        vocabulary.Accept(Frame(0)); vocabulary.Accept(Frame(1, "Unfinished"));
        Check(vocabulary.Names.Length == 2, "old names survive unfinished refresh");
        vocabulary.Accept(Frame(0));
        for (int i = 0; i < RecognitionVocabulary.MaximumNames + 100; i++) vocabulary.Accept(Frame(1, "Cosmetic " + i));
        Check(vocabulary.Accept(Frame(2)), "new bounded catalog publishes");
        Check(vocabulary.Names.Length == RecognitionVocabulary.MaximumNames, "catalog bounded at 512 unique names");
        Check(!vocabulary.Accept(new WireMessage(WireKind.Command, 0, text: "not a reset", command: new JaceCommand(CommandKind.Equip, "Top Hat"))), "commands cannot reset vocabulary");
        Check(!vocabulary.Accept(new WireMessage(WireKind.Vocabulary, 0, command: new JaceCommand(CommandKind.Equip, "Top Hat"))), "malformed vocabulary cannot reset");
        vocabulary.Accept(Frame(0));
        Check(vocabulary.Accept(Frame(2)) && vocabulary.Names.Length == 0, "complete empty inventory clears old names");
    }

    private static void OutfitGrammarAlignment()
    {
        string[] choices = RecognitionGrammars.OutfitNumbers();
        Check(choices.Length == 99 && choices.Distinct().Count() == 99, "99 unique spoken outfit choices");
        for (int i = 0; i < choices.Length; i++)
        {
            var session = new VoiceSession();
            IReadOnlyList<VoiceEvent> events = session.Process("hey jace equip outfit " + choices[i], 1, 0);
            JaceCommand? command = events.SingleOrDefault(item => item.Kind == VoiceEventKind.Command)?.Command;
            Check(command?.Kind == CommandKind.EquipOutfit && command.Argument == (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "spoken outfit grammar/parser alignment " + choices[i]);
        }
    }

    private static void CompletionRace()
    {
        // Invoke the actual managed completion handler without constructing any
        // native SAPI objects. A synchronous continuation recreates the race:
        // signaling completion resumes the waiter, which clears the pause flag.
        var connection = (PipeConnection)RuntimeHelpers.GetUninitializedObject(typeof(PipeConnection));
        using var stop = new CancellationTokenSource();
        using var host = new SpeechHost(connection, stop);
        Type type = typeof(SpeechHost);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo paused = type.GetField("_paused", flags)!;
        FieldInfo ended = type.GetField("_recognitionEnded", flags)!;
        MethodInfo complete = type.GetMethod("OnRecognizeCompleted", flags)!;
        object args = RuntimeHelpers.GetUninitializedObject(typeof(RecognizeCompletedEventArgs));
        var completion = new TaskCompletionSource<bool>();
        ended.SetValue(host, completion); paused.SetValue(host, true);
        Task restarted = completion.Task.ContinueWith(_ => paused.SetValue(host, false), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        complete.Invoke(host, new[] { null, args });
        Check(restarted.IsCompleted && (bool)paused.GetValue(host)! == false, "completion continuation reproduces immediate restart");
        object channel = type.GetField("_events", flags)!.GetValue(host)!;
        object reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        MethodInfo read = reader.GetType().GetMethod("TryRead")!;
        object?[] next = { null };
        Check(!(bool)read.Invoke(reader, next)!, "intentional cancellation never becomes false microphone failure after restart");
        ended.SetValue(host, new TaskCompletionSource<bool>()); paused.SetValue(host, false);
        complete.Invoke(host, new[] { null, args }); next[0] = null;
        Check((bool)read.Invoke(reader, next)! && next[0]?.GetType().Name == "SpeechFailed", "unexpected microphone completion still reports a failure");
    }
}
