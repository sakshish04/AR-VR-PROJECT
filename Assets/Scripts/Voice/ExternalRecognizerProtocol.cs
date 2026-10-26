using System;
using System.Globalization;

namespace Group05.VoiceVR
{
    /// <summary>
    /// The line protocol between Unity and the speech helper process (Tools/VoiceHelper/SystemSpeechHelper.ps1), and the confidence mapping.
    /// Pure C# (no Unity types) so it can be unit tested.
    ///
    /// Why a helper exists: Unity's own Windows keyword recognizer was observed to deliver phrases only while the Unity window had keyboard focus
    /// (every logged recognition callback had Application.isFocused = true). In the Meta XR Simulator the simulator window has focus, so no phrase arrived.
    /// The helper is a separate process running Windows' System.Speech engine on the default microphone, so focus does not matter.
    ///
    /// Lines the helper writes (one per line, UTF-8):
    ///   READY|&lt;engine id&gt;                         the engine is listening
    ///   PHRASE|&lt;confidence 0..1&gt;|&lt;speech end, UTC ISO-8601 or empty&gt;|&lt;recognized text&gt;
    ///   HEARD|rejected|&lt;confidence 0..1&gt;|&lt;best guess text, may be empty&gt;   speech was detected but matched no command (never executed)
    ///   PROBLEM|&lt;TooQuiet, TooLoud, TooNoisy, TooFast, TooSlow, NoSignal, ...&gt;   why the engine could not understand the audio
    ///   ERROR|&lt;message&gt;
    /// Anything else is ignored. The recognized text is always one of the fixed grammar phrases; it is never executed, only parsed by VoiceIntentParser.
    /// </summary>
    public static class ExternalRecognizerProtocol
    {
        public enum Kind { Ignored, Ready, Phrase, Error, Heard, Problem, Hypothesis }

        public struct Message
        {
            public Kind Kind;
            public string Text;          // phrase text, engine id or error message
            public float Confidence;     // 0..1, valid for Phrase
            public bool HasSpeechEnd;
            public DateTime SpeechEndUtc;
        }

        /// <summary>
        /// PROVISIONAL thresholds: System.Speech confidence at or above 0.50 is High, at or above 0.20 Medium, otherwise Low. System.Speech scores a correctly
        /// recognised phrase from a large keyword list much lower than Unity's categories suggest: in the first live session a spoken "select that" scored 0.22.
        /// The earlier 0.80 / 0.60 guesses rejected every real utterance. Still not calibrated; collect more real scores before trusting these.
        /// </summary>
        public const float HighThreshold = 0.50f;
        public const float MediumThreshold = 0.20f;

        /// <summary>Same ordering as UnityEngine.Windows.Speech.ConfidenceLevel: 0 High, 1 Medium, 2 Low (a larger number is less confident).</summary>
        public static int RankFromConfidence(float confidence)
        {
            if (float.IsNaN(confidence)) return 2;
            if (confidence >= HighThreshold) return 0;
            if (confidence >= MediumThreshold) return 1;
            return 2;
        }

        public static string RankName(int rank)
        {
            switch (rank) { case 0: return "High"; case 1: return "Medium"; default: return "Low"; }
        }

        /// <summary>Plain-language advice for an audio problem reported by the engine.</summary>
        public static string ProblemAdvice(string problem)
        {
            switch (problem)
            {
                case "TooQuiet": return "Microphone says: too quiet. Speak a little louder or move closer to the laptop.";
                case "TooLoud": return "Microphone says: too loud (distorted). Speak softer or move back a little.";
                case "TooNoisy": return "Microphone says: too much background noise. Move to a quieter spot or closer to the microphone.";
                case "TooFast": return "Microphone says: too fast. Speak a little slower.";
                case "TooSlow": return "Microphone says: too slow. Say the phrase in one go.";
                case "NoSignal": return "Microphone says: no signal. Check the microphone is not muted and is the Windows default input.";
                default: return "Microphone problem reported: " + (problem ?? "unknown") + ".";
            }
        }

        public static Message Parse(string line)
        {
            var m = new Message { Kind = Kind.Ignored, Text = "", Confidence = float.NaN };
            if (string.IsNullOrEmpty(line)) return m;
            line = line.TrimEnd('\r', '\n');
            int bar = line.IndexOf('|');
            if (bar <= 0) return m;
            string tag = line.Substring(0, bar);
            string rest = line.Substring(bar + 1);

            if (tag == "READY") { m.Kind = Kind.Ready; m.Text = rest; return m; }
            if (tag == "ERROR") { m.Kind = Kind.Error; m.Text = rest; return m; }
            if (tag == "PROBLEM") { m.Kind = Kind.Problem; m.Text = rest.Trim(); return m; }
            if (tag == "HYP")
            {
                int hb = rest.IndexOf('|');
                float hc;
                if (hb < 0 || !float.TryParse(rest.Substring(0, hb), NumberStyles.Float, CultureInfo.InvariantCulture, out hc) || float.IsNaN(hc) || float.IsInfinity(hc)) return m;
                m.Kind = Kind.Hypothesis;
                m.Confidence = hc;
                m.Text = rest.Substring(hb + 1).Trim();
                return m;
            }
            if (tag == "HEARD")
            {
                // HEARD|rejected|confidence|text
                string[] parts = rest.Split(new[] { '|' }, 3);
                if (parts.Length < 3) return m;
                float hc;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out hc) || float.IsNaN(hc) || float.IsInfinity(hc)) hc = float.NaN;
                m.Kind = Kind.Heard;
                m.Confidence = hc;
                m.Text = parts[2].Trim();
                return m;
            }
            if (tag != "PHRASE") return m;

            // PHRASE|confidence|speechEnd|text   (the text is last and may contain anything except a newline)
            int b1 = rest.IndexOf('|');
            if (b1 < 0) return m;
            int b2 = rest.IndexOf('|', b1 + 1);
            if (b2 < 0) return m;
            float conf;
            if (!float.TryParse(rest.Substring(0, b1), NumberStyles.Float, CultureInfo.InvariantCulture, out conf)) return m;
            if (float.IsNaN(conf) || float.IsInfinity(conf) || conf < 0f || conf > 1f) return m;
            string text = rest.Substring(b2 + 1).Trim();
            if (text.Length == 0) return m;

            m.Kind = Kind.Phrase;
            m.Confidence = conf;
            m.Text = text;
            string end = rest.Substring(b1 + 1, b2 - b1 - 1).Trim();
            DateTime dt;
            if (end.Length > 0 && DateTime.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
            {
                m.HasSpeechEnd = true;
                m.SpeechEndUtc = dt;
            }
            return m;
        }

        /// <summary>Milliseconds from the helper's speech end to now, or NaN when unknown or not plausible (same sanity range as the Unity path).</summary>
        public static float SinceSpeechEndMs(Message m, DateTime nowUtc)
        {
            if (!m.HasSpeechEnd) return float.NaN;
            double d = (nowUtc - m.SpeechEndUtc).TotalMilliseconds;
            return d >= 0.0 && d < 60000.0 ? (float)d : float.NaN;
        }
    }
}
