using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
        Help
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

        public bool IsActionable { get { return Status == ParseStatus.Ok && Kind != IntentKind.Unknown; } }

        public ParsedIntent(ParseStatus status, IntentKind kind, string raw, string normalized, string target,
                            string destination, string direction, float parseScore, string error)
        {
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
            "i", "want", "to", "like", "need", "let", "me", "lets", "the", "a", "an", "it", "that", "this", "those", "these", "one"
        };

        // Deictic words that mean "use the gaze" rather than naming an object.
        private static readonly HashSet<string> Deictic = new HashSet<string> { "that", "this", "it", "there", "here", "those", "these", "one", "over" };

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
            // Longest matching phrase of that kind anchors name extraction.
            string anchor = kept[0].Phrase;
            foreach (Match m in kept) if (m.Phrase.Length > anchor.Length) anchor = m.Phrase;

            string direction = null, target = null, destination = null;
            if (kind == IntentKind.Move)
            {
                direction = FindDirection(tokens);
                ExtractMoveNames(tokens, anchor, out target, out destination);
            }
            else if (kind == IntentKind.Select || kind == IntentKind.Focus)
            {
                target = ExtractWordsAfter(tokens, anchor);
            }

            return new ParsedIntent(ParseStatus.Ok, kind, text, norm, target, destination, direction, ComputeParseScore(tokens), null);
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
            "select that", "grab this", "move there", "move left", "move right", "release", "cancel",
            "reset", "what is this", "what can I say"
        };

        /// <summary>
        /// Exact phrases for a keyword-style recognizer (which can only return phrases it was given).
        /// Every entry must parse to an actionable intent; a standalone test enforces this.
        /// </summary>
        public static readonly string[] KeywordGrammar =
        {
            "select that", "select target", "grab this",
            "move there", "move to gaze", "move left", "move right",
            "release", "drop it",
            "cancel", "reset", "focus", "what is this",
            "help", "what can I say"
        };

        /// <summary>Canonical phrase sent by each numbered fallback key (1..7). Fallback is simulated input, not speech.</summary>
        public static readonly string[] FallbackKeyPhrases =
        {
            "select that", "move there", "release", "cancel", "reset", "what is this", "help"
        };
    }
}
