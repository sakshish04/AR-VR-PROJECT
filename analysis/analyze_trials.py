"""Paired analysis of per-trial CSV files written by Assets/Scripts/Telemetry/TrialCsvWriter.cs.

Usage (from the repository root):
    python analysis/analyze_trials.py --out analysis_results/run1.json  path/to/trials_real_<session>.csv [more.csv ...]

What it does
  * Accepts only files whose header is exactly the PROPOSED canonical trial schema (TrialRecord.CsvHeader).
  * Uses only rows with data_origin == "real". Rows from self-tests or simulated data are REFUSED unless
    --allow-nonreal is given; in that case the output is stamped "NOT PARTICIPANT DATA" and must not be reported as a finding.
  * Aggregates per participant and condition, keeps only participants that have BOTH conditions
    ("controller" and "gaze_voice"), then runs the paired tests from stats.py.
  * Writes a new JSON file; it refuses to overwrite an existing file.

What it does NOT do: decide whether the study design is valid, whether consent/ethics are in place, or whether N is
sufficient. It warns when fewer than 10 paired participants are present (dossier section 4 minimum) and it never prints
a conclusion. The condition names, the variables and the choice of tests are PENDING CONFIRMATION by the professor.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import os
import sys
from collections import defaultdict
from typing import Dict, Iterable, List

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import stats  # noqa: E402

EXPECTED_HEADER = (
    "schema_version,data_origin,session_id,participant_pseudonym,condition,trial_index,trial_id,"
    "start_utc,end_utc,target_name,action,outcome,failure_reason,error_count,input_modality,"
    "target_distance_m,target_width_m,fitts_id_bits,movement_time_raw_s,movement_time_used_s,"
    "mt_safeguard_applied,throughput_bps,recognition_latency_ms,command_text,fitts_note"
).split(",")

CONDITION_A = "controller"
CONDITION_B = "gaze_voice"
MIN_PAIRS_ADVISORY = 10


class AnalysisError(ValueError):
    pass


def read_rows(paths: Iterable[str]) -> List[Dict[str, str]]:
    rows: List[Dict[str, str]] = []
    for p in paths:
        with open(p, newline="", encoding="utf-8") as f:
            reader = csv.reader(f)
            try:
                header = next(reader)
            except StopIteration:
                raise AnalysisError(f"{p}: file is empty")
            if header != EXPECTED_HEADER:
                raise AnalysisError(f"{p}: header does not match the proposed trial schema; refusing to guess")
            for line_no, rec in enumerate(reader, start=2):
                if len(rec) != len(EXPECTED_HEADER):
                    raise AnalysisError(f"{p}: line {line_no} has {len(rec)} fields, expected {len(EXPECTED_HEADER)}")
                rows.append(dict(zip(header, rec)))
    return rows


def _num(s: str):
    if s is None or s == "":
        return None
    try:
        v = float(s)
    except ValueError:
        return None
    return v if math.isfinite(v) else None


def aggregate(rows: List[Dict[str, str]], allow_nonreal: bool = False):
    """Returns (per-participant-per-condition table, notes). Raises if non-real rows are present and not allowed."""
    origins = {r["data_origin"] for r in rows}
    notes: List[str] = []
    nonreal = origins - {"real"}
    if nonreal and not allow_nonreal:
        raise AnalysisError(
            "refusing non-participant data (data_origin=" + ", ".join(sorted(nonreal)) +
            "). Pass --allow-nonreal only for tooling checks; results from such data are NOT findings.")
    if nonreal:
        notes.append("NOT PARTICIPANT DATA: includes data_origin " + ", ".join(sorted(nonreal)))
    groups: Dict[tuple, List[Dict[str, str]]] = defaultdict(list)
    for r in rows:
        if r["condition"] not in (CONDITION_A, CONDITION_B):
            raise AnalysisError(f"unknown condition {r['condition']!r}")
        groups[(r["participant_pseudonym"], r["condition"])].append(r)

    table: Dict[tuple, Dict[str, float]] = {}
    for key, rs in groups.items():
        success = [r for r in rs if r["outcome"] == "success"]
        mts = [v for v in (_num(r["movement_time_raw_s"]) for r in success) if v is not None]
        tps = [v for v in (_num(r["throughput_bps"]) for r in success) if v is not None]
        entry = {"trials": len(rs), "successes": len(success), "success_rate": len(success) / len(rs)}
        if mts:
            entry["mean_movement_time_s"] = math.fsum(mts) / len(mts)
        if tps:
            entry["mean_throughput_bps"] = math.fsum(tps) / len(tps)
        table[key] = entry
    return table, notes


def paired_report(table, variable: str) -> dict:
    participants = sorted({p for (p, _c) in table})
    a, b, used = [], [], []
    for p in participants:
        ea, eb = table.get((p, CONDITION_A)), table.get((p, CONDITION_B))
        if ea is None or eb is None or variable not in ea or variable not in eb:
            continue
        a.append(ea[variable])
        b.append(eb[variable])
        used.append(p)
    out: dict = {"variable": variable, "n_pairs": len(used), "participants_used": used,
                 "difference_definition": f"{CONDITION_A} minus {CONDITION_B}"}
    if len(used) < 2:
        out["error"] = "fewer than 2 participants have both conditions"
        return out
    try:
        t = stats.paired_t_test(a, b)
        out["paired_t"] = {"n": t.n, "mean_difference": t.mean_difference, "sd_difference": t.sd_difference,
                           "t": t.t, "df": t.df, "p_two_sided": t.p_two_sided}
    except stats.StatsError as e:
        out["paired_t_error"] = str(e)
    try:
        d = stats.cohens_d(a, b)
        out["cohens_d"] = {"d_z": d.d_z, "d_av": d.d_av}
    except stats.StatsError as e:
        out["cohens_d_error"] = str(e)
    try:
        w = stats.wilcoxon_signed_rank(a, b)
        out["wilcoxon"] = {"n_effective": w.n_effective, "n_zero_dropped": w.n_zero_dropped, "statistic": w.statistic,
                           "p_two_sided": w.p_two_sided, "method": w.method}
    except stats.StatsError as e:
        out["wilcoxon_error"] = str(e)
    return out


def analyze(rows: List[Dict[str, str]], allow_nonreal: bool = False) -> dict:
    table, notes = aggregate(rows, allow_nonreal)
    report = {
        "status": "NOT PARTICIPANT DATA" if notes else "participant data (data_origin=real)",
        "notes": notes,
        "conditions": [CONDITION_A, CONDITION_B],
        "analyses": [paired_report(table, v) for v in ("mean_movement_time_s", "mean_throughput_bps", "success_rate")],
        "warnings": [],
        "disclaimer": ("Statistics only. Study design, consent/ethics, sample size and the choice of tests are PENDING "
                       "CONFIRMATION. No conclusion is drawn here."),
    }
    n_pairs = max((a["n_pairs"] for a in report["analyses"]), default=0)
    if n_pairs < MIN_PAIRS_ADVISORY:
        report["warnings"].append(f"only {n_pairs} paired participant(s); the dossier minimum is N >= {MIN_PAIRS_ADVISORY}")
    return report


def write_new_json(path: str, obj: dict) -> None:
    """Writes a NEW file; raises FileExistsError instead of overwriting."""
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "x", encoding="utf-8") as f:
        json.dump(obj, f, indent=2, sort_keys=True)
        f.write("\n")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("csv", nargs="+", help="trial CSV file(s) in the proposed schema")
    ap.add_argument("--out", required=True, help="NEW JSON file to write (never overwritten)")
    ap.add_argument("--allow-nonreal", action="store_true",
                    help="permit selftest/simulated rows for tooling checks; output is stamped NOT PARTICIPANT DATA")
    args = ap.parse_args(argv)
    try:
        rep = analyze(read_rows(args.csv), allow_nonreal=args.allow_nonreal)
        write_new_json(args.out, rep)
    except (AnalysisError, FileExistsError, OSError) as e:
        print("ERROR:", e, file=sys.stderr)
        return 2
    print(f"wrote {args.out}  [{rep['status']}]")
    for w in rep["warnings"]:
        print("WARNING:", w)
    return 0


if __name__ == "__main__":
    sys.exit(main())
