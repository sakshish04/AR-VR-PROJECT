namespace Group05.VoiceVR
{
    public enum ConfidenceDecision
    {
        /// <summary>Pass the phrase on to the parser/consumer.</summary>
        Accept,
        /// <summary>Do not execute; tell the user what was heard so they can repeat it.</summary>
        IgnoreWithNotice
    }

    /// <summary>
    /// Decides what to do with a recognized phrase given the recognizer's confidence. Unity-independent so it can be unit tested.
    /// The recognizer is created at its LOWEST floor (so nothing is discarded invisibly) and this policy applies the real bar.
    /// Ranks follow UnityEngine.Windows.Speech.ConfidenceLevel: 0 High, 1 Medium, 2 Low, 3 Rejected. A LARGER rank means LESS confident.
    /// </summary>
    public static class VoiceConfidencePolicy
    {
        public const int RankHigh = 0, RankMedium = 1, RankLow = 2, RankRejected = 3;

        /// <summary>Intents that undo or only report, so a wrong low-confidence match cannot move or select anything.</summary>
        public static bool IsUndoOrInfo(IntentKind kind)
        {
            return kind == IntentKind.Cancel || kind == IntentKind.Reset || kind == IntentKind.Release
                || kind == IntentKind.Help || kind == IntentKind.Focus;
        }

        public static ConfidenceDecision Decide(int confidenceRank, int floorRank, IntentKind kind, bool acceptLowConfidenceUndoCommands)
        {
            if (confidenceRank >= RankRejected) return ConfidenceDecision.IgnoreWithNotice;
            if (confidenceRank <= floorRank) return ConfidenceDecision.Accept;
            return acceptLowConfidenceUndoCommands && IsUndoOrInfo(kind) ? ConfidenceDecision.Accept : ConfidenceDecision.IgnoreWithNotice;
        }
    }
}
