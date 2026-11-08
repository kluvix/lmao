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
foreach (string code in new[] { "jace34", "jace 34", "jace 3 four", "jace three four", "jace thirty four", "jace thirty-four" })
    ExpectCommand("hey jace join code " + code, CommandKind.JoinRoom, "JACE34");
ExpectCommand("hey jace please join code jace 34", CommandKind.JoinRoom, "JACE34");
ExpectCommand("hey jace can you join code jace thirty four", CommandKind.JoinRoom, "JACE34");
ExpectCommand("hey jace join code FOR", CommandKind.JoinRoom, "FOR");
ExpectCommand("hey jace join code for 34", CommandKind.JoinRoom, "FOR34");
ExpectCommand("hey jace join code to 34", CommandKind.JoinRoom, "TO34");
ExpectCommand("hey jace change my color to 905", CommandKind.SetColor, "905");
ExpectCommand("hey jace change my color to nine zero five", CommandKind.SetColor, "905");
ExpectCommand("hey jace change my colour to nine oh five", CommandKind.SetColor, "905");
ExpectCommand("hey jace set color 123", CommandKind.SetColor, "123");
ExpectCommand("hey jace set color to 009", CommandKind.SetColor, "009");
ExpectCommand("hey jace change color 123", CommandKind.SetColor, "123");
ExpectCommand("hey jace change color to 009", CommandKind.SetColor, "009");
ExpectCommand("hey jace change my color to to for oh", CommandKind.SetColor, "240");
ExpectCommand("hey jace change my color to nine hundred five", CommandKind.SetColor, "905");
ExpectCommand("hey jace change my color to nine hundred and five", CommandKind.SetColor, "905");
ExpectCommand("hey jace change my color two four zero", CommandKind.SetColor, "240");
ExpectCommand("hey jace set my color two four zero", CommandKind.SetColor, "240");
ExpectCommand("hey jace change my color two nine zero five", CommandKind.SetColor, "905");
ExpectCommand("hey jace please change my color to too for zero", CommandKind.SetColor, "240");
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
ExpectCommand("hey jace equip Jace’s Shirt", CommandKind.Equip, "Jace's Shirt");
ExpectCommand("hey jace equip Red–White T-Shirt", CommandKind.Equip, "Red-White T-Shirt");
ExpectCommand("hey jace equip Mr. Fluffy (Red)", CommandKind.Equip, "Mr. Fluffy (Red)");
ExpectCommand("hey jace equip For Fun Hat", CommandKind.Equip, "For Fun Hat");
ExpectCommand("hey jace equip outfit one", CommandKind.EquipOutfit, "1");
ExpectCommand("hey jace equip outfit number 003", CommandKind.EquipOutfit, "3");
ExpectCommand("hey jace wear outfit 2", CommandKind.EquipOutfit, "2");
ExpectCommand("hey jace put on outfit number twelve", CommandKind.EquipOutfit, "12");
ExpectCommand("hey jace equip outfit nineteen", CommandKind.EquipOutfit, "19");
ExpectCommand("hey jace equip outfit twenty one", CommandKind.EquipOutfit, "21");
ExpectCommand("hey jace equip outfit ninety-nine", CommandKind.EquipOutfit, "99");
ExpectCommand("hey jace equip outfit 999", CommandKind.EquipOutfit, "999");
ExpectCommand("hey jace equip outfit too", CommandKind.EquipOutfit, "2");
ExpectCommand("hey jace equip outfit for", CommandKind.EquipOutfit, "4");
ExpectCommand("hey jace can you equip outfit number too", CommandKind.EquipOutfit, "2");
Check((int)CommandKind.EquipOutfit == 5 && (int)CommandKind.Unequip == 4, "appended outfit preserves existing wire command values");
Check((int)WireKind.Vocabulary == 6 && (int)WireKind.Quit == 5, "appended vocabulary preserves existing wire frame values");

var session = new VoiceSession();
Check(session.Process("join code BANANA", 1, 0).Count == 0, "no command without wake");
Check(session.Process("I heard hey jace join code BANANA", 1, 1).Count == 0, "wake must start utterance");
Check(session.Process("hey jacey join code BANANA", 1, 2).Count == 0, "wake requires word boundary");
Check(session.Process("hey jace", 1, 3).Select(e => e.Kind).SequenceEqual(new[] { VoiceEventKind.Wake }), "wake alone arms and emits one event");
Check(Command(session.Process("join code BANANA", 1, 4))?.Command?.Argument == "BANANA", "next utterance command");
Check(session.Process("join code OTHER", 1, 5).Count == 0, "successful command consumes arm");

session = new VoiceSession();
session.Process("hey jace", 1, 0);
var unknown = session.Process("do anything", 1, 1);
Check(Command(unknown) == null && unknown.SingleOrDefault()?.Text == "Say that again.", "unknown command uses short retry rather than long try-menu");
Check(session.IsAwaitingCommand(2) && session.PendingKind == null, "unknown command preserves trusted command window");
Check(Command(session.Process("join code jace thirty four", .5f, 2))?.Command is { Kind: CommandKind.JoinRoom, Argument: "JACE34" }, "retry after unknown command succeeds without another wake");
Check(session.Process("join code OTHER", 1, 3).Count == 0, "retry success still consumes one-command authorization");
foreach (string ambient in new[] { "please join code jace 34", "can you equip outfit too", "change my color to to for oh" })
    Check(new VoiceSession().Process(ambient, 1, 0).Count == 0, "polite and numeric forms never authorize ambient speech: " + ambient);

session = new VoiceSession();
var previewWake = session.Preview("hey jace join code", 1, 0);
Check(previewWake.Any(e => e.Kind == VoiceEventKind.Wake) && session.PendingKind == null && !session.IsAwaitingCommand(0), "candidate preview cannot arm the live session");
session.Process("hey jace", 1, 0);
Check(Command(session.Preview("join code jace 34", .5f, 1))?.Command?.Argument == "JACE34" && session.IsAwaitingCommand(1), "candidate preview returns parsed room without consuming live authorization");
Check(Command(session.Process("join code jace 34", .5f, 1))?.Command?.Argument == "JACE34", "actual processing after candidate preview still executes");
session.Process("hey jace change my color to", 1, 2);
Check(Command(session.Preview("nine zero five", .5f, 3))?.Command is { Kind: CommandKind.SetColor, Argument: "905" } && session.PendingKind == CommandKind.SetColor, "candidate preview cannot consume the pending numeric answer");
Check(Command(session.Process("nine zero five", .5f, 3))?.Command?.Argument == "905", "pending numeric answer remains usable after preview");
session.Process("hey jace join code", 1, 4);
Check(session.Preview("BANANA", 1, double.NaN).Count == 0 && session.PendingKind == CommandKind.JoinRoom && session.IsAwaitingCommand(5), "invalid preview clock cannot reset live pending state");
Check(Command(session.Process("BANANA", 1, 5))?.Command?.Argument == "BANANA", "live room answer survives rejected candidate preview");
session.Process("hey jace", 1, 6);
session.Preview("do anything", 1, 15);
Check(!session.IsAwaitingCommand(16.001), "unknown preview cannot extend live command expiry");

foreach (var pending in new[]
{
    ("join code", "b a n a n a", CommandKind.JoinRoom, "BANANA"),
    ("join code", "jace thirty four", CommandKind.JoinRoom, "JACE34"),
    ("join code", "join code jace thirty four", CommandKind.JoinRoom, "JACE34"),
    ("change my color to", "nine zero five", CommandKind.SetColor, "905"),
    ("change my color to red", "change my color to nine zero five", CommandKind.SetColor, "905"),
    ("change the time to", "night", CommandKind.SetTime, "night"),
    ("set time", "night", CommandKind.SetTime, "night"),
    ("change the time to", "six p m", CommandKind.SetTime, "6pm"),
    ("equip", "Top Hat", CommandKind.Equip, "Top Hat"),
    ("equip", "Please Hat", CommandKind.Equip, "Please Hat"),
    ("equip", "Wear Hat", CommandKind.Equip, "Wear Hat"),
    ("equip", "Please Wear Hat", CommandKind.Equip, "Please Wear Hat"),
    ("unequip", "Wear Hat", CommandKind.Unequip, "Wear Hat"),
    ("unequip", "Please Wear Hat", CommandKind.Unequip, "Please Wear Hat"),
    ("unequip", "Top Hat", CommandKind.Unequip, "Top Hat"),
    ("equip outfit", "two", CommandKind.EquipOutfit, "2"),
    ("equip outfit", "equip outfit two", CommandKind.EquipOutfit, "2"),
    ("equip outfit number", "number three", CommandKind.EquipOutfit, "3"),
    ("equip outfit", "too", CommandKind.EquipOutfit, "2"),
    ("change my color to", "to for oh", CommandKind.SetColor, "240"),
    ("change my color to", "change my color two four zero", CommandKind.SetColor, "240"),
    ("set my color to", "set my color two four zero", CommandKind.SetColor, "240"),
    ("change my color to", "change my color two nine zero five", CommandKind.SetColor, "905")
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

foreach (float confidence in new[] { float.NaN, float.PositiveInfinity, -1f, 1.1f })
{
    session = new VoiceSession();
    session.Process("hey jace join code", 1, 0);
    Check(session.Process("BANANA", confidence, 1).Count == 0, "reject confidence " + confidence);
    Check(session.Process("BANANA", 1, 2).Count == 0, "bad confidence clears pending " + confidence);
}
Check(Command(new VoiceSession().Process("hey jace join code BANANA", VoiceSession.MinimumConfidence, 0)) != null, "confidence valid at exact threshold");
foreach (float confidence in new[] { 0f, 0.39f, 0.64f })
    Check(new VoiceSession().Process("hey jace equip outfit one", confidence, 0).Count == 0, "wake still requires trusted confidence " + confidence);
session = new VoiceSession();
Check(!session.IsAwaitingCommand(0) && session.PendingKind == null, "fresh session has no active window");
session.Process("hey jace change my color to", 1, 1);
Check(session.IsAwaitingCommand(1) && session.IsAwaitingCommand(11) && !session.IsAwaitingCommand(11.001), "active state follows exact window boundaries");
Check(!session.IsAwaitingCommand(double.NaN) && !session.IsAwaitingCommand(double.PositiveInfinity) && !session.IsAwaitingCommand(0), "active state rejects invalid or backwards clock");
Check(session.PendingKind == CommandKind.SetColor, "state exposes pending color kind");
Check(session.Process("unrelated background noise", .1f, 2).Count == 0, "uncertain ambient noise ignored during pending window");
Check(session.PendingKind == CommandKind.SetColor && session.IsAwaitingCommand(3), "uncertain noise preserves pending window");
Check(Command(session.Process("nine zero five", VoiceSession.MinimumFollowUpConfidence, 3))?.Command?.Argument == "905", "short numeric answer accepts followup threshold");
Check(session.PendingKind == null && !session.IsAwaitingCommand(4), "successful followup clears active state");
Check(session.Process("equip outfit 2", .5f, 4).Count == 0, "lower followup threshold never arms ambient commands");
session.Process("hey jace", 1, 5);
Check(Command(session.Process("equip Top Hat", .5f, 6))?.Command?.Kind == CommandKind.Equip, "complete followup command accepts trusted active window");
session.Process("hey jace equip outfit", 1, 7);
Check(session.Process("two", .39f, 8).Count == 0, "below followup threshold ignored");
Check(Command(session.Process("two", .6f, 9))?.Command?.Argument == "2", "ignored short answer can be retried within same window");
session.Process("hey jace", 1, 10);
Check(session.Process("equip Top Hat", .5f, 20.001).Count == 0, "expired wake never accepts lower confidence command");

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
    "join code two unknown", "join code ABCDEFGHIJK", "join code ABC/DEF", "join code ABC;DEF",
    "join code jace thirty four and join code XX", "join code jace 34 equip Hat", "join code room with words",
    "equip outfit zero", "equip outfit -1", "equip outfit 1000", "equip outfit first twenty", "equip outfit 2 and join code ROOM",
    "change my color to red", "change my color to #a0B1c2", "change my color to ten zero five", "change my color to red and equip Hat", "change the time to tomorrow",
    "change the time to -1", "change the time to 256", "equip ../Hat", "equip Hat; join code ROOM", "unequip " + new string('A', 65),
    "change the time to 0am", "change the time to 13pm", "change the time to six am tomorrow", "change the time to 6:30 pm",
    "change the time to twenty am", "change the time to one two pm", "change the time to 12:00:00am", "change the time to tonight"
})
{
    session = new VoiceSession();
    var events = session.Process("hey jace " + invalid, 1, 0);
    Check(Command(events) == null && events.Any(e => e.Kind == VoiceEventKind.Prompt && e.Text.Length > 0), "invalid command prompts: " + invalid);
    // Repeating the full pending operation is allowed. A rejected color,
    // outfit, time, or cosmetic argument must never switch to joining a room.
    JaceCommand? retry = Command(session.Process("join code BANANA", 1, 1))?.Command;
    if (invalid.StartsWith("join code ", StringComparison.Ordinal))
        Check(retry is { Kind: CommandKind.JoinRoom, Argument: "BANANA" }, "invalid room argument can retry the full same operation: " + invalid);
    else
        Check(retry?.Kind != CommandKind.JoinRoom, "invalid argument never authorizes unrelated room command: " + invalid);
}
session = new VoiceSession();
var badColor = session.Process("hey jace change my color to red", 1, 0);
Check(Command(badColor) == null && badColor.Any(e => e.Kind == VoiceEventKind.Prompt && e.Text.Length is > 0 and <= 120), "named color requests a concise numeric correction");
Check(session.PendingKind == CommandKind.SetColor && session.IsAwaitingCommand(1), "invalid same-utterance color preserves intended operation");
Check(Command(session.Process("nine zero five", .5f, 1))?.Command?.Argument == "905", "numeric correction works without another wake");
session.Process("hey jace equip outfit", 1, 2);
var invalidOutfit = session.Process("zero", .5f, 11);
Check(Command(invalidOutfit) == null && invalidOutfit.Any(e => e.Kind == VoiceEventKind.Prompt), "invalid pending outfit prompts for another number");
Check(session.PendingKind == CommandKind.EquipOutfit && session.IsAwaitingCommand(21) && !session.IsAwaitingCommand(21.001), "invalid pending answer refreshes full ten-second reply window");
Check(Command(session.Process("two", .5f, 20))?.Command is { Kind: CommandKind.EquipOutfit, Argument: "2" }, "retry after original window still uses retained outfit operation");
session.Process("hey jace change my color to red", 1, 30);
session.Process("cancel", .5f, 31);
Check(!session.IsAwaitingCommand(31) && session.PendingKind == null && session.Process("905", 1, 32).Count == 0, "cancellation also clears retained correction intent");
session = new VoiceSession();
Check(session.Process("hey jace join code BANANA\n", 1, 0).Count == 0, "control character recognition rejected");
Check(session.Process("hey jace equip " + new string('A', 300), 1, 1).Count == 0, "oversize recognition rejected");

foreach (var sample in new[] { ("905", "905", 1f, 0f, 5f / 9), ("9, 0, 5", "905", 1f, 0f, 5f / 9), ("nine zero five", "905", 1f, 0f, 5f / 9), ("nine oh five", "905", 1f, 0f, 5f / 9), ("0 nought 9", "009", 0f, 0f, 1f), ("  zero, zero, zero  ", "000", 0f, 0f, 0f), ("NINE naught NINER", "909", 1f, 0f, 1f) })
{
    Check(ColorSpec.TryParse(sample.Item1, out var color) && color.Label == sample.Item2 && Math.Abs(color.R - sample.Item3) < .0001 && Math.Abs(color.G - sample.Item4) < .0001 && Math.Abs(color.B - sample.Item5) < .0001, "color " + sample.Item1);
}
for (int red = 0; red <= 9; red++)
    for (int green = 0; green <= 9; green++)
        for (int blue = 0; blue <= 9; blue++)
        {
            string code = $"{red}{green}{blue}";
            Check(ColorSpec.TryParse(code, out var color) && color.Label == code && Math.Abs(color.R - red / 9f) < .0001 && Math.Abs(color.G - green / 9f) < .0001 && Math.Abs(color.B - blue / 9f) < .0001, "game RGB code " + code);
        }
foreach (string value in new[] { "", "12", "1234", "9 0 10", "nine zero", "-1 0 5", "9 -1 5", "#123", "#GG0000", "red\nblue", "red\n", "red and blue", "#FF0080", "hex ff0080", "grey", "red", "blue", "FF0000" })
    Check(!ColorSpec.TryParse(value, out _), "invalid or non-game color " + value);

foreach (var message in new[]
{
    new WireMessage(WireKind.Hello, text: "Hey Jace 1.0"),
    new WireMessage(WireKind.Wake, 1),
    new WireMessage(WireKind.Command, int.MaxValue, command: new JaceCommand(CommandKind.JoinRoom, "BANANA")),
    new WireMessage(WireKind.Command, 7, command: new JaceCommand(CommandKind.SetColor, "905")),
    new WireMessage(WireKind.Result, 2, false, "Can't join: café 🍌"),
    new WireMessage(WireKind.Result, 3, true, new string('R', 512)),
    new WireMessage(WireKind.Status, text: "Listening"),
    new WireMessage(WireKind.Quit),
    new WireMessage(WireKind.Command, 8, command: new JaceCommand(CommandKind.EquipOutfit, "2")),
    new WireMessage(WireKind.Vocabulary, 1, text: "Top Hat|Jace’s Shirt")
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
