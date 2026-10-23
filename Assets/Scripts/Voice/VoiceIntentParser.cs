using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Group05.VoiceVR
{
    public enum IntentKind
    {
        Unknown,
        Focus,
        Select,
        Move,
        Release,
        Cancel,
        Reset,
        Help,
        /// <summary>Grab AND carry: the object follows the user until a release / drop command. Appended last so existing numeric values are unchanged.</summary>
        Grab
    }

    /// <summary>
    /// Outcome of parsing. Only <see cref="Ok"/> may lead to an action; every other status must be rejected.
    /// </summary>
    public enum ParseStatus
    {
        Ok,
        Empty,
        /// <summary>No vocabulary phrase found.</summary>
        Unknown,
        /// <summary>A negation word is present ("do not release"); never executed.</summary>
        Negated,
        /// <summary>More than one different action matched ("select and release"); never executed.</summary>
        Ambiguous
    }

    /// <summary>
    /// Where an utterance came from. Keeps real speech distinguishable from fallback input in logs and telemetry.
    /// </summary>
    public enum InputModality
    {
        Voice,
        KeyboardFallback,
        TextFallback,
        /// <summary>Physical-controller condition (no speech involved).</summary>
        Controller
    }

    /// <summary>Structured result of parsing one utterance.</summary>
    public readonly struct ParsedIntent
    {
        public readonly ParseStatus Status;
        /// <summary>The action. <see cref="IntentKind.Unknown"/> whenever Status is not Ok.</summary>
        public readonly IntentKind Kind;
        public readonly string RawText;
        public readonly string NormalizedText;
        /// <summary>Spoken reference to the object acted on, e.g. "red cube" from "select the red cube". Null for "that"/"this"/"it" (meaning: use gaze).</summary>
        public readonly string TargetName;
        /// <summary>Spoken destination, e.g. "shelf" from "move it to the shelf". Null for "there"/"here" (meaning: use gaze point).</summary>
        public readonly string DestinationName;
        /// <summary>Optional direction word (left/right/up/down/forward/back...). Null if absent.</summary>
        public readonly string Direction;
        /// <summary>
        /// Heuristic text-match score in [0,1] based on how many unexplained words surround the phrase.
        /// NOT an acoustic or recognizer confidence; never compare it to a speech-recognition threshold.
        /// 0 whenever Status is not Ok.
        /// </summary>
        public readonly float ParseScore;
        public readonly string Error;
        /// <summary>Spoken move distance in metres ("30 centimeters" is 0.3). NaN when none was spoken (the caller then uses its default step).</summary>
        public readonly float DistanceMeters;

        public bool HasDistance { get { return !float.IsNaN(DistanceMeters); } }

        public bool IsActionable { get { return Status == ParseStatus.Ok && Kind != IntentKind.Unknown; } }

        public ParsedIntent(ParseStatus status, IntentKind kind, string raw, string normalized, string target,
                            string destination, string direction, float parseScore, string error)
            : this(status, kind, raw, normalized, target, destination, direction, parseScore, error, float.NaN) { }

        public ParsedIntent(ParseStatus status, IntentKind kind, string raw, string normalized, string target,
                            string destination, string direction, float parseScore, string error, float distanceMeters)
        {
            DistanceMeters = distanceMeters;
            Status = status;
            Kind = kind;
            RawText = raw;
            NormalizedText = normalized;
            TargetName = target;
            DestinationName = destination;
            Direction = direction;
            ParseScore = parseScore;
            Error = error;
        }

        public override string ToString()
        {
            return Status + "/" + Kind + " (parse score " + ParseScore.ToString("F2", CultureInfo.InvariantCulture)
                   + (TargetName != null ? ", target=" + TargetName : "")
                   + (DestinationName != null ? ", dest=" + DestinationName : "")
                   + (Direction != null ? ", dir=" + Direction : "")
                   + (HasDistance ? ", distance=" + DistanceMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m" : "")
                   + (Error != null ? ", error=" + Error : "") + ")";
        }
    }

    /// <summary>
    /// Deterministic keyword/phrase intent parser. No Unity or speech-service dependency:
    /// it maps already-recognized text to an <see cref="IntentKind"/>.
    /// Safety rules: negated or ambiguous (multi-action) utterances are rejected, never guessed.
    /// </summary>
    public static class VoiceIntentParser
    {
        // Words that carry no intent and are stripped when estimating the parse score and extracting names.
        private static readonly HashSet<string> Fillers = new HashSet<string>
        {
            "please", "um", "uh", "hey", "okay", "ok", "now", "just", "kindly", "can", "could", "you", "would", "will",
            "i", "want", "to", "like", "need", "let", "me", "lets", "the", "a", "an", "it", "that", "this", "those", "these", "one", "by"
        };

        // Deictic words that mean "use the gaze" rather than naming an object. "target" is generic ("select target" is a KeywordGrammar phrase):
        // without it the parser read "target" as an object NAME, and every real target ("blue sphere") then failed with target_mismatch.
        private static readonly HashSet<string> Deictic = new HashSet<string> { "that", "this", "it", "there", "here", "those", "these", "one", "over", "target", "object", "thing", "item" };

        private static readonly HashSet<string> NegationWords = new HashSet<string>
        {
            "not", "no", "dont", "never", "cant", "cannot", "wont", "didnt", "doesnt", "isnt", "shouldnt", "without", "nothing"
        };

        private sealed class Vocabulary
        {
            public readonly IntentKind Kind;
            public readonly string[] Phrases;
            public Vocabulary(IntentKind kind, params string[] phrases) { Kind = kind; Phrases = phrases; }
        }

        private static readonly Vocabulary[] Vocabularies =
        {
            // Grab = select + carry. These are LONGER than the Select words they contain ("grab", "pick up") and the Move word "carry",
            // so the subsumption rule below picks Grab; plain "grab this" / "pick up the cube" still mean Select.
            new Vocabulary(IntentKind.Grab,
                "grab this object", "grab that object", "grab the object", "carry this object", "carry that object", "carry the object",
                "pick up this object", "pick up that object", "pick up the object"),
            new Vocabulary(IntentKind.Help,
                "what can i say", "what do i say", "what can i do", "what commands", "which commands",
                "list commands", "show commands", "help", "commands", "instructions"),
            new Vocabulary(IntentKind.Cancel,
                "cancel", "abort", "never mind", "nevermind", "stop", "undo", "go back", "forget it"),
            new Vocabulary(IntentKind.Reset,
                "reset", "start over", "restart"),
            new Vocabulary(IntentKind.Release,
                "release", "drop", "let go", "put down", "put it down", "put that down", "put this down",
                "set down", "set it down", "done", "finish", "finished", "confirm"),
            new Vocabulary(IntentKind.Focus,
                "focus", "identify", "what is this", "what is that", "whats this", "whats that",
                "what am i looking at", "where am i looking", "look at"),
            new Vocabulary(IntentKind.Move,
                "move", "push", "put", "place", "bring", "carry", "shift", "send", "go", "slide", "relocate"),
            new Vocabulary(IntentKind.Select,
                "select", "grab", "pick", "pick up", "choose", "hold", "take", "get", "lock on")
        };

        private static readonly string[] Directions = { "left", "right", "up", "down", "forward", "back", "backward", "closer", "away" };

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            bool lastSpace = true;
            foreach (char raw in text)
            {
                char c = char.ToLowerInvariant(raw);
                if (char.IsLetterOrDigit(c)) { sb.Append(c); lastSpace = false; }
                else if (c == '\'' || c == '’') { /* drop apostrophes: "what's" -> "whats" */ }
                else if (!lastSpace) { sb.Append(' '); lastSpace = true; }
            }
            return sb.ToString().Trim();
        }

        public static ParsedIntent Parse(string text)
        {
            string norm = Normalize(text);
            if (norm.Length == 0)
                return Reject(ParseStatus.Empty, text, norm, "Empty command.");

            string[] tokens = norm.Split(' ');

            // "never mind" is a cancel phrase, not a negation, so mask it before checking negation words.
            bool negated = false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!NegationWords.Contains(tokens[i])) continue;
                if (tokens[i] == "never" && i + 1 < tokens.Length && tokens[i + 1] == "mind") continue;
                negated = true;
                break;
            }

            // Collect every phrase match, then drop matches subsumed by a longer phrase of a different action
            // (e.g. "put" inside "put it down", "go" inside "go back").
            var matches = new List<Match>();
            string padded = " " + norm + " ";
            foreach (Vocabulary v in Vocabularies)
                foreach (string p in v.Phrases)
                    if (padded.Contains(" " + p + " ")) matches.Add(new Match(v.Kind, p));

            var kept = new List<Match>();
            foreach (Match m in matches)
            {
                bool subsumed = false;
                foreach (Match other in matches)
                {
                    if (other.Kind != m.Kind && other.Phrase.Length > m.Phrase.Length
                        && (" " + other.Phrase + " ").Contains(" " + m.Phrase + " "))
                    { subsumed = true; break; }
                }
                if (!subsumed) kept.Add(m);
            }

            var kinds = new HashSet<IntentKind>();
            foreach (Match m in kept) kinds.Add(m.Kind);

            if (kinds.Count == 0)
                return Reject(ParseStatus.Unknown, text, norm,
                    "Unknown command '" + norm + "'. Say 'help' to hear what is supported.");
            if (negated)
                return Reject(ParseStatus.Negated, text, norm, "Negated command not executed: '" + norm + "'.");
            if (kinds.Count > 1)
                return Reject(ParseStatus.Ambiguous, text, norm,
                    "Ambiguous command not executed: '" + norm + "'. Give one action at a time.");

            IntentKind kind = kept[0].Kind;
            string[] words = tokens;
            float distance = float.NaN;
            // Longest matching phrase of that kind anchors name extraction.
            string anchor = kept[0].Phrase;
            foreach (Match m in kept) if (m.Phrase.Length > anchor.Length) anchor = m.Phrase;

            string direction = null, target = null, destination = null;
            if (kind == IntentKind.Move)
            {
                distance = ExtractDistance(text, tokens, out words);   // "30 centimeters", "0.5 m", "thirty centimeters", "half a meter"
                direction = FindDirection(words);
                ExtractMoveNames(words, anchor, out target, out destination);
            }
            else if (kind == IntentKind.Select || kind == IntentKind.Focus)
            {
                target = ExtractWordsAfter(tokens, anchor);
            }

            return new ParsedIntent(ParseStatus.Ok, kind, text, norm, target, destination, direction, ComputeParseScore(words), null, distance);
        }

        // ---- spoken distances ----

        private static readonly Regex DigitDistance = new Regex(
            @"(?<![\w.,])(\d+(?:[.,]\d+)?)\s*(millimet(?:er|re)s?|centimet(?:er|re)s?|met(?:er|re)s?|mm|cm|m)\b",
            RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, int> NumberWords = new Dictionary<string, int>
        {
            { "zero", 0 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 }, { "six", 6 }, { "seven", 7 }, { "eight", 8 },
            { "nine", 9 }, { "ten", 10 }, { "eleven", 11 }, { "twelve", 12 }, { "thirteen", 13 }, { "fourteen", 14 }, { "fifteen", 15 },
            { "sixteen", 16 }, { "seventeen", 17 }, { "eighteen", 18 }, { "nineteen", 19 }, { "twenty", 20 }, { "thirty", 30 }, { "forty", 40 },
            { "fifty", 50 }, { "sixty", 60 }, { "seventy", 70 }, { "eighty", 80 }, { "ninety", 90 }
        };

        private static float UnitMeters(string unit)
        {
            if (unit.StartsWith("milli") || unit == "mm") return 0.001f;
            if (unit.StartsWith("centi") || unit == "cm") return 0.01f;
            return 1f;
        }

        private static bool IsUnitWord(string t)
        {
            return t == "cm" || t == "mm" || t.StartsWith("centimet") || t.StartsWith("millimet") || t == "meter" || t == "meters" || t == "metre" || t == "metres";
        }

        /// <summary>
        /// Finds a spoken distance and returns it in metres (NaN if none). <paramref name="remaining"/> is the token list WITHOUT the distance words,
        /// so numbers and units are never mistaken for an object name. Digits are read from the raw text because normalisation drops the decimal point.
        /// </summary>
        private static float ExtractDistance(string raw, string[] tokens, out string[] remaining)
        {
            remaining = tokens;
            string lower = (raw ?? string.Empty).ToLowerInvariant();
            System.Text.RegularExpressions.Match m = DigitDistance.Match(lower);
            if (m.Success)
            {
                double v;
                if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    string rest = lower.Remove(m.Index, m.Length).Insert(m.Index, " ");
                    string normRest = Normalize(rest);
                    remaining = normRest.Length == 0 ? new string[0] : normRest.Split(' ');
                    return (float)(v * UnitMeters(m.Groups[2].Value));
                }
            }
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!IsUnitWord(tokens[i])) continue;
                int start = i;
                double value = double.NaN;
                if (i >= 1 && tokens[i - 1] == "half") { start = i - 1; value = 0.5; }
                else if (i >= 2 && tokens[i - 1] == "a" && tokens[i - 2] == "half") { start = i - 2; value = 0.5; }
                else
                {
                    int s = i;
                    while (s - 1 >= 0 && (NumberWords.ContainsKey(tokens[s - 1]) || tokens[s - 1] == "hundred")) s--;
                    if (s < i)
                    {
                        int current = 0;
                        for (int k = s; k < i; k++)
                        {
                            if (tokens[k] == "hundred") current = (current == 0 ? 1 : current) * 100;
                            else current += NumberWords[tokens[k]];
                        }
                        start = s;
                        value = current;
                    }
                }
                if (double.IsNaN(value)) continue;
                var rem = new List<string>();
                for (int k = 0; k < tokens.Length; k++) if (k < start || k > i) rem.Add(tokens[k]);
                remaining = rem.ToArray();
                return (float)(value * UnitMeters(tokens[i]));
            }
            return float.NaN;
        }

        private struct Match
        {
            public readonly IntentKind Kind;
            public readonly string Phrase;
            public Match(IntentKind kind, string phrase) { Kind = kind; Phrase = phrase; }
        }

        private static ParsedIntent Reject(ParseStatus status, string raw, string norm, string error)
        {
            return new ParsedIntent(status, IntentKind.Unknown, raw, norm, null, null, null, 0f, error);
        }

        // Fewer unexplained words -> higher score. Heuristic only; see ParsedIntent.ParseScore.
        private static float ComputeParseScore(string[] tokens)
        {
            int meaningful = 0;
            foreach (string t in tokens)
                if (!Fillers.Contains(t)) meaningful++;
            if (meaningful <= 2) return 1f;
            return Math.Max(0.6f, 1f - 0.1f * (meaningful - 2));
        }

        private static int IndexOfPhrase(string[] tokens, string phrase)
        {
            string[] p = phrase.Split(' ');
            for (int i = 0; i + p.Length <= tokens.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < p.Length; j++)
                    if (tokens[i + j] != p[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        private static bool IsDirection(string token)
        {
            foreach (string d in Directions) if (token == d) return true;
            return false;
        }

        private static string FindDirection(string[] tokens)
        {
            foreach (string token in tokens)
                if (IsDirection(token)) return token;
            return null;
        }

        // Joins non-filler, non-deictic words in [start, end). Returns null when nothing remains (meaning: use gaze).
        private static string JoinNameWords(string[] tokens, int start, int end)
        {
            var words = new List<string>();
            for (int i = Math.Max(0, start); i < end && i < tokens.Length; i++)
            {
                string t = tokens[i];
                if (Fillers.Contains(t) || Deictic.Contains(t) || IsDirection(t)) continue;
                words.Add(t);
            }
            return words.Count == 0 ? null : string.Join(" ", words.ToArray());
        }

        private static string ExtractWordsAfter(string[] tokens, string anchor)
        {
            int idx = IndexOfPhrase(tokens, anchor);
            if (idx < 0) return null;
            return JoinNameWords(tokens, idx + anchor.Split(' ').Length, tokens.Length);
        }

        // "move the red cube to the shelf" -> target "red cube", destination "shelf".
        private static void ExtractMoveNames(string[] tokens, string anchor, out string target, out string destination)
        {
            target = null;
            destination = null;
            int verb = IndexOfPhrase(tokens, anchor);
            int start = verb < 0 ? 0 : verb + anchor.Split(' ').Length;
            int prep = -1;
            for (int i = start; i < tokens.Length; i++)
                if (tokens[i] == "to" || tokens[i] == "onto" || tokens[i] == "on" || tokens[i] == "next")
                { prep = i; break; }
            if (prep < 0)
            {
                target = JoinNameWords(tokens, start, tokens.Length);
                return;
            }
            target = JoinNameWords(tokens, start, prep);
            destination = JoinNameWords(tokens, prep + 1, tokens.Length);
        }

        /// <summary>Short list shown to the user for HELP.</summary>
        public static readonly string[] SupportedPhrases =
        {
            "select that", "grab this", "move there", "move left", "move right", "move up", "move down",
            "move forward", "move back", "release", "cancel", "reset", "what is this", "what can I say",
            "move left thirty centimeters", "grab this object", "drop this object"
        };

        /// <summary>
        /// Exact phrases for a keyword-style recognizer (which can only return phrases it was given).
        /// Every entry must parse to an actionable intent; a standalone test enforces this.
        /// </summary>
        public static readonly string[] KeywordGrammar = BuildKeywordGrammar();

        private static string[] BuildKeywordGrammar()
        {
            var g = new List<string>
            {
                "select that", "select target", "grab this",
                "move there", "move to gaze", "move left", "move right",
                "move up", "move down", "move forward", "move back",
                "release", "drop it",
                "cancel", "reset", "focus", "what is this",
                "help", "what can I say",
                // Longer natural alternatives. In the session logs the recognizer delivered "drop it" 7 times and the one-word "release"
                // never, and one-word commands (cancel 2, reset 3) were rare: short single words are weak keyword-recognizer targets.
                // Each parses to the SAME intent as its short form (the standalone grammar test enforces it).
                "release it", "let go", "put it down",
                "cancel that", "never mind",
                "start over", "select this",
                // Grab and carry: the object follows you until you drop it.
                "grab this object", "carry this object", "pick up this object", "drop this object"
            };
            // Quantified moves. A keyword recognizer can only return phrases it was given, so the distances are an explicit list, spoken as
            // words (digits are unreliable for a keyword grammar). "move left" with no distance keeps the default step.
            string[] dirs = { "left", "right", "up", "down" };
            string[] amounts = { "ten centimeters", "twenty centimeters", "thirty centimeters", "forty centimeters", "fifty centimeters", "one meter" };
            foreach (string d in dirs)
                foreach (string a in amounts)
                {
                    g.Add("move " + d + " " + a);
                    g.Add("move this object " + a + " to the " + d);
                }
            return g.ToArray();
        }

        /// <summary>Canonical phrase sent by each numbered fallback key (1..7). Fallback is simulated input, not speech.</summary>
        public static readonly string[] FallbackKeyPhrases =
        {
            "select that", "move there", "release", "cancel", "reset", "what is this", "help"
        };
    }
}
