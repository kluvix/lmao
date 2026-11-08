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

    private static void Main()
    {
        Vocabulary();
        OutfitGrammarAlignment();
        CompletionRace();
        Console.WriteLine("PASS: " + _checks + " managed speech-host checks. No microphone, recognizer, synthesizer, or game was started.");
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
