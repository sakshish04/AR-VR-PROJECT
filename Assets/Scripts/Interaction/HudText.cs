using System;
using System.Text;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Unity-independent text formatting shared by the desktop HUD (IMGUI) and the headset panel (XrHudPanel), kept free of any
    /// UnityEngine type so the standalone tests can exercise it. One definition of the command hints means the two displays
    /// cannot disagree about what to say.
    /// </summary>
    public static class HudText
    {
        /// <summary>The "say ..." hint line. Mentions "drop it": in the session logs the recognizer delivered it reliably and the one-word "release" never.</summary>
        public static string CommandHints(bool hasSelection, bool compact, bool carrying = false)
        {
            if (carrying)
                return compact ? "drop this object (or drop it) | cancel"
                               : "drop this object (or 'drop it')  |  cancel  |  the object follows you until you drop it";
            if (hasSelection)
                return compact ? "move left 30 cm / there | grab this object | drop it | cancel"
                               : "move there  |  move left / right / up / down [thirty centimeters]  |  grab this object  |  release (or 'drop it')  |  cancel";
            return "select that  |  grab this object  |  what is this  |  reset  |  help";
        }

        /// <summary>Word-wraps to at most <paramref name="maxChars"/> per line. A single word longer than the limit stays whole (never cut).</summary>
        public static string Wrap(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (maxChars < 1) maxChars = 1;
            if (s.Length <= maxChars) return s;
            var sb = new StringBuilder(s.Length + 4);
            int col = 0;
            foreach (string word in s.Split(' '))
            {
                if (word.Length == 0) continue;
                if (col > 0 && col + 1 + word.Length > maxChars) { sb.Append('\n'); col = 0; }
                if (col > 0) { sb.Append(' '); col++; }
                sb.Append(word);
                col += word.Length;
            }
            return sb.ToString();
        }

        /// <summary>
        /// <see cref="Wrap"/>, then at most <paramref name="maxLines"/> lines. When lines are dropped, the last kept line ends in "..." and still
        /// fits <paramref name="maxChars"/>. Used by the headset panel so one long result can never grow the panel into the centre of the view
        /// (the full text stays in the Console and the desktop HUD).
        /// </summary>
        public static string WrapLimited(string s, int maxChars, int maxLines)
        {
            string wrapped = Wrap(s, maxChars);
            if (maxLines < 1) maxLines = 1;
            string[] lines = wrapped.Split('\n');
            if (lines.Length <= maxLines) return wrapped;
            var sb = new StringBuilder();
            for (int i = 0; i < maxLines - 1; i++) sb.Append(lines[i]).Append('\n');
            string last = lines[maxLines - 1];
            int room = Math.Max(0, maxChars - 3);
            if (last.Length > room) last = last.Substring(0, room);
            sb.Append(last).Append("...");
            return sb.ToString();
        }

        /// <summary>
        /// What the F8 typed-command box shows in the headset panel: the end of the text typed so far, then a caret. A long entry keeps its
        /// LAST characters (where the user is typing), preceded by "...". Never longer than <paramref name="maxChars"/>.
        /// </summary>
        public static string TypedBoxText(string typed, int maxChars)
        {
            string line = (typed ?? "") + "_";
            if (maxChars < 4) maxChars = 4;
            if (line.Length <= maxChars) return line;
            return "..." + line.Substring(line.Length - (maxChars - 3));
        }

        /// <summary>Number of lines in <paramref name="s"/> (0 for null/empty).</summary>
        public static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 1;
            for (int i = 0; i < s.Length; i++) if (s[i] == '\n') n++;
            return n;
        }
    }
}
