using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Group05.VoiceVR
{
    public enum TrialPhase { NotStarted, Running, Done }

    /// <summary>What ended a trial. Movement time is measured from the moment the target appeared to the moment of the accepted selection (or the time limit).</summary>
    public struct TrialResult
    {
        public TrialSpec Spec;
        public TrialOutcome Outcome;
        /// <summary>Empty on success; otherwise one of wrong_target, timeout, abandoned (or the reason given to Abandon).</summary>
        public string FailureReason;
        /// <summary>Selection attempts that hit no target or were rejected, plus a wrong-target selection if that ended the trial.</summary>
        public int ErrorCount;
        public float MovementTimeS;
        /// <summary>Name of the target that was selected; empty for timeout and abandoned.</summary>
        public string SelectedName;
    }

    /// <summary>
    /// The state machine of ONE trial. Pure C#, no Unity types, so it is unit tested.
    /// Rules (PROPOSED in the Phase 04 protocol; what counts as an error and the time limit are UNRESOLVED until the pilot):
    ///  * a selection of the GOAL target ends the trial as a success;
    ///  * a selection of any OTHER target ends it as a failure "wrong_target" (and counts as one error);
    ///  * a selection attempt that hits no target (or is rejected, for example no focus) is NOT the end: it adds one error and the trial continues;
    ///  * no accepted selection within the time limit ends it as a failure "timeout".
    /// </summary>
    public sealed class TrialEngine
    {
        public TrialSpec Spec { get; private set; }
        public TrialPhase Phase { get; private set; }
        public int ErrorCount { get; private set; }
        public float StartedAt { get; private set; }
        public float TimeLimitS { get; private set; }

        public void Start(TrialSpec spec, float now, float timeLimitS)
        {
            Spec = spec;
            Phase = TrialPhase.Running;
            ErrorCount = 0;
            StartedAt = now;
            TimeLimitS = timeLimitS;
        }

        public float Elapsed(float now) { return Phase == TrialPhase.Running ? Math.Max(0f, now - StartedAt) : 0f; }

        /// <summary>A selection attempt. <paramref name="hitTarget"/> false = nothing was hit or the attempt was rejected. Returns the result when the trial ends, else null.</summary>
        public TrialResult? Select(bool hitTarget, bool onGoal, string targetName, float now)
        {
            if (Phase != TrialPhase.Running) return null;
            if (!hitTarget) { ErrorCount++; return null; }
            float mt = Math.Max(0f, now - StartedAt);
            if (onGoal) return Finish(TrialOutcome.Success, "", mt, targetName);
            ErrorCount++;
            return Finish(TrialOutcome.Failure, "wrong_target", mt, targetName);
        }

        public TrialResult? Tick(float now)
        {
            if (Phase != TrialPhase.Running) return null;
            if (now - StartedAt >= TimeLimitS) return Finish(TrialOutcome.Failure, "timeout", TimeLimitS, "");
            return null;
        }

        public TrialResult? Abandon(float now, string reason)
        {
            if (Phase != TrialPhase.Running) return null;
            return Finish(TrialOutcome.Cancelled, string.IsNullOrEmpty(reason) ? "abandoned" : reason, Math.Max(0f, now - StartedAt), "");
        }

        private TrialResult Finish(TrialOutcome outcome, string reason, float mt, string name)
        {
            Phase = TrialPhase.Done;
            return new TrialResult { Spec = Spec, Outcome = outcome, FailureReason = reason, ErrorCount = ErrorCount, MovementTimeS = mt, SelectedName = name ?? "" };
        }
    }

    /// <summary>Turns a finished trial into a <see cref="TrialRecord"/> (the existing, tested 25-column schema). The writer validates it before anything is written.</summary>
    public static class TrialRecordFactory
    {
        public static TrialRecord Create(TrialResult r, string sessionId, DataOrigin origin, string pseudonym, string condition,
                                         InputModality modality, DateTime startUtc, DateTime endUtc, string commandText, float recognitionLatencyMs)
        {
            return new TrialRecord
            {
                Origin = origin,
                SessionId = sessionId,
                ParticipantPseudonym = pseudonym,
                Condition = condition,
                TrialIndex = r.Spec.Index,
                StartUtc = startUtc,
                EndUtc = endUtc < startUtc ? startUtc : endUtc,
                TargetName = r.SelectedName != null && r.SelectedName.Length > 0 ? r.SelectedName : "goal",
                Action = "select",
                Outcome = r.Outcome,
                FailureReason = r.FailureReason ?? "",
                ErrorCount = r.ErrorCount,
                Modality = modality,
                DistanceM = r.Spec.DistanceM,
                WidthM = r.Spec.WidthM,
                MovementTimeS = r.MovementTimeS,
                RecognitionLatencyMs = modality == InputModality.Voice ? recognitionLatencyMs : float.NaN,
                CommandText = commandText ?? ""
            };
        }

        /// <summary>The pseudonym each data origin must carry (Real needs a real P01-style code supplied by the experimenter).</summary>
        public static string PseudonymFor(DataOrigin origin, string realPseudonym)
        {
            switch (origin)
            {
                case DataOrigin.Real: return realPseudonym ?? "";
                case DataOrigin.Selftest: return "SELFTEST";
                default: return "SIM";
            }
        }
    }

    /// <summary>The small text sidecar written next to a session's CSV files: what produced the numbers (formula, protocol status, route, seed, parameters).</summary>
    public static class TrialSessionInfo
    {
        public const string FormulaLabel = "shannon: ID = log2(D/W + 1); TP = ID / MT";

        public static string Build(string sessionId, DataOrigin origin, string pseudonym, string condition, uint seed, float timeLimitS, int repetitions,
                                   float[] distancesM, float[] widthsM, string recognizerRoute, string protocolStatus, DateTime startedUtc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("session_id: " + sessionId);
            sb.AppendLine("data_origin: " + origin.ToString().ToLowerInvariant());
            sb.AppendLine("participant: " + pseudonym);
            sb.AppendLine("condition: " + condition);
            sb.AppendLine("started_utc: " + startedUtc.ToString("o", CultureInfo.InvariantCulture));
            sb.AppendLine("seed: " + seed.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("fitts_formula: " + FormulaLabel + " (WORKING DECISION 2026-10-25; the dossier states log2(2D/W): professor confirmation pending, decision D-02)");
            sb.AppendLine("distances_m: " + Join(distancesM));
            sb.AppendLine("widths_m: " + Join(widthsM));
            sb.AppendLine("repetitions_per_combination: " + repetitions.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("time_limit_s: " + timeLimitS.ToString("0.###", CultureInfo.InvariantCulture) + " (PROVISIONAL, to be set from the pilot)");
            sb.AppendLine("recognizer_route: " + recognizerRoute);
            sb.AppendLine("protocol_status: " + protocolStatus);
            sb.AppendLine("files: practice trials are in the file whose session id ends with -practice and are never analysed");
            return sb.ToString();
        }

        private static string Join(float[] v)
        {
            var parts = new List<string>();
            foreach (float f in v) parts.Add(f.ToString("0.###", CultureInfo.InvariantCulture));
            return string.Join(", ", parts.ToArray());
        }
    }
}
