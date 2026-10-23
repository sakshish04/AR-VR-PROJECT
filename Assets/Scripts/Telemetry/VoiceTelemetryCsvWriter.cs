using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Group05.VoiceVR
{
    /// <summary>
    /// One row of the VOICE telemetry CSV (schema version 1, 15 columns, agreed in the Phase 2A specification).
    /// One row per RECOGNIZER event: a recognized phrase that was accepted, ignored (low confidence or rejected by the parser), or a
    /// recognizer error. Typed (F8) and keyboard-shortcut input never produces a row, and nothing is recorded for speech the recognizer
    /// did not return. The phrase is the grammar phrase the recognizer matched, never audio.
    ///
    /// This is a SEPARATE file from the gaze event log (<see cref="GazeEventRecord"/>) and the trial file (<see cref="TrialRecord"/>);
    /// neither of those schemas is touched. There is deliberately no data_origin or participant column. Pure C# (no Unity dependency).
    /// Missing or inapplicable values are EMPTY fields; "NaN", "Infinity" and invented zeroes are never written.
    /// </summary>
    public sealed class VoiceTelemetryRecord
    {
        public const int SchemaVersion = 1;
        public const int ColumnCount = 15;

        public const string CsvHeader =
            "schema_version,session_id,sequence_id,timestamp_utc,realtime_s,platform,recognized_phrase,confidence_rank," +
            "minimum_confidence_rank,intent,decision,reason,speech_end_to_event_ms,parse_duration_ms,action_outcome";

        // decision
        public const string DecisionAccepted = "accepted";
        public const string DecisionIgnored = "ignored";
        public const string DecisionError = "error";

        // reason (stable codes)
        public const string ReasonConfidencePass = "confidence_pass";
        /// <summary>Below the confidence floor but accepted because the command only undoes or reports (cancel, reset, release, help, focus).</summary>
        public const string ReasonLowConfidenceUndoAllowed = "low_confidence_undo_allowed";
        public const string ReasonLowConfidence = "low_confidence";
        public const string ReasonParseRejected = "parse_rejected";
        public const string ReasonRecognizerError = "recognizer_error";

        /// <summary>Assigned by the writer (1, 2, 3 ... within a session).</summary>
        public int SequenceId;
        /// <summary>Assigned by the writer.</summary>
        public string SessionId;
        /// <summary>UTC, <see cref="DateTimeKind.Utc"/>. Written with millisecond precision and a trailing Z.</summary>
        public DateTime TimestampUtc;
        /// <summary>Unity Time.realtimeSinceStartup at the recognizer callback, seconds. Finite and >= 0.</summary>
        public float RealtimeS;
        /// <summary>Application.platform name, e.g. WindowsEditor or WindowsPlayer.</summary>
        public string Platform;
        /// <summary>Grammar phrase returned by the recognizer; empty for an error row.</summary>
        public string RecognizedPhrase;
        /// <summary>Recognizer confidence name (High / Medium / Low / Rejected); empty when none is available.</summary>
        public string ConfidenceRank;
        /// <summary>The configured floor name (Low / Medium / High); empty when unavailable.</summary>
        public string MinimumConfidenceRank;
        /// <summary>Parsed intent (Select, Move, ...); empty when the parser did not produce an actionable intent.</summary>
        public string Intent;
        public string Decision;
        public string Reason;
        /// <summary>Recognizer-reported end of speech to callback. NaN (written empty) when unavailable. Not validated end-to-end latency (D-11).</summary>
        public float SpeechEndToEventMs = float.NaN;
        public float ParseDurationMs = float.NaN;
        /// <summary>Outcome observed synchronously when the row is written (executed, cancelled, or a rejection reason); empty if none was observed.</summary>
        public string ActionOutcome;

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrEmpty(SessionId) || SessionCsvFile.SanitizeToken(SessionId) != SessionId)
                errors.Add("session_id must be non-empty and contain only letters, digits, '_' or '-'");
            if (SequenceId < 1) errors.Add("sequence_id must be >= 1");
            if (TimestampUtc.Kind != DateTimeKind.Utc) errors.Add("timestamp_utc must be a UTC DateTime");
            if (!IsFinite(RealtimeS) || RealtimeS < 0f) errors.Add("realtime_s must be finite and >= 0");
            if (string.IsNullOrEmpty(Platform)) errors.Add("platform is required");
            if (Decision != DecisionAccepted && Decision != DecisionIgnored && Decision != DecisionError)
                errors.Add("decision must be 'accepted', 'ignored' or 'error'");
            if (string.IsNullOrEmpty(Reason)) errors.Add("reason is required");
            if (Decision != DecisionError && string.IsNullOrEmpty(RecognizedPhrase))
                errors.Add("recognized_phrase is required unless decision is 'error'");
            if (!IsMissingOrNonNegative(SpeechEndToEventMs)) errors.Add("speech_end_to_event_ms must be missing or finite and >= 0");
            if (!IsMissingOrNonNegative(ParseDurationMs)) errors.Add("parse_duration_ms must be missing or finite and >= 0");
            return errors;
        }

        public string ToCsvRow()
        {
            return FittsMath.CsvRow(
                SchemaVersion.ToString(CultureInfo.InvariantCulture),
                SessionId,
                SequenceId.ToString(CultureInfo.InvariantCulture),
                TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                RealtimeS.ToString("F3", CultureInfo.InvariantCulture),
                Platform,
                RecognizedPhrase,
                ConfidenceRank,
                MinimumConfidenceRank,
                Intent,
                Decision,
                Reason,
                Number(SpeechEndToEventMs),
                Number(ParseDurationMs),
                ActionOutcome);
        }

        /// <summary>Up to three decimals, invariant culture, trailing zeros trimmed; an unavailable value becomes an empty field.</summary>
        public static string Number(float v)
        {
            if (!IsFinite(v)) return string.Empty;
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool IsMissingOrNonNegative(float v) { return float.IsNaN(v) || (IsFinite(v) && v >= 0f); }

        /// <summary>
        /// Turns the recognizer-side facts into the decision and reason columns. Only known facts are used:
        ///   IgnoreWithNotice                         -> ignored / low_confidence
        ///   Accept, but the parser found no action    -> ignored / parse_rejected
        ///   Accept below the floor (undo/info only)   -> accepted / low_confidence_undo_allowed
        ///   Accept at or above the floor              -> accepted / confidence_pass
        /// Ranks follow UnityEngine.Windows.Speech.ConfidenceLevel: 0 High, 1 Medium, 2 Low, 3 Rejected (larger = less confident).
        /// </summary>
        public static void Classify(int confidenceRank, int floorRank, ParsedIntent parsed, ConfidenceDecision decision,
                                    out string decisionText, out string reason)
        {
            if (decision == ConfidenceDecision.IgnoreWithNotice)
            {
                decisionText = DecisionIgnored;
                reason = ReasonLowConfidence;
                return;
            }
            if (!parsed.IsActionable)
            {
                decisionText = DecisionIgnored;
                reason = ReasonParseRejected;
                return;
            }
            decisionText = DecisionAccepted;
            reason = confidenceRank > floorRank ? ReasonLowConfidenceUndoAllowed : ReasonConfidencePass;
        }

        /// <summary>The intent column: the parsed intent when it is actionable, otherwise empty (Unknown is "no intent", not a value).</summary>
        public static string IntentText(ParsedIntent parsed)
        {
            return parsed.IsActionable ? parsed.Kind.ToString() : string.Empty;
        }

        /// <summary>The action_outcome column: "executed", "cancelled", the rejection reason (e.g. "no_focus"), or "not_implemented".</summary>
        public static string OutcomeCode(CommandOutcome outcome, string reason)
        {
            switch (outcome)
            {
                case CommandOutcome.Executed: return "executed";
                case CommandOutcome.Cancelled: return "cancelled";
                case CommandOutcome.NotImplemented: return "not_implemented";
                default: return string.IsNullOrEmpty(reason) ? "rejected" : reason;
            }
        }
    }

    /// <summary>
    /// Append-only, never-overwrite writer for the voice telemetry CSV of ONE logging session. The file
    /// <c>voice_telemetry_&lt;sessionId&gt;.csv</c> is created by the FIRST successful write (CreateNew; "_2", "_3"... when the name is taken)
    /// together with the header; every later row is appended and the file is closed again, so nothing sits in a buffer that a crash or a
    /// Play Mode stop could lose. The writer owns session_id and sequence_id. Never throws: failures return false and set
    /// <see cref="LastError"/>. Pure C# (no Unity dependency).
    /// </summary>
    public sealed class VoiceTelemetryCsvWriter
    {
        private readonly string folder;
        private readonly string baseName;
        private string filePath;

        public string SessionId { get; private set; }
        public string LastError { get; private set; }
        /// <summary>Rows successfully written; the next row gets sequence_id RowsWritten + 1.</summary>
        public int RowsWritten { get; private set; }
        /// <summary>Full path once the first row is written; before that, the intended path (display only).</summary>
        public string FilePath { get { return filePath; } }

        public VoiceTelemetryCsvWriter(string folder, string sessionId)
        {
            this.folder = folder;
            SessionId = SessionCsvFile.SanitizeToken(sessionId);
            baseName = "voice_telemetry_" + SessionId;
            filePath = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, baseName + ".csv");
        }

        /// <summary>yyyyMMddTHHmmssZ plus a short unique suffix, so two sessions started in the same second still get different ids.</summary>
        public static string NewSessionId(DateTime utcNow, string uniqueSuffix)
        {
            string id = SessionCsvFile.NewSessionId(utcNow);
            string s = SessionCsvFile.SanitizeToken(uniqueSuffix);
            return string.IsNullOrEmpty(uniqueSuffix) ? id : id + "-" + s;
        }

        /// <summary>Assigns session_id and sequence_id, validates, and writes. Returns false (and sets LastError) instead of throwing.</summary>
        public bool TryWrite(VoiceTelemetryRecord record)
        {
            LastError = null;
            try
            {
                if (record == null) { LastError = "record is null"; return false; }
                if (string.IsNullOrEmpty(folder)) { LastError = "output folder is not set"; return false; }

                record.SessionId = SessionId;
                record.SequenceId = RowsWritten + 1;
                List<string> errors = record.Validate();
                if (errors.Count > 0) { LastError = "invalid record: " + string.Join("; ", errors.ToArray()); return false; }
                string row = record.ToCsvRow();

                string fullFolder = Path.GetFullPath(folder);
                Directory.CreateDirectory(fullFolder);

                if (RowsWritten == 0)
                {
                    for (int n = 1; n <= 99; n++)
                    {
                        string candidate = Path.GetFullPath(Path.Combine(fullFolder, baseName + (n == 1 ? "" : "_" + n) + ".csv"));
                        if (!candidate.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase)) { LastError = "path escaped output folder"; return false; }
                        try
                        {
                            using (var fs = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                            {
                                w.WriteLine(VoiceTelemetryRecord.CsvHeader);
                                w.WriteLine(row);
                            }
                            filePath = candidate;
                            RowsWritten = 1;
                            return true;
                        }
                        catch (IOException) when (File.Exists(candidate)) { /* name taken: try the next suffix */ }
                    }
                    LastError = "could not find a free file name";
                    return false;
                }

                using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                    w.WriteLine(row);
                RowsWritten++;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
