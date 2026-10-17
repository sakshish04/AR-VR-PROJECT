using System;
using System.Globalization;
using System.Text;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Outcome of one Fitts calculation. Raw and safeguarded movement time are kept separate so measured data
    /// is never silently altered. When <see cref="IsValid"/> is false every numeric field is NaN and
    /// <see cref="Error"/> says why; consumers must not treat such a result as a trial value.
    /// </summary>
    public readonly struct FittsResult
    {
        public readonly bool IsValid;
        public readonly string Error;
        /// <summary>Index of difficulty in bits.</summary>
        public readonly float IdBits;
        /// <summary>Measured movement time in seconds, exactly as supplied.</summary>
        public readonly float RawMovementTimeS;
        /// <summary>Movement time used for throughput: equals raw unless the minimum-MT safeguard applied.</summary>
        public readonly float UsedMovementTimeS;
        public readonly bool SafeguardApplied;
        /// <summary>Throughput in bits per second, computed from <see cref="UsedMovementTimeS"/>.</summary>
        public readonly float ThroughputBps;

        public FittsResult(bool isValid, string error, float idBits, float rawMt, float usedMt, bool safeguardApplied, float tp)
        {
            IsValid = isValid;
            Error = error;
            IdBits = idBits;
            RawMovementTimeS = rawMt;
            UsedMovementTimeS = usedMt;
            SafeguardApplied = safeguardApplied;
            ThroughputBps = tp;
        }

        public static FittsResult Invalid(string error)
        {
            return new FittsResult(false, error, float.NaN, float.NaN, float.NaN, false, float.NaN);
        }
    }

    /// <summary>
    /// Fitts' Law helpers, Shannon formulation (PROVISIONAL, PENDING CONFIRMATION by the professor):
    ///   ID = log2(D / W + 1)   [bits]       TP = ID / MT   [bits/s]
    /// Units: D and W must use the same distance unit (metres in this project); MT is in seconds.
    /// Invalid input is reported explicitly (never silently turned into NaN/Infinity/0).
    /// </summary>
    public static class FittsMath
    {
        /// <summary>
        /// Optional minimum movement time used ONLY for the throughput denominator, copied from the starter
        /// logger's Mathf.Max(0.1f, MT). It is a PENDING CONFIRMATION safeguard, not a measurement. Pass 0 to disable.
        /// </summary>
        public const float DefaultMinMovementTimeS = 0.1f;

        public static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }

        /// <summary>D must be finite and >= 0; W must be finite and > 0.</summary>
        public static bool TryIndexOfDifficulty(float distance, float width, out float idBits, out string error)
        {
            idBits = float.NaN;
            if (!IsFinite(distance)) { error = "distance is not a finite number"; return false; }
            if (!IsFinite(width)) { error = "width is not a finite number"; return false; }
            if (distance < 0f) { error = "distance must be >= 0"; return false; }
            if (width <= 0f) { error = "width must be > 0"; return false; }
            idBits = (float)(Math.Log(distance / (double)width + 1.0) / Math.Log(2.0));
            error = null;
            return true;
        }

        /// <param name="minMovementTimeS">Safeguard floor for the throughput denominator; 0 disables it.</param>
        public static FittsResult Compute(float distance, float width, float movementTimeS,
                                          float minMovementTimeS = DefaultMinMovementTimeS)
        {
            float id;
            string error;
            if (!TryIndexOfDifficulty(distance, width, out id, out error))
                return FittsResult.Invalid(error);
            if (!IsFinite(movementTimeS)) return FittsResult.Invalid("movement time is not a finite number");
            if (movementTimeS <= 0f) return FittsResult.Invalid("movement time must be > 0 s");
            if (!IsFinite(minMovementTimeS) || minMovementTimeS < 0f) return FittsResult.Invalid("minimum movement time must be finite and >= 0");

            bool applied = movementTimeS < minMovementTimeS;
            float used = applied ? minMovementTimeS : movementTimeS;
            return new FittsResult(true, null, id, movementTimeS, used, applied, id / used);
        }

        // ---- CSV helpers (invariant culture, RFC 4180 quoting) ----

        /// <summary>Fixed-decimal invariant formatting. A non-finite value becomes an EMPTY field (explicitly "no value").</summary>
        public static string F(float v, int decimals)
        {
            if (!IsFinite(v)) return string.Empty;
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            bool needsQuotes = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuotes) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        public static string CsvRow(params string[] fields)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(CsvEscape(fields[i]));
            }
            return sb.ToString();
        }
    }
}
