using HeyJace.Core;

namespace HeyJace.Speech;

// The game supplies only owned wardrobe names. A complete bounded transaction
// replaces the previous vocabulary; partial transactions never erase it.
internal sealed class RecognitionVocabulary
{
    internal const int MaximumNames = 4096;
    private HashSet<string>? _receiving;
    internal string[] Names { get; private set; } = Array.Empty<string>();

    internal bool Accept(WireMessage frame)
    {
        if (frame.Kind != WireKind.Vocabulary || frame.Command != null) return false;
        if (frame.Id == 0)
        {
            _receiving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return false;
        }
        if (_receiving == null) return false;
        if (frame.Id == 1)
        {
            foreach (string candidate in frame.Text.Split('|'))
            {
                string name = candidate.Trim();
                if (name.Length is < 1 or > 64 || name.Any(char.IsControl) ||
                    name.IndexOfAny(new[] { '\u2028', '\u2029' }) >= 0 || !name.Any(char.IsLetterOrDigit)) continue;
                if (_receiving.Count < MaximumNames) _receiving.Add(name);
            }
            return false;
        }
        if (frame.Id != 2) return false;
        string[] received = _receiving.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        _receiving = null;
        if (Names.SequenceEqual(received, StringComparer.OrdinalIgnoreCase)) return false;
        Names = received;
        return true;
    }
}
