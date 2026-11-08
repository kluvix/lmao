using System.Speech.Recognition;
using HeyJace.Core;

namespace HeyJace.Speech;

internal static class RecognitionSemantics
{
    internal static JaceCommand? Read(RecognizedPhrase phrase)
    {
        try
        {
            SemanticValue semantics = phrase.Semantics;
            if (!semantics.ContainsKey("jaceIntent")) return null;
            SemanticValue intent = semantics["jaceIntent"];
            SemanticValue? argument = semantics.ContainsKey("jaceArgument") ? semantics["jaceArgument"] :
                intent.ContainsKey("jaceArgument") ? intent["jaceArgument"] : null;
            return Create(intent.Value as string, argument?.Value as string);
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or System.Runtime.InteropServices.COMException)
        { return null; }
    }

    internal static JaceCommand? Create(string? intent, string? argument)
    {
        if (!Enum.TryParse(intent, false, out CommandKind kind) || kind is not (CommandKind.Equip or CommandKind.Unequip) ||
            string.IsNullOrWhiteSpace(argument) || argument.Length > 64 || argument.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029')) return null;
        return new JaceCommand(kind, argument);
    }
}
