"""Summarise REAL study files in this folder (coding_sheet.csv, sus_responses.csv, tlx_responses.csv).

Reads only files you filled in from real sessions; it never uses sample data. Prints N, mean, SD and median
and writes study_summary.csv. SUS scoring follows Brooke (1996): odd items score-1, even items 5-score, sum x 2.5.
"""
import csv
import os
import re
import statistics as st
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CODE = re.compile(r"^P\d{2,3}$")


def read(name):
    path = os.path.join(HERE, name)
    if not os.path.exists(path):
        return None
    with open(path, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    for r in rows:
        if not CODE.match((r.get("participant_code") or "").strip()):
            sys.exit("%s: participant_code must look like P01 (no names): %r" % (name, r.get("participant_code")))
    return rows


def summary(values):
    values = [v for v in values if v is not None]
    if not values:
        return (0, None, None, None)
    sd = st.stdev(values) if len(values) > 1 else 0.0
    return (len(values), st.mean(values), sd, st.median(values))


def sus_score(r):
    q = [int(r["q%d" % i]) for i in range(1, 11)]
    if any(not 1 <= v <= 5 for v in q):
        sys.exit("SUS answers must be 1 to 5 (%s)" % r["participant_code"])
    return (sum(q[i] - 1 for i in range(0, 10, 2)) + sum(5 - q[i] for i in range(1, 10, 2))) * 2.5


def tlx_score(r):
    keys = ["mental", "physical", "temporal", "performance", "effort", "frustration"]
    v = [float(r[k]) for k in keys]
    if any(not 0 <= x <= 100 for x in v):
        sys.exit("TLX values must be 0 to 100 (%s)" % r["participant_code"])
    return sum(v) / 6.0


def by_condition(rows, fn):
    out = {}
    for r in rows:
        out.setdefault((r.get("condition") or "all").strip(), []).append(fn(r))
    return out


def main():
    lines = [("measure", "condition", "N", "mean", "SD", "median")]
    sus = read("sus_responses.csv")
    if sus:
        for c, v in by_condition(sus, sus_score).items():
            lines.append(("SUS (0-100)", c) + summary(v))
    tlx = read("tlx_responses.csv")
    if tlx:
        for c, v in by_condition(tlx, tlx_score).items():
            lines.append(("NASA-TLX raw (0-100)", c) + summary(v))
    cs = read("coding_sheet.csv")
    if cs:
        for c, v in by_condition(cs, lambda r: float(r["first_try_success"])).items():
            lines.append(("first-try success rate (0-1)", c) + summary(v))
        sec = {}
        for r in cs:
            if (r.get("seconds_to_result") or "").strip():
                sec.setdefault((r.get("condition") or "all").strip(), []).append(float(r["seconds_to_result"]))
        for c, v in sec.items():
            lines.append(("seconds to result", c) + summary(v))
        people = sorted({r["participant_code"] for r in cs})
        print("participants in the coding sheet:", len(people), people)
    if len(lines) == 1:
        sys.exit("No data files found. Copy the *_TEMPLATE.csv files, fill them in from real sessions, then run again.")
    for l in lines:
        print(", ".join("" if x is None else ("%.2f" % x if isinstance(x, float) else str(x)) for x in l))
    with open(os.path.join(HERE, "study_summary.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        for l in lines:
            w.writerow(["" if x is None else x for x in l])
    print("written:", os.path.join(HERE, "study_summary.csv"))


if __name__ == "__main__":
    main()
