using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Group05.VoiceVR
{
    /// <summary>Provenance of a record. Only <see cref="Real"/> may ever be reported as participant data.</summary>
    public enum DataOrigin
    {
        /// <summary>Genuine participant session with consent. Requires a pseudonymous participant id (P01, P02...).</summary>
        Real,
        /// <summary>Developer self-test or demo run. Never experimental evidence.</summary>
        Selftest,
        /// <summary>Synthetic/generated values. Never experimental evidence.</summary>
        Simulated
    }

    public enum TrialOutcome { Success, Failure, Cancelled }

    /// <summary>
    /// One per-trial row of the PROPOSED canonical schema (PENDING CONFIRMATION; see docs/PHASE2_IMPLEMENTATION_REPORT.md).
    /// Frame-level samples stay in the separate file written by the starter logger and are not part of this record.
    /// Pure C# so the schema can be tested without Unity.
    /// </summary>
    public class TrialRecord
    {
        public const string SchemaVersion = "0.1-proposed";

        public const string ConditionController = "controller";
        public const string ConditionGazeVoice = "gaze_voice";

        public const string CsvHeader =
            "schema_version,data_origin,session_id,participant_pseudonym,condition,trial_index,trial_id," +
            "start_utc,end_utc,target_name,action,outcome,failure_reason,error_count,input_modality," +
            "target_distance_m,target_width_m,fitts_id_bits,movement_time_raw_s,movement_time_used_s," +
            "mt_safeguard_applied,throughput_bps,recognition_latency_ms,command_text,fitts_note";

        public const int ColumnCount = 25;
        public const int MaxCommandTextLength = 80;

        private static readonly Regex RealPseudonym = new Regex("^P[0-9]{2,3}$", RegexOptions.CultureInvariant);

        public DataOrigin Origin;
        public string SessionId;
        /// <summary>Pseudonym only: P01.. for Real data, SELFTEST or SIM otherwise. Never a name, roll number or email.</summary>
        public string ParticipantPseudonym;
        /// <summary>"controller" or "gaze_voice" (the two core working conditions; study protocol PENDING CONFIRMATION).</summary>
        public string Condition;
        public int TrialIndex;
        public DateTime StartUtc;
        public DateTime EndUtc;
        public string TargetName;
        public string Action;
        public TrialOutcome Outcome;
        public string FailureReason;
        public int ErrorCount;
        public InputModality Modality;
        /// <summary>Metres.</summary>
        public float DistanceM;
        /// <summary>Metres.</summary>
        public float WidthM;
        /// <summary>Measured movement time in seconds, unmodified.</summary>
        public float MovementTimeS;
        /// <summary>Speech latency in ms; must stay NaN unless Modality is Voice and it was actually measured.</summary>
        public float RecognitionLatencyMs = float.NaN;
        public string CommandText;
        /// <summary>Minimum-MT safeguard applied to throughput only (0 disables). PENDING CONFIRMATION.</summary>
        public float MinMovementTimeSafeguardS = FittsMath.DefaultMinMovementTimeS;

        public string TrialId { get { return SessionId + "-T" + TrialIndex.ToString("D3", CultureInfo.InvariantCulture); } }

        public FittsResult Fitts { get { return FittsMath.Compute(DistanceM, WidthM, MovementTimeS, MinMovementTimeSafeguardS); } }

        /// <summary>Returns every schema violation; an empty list means the record may be written.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrEmpty(SessionId) || TrialCsvWriter.SanitizeToken(SessionId) != SessionId)
                errors.Add("session_id must be non-empty and contain only letters, digits, '_' or '-'");
            if (TrialIndex < 1) errors.Add("trial_index must be >= 1");
            if (Condition != ConditionController && Condition != ConditionGazeVoice)
                errors.Add("condition must be 'controller' or 'gaze_voice'");

            if (Origin == DataOrigin.Real)
            {
                if (string.IsNullOrEmpty(ParticipantPseudonym) || !RealPseudonym.IsMatch(ParticipantPseudonym))
                    errors.Add("Real data needs a pseudonym like P01 (no names, roll numbers or emails)");
            }
            else if (Origin == DataOrigin.Selftest)
            {
                if (ParticipantPseudonym != "SELFTEST") errors.Add("Selftest data must use participant 'SELFTEST'");
            }
            else if (ParticipantPseudonym != "SIM")
            {
                errors.Add("Simulated data must use participant 'SIM'");
            }

            if (EndUtc < StartUtc) errors.Add("end_utc is before start_utc");
            if (ErrorCount < 0) errors.Add("error_count must be >= 0");
            if (Outcome != TrialOutcome.Success && string.IsNullOrEmpty(FailureReason))
                errors.Add("a failed or cancelled trial needs a failure_reason");
            if (Modality != InputModality.Voice && !float.IsNaN(RecognitionLatencyMs))
                errors.Add("recognition_latency_ms is only valid for Voice input");
            if (!float.IsNaN(RecognitionLatencyMs) && (RecognitionLatencyMs < 0f || float.IsInfinity(RecognitionLatencyMs)))
                errors.Add("recognition_latency_ms must be a finite value >= 0");
            return errors;
        }

        public string ToCsvRow()
        {
            FittsResult f = Fitts;
            bool tpValid = f.IsValid && Outcome == TrialOutcome.Success;
            return FittsMath.CsvRow(
                SchemaVersion,
                Origin.ToString().ToLowerInvariant(),
                SessionId,
                ParticipantPseudonym,
                Condition,
                TrialIndex.ToString(CultureInfo.InvariantCulture),
                TrialId,
                StartUtc.ToString("o", CultureInfo.InvariantCulture),
                EndUtc.ToString("o", CultureInfo.InvariantCulture),
                TargetName,
                Action,
                Outcome.ToString().ToLowerInvariant(),
                FailureReason,
                ErrorCount.ToString(CultureInfo.InvariantCulture),
                Modality.ToString(),
                FittsMath.F(DistanceM, 3),
                FittsMath.F(WidthM, 3),
                f.IsValid ? FittsMath.F(f.IdBits, 3) : string.Empty,
                FittsMath.F(MovementTimeS, 3),
                f.IsValid ? FittsMath.F(f.UsedMovementTimeS, 3) : string.Empty,
                f.IsValid ? (f.SafeguardApplied ? "1" : "0") : string.Empty,
                tpValid ? FittsMath.F(f.ThroughputBps, 3) : string.Empty,
                FittsMath.F(RecognitionLatencyMs, 1),
                CleanCommandText(CommandText),
                f.IsValid ? string.Empty : f.Error);
        }

        private static string CleanCommandText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string t = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return t.Length <= MaxCommandTextLength ? t : t.Substring(0, MaxCommandTextLength);
        }
    }
}
