namespace HeyJace.Core;

public enum CommandKind { JoinRoom, SetColor, SetTime, Equip, Unequip }

public sealed class JaceCommand
{
    public CommandKind Kind { get; }
    public string Argument { get; }
    public JaceCommand(CommandKind kind, string argument) { Kind = kind; Argument = argument; }
}

public readonly struct ActionResult
{
    public bool Success { get; }
    public string Message { get; }
    public ActionResult(bool success, string message) { Success = success; Message = message; }
}

public enum VoiceEventKind { Wake, Prompt, Command }

public sealed class VoiceEvent
{
    public VoiceEventKind Kind { get; }
    public string Text { get; }
    public JaceCommand? Command { get; }
    public VoiceEvent(VoiceEventKind kind, string text = "", JaceCommand? command = null)
    { Kind = kind; Text = text; Command = command; }
}

public enum WireKind { Hello, Wake, Command, Result, Status, Quit }

public sealed class WireMessage
{
    public WireKind Kind { get; }
    public int Id { get; }
    public bool Success { get; }
    public string Text { get; }
    public JaceCommand? Command { get; }
    public WireMessage(WireKind kind, int id = 0, bool success = true, string text = "", JaceCommand? command = null)
    { Kind = kind; Id = id; Success = success; Text = text; Command = command; }
}
