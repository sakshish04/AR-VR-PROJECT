using System;
using System.Collections.Generic;
using System.Globalization;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Small deterministic random generator (xorshift32). The same seed gives the same sequence on every platform and runtime, unlike System.Random,
    /// so a recorded seed reproduces a trial order exactly. Pure C#.
    /// </summary>
    public sealed class SeededRandom
    {
        private uint state;

        public SeededRandom(uint seed) { state = seed == 0 ? 0x9E3779B9u : seed; }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)(NextUInt() % (uint)maxExclusive);
        }
    }

    /// <summary>One planned trial: which distance and width, in which block, and where the target goes (lateral offset from the session anchor, metres).</summary>
    public struct TrialSpec
    {
        /// <summary>1-based within its file (practice and measured trials are numbered separately).</summary>
        public int Index;
        public bool Practice;
        /// <summary>0 for practice; 1..repetitions for measured blocks.</summary>
        public int Block;
        public float DistanceM;
        public float WidthM;
        /// <summary>Lateral position of this trial's target in metres (the previous target was exactly <see cref="DistanceM"/> away).</summary>
        public float TargetX;
        /// <summary>Lateral position of the previous target (the home marker for the first trial), where the participant is already looking.</summary>
        public float PreviousX;
    }

    /// <summary>
    /// Builds the trial list described in the Phase 04 protocol (section 3.1, PROPOSED, UNRESOLVED until the pilot): every distance x width combination once
    /// as practice, then <c>repetitions</c> measured blocks, each block containing every combination once in a random order. Pure C#, fully determined by the seed.
    /// Each target is placed exactly one distance D from the previous one along a horizontal line, so the amplitude of every selection equals the trial's D.
    /// </summary>
    public static class FittsTrialPlanner
    {
        public static readonly float[] DefaultDistancesM = { 0.5f, 1.0f, 2.0f };
        public static readonly float[] DefaultWidthsM = { 0.30f, 0.15f, 0.08f };
        public const int DefaultRepetitions = 3;

        public static List<TrialSpec> Build(uint seed, float[] distancesM, float[] widthsM, int repetitions, bool includePractice)
        {
            if (distancesM == null || widthsM == null || distancesM.Length == 0 || widthsM.Length == 0) throw new ArgumentException("distances and widths are required");
            if (repetitions < 1) throw new ArgumentException("repetitions must be >= 1");
            foreach (float d in distancesM) if (!(d > 0f) || float.IsInfinity(d)) throw new ArgumentException("distances must be positive and finite");
            foreach (float w in widthsM) if (!(w > 0f) || float.IsInfinity(w)) throw new ArgumentException("widths must be positive and finite");

            var rng = new SeededRandom(seed);
            var combos = new List<float[]>();
            foreach (float d in distancesM) foreach (float w in widthsM) combos.Add(new[] { d, w });

            float limit = 0f;
            foreach (float d in distancesM) limit = Math.Max(limit, d);

            var all = new List<TrialSpec>();
            if (includePractice) AddBlock(all, combos, rng, true, 0, limit);
            for (int b = 1; b <= repetitions; b++) AddBlock(all, combos, rng, false, b, limit);
            return all;
        }

        private static void AddBlock(List<TrialSpec> all, List<float[]> combos, SeededRandom rng, bool practice, int block, float limit)
        {
            var order = new List<float[]>(combos);
            for (int i = order.Count - 1; i > 0; i--)   // Fisher-Yates
            {
                int j = rng.Next(i + 1);
                float[] t = order[i]; order[i] = order[j]; order[j] = t;
            }
            // Positions are chained across the whole file; the chain restarts at the home marker (x = 0) for the first trial of each file.
            float x = 0f;
            int dir = 0;
            for (int k = all.Count - 1; k >= 0; k--)
                if (all[k].Practice == practice) { x = all[k].TargetX; dir = Math.Sign(all[k].TargetX - all[k].PreviousX); break; }
            int index = 0;
            for (int k = 0; k < all.Count; k++) if (all[k].Practice == practice) index++;

            foreach (float[] c in order)
            {
                float d = c[0], w = c[1];
                int want = dir == 0 ? (rng.Next(2) == 0 ? 1 : -1) : -dir;     // alternate sides
                float nx = x + want * d;
                if (Math.Abs(nx) > limit + 1e-4f)                               // stay inside the playing field: take the other side
                {
                    want = -want;
                    nx = x + want * d;
                }
                if (Math.Abs(nx) > limit + 1e-4f) { want = Math.Sign(-x); if (want == 0) want = 1; nx = x + want * d; }
                index++;
                all.Add(new TrialSpec { Index = index, Practice = practice, Block = block, DistanceM = d, WidthM = w, PreviousX = x, TargetX = nx });
                x = nx;
                dir = want;
            }
        }

        /// <summary>Whole-number seed from a time value, so an unseeded session still records a seed that reproduces it.</summary>
        public static uint SeedFromTicks(long ticks)
        {
            unchecked { return (uint)(ticks ^ (ticks >> 32)); }
        }

        public static string Describe(TrialSpec s)
        {
            return (s.Practice ? "practice " : "trial ") + s.Index.ToString(CultureInfo.InvariantCulture)
                   + " D=" + s.DistanceM.ToString("0.##", CultureInfo.InvariantCulture) + " m, W=" + s.WidthM.ToString("0.##", CultureInfo.InvariantCulture) + " m";
        }
    }
}
