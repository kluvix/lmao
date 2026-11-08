using System;
using System.Globalization;
using System.Text;

namespace HeyJace.Core;

/// <summary>One UTF-8 line: kind, request id, success, base64 text, command kind or '-', base64 argument.</summary>
public static class PipeProtocol
{
    public const string Handshake = "HeyJace/1.1";
    public const int MaxLineLength = 4096;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static string Encode(WireMessage message)
    {
        if (message == null || !Valid(message)) throw new ArgumentException("Invalid pipe message.", nameof(message));
        string line;
        try
        {
            line = ((int)message.Kind).ToString(CultureInfo.InvariantCulture) + "\t" +
                message.Id.ToString(CultureInfo.InvariantCulture) + "\t" + (message.Success ? "1" : "0") + "\t" +
                ToBase64(message.Text) + "\t" +
                (message.Command == null ? "-" : ((int)message.Command.Kind).ToString(CultureInfo.InvariantCulture)) + "\t" +
                ToBase64(message.Command?.Argument ?? "");
        }
        catch (EncoderFallbackException exception)
        { throw new ArgumentException("Message contains invalid Unicode.", nameof(message), exception); }
        if (line.Length > MaxLineLength) throw new ArgumentException("Pipe line is too long.", nameof(message));
        return line;
    }

    public static bool TryDecode(string line, out WireMessage? message)
    {
        message = null;
        if (line == null || line.Length > MaxLineLength) return false;
        string[] fields = line.Split('\t');
        if (fields.Length != 6 || !TryNumber(fields[0], out int kindValue) || !Enum.IsDefined(typeof(WireKind), kindValue) ||
            !TryNumber(fields[1], out int id) || (fields[2] != "0" && fields[2] != "1")) return false;
        if (!FromBase64(fields[3], 512, out string text) || !FromBase64(fields[5], 64, out string argument)) return false;
        JaceCommand? command = null;
        if (fields[4] != "-")
        {
            if (!TryNumber(fields[4], out int commandValue) || !Enum.IsDefined(typeof(CommandKind), commandValue)) return false;
            command = new JaceCommand((CommandKind)commandValue, argument);
        }
        else if (argument.Length != 0) return false;
        var decoded = new WireMessage((WireKind)kindValue, id, fields[2] == "1", text, command);
        if (!Valid(decoded)) return false;
        message = decoded;
        return true;
    }

    private static bool Valid(WireMessage message)
    {
        if (!Enum.IsDefined(typeof(WireKind), message.Kind) || message.Id < 0 || !ValidText(message.Text, 512)) return false;
        if (message.Kind == WireKind.Command)
            return message.Command != null && Enum.IsDefined(typeof(CommandKind), message.Command.Kind) &&
                ValidText(message.Command.Argument, 64) && message.Command.Argument.Length > 0;
        return message.Command == null;
    }

    private static bool ValidText(string text, int max)
    {
        if (text == null || text.Length > max) return false;
        foreach (char c in text) if (char.IsControl(c) || c == '\u2028' || c == '\u2029') return false;
        return true;
    }

    private static bool TryNumber(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0;

    private static string ToBase64(string value) => Convert.ToBase64String(Utf8.GetBytes(value));

    private static bool FromBase64(string value, int maxChars, out string text)
    {
        text = "";
        // Each UTF-16 character needs at most three UTF-8 bytes; this also bounds allocation before conversion.
        if (value.Length > ((maxChars * 3 + 2) / 3) * 4) return false;
        try
        {
            byte[] bytes = Convert.FromBase64String(value);
            if (Convert.ToBase64String(bytes) != value) return false;
            text = Utf8.GetString(bytes);
            return ValidText(text, maxChars);
        }
        catch (FormatException) { return false; }
        catch (DecoderFallbackException) { return false; }
    }
}
