using HeyJace.Core;

namespace HeyJace.Speech;

internal readonly record struct RecognitionCandidate(string Text, float Confidence, JaceCommand? SemanticCommand = null, bool AmbiguousAction = false);

// Alternatives may rescue a command, but never replace one already understood,
// invent a wake, or execute more than one interpretation of the same utterance.
internal static class RecognitionCandidates
{
    internal static RecognitionCandidate Select(VoiceSession session, IReadOnlyList<RecognitionCandidate> candidates, double time)
    {
        RecognitionCandidate first = candidates[0];
        IReadOnlyList<VoiceEvent> primary = session.Preview(first.Text, first.Confidence, time, first.SemanticCommand);
        bool active = session.IsAwaitingCommand(time);
        bool woke = primary.Any(item => item.Kind == VoiceEventKind.Wake);
        if (!active && !woke) return first;
        JaceCommand? primaryCommand = primary.FirstOrDefault(item => item.Kind == VoiceEventKind.Command)?.Command;
        if (primaryCommand != null)
        {
            if (IsUnclearEquipVerb(first.Text) && HasOppositeAlternative(session, candidates, primaryCommand, first.Confidence, time))
                return first with { AmbiguousAction = true };
            return first;
        }
        CommandKind? pending = active && !woke ? session.PendingKind : null;
        CommandKind? explicitIntent = first.SemanticCommand?.Kind;
        if (VoiceSession.TryIdentifyIntent(first.Text, out CommandKind identified)) explicitIntent = identified;
        float minimum = Math.Max(VoiceSession.MinimumFollowUpConfidence, first.Confidence - .15f);
        for (int index = 1; index < Math.Min(candidates.Count, 5); index++)
        {
            RecognitionCandidate candidate = candidates[index];
            if (!float.IsFinite(candidate.Confidence) || candidate.Confidence < minimum) continue;
            JaceCommand? command = session.Preview(candidate.Text, candidate.Confidence, time, candidate.SemanticCommand)
                .FirstOrDefault(item => item.Kind == VoiceEventKind.Command)?.Command;
            if (command != null && (!pending.HasValue || command.Kind == pending.Value) &&
                (!explicitIntent.HasValue || command.Kind == explicitIntent.Value)) return candidate;
        }
        return first;
    }

    internal static IReadOnlyList<VoiceEvent> Process(VoiceSession session, RecognitionCandidate selected, double time)
    {
        if (!selected.AmbiguousAction) return session.Process(selected.Text, selected.Confidence, time, selected.SemanticCommand);
        bool woke = session.Preview(selected.Text, selected.Confidence, time, selected.SemanticCommand).Any(item => item.Kind == VoiceEventKind.Wake);
        var events = new List<VoiceEvent>();
        if (woke) events.AddRange(session.Process("hey jace", selected.Confidence, time));
        if (session.IsAwaitingCommand(time))
            events.Add(new VoiceEvent(VoiceEventKind.Prompt, "Say 'hey jace put on' or 'hey jace take off'."));
        return events;
    }

    private static bool HasOppositeAlternative(VoiceSession session, IReadOnlyList<RecognitionCandidate> candidates,
        JaceCommand primary, float confidence, double time)
    {
        if (primary.Kind != CommandKind.Equip && primary.Kind != CommandKind.Unequip) return false;
        CommandKind opposite = primary.Kind == CommandKind.Equip ? CommandKind.Unequip : CommandKind.Equip;
        for (int i = 1; i < Math.Min(candidates.Count, 5); i++)
        {
            RecognitionCandidate candidate = candidates[i];
            if (!float.IsFinite(candidate.Confidence) || candidate.Confidence < Math.Max(VoiceSession.MinimumFollowUpConfidence, confidence - .06f)) continue;
            JaceCommand? command = session.Preview(candidate.Text, candidate.Confidence, time, candidate.SemanticCommand)
                .FirstOrDefault(item => item.Kind == VoiceEventKind.Command)?.Command;
            if (command?.Kind == opposite && SameName(command.Argument, primary.Argument)) return true;
        }
        return false;
    }

    private static bool SameName(string first, string second) =>
        string.Equals(new string(first.Where(char.IsLetterOrDigit).ToArray()), new string(second.Where(char.IsLetterOrDigit).ToArray()), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnclearEquipVerb(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:^|[\s,])(?:equip|quip|quipe|unequip|unequipe|unquip|unquipe)(?:$|\s)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
