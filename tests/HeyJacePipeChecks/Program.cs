using System.IO.Pipes;
using System.Text;
using HeyJace;
using HeyJace.Core;

int assertions = 0;
void Check(bool value, string name) { assertions++; if (!value) throw new Exception(name); }
async Task<WireMessage> Next(SpeechBridge bridge, WireKind kind)
{
    using var deadline = new CancellationTokenSource(4000);
    while (true)
    {
        deadline.Token.ThrowIfCancellationRequested();
        while (bridge.TryRead(out WireMessage? frame)) if (frame?.Kind == kind) return frame;
        await Task.Delay(10, deadline.Token);
    }
}

await using (var session = await Session.Create())
{
    Check(!session.Bridge.Connected, "bridge is not ready before the helper handshake");
    await session.Send(new WireMessage(WireKind.Hello, text: PipeProtocol.Handshake));
    await session.Writer.WriteLineAsync("malformed line");
    await session.Send(new WireMessage(WireKind.Wake, text: "what's up"));
    await session.Send(new WireMessage(WireKind.Command, 7, command: new JaceCommand(CommandKind.JoinRoom, "BANANA")));
    Check((await Next(session.Bridge, WireKind.Wake)).Text == "what's up", "wake delivery");
    WireMessage command = await Next(session.Bridge, WireKind.Command);
    Check(command.Id == 7 && command.Command?.Argument == "BANANA", "command delivery after malformed line");
    Check(session.Bridge.Connected, "verified helper handshake marks bridge ready");
    session.Bridge.SendResult(7, new ActionResult(true, "Joining\r\nBANANA"));
    string? reply = await session.Reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));
    Check(reply != null && PipeProtocol.TryDecode(reply, out WireMessage? decoded) && decoded != null && decoded.Kind == WireKind.Result && decoded.Id == 7 && decoded.Text == "Joining  BANANA", "sanitized duplex reply");
    session.Bridge.SendResult(8, new ActionResult(false, new string('x', 800)));
    reply = await session.Reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));
    Check(reply != null && PipeProtocol.TryDecode(reply, out WireMessage? capped) && capped != null && capped.Text.Length == 500, "bounded result text");
    Check(session.Bridge.TrySendVocabulary(0, "") && session.Bridge.TrySendVocabulary(1, "Top Hat|Bunny Ears") &&
        session.Bridge.TrySendVocabulary(2, ""), "owned-name vocabulary queued without blocking");
    for (int phase = 0; phase < 3; phase++)
    {
        reply = await session.Reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));
        Check(reply != null && PipeProtocol.TryDecode(reply, out WireMessage? vocabulary) && vocabulary != null &&
            vocabulary.Kind == WireKind.Vocabulary && vocabulary.Id == phase &&
            vocabulary.Text == (phase == 1 ? "Top Hat|Bunny Ears" : ""), "vocabulary phase " + phase + " survives actual duplex pipe");
    }
}

await using (var session = await Session.Create())
{
    await session.Send(new WireMessage(WireKind.Hello, text: "HeyJace/1.2"));
    WireMessage failure = await Next(session.Bridge, WireKind.Status);
    Check(!failure.Success && failure.Text.Contains("Replace the whole HeyJace folder"), "mixed old speech helper gets actionable version error");
    Check(!session.Bridge.Connected, "old helper never becomes ready");
}

await using (var session = await Session.Create())
{
    await session.Send(new WireMessage(WireKind.Command, 1, command: new JaceCommand(CommandKind.SetTime, "night")));
    WireMessage failure = await Next(session.Bridge, WireKind.Status);
    Check(!failure.Success && failure.Text.Contains("Unexpected speech helper protocol"), "requires hello before command and preserves error status");
    Check(!session.Bridge.TryRead(out WireMessage? extra) || extra?.Kind != WireKind.Command, "unverified command not queued");
}

await using (var session = await Session.Create())
{
    await session.Send(new WireMessage(WireKind.Hello, text: PipeProtocol.Handshake));
    await session.Writer.WriteLineAsync(new string('z', PipeProtocol.MaxLineLength + 1));
    Check((await Next(session.Bridge, WireKind.Status)).Text.Contains("too long"), "overlong incoming frame rejected");
}

await using (var session = await Session.Create())
{
    await session.Send(new WireMessage(WireKind.Hello, text: PipeProtocol.Handshake));
    await session.Writer.WriteAsync(PipeProtocol.Encode(new WireMessage(WireKind.Command, 3, command: new JaceCommand(CommandKind.Equip, "bunny"))));
    await session.Writer.FlushAsync();
    session.Server.Dispose();
    Check((await Next(session.Bridge, WireKind.Status)).Text.Contains("closed"), "EOF closes session");
    Check(!session.Bridge.TryRead(out WireMessage? extra) || extra?.Kind != WireKind.Command, "partial final frame discarded");
}

await using (var session = await Session.Create())
{
    session.Bridge.Dispose(); session.Bridge.Dispose();
    string? quit = await session.Reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));
    Check(quit == null || (PipeProtocol.TryDecode(quit, out WireMessage? message) && message != null && message.Kind == WireKind.Quit), "idempotent shutdown closes or quits");
}

await using (var session = await Session.Create())
{
    await session.Send(new WireMessage(WireKind.Hello, text: PipeProtocol.Handshake));
    await session.Send(new WireMessage(WireKind.Wake));
    await Next(session.Bridge, WireKind.Wake);
    // The helper deliberately never reads replies. A full OS pipe must not
    // freeze the Unity thread or prevent the user's F10/off action.
    var clock = System.Diagnostics.Stopwatch.StartNew();
    for (int i = 0; i < 1000; i++) session.Bridge.SendResult(i + 1, new ActionResult(true, new string('x', 500)));
    Check(clock.ElapsedMilliseconds < 1000, "nonreading helper cannot block command replies on main thread");
    clock.Restart(); session.Bridge.Dispose();
    Check(clock.ElapsedMilliseconds < 1000, "nonreading helper cannot block shutdown on main thread");
}
Console.WriteLine($"PASS: {assertions} live named-pipe integration assertions. No game, microphone, or speech engine was executed.");

sealed class Session : IAsyncDisposable
{
    internal NamedPipeServerStream Server { get; }
    internal SpeechBridge Bridge { get; } = new();
    internal StreamWriter Writer { get; private set; } = null!;
    internal StreamReader Reader { get; private set; } = null!;
    private Session(string name) => Server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    internal static async Task<Session> Create()
    {
        string name = "HeyJaceTest_" + Guid.NewGuid().ToString("N");
        var session = new Session(name);
        try
        {
            session.Bridge.ConnectPipe(name);
            using var timeout = new CancellationTokenSource(4000);
            await session.Server.WaitForConnectionAsync(timeout.Token);
            session.Writer = new StreamWriter(session.Server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            session.Reader = new StreamReader(session.Server, Encoding.UTF8, false, 1024, true);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }
    internal Task Send(WireMessage message) => Writer.WriteLineAsync(PipeProtocol.Encode(message));
    public ValueTask DisposeAsync()
    {
        Bridge.Dispose();
        try { Writer?.Dispose(); } catch { }
        try { Reader?.Dispose(); } catch { }
        Server.Dispose();
        return ValueTask.CompletedTask;
    }
}
