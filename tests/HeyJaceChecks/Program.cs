using System.Text;
using HeyJace.Core;

int checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
}

VoiceEvent? Command(IReadOnlyList<VoiceEvent> events) => events.SingleOrDefault(e => e.Kind == VoiceEventKind.Command);
void ExpectCommand(string utterance, CommandKind kind, string argument)
{
    var events = new VoiceSession().Process(utterance, 0.95f, 1);
    Check(events.Count(e => e.Kind == VoiceEventKind.Wake) == 1, utterance + " wakes once");
    Check(Command(events)?.Command is { } command && command.Kind == kind && command.Argument == argument, utterance + " parses");
}

ExpectCommand("hey jace join code BANANA", CommandKind.JoinRoom, "BANANA");
ExpectCommand("hey jace join code CANCEL", CommandKind.JoinRoom, "CANCEL");
ExpectCommand("hey jace join the room BANANA", CommandKind.JoinRoom, "BANANA");
ExpectCommand("hey jace join the room code BANANA", CommandKind.JoinRoom, "BANANA");
ExpectCommand("Hey, Jace! join room code b a n a n a.", CommandKind.JoinRoom, "BANANA");
ExpectCommand("hey jace join code bravo alpha november alpha november alpha", CommandKind.JoinRoom, "BANANA");
ExpectCommand("hey jace join code nine zero five", CommandKind.JoinRoom, "905");
ExpectCommand("hey jace join code a bee see one two three", CommandKind.JoinRoom, "ABC123");
ExpectCommand("hey jace join code alpha", CommandKind.JoinRoom, "A");
ExpectCommand("hey jace join code 00abc012", CommandKind.JoinRoom, "00ABC012");
ExpectCommand("hey jace join code double you x ray zed", CommandKind.JoinRoom, "WXZ");
ExpectCommand("hey jace change my color to red", CommandKind.SetColor, "red");
ExpectCommand("hey jace change my color to nine zero five", CommandKind.SetColor, "905");
ExpectCommand("hey jace change my colour to #a0B1c2", CommandKind.SetColor, "#A0B1C2");
ExpectCommand("hey jace set color red", CommandKind.SetColor, "red");
ExpectCommand("hey jace set color to blue", CommandKind.SetColor, "blue");
ExpectCommand("hey jace change color red", CommandKind.SetColor, "red");
ExpectCommand("hey jace change color to blue", CommandKind.SetColor, "blue");
ExpectCommand("hey jace change the time to midnight", CommandKind.SetTime, "midnight");
ExpectCommand("hey jace change the time to 007", CommandKind.SetTime, "7");
ExpectCommand("hey jace change the time to five", CommandKind.SetTime, "5");
ExpectCommand("hey jace set time night", CommandKind.SetTime, "night");
ExpectCommand("hey jace set time to night", CommandKind.SetTime, "night");
foreach (var time in new[]
{
    ("morning", "dawn"), ("sunrise", "dawn"), ("midday", "noon"),
    ("normal", "auto"), ("default", "auto"), ("automatic", "auto"), ("restore", "auto"), ("reset", "auto"),
    ("six am", "6am"), ("six p m", "6pm"), ("6am", "6am"), ("12 pm", "12pm"), ("twelve a m", "12am"),
    ("ten p.m.", "10pm"), ("eleven a. m.", "11am"), ("06:00 AM", "6am"), ("one pm", "1pm"), ("9:00pm", "9pm")
})
    ExpectCommand("hey jace change the time to " + time.Item1, CommandKind.SetTime, time.Item2);
ExpectCommand("hey jace equip Top Hat", CommandKind.Equip, "Top Hat");
ExpectCommand("hey jace unequip Jace's Shirt", CommandKind.Unequip, "Jace's Shirt");
ExpectCommand("hey jace wear Top Hat", CommandKind.Equip, "Top Hat");
ExpectCommand("hey jace put on Top Hat", CommandKind.Equip, "Top Hat");
ExpectCommand("hey jace take off Top Hat", CommandKind.Unequip, "Top Hat");
ExpectCommand("hey jace remove Top Hat", CommandKind.Unequip, "Top Hat");

var session = new VoiceSession();
Check(session.Process("join code BANANA", 1, 0).Count == 0, "no command without wake");
Check(session.Process("I heard hey jace join code BANANA", 1, 1).Count == 0, "wake must start utterance");
Check(session.Process("hey jacey join code BANANA", 1, 2).Count == 0, "wake requires word boundary");
Check(session.Process("hey jace", 1, 3).Select(e => e.Kind).SequenceEqual(new[] { VoiceEventKind.Wake }), "wake alone arms and emits one event");
Check(Command(session.Process("join code BANANA", 1, 4))?.Command?.Argument == "BANANA", "next utterance command");
Check(session.Process("join code OTHER", 1, 5).Count == 0, "successful command consumes arm");

foreach (var pending in new[]
{
    ("join code", "b a n a n a", CommandKind.JoinRoom, "BANANA"),
    ("change my color to", "nine zero five", CommandKind.SetColor, "905"),
    ("change the time to", "night", CommandKind.SetTime, "night"),
    ("set time", "night", CommandKind.SetTime, "night"),
    ("change the time to", "six p m", CommandKind.SetTime, "6pm"),
    ("equip", "Top Hat", CommandKind.Equip, "Top Hat"),
    ("unequip", "Top Hat", CommandKind.Unequip, "Top Hat")
})
{
    session = new VoiceSession();
    var first = session.Process("hey jace " + pending.Item1, 1, 0);
    Check(first.Count == 2 && first[0].Kind == VoiceEventKind.Wake && first[1].Kind == VoiceEventKind.Prompt, pending.Item1 + " prompts");
    var command = Command(session.Process(pending.Item2, 1, 10))?.Command;
    Check(command?.Kind == pending.Item3 && command.Argument == pending.Item4, pending.Item1 + " accepts pending value at boundary");
    Check(session.Process(pending.Item2, 1, 10.1).Count == 0, pending.Item1 + " consumes pending arm");
}

foreach (string cancellation in new[] { "cancel", "never mind", "stop" })
{
    session = new VoiceSession();
    session.Process("hey jace join code", 1, 0);
    var events = session.Process(cancellation, 1, 1);
    Check(events.Count == 1 && events[0].Kind == VoiceEventKind.Prompt && events[0].Text == "Cancelled." && Command(events) == null, "pending room cancellation " + cancellation);
    Check(session.Process("BANANA", 1, 2).Count == 0, "cancellation clears pending room " + cancellation);
}
session = new VoiceSession();
Check(session.Process("cancel", 1, 0).Count == 0, "ambient cancellation has no reply");
Check(session.Process("hey jace cancel", 1, 1).Select(e => e.Kind).SequenceEqual(new[] { VoiceEventKind.Wake, VoiceEventKind.Prompt }), "wake cancellation emits wake and prompt");

session = new VoiceSession();
session.Process("hey jace", 1, 0);
Check(session.Process("join code BANANA", 1, 10.001).Count == 0, "arm expires after ten seconds");
session.Process("hey jace join code", 1, 20);
Check(session.Process("BANANA", 1, 30.001).Count == 0, "pending expires after ten seconds");
session.Process("hey jace", 1, 40);
Check(Command(session.Process("join code BANANA", 1, 50)) != null, "arm valid at exact boundary");
session.Process("hey jace", 1, 60);
var renewed = session.Process("hey jace change the time to day", 1, 61);
Check(renewed.Count(e => e.Kind == VoiceEventKind.Wake) == 1 && Command(renewed)?.Command?.Argument == "day", "new wake replaces old arm");
session.Process("hey jace join code", 1, 70);
Check(Command(session.Process("hey jace equip Hat", 1, 71))?.Command?.Kind == CommandKind.Equip, "new wake replaces pending argument");

foreach (float confidence in new[] { 0f, 0.64f, float.NaN, float.PositiveInfinity, -1f, 1.1f })
{
    session = new VoiceSession();
    session.Process("hey jace join code", 1, 0);
    Check(session.Process("BANANA", confidence, 1).Count == 0, "reject confidence " + confidence);
    Check(session.Process("BANANA", 1, 2).Count == 0, "bad confidence clears pending " + confidence);
}
Check(Command(new VoiceSession().Process("hey jace join code BANANA", VoiceSession.MinimumConfidence, 0)) != null, "confidence valid at exact threshold");
foreach (double invalidTime in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
{
    session = new VoiceSession();
    session.Process("hey jace join code", 1, 1);
    Check(session.Process("BANANA", 1, invalidTime).Count == 0, "reject nonfinite time " + invalidTime);
    Check(session.Process("BANANA", 1, 2).Count == 0, "nonfinite time clears pending " + invalidTime);
}
session = new VoiceSession();
session.Process("hey jace join code", 1, 2);
Check(session.Process("BANANA", 1, 1).Count == 0, "backwards time rejected");
Check(session.Process("BANANA", 1, 3).Count == 0, "backwards time clears pending");
session.Process("hey jace join code", 1, 4);
session.Reset();
Check(session.Process("BANANA", 1, 5).Count == 0, "reset clears intent");

foreach (string invalid in new[]
{
    "do anything", "join code two unknown", "join code ABCDEFGHIJK", "join code ABC/DEF", "join code ABC;DEF",
    "change my color to ten zero five", "change my color to red and equip Hat", "change the time to tomorrow",
    "change the time to -1", "change the time to 256", "equip ../Hat", "equip Hat; join code ROOM", "unequip " + new string('A', 65),
    "change the time to 0am", "change the time to 13pm", "change the time to six am tomorrow", "change the time to 6:30 pm",
    "change the time to twenty am", "change the time to one two pm", "change the time to 12:00:00am", "change the time to tonight"
})
{
    session = new VoiceSession();
    var events = session.Process("hey jace " + invalid, 1, 0);
    Check(Command(events) == null && events.Any(e => e.Kind == VoiceEventKind.Prompt && e.Text.Length > 0), "invalid command prompts: " + invalid);
    Check(session.Process("join code BANANA", 1, 1).Count == 0, "invalid command consumes arm: " + invalid);
}
session = new VoiceSession();
Check(session.Process("hey jace join code BANANA\n", 1, 0).Count == 0, "control character recognition rejected");
Check(session.Process("hey jace equip " + new string('A', 300), 1, 1).Count == 0, "oversize recognition rejected");

foreach (var sample in new[] { ("905", "905", 1f, 0f, 5f / 9), ("9, 0, 5", "905", 1f, 0f, 5f / 9), ("nine zero five", "905", 1f, 0f, 5f / 9), ("#FF0080", "#FF0080", 1f, 0f, 128f / 255), ("hex ff0080", "#FF0080", 1f, 0f, 128f / 255), ("grey", "gray", 128f / 255, 128f / 255, 128f / 255) })
{
    Check(ColorSpec.TryParse(sample.Item1, out var color) && color.Label == sample.Item2 && Math.Abs(color.R - sample.Item3) < .0001 && Math.Abs(color.G - sample.Item4) < .0001 && Math.Abs(color.B - sample.Item5) < .0001, "color " + sample.Item1);
}
foreach (string name in new[] { "black", "white", "red", "green", "blue", "yellow", "cyan", "magenta", "pink", "purple", "orange", "brown", "teal", "navy", "gold" })
    Check(ColorSpec.TryParse(name, out _), "palette color " + name);
foreach (string value in new[] { "", "12", "1234", "9 0 10", "nine zero", "-1 0 5", "#123", "#GG0000", "red\nblue", "red\n", "red and blue" })
    Check(!ColorSpec.TryParse(value, out _), "invalid color " + value);

foreach (var message in new[]
{
    new WireMessage(WireKind.Hello, text: "Hey Jace 1.0"),
    new WireMessage(WireKind.Wake, 1),
    new WireMessage(WireKind.Command, int.MaxValue, command: new JaceCommand(CommandKind.JoinRoom, "BANANA")),
    new WireMessage(WireKind.Command, 7, command: new JaceCommand(CommandKind.SetColor, "#FF00AA")),
    new WireMessage(WireKind.Result, 2, false, "Can't join: café 🍌"),
    new WireMessage(WireKind.Result, 3, true, new string('R', 512)),
    new WireMessage(WireKind.Status, text: "Listening"),
    new WireMessage(WireKind.Quit)
})
{
    string line = PipeProtocol.Encode(message);
    Check(line.Length <= PipeProtocol.MaxLineLength && !line.Contains('\n') && line.Split('\t').Length == 6, "bounded six-field wire message " + message.Kind);
    Check(PipeProtocol.TryDecode(line, out var decoded) && decoded != null && decoded.Kind == message.Kind && decoded.Id == message.Id && decoded.Success == message.Success && decoded.Text == message.Text && decoded.Command?.Kind == message.Command?.Kind && decoded.Command?.Argument == message.Command?.Argument, "wire roundtrip " + message.Kind);
}
string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
foreach (string badLine in new[]
{
    "", new string('x', 4097), "0\t0\t1\t\t-", "0\t0\t1\t\t-\t\textra",
    "99\t0\t1\t\t-\t", "0\t-1\t1\t\t-\t", "0\t2147483648\t1\t\t-\t", "0\t0\ttrue\t\t-\t",
    "0\t0\t1\t%%%\t-\t", "0\t0\t1\twyg=\t-\t", "0\t0\t1\tY Q==\t-\t",
    "0\t0\t1\t" + Base64("hello\nworld") + "\t-\t", "0\t0\t1\t" + Base64(new string('x', 513)) + "\t-\t",
    "2\t1\t1\t\t99\t" + Base64("BANANA"), "2\t1\t1\t\t-\t", "2\t1\t1\t\t0\t",
    "2\t1\t1\t\t0\t" + Base64(new string('A', 65)), "2\t1\t1\t\t0\t" + Base64("ABC\tDEF"),
    "0\t1\t1\t\t0\t" + Base64("BANANA"), "0\t1\t1\t\t-\t" + Base64("BANANA")
})
    Check(!PipeProtocol.TryDecode(badLine, out var badMessage) && badMessage == null, "malformed pipe line " + checks);

foreach (var invalidMessage in new[]
{
    new WireMessage((WireKind)123), new WireMessage(WireKind.Wake, -1), new WireMessage(WireKind.Command),
    new WireMessage(WireKind.Command, command: new JaceCommand((CommandKind)123, "x")),
    new WireMessage(WireKind.Command, command: new JaceCommand(CommandKind.Equip, new string('A', 65))),
    new WireMessage(WireKind.Status, text: "hello\0world"), new WireMessage(WireKind.Result, text: new string('x', 513)),
    new WireMessage(WireKind.Status, text: "\ud800"), new WireMessage(WireKind.Wake, command: new JaceCommand(CommandKind.Equip, "Hat"))
})
{
    bool threw = false;
    try { PipeProtocol.Encode(invalidMessage); } catch (ArgumentException) { threw = true; }
    Check(threw, "invalid outgoing message rejected " + checks);
}

// Every bounded line is untrusted input; malformed random data must be a clean false, never an exception.
var random = new Random(42);
for (int i = 0; i < 500; i++)
{
    var characters = new char[random.Next(0, 200)];
    for (int j = 0; j < characters.Length; j++) characters[j] = (char)random.Next(0, 256);
    Check(!PipeProtocol.TryDecode(new string(characters), out _), "random malformed wire input " + i);
}

Console.WriteLine($"Hey Jace checks passed: {checks}");
