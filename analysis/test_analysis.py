"""Tests for analysis/wer.py, analysis/stats.py and analysis/analyze_trials.py.

Run from the repository root:   python -m unittest discover -s analysis -v
(or:  python analysis/test_analysis.py)

ALL numbers here are throw-away TEST FIXTURES created in memory or in a temp directory. They are not data, they are
not stored in the repository, and nothing computed from them is a finding.
scipy is used ONLY to cross-check the pure-Python implementations; those tests are skipped if scipy is not installed.
"""
import csv
import math
import os
import random
import re
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import analyze_trials as at  # noqa: E402
import stats  # noqa: E402
import wer  # noqa: E402

try:
    import scipy.stats as sps
except Exception:  # pragma: no cover
    sps = None


class WerTests(unittest.TestCase):
    def test_identical_is_zero(self):
        r = wer.word_errors("select that", "Select THAT!")
        self.assertEqual((r.substitutions, r.deletions, r.insertions, r.reference_words), (0, 0, 0, 2))
        self.assertEqual(r.wer, 0.0)

    def test_deletion(self):
        r = wer.word_errors("the cat sat", "the cat")
        self.assertEqual((r.substitutions, r.deletions, r.insertions), (0, 1, 0))
        self.assertAlmostEqual(r.wer, 1 / 3)

    def test_insertion(self):
        r = wer.word_errors("move there", "please move there")
        self.assertEqual((r.substitutions, r.deletions, r.insertions), (0, 0, 1))
        self.assertAlmostEqual(r.wer, 0.5)

    def test_substitution(self):
        r = wer.word_errors("grab this", "grab that")
        self.assertEqual((r.substitutions, r.deletions, r.insertions), (1, 0, 0))

    def test_mixed_counts_sum_to_edit_distance(self):
        r = wer.word_errors("one two three four", "one three five four six")
        self.assertEqual(r.errors, 3)  # sub two->three? alignment minimises total edits
        self.assertEqual(r.reference_words, 4)

    def test_empty_reference_is_undefined_not_zero(self):
        r = wer.word_errors("", "anything")
        self.assertEqual(r.insertions, 1)
        with self.assertRaises(ValueError):
            _ = r.wer

    def test_empty_hypothesis_is_all_deletions(self):
        r = wer.word_errors("select that", "")
        self.assertEqual((r.deletions, r.reference_words), (2, 2))
        self.assertEqual(r.wer, 1.0)

    def test_none_inputs(self):
        self.assertEqual(wer.word_errors(None, None).errors, 0)

    def test_apostrophes_and_punctuation_match_csharp_normalizer(self):
        self.assertEqual(wer.normalize("What's that?"), ["whats", "that"])
        self.assertEqual(wer.normalize("  a,b;;c  "), ["a", "b", "c"])

    def test_corpus_wer_pools_counts(self):
        r = wer.corpus_wer([("a b", "a b"), ("c d e f", "c")])
        self.assertEqual((r.errors, r.reference_words), (3, 6))
        self.assertAlmostEqual(r.wer, 0.5)


class StatsEdgeTests(unittest.TestCase):
    def test_unequal_length(self):
        with self.assertRaises(stats.StatsError):
            stats.paired_t_test([1, 2, 3], [1, 2])

    def test_too_few(self):
        with self.assertRaises(stats.StatsError):
            stats.paired_t_test([1], [2])

    def test_non_finite_and_non_numeric(self):
        for bad in (float("nan"), float("inf"), "x", None, True):
            with self.assertRaises(stats.StatsError):
                stats.paired_t_test([1, 2, bad], [1, 2, 3])

    def test_zero_variance_differences(self):
        with self.assertRaises(stats.StatsError):
            stats.paired_t_test([2, 3, 4], [1, 2, 3])  # every difference is exactly 1
        with self.assertRaises(stats.StatsError):
            stats.cohens_d([2, 3, 4], [1, 2, 3])

    def test_all_zero_differences_wilcoxon(self):
        with self.assertRaises(stats.StatsError):
            stats.wilcoxon_signed_rank([1, 2, 3], [1, 2, 3])

    def test_known_small_example(self):
        # hand-computed: diffs = [1,-1,2,3] -> mean 1.25, sd 1.7078..., t = 1.4640...
        a, b = [3, 1, 5, 6], [2, 2, 3, 3]
        t = stats.paired_t_test(a, b)
        self.assertEqual((t.n, t.df), (4, 3))
        self.assertAlmostEqual(t.mean_difference, 1.25)
        self.assertAlmostEqual(t.t, 1.25 / (math.sqrt(2.9166666666666665) / 2), places=9)

    def test_wilcoxon_zero_differences_dropped(self):
        w = stats.wilcoxon_signed_rank([1, 2, 3, 4, 9], [1, 2, 3, 6, 5])
        self.assertEqual(w.n_zero_dropped, 3)
        self.assertEqual(w.n_effective, 2)

    def test_exact_vs_tie_method(self):
        self.assertEqual(stats.wilcoxon_signed_rank([1, 2, 4, 8], [0, 0, 0, 0]).method, "exact")
        self.assertEqual(stats.wilcoxon_signed_rank([1, 1, 2, 3], [0, 0, 0, 0]).method, "normal-approximation")


@unittest.skipIf(sps is None, "scipy not installed: cross-check skipped")
class StatsCrossCheckAgainstScipy(unittest.TestCase):
    def _samples(self, seed, n):
        rng = random.Random(seed)
        a = [rng.gauss(2.0, 0.8) for _ in range(n)]
        b = [x - rng.gauss(0.4, 0.5) for x in a]
        return a, b

    def test_paired_t_matches_scipy(self):
        for seed, n in ((1, 6), (2, 10), (3, 17), (4, 30), (5, 120)):
            a, b = self._samples(seed, n)
            mine = stats.paired_t_test(a, b)
            ref = sps.ttest_rel(a, b)
            self.assertAlmostEqual(mine.t, float(ref.statistic), places=9, msg=f"seed {seed}")
            self.assertAlmostEqual(mine.p_two_sided, float(ref.pvalue), places=9, msg=f"seed {seed}")
            self.assertEqual(mine.df, n - 1)

    def test_extreme_t_p_value(self):
        a = [10.0 + 0.01 * i for i in range(12)]
        b = [x - 5.0 - 0.001 * (i % 3) for i, x in enumerate(a)]
        mine, ref = stats.paired_t_test(a, b), sps.ttest_rel(a, b)
        self.assertAlmostEqual(math.log10(mine.p_two_sided), math.log10(float(ref.pvalue)), places=6)

    def test_cohens_d_formulas(self):
        a, b = self._samples(7, 15)
        d = stats.cohens_d(a, b)
        diffs = [x - y for x, y in zip(a, b)]
        self.assertAlmostEqual(d.d_z, sum(diffs) / len(diffs) / (sum((x - sum(diffs) / len(diffs)) ** 2 for x in diffs) / (len(diffs) - 1)) ** 0.5, places=12)
        sa, sb = sps.tstd(a), sps.tstd(b)
        self.assertAlmostEqual(d.d_av, (sum(diffs) / len(diffs)) / math.sqrt((sa ** 2 + sb ** 2) / 2), places=9)

    def test_wilcoxon_exact_matches_scipy(self):
        for seed, n in ((11, 5), (12, 8), (13, 12), (14, 20), (15, 40)):
            a, b = self._samples(seed, n)
            mine = stats.wilcoxon_signed_rank(a, b)
            ref = sps.wilcoxon(a, b, method="exact")
            self.assertEqual(mine.method, "exact")
            self.assertAlmostEqual(mine.statistic, float(ref.statistic), places=9, msg=f"seed {seed}")
            self.assertAlmostEqual(mine.p_two_sided, float(ref.pvalue), places=9, msg=f"seed {seed}")

    def test_wilcoxon_normal_approximation_with_ties_matches_scipy(self):
        a = [5, 7, 6, 9, 8, 7, 10, 12, 11, 9, 8, 14, 13, 12, 15]
        b = [4, 5, 6, 7, 8, 5, 8, 10, 9, 9, 6, 12, 10, 12, 12]
        mine = stats.wilcoxon_signed_rank(a, b)
        ref = sps.wilcoxon(a, b, method="approx", correction=False, zero_method="wilcox")
        self.assertEqual(mine.method, "normal-approximation")
        self.assertAlmostEqual(mine.statistic, float(ref.statistic), places=9)
        self.assertAlmostEqual(mine.p_two_sided, float(ref.pvalue), places=9)


def _trial_row(participant, condition, outcome="success", mt="1.500", tp="2.000", origin="real", idx=1):
    row = dict.fromkeys(at.EXPECTED_HEADER, "")
    row.update(schema_version="0.1-proposed", data_origin=origin, session_id="S1", participant_pseudonym=participant,
               condition=condition, trial_index=str(idx), trial_id=f"S1-T{idx:03d}", outcome=outcome,
               movement_time_raw_s=mt, throughput_bps=tp if outcome == "success" else "", input_modality="Voice")
    return row


def _write_csv(path, rows, header=None):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header or at.EXPECTED_HEADER)
        for r in rows:
            w.writerow([r[h] for h in at.EXPECTED_HEADER])


class AnalyzerTests(unittest.TestCase):
    def _fixture_rows(self, origin="real", n=12, seed=3):
        rng = random.Random(seed)
        rows = []
        for i in range(1, n + 1):
            p = f"P{i:02d}"
            base = rng.uniform(1.5, 3.0)
            for c, shift in (("controller", 0.0), ("gaze_voice", -rng.uniform(0.2, 1.0))):
                for k in range(1, 4):
                    mt = base + shift + rng.uniform(-0.1, 0.1)
                    rows.append(_trial_row(p, c, mt=f"{mt:.3f}", tp=f"{2.0 / mt:.3f}", origin=origin, idx=k))
        return rows

    def test_header_matches_the_csharp_source(self):
        src = os.path.join(HERE, "..", "Assets", "Scripts", "Telemetry", "TrialRecord.cs")
        with open(src, encoding="utf-8") as f:
            text = f.read()
        block = re.search(r"CsvHeader\s*=\s*(.*?);", text, re.S).group(1)
        header = "".join(re.findall(r'"([^"]*)"', block)).split(",")
        self.assertEqual(header, at.EXPECTED_HEADER)

    def test_real_data_is_analysed(self):
        rep = at.analyze(self._fixture_rows())
        self.assertEqual(rep["status"], "participant data (data_origin=real)")
        mt = rep["analyses"][0]
        self.assertEqual(mt["n_pairs"], 12)
        self.assertIn("paired_t", mt)
        self.assertIn("wilcoxon", mt)
        self.assertEqual(rep["warnings"], [])

    def test_small_n_warns(self):
        rep = at.analyze(self._fixture_rows(n=4))
        self.assertTrue(any("N >= 10" in w for w in rep["warnings"]))

    def test_selftest_and_simulated_rows_are_refused_by_default(self):
        for origin in ("selftest", "simulated"):
            with self.assertRaises(at.AnalysisError):
                at.analyze(self._fixture_rows(origin=origin))

    def test_nonreal_allowed_only_with_explicit_flag_and_is_stamped(self):
        rep = at.analyze(self._fixture_rows(origin="selftest"), allow_nonreal=True)
        self.assertEqual(rep["status"], "NOT PARTICIPANT DATA")
        self.assertTrue(rep["notes"])

    def test_mixed_real_and_nonreal_is_refused(self):
        rows = self._fixture_rows() + self._fixture_rows(origin="selftest")
        with self.assertRaises(at.AnalysisError):
            at.analyze(rows)

    def test_only_participants_with_both_conditions_are_paired(self):
        rows = self._fixture_rows(n=5) + [_trial_row("P99", "controller", mt="9.0", tp="0.2")]
        rep = at.analyze(rows)
        self.assertNotIn("P99", rep["analyses"][0]["participants_used"])
        self.assertEqual(rep["analyses"][0]["n_pairs"], 5)

    def test_failed_trials_do_not_contribute_to_time_or_throughput(self):
        rows = [_trial_row("P01", "controller", mt="1.0", tp="2.0", idx=1),
                _trial_row("P01", "controller", outcome="failure", mt="99.0", idx=2),
                _trial_row("P01", "gaze_voice", mt="0.5", tp="4.0", idx=1),
                _trial_row("P02", "controller", mt="2.0", tp="1.0", idx=1),
                _trial_row("P02", "gaze_voice", mt="1.0", tp="2.0", idx=1)]
        table, _ = at.aggregate(rows)
        self.assertAlmostEqual(table[("P01", "controller")]["mean_movement_time_s"], 1.0)
        self.assertAlmostEqual(table[("P01", "controller")]["success_rate"], 0.5)

    def test_unknown_condition_rejected(self):
        with self.assertRaises(at.AnalysisError):
            at.aggregate([_trial_row("P01", "gaze_dwell")])

    def test_cli_refuses_bad_header_and_never_overwrites(self):
        with tempfile.TemporaryDirectory() as d:
            good, bad, out = (os.path.join(d, n) for n in ("good.csv", "bad.csv", "out.json"))
            _write_csv(good, self._fixture_rows())
            with open(bad, "w", newline="", encoding="utf-8") as f:
                f.write("a,b,c\n1,2,3\n")
            self.assertEqual(at.main([bad, "--out", os.path.join(d, "x.json")]), 2)
            self.assertFalse(os.path.exists(os.path.join(d, "x.json")))
            self.assertEqual(at.main([good, "--out", out]), 0)
            with open(out, encoding="utf-8") as fh:
                first = fh.read()
            self.assertEqual(at.main([good, "--out", out]), 2)          # second write must be refused
            with open(out, encoding="utf-8") as fh:
                self.assertEqual(fh.read(), first)                      # and the first file is untouched

    def test_cli_refuses_selftest_file_without_flag(self):
        with tempfile.TemporaryDirectory() as d:
            f = os.path.join(d, "t.csv")
            _write_csv(f, self._fixture_rows(origin="selftest"))
            self.assertEqual(at.main([f, "--out", os.path.join(d, "o.json")]), 2)
            self.assertEqual(at.main([f, "--out", os.path.join(d, "o2.json"), "--allow-nonreal"]), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
