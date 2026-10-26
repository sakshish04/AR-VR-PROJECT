"""Join voice-telemetry rows to trial rows by timestamp (Phase 05, "link voice CSV rows to trial ids").

Usage:  python link_voice_to_trials.py <trials.csv> <voice_telemetry.csv> [out.csv]

A voice row belongs to a trial when its timestamp_utc lies in [start_utc, end_utc] of that trial. Rows outside every trial
(for example a command spoken between trials) get an empty trial_id. Nothing is modified in place; the result is a new CSV.
Practice files are joined the same way, so keep them separate from measured files when analysing.
"""
import csv
import sys
from datetime import datetime, timezone


def parse_utc(text):
    text = text.strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    if "." in text:  # .NET writes 7 fractional digits; Python accepts at most 6
        head, rest = text.split(".", 1)
        frac = ""
        i = 0
        while i < len(rest) and rest[i].isdigit():
            frac += rest[i]
            i += 1
        text = head + "." + frac[:6].ljust(6, "0") + rest[i:]
    dt = datetime.fromisoformat(text)
    return dt if dt.tzinfo else dt.replace(tzinfo=timezone.utc)


def link(trials, voice_rows):
    spans = [(parse_utc(t["start_utc"]), parse_utc(t["end_utc"]), t["trial_id"]) for t in trials]
    out = []
    for v in voice_rows:
        ts = parse_utc(v["timestamp_utc"])
        tid = ""
        for start, end, trial_id in spans:
            if start <= ts <= end:
                tid = trial_id
                break
        row = dict(v)
        row["trial_id"] = tid
        out.append(row)
    return out


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    with open(argv[1], newline="", encoding="utf-8") as f:
        trials = list(csv.DictReader(f))
    with open(argv[2], newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        fields = list(reader.fieldnames) + ["trial_id"]
        rows = link(trials, list(reader))
    out_path = argv[3] if len(argv) > 3 else argv[2].replace(".csv", "_linked.csv")
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)
    matched = sum(1 for r in rows if r["trial_id"])
    print("%d voice rows, %d inside a trial, written to %s" % (len(rows), matched, out_path))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
