# Analysis plan — DRAFT v0.1

**DRAFT. Not approved. Written before any study data exists (none does).** The exit criteria of Phase 04 require the analysis plan to be written and dated **before data collection**; once the professor approves it, it is frozen (v1.0) and any later change is recorded with the reason and date. No outcome, test or exclusion rule may be chosen after looking at study data.

Date written: 2026-10-24. Author: project team. Approval: **PENDING PROFESSOR APPROVAL** (none given).

## 1. Scope and status of inputs

| Item | Status |
|---|---|
| Design: within-subject, C1 controller vs C2 gaze+voice, AB/BA order | PROPOSED (D-04) |
| Population: able-bodied volunteers; accessibility framing | **PENDING PROFESSOR APPROVAL** (D-05, D-05a) |
| Sample size | **PENDING PROFESSOR APPROVAL** (Q-07); dossier §4 says at least 10, recommends 16–20 |
| Outcomes | UNRESOLVED suggestion (Q-01) |
| Fitts' Law formula | **PENDING PROFESSOR APPROVAL** (D-02): Shannon `log2(D/W+1)` vs dossier `log2(2D/W)` |

Everything below holds under either formula; the formula only changes how the ID column is computed (section 4).

## 2. Data used

Only rows with data origin `Real` and a pseudonym `P01`.. count. Files with origin `Selftest` or `Simulated`, the developers' own voice CSVs, and everything under `telemetry/selftest/` or `telemetry/SIMULATED_*` are **never** analysed as participant data (D-18). The voice CSV has no origin column; its files are matched to participants by the session sheet and the folder, and a file without a session-sheet entry is excluded.

Per trial the analysis needs: participant, condition, trial index, target distance D, target width W, raw movement time, outcome and reason (success / failure / cancelled; wrong target, timeout, abandoned), error count. Per voice event: recognized phrase, recognizer, confidence rank, decision, outcome, speech-end-to-event time.

## 3. Primary and secondary outcomes (PROPOSED, Q-01)

| Role | Outcome | Computed per participant per condition as |
|---|---|---|
| Co-primary | Task completion latency | **median** movement time over successful measured trials (median because times are right-skewed) |
| Co-primary | Interaction failure rate | failed trials ÷ measured trials; for C2 also rejected commands ÷ commands (reported separately) |
| Secondary | Throughput | mean of per-trial ID ÷ MT over successful trials, per participant, under the confirmed formula |
| Secondary | Workload, usability, sickness | NASA-TLX raw score (mean of 6 subscales), SUS (0–100), SSQ change from baseline |
| Descriptive | Command latency and recognition | median and spread of speech-end-to-event; ignore rate; confidence-rank counts; phrase accuracy for prompted phrases |

Practice trials are never analysed.

## 4. Index of difficulty and throughput

Raw D, W and movement time are stored per trial, so ID can be recomputed under either formula. **Nothing is mixed:** one formula is chosen by the professor before analysis, stated on every table and figure, and the other may be given in an appendix. Nominal width vs effective width, and the minimum-movement-time floor (D-03), are decided together with D-02; raw MT is always kept and the floor, if any, is applied only to throughput.

## 5. Confirmatory comparisons

For each co-primary outcome: paired comparison C2 vs C1 across participants.
- Check the distribution of the paired differences (plot and Shapiro–Wilk as a guide, not a rule).
- Approximately normal → paired t-test; otherwise → Wilcoxon signed-rank test.
- Report the mean (or median) difference, its **95% confidence interval**, and an effect size (Cohen's d_z for paired data, or the rank-biserial correlation for Wilcoxon).
- Two co-primary outcomes: the significance threshold is divided between them (Holm correction) unless the professor prefers another rule; this is stated in advance.

Secondary and descriptive outcomes are reported with the same paired approach but labelled **exploratory**; no claim rests on them alone.

## 6. Order and carry-over

Participants alternate C1-first and C2-first. Report the outcome by condition order (AB vs BA) as a descriptive check for order effects; with small N this is a warning, not a test. Learning across trials is also shown (trial index vs time) per condition.

## 7. Exclusion and missing data (written in advance)

- A **trial** is excluded only for a recorded technical failure (software crash, tracking loss, microphone failure), an experimenter error, or the participant stopping; the reason is stored and the count reported. It is **never** excluded for being slow, wrong or inconvenient.
- A **participant** is excluded only if a session cannot be completed for a reason unrelated to the outcome (for example equipment failure) or if the stop rules ended it before both conditions were done; their partial data is reported separately and not pooled silently.
- Missing questionnaires are reported as missing, not filled in. Analysis of a measure uses the participants who have it, and N is stated per table.
- Timeouts count as **failures**, not as missing.

## 8. Sample size and power

Not decided here (Q-07). The only legitimate effect-size source is the team's pilot (labelled `Selftest`), and the numbers in the guide (§1.1) and the manuscript blueprint (Table I) are **not** to be used: they come from no session recorded in this project. The final N is stated with its justification, or, if it is simply the number recruited, the report says so and shows the confidence intervals.

## 9. Reporting

- Table per measure: N, mean or median, SD or IQR, per condition (dossier §4 table).
- Per-condition plots from the raw CSV in the repository; the figures regenerate from the data (dossier §4 declaration).
- Every figure and table states: the recognizer used, the Fitts formula, the data origin, N.
- Limits stated: able-bodied sample only unless D-05a changes it; keyword recogniser with a fixed phrase list; recognizer-reported (not end-to-end) latency; provisional confidence thresholds; simulator vs headset (Q-04).

## 10. Tools

`analysis/stats.py`, `analysis/analyze_trials.py`, `analysis/wer.py` and the questionnaire calculators in `telemetry/` exist; they have only been run on synthetic data. They are checked against hand-calculated examples before use on real data (Phase 06).
