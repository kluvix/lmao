using HeyJace.Core;

namespace HeyJace.Speech;

internal readonly record struct RecognitionCandidate(string Text, float Confidence);

// Alternatives may rescue a command, but never replace one already understood,
// invent a wake, or execute more than one interpretation of the same utterance.
internal static class RecognitionCandidates
{
    internal static RecognitionCandidate Select(VoiceSession session, IReadOnlyList<RecognitionCandidate> candidates, double time)
    {
        RecognitionCandidate first = candidates[0];
        IReadOnlyList<VoiceEvent> primary = session.Preview(first.Text, first.Confidence, time);
        if (primary.Any(item => item.Kind == VoiceEventKind.Command)) return first;
        bool active = session.IsAwaitingCommand(time);
        bool woke = primary.Any(item => item.Kind == VoiceEventKind.Wake);
        if (!active && !woke) return first;
        CommandKind? pending = active && !woke ? session.PendingKind : null;
        float minimum = Math.Max(VoiceSession.MinimumFollowUpConfidence, first.Confidence - .15f);
        for (int index = 1; index < Math.Min(candidates.Count, 5); index++)
        {
            RecognitionCandidate candidate = candidates[index];
            if (!float.IsFinite(candidate.Confidence) || candidate.Confidence < minimum) continue;
            JaceCommand? command = session.Preview(candidate.Text, candidate.Confidence, time)
                .FirstOrDefault(item => item.Kind == VoiceEventKind.Command)?.Command;
            if (command != null && (!pending.HasValue || command.Kind == pending.Value)) return candidate;
        }
        return first;
    }
}
