# Roadmap Phase 04 — Research Protocol and Experiment Design

**Status: PROTOCOL DRAFT v0.4 (2026-10-25), complete as a document, NOT approved.** Every section is written out, but the decisions that make it a usable study protocol belong to the professor and have not been given. The project owner has recorded WORKING DECISIONS on 2026-10-25 (Fitts formula, participant mix); these let the project proceed but are **not** the professor's approval. No participant has been recruited, no session run, and no study result exists. Nothing in this file is evidence. Every number is a proposal, a placeholder, or marked "measured" with its source.

Roadmap position: Phase 04 of the agreed sequence (0 audit, 1 core interaction, 2 voice reliability, 3 voice telemetry CSV, **4 research protocol**, 5 live trial manager and Fitts' telemetry, 6 analysis pipeline, 7 XR input and integration, 8 UI polish, 9 documentation). Phase 05 and later do not start until this protocol is approved.

Naming notes: `docs/PHASE4_SPATIAL_ACTIONS_AND_HUD.md` belongs to an earlier numbering of the work (MOVE/RELEASE and the HUD) and is **not** this phase; it is unchanged. Decision IDs in this file are `D-nn` (the numbers used in `docs/DECISION_REGISTER.md`), `D-05a`, and `Q-01` to `Q-07` (new in this phase; the register carries them too). `Q-nn` is used so that decision IDs are never confused with participant IDs such as `P01`.

Sources reviewed for this version: `docs/DECISION_REGISTER.md`; `RESEARCH_AND_IMPLEMENTATION_GUIDE.md` (the project guide); `docs/IVRAR_Group_05_Technical_Dossier.docx` (read directly, §1, §1.1, §2 and §4); `docs/RESEARCH_PAPER_MANUSCRIPT_BLUEPRINT.md`; the telemetry code and analysis tools in the repository.

## Decision status (read this first)

Every decision in this file carries one of these labels. **No decision is CONFIRMED yet: the professor has not approved anything.**

| Label | Meaning |
|---|---|
| **CONFIRMED** | approved by the professor (or the responsible authority), with the date and where it is recorded |
| **PROPOSED** | the project owner's current proposal; the team stands behind it for now, but it is not approved |
| **WORKING DECISION** | the project owner has decided to go ahead with this for all project work (code, data files, documents); it is recorded with its date, is **not** approved by the professor, and must be confirmed or reversed by the professor. Used only where the owner explicitly decided |
| **PENDING PROFESSOR APPROVAL** | cannot be treated as settled until the professor decides; work that depends on it must wait or be marked provisional |
| **UNRESOLVED** | nobody has decided; options or defaults are only suggestions |

Summary of the project owner's current position (2026-10-23):

| # | Decision | Status |
|---|---|---|
| 1 | Compare controller interaction against gaze + voice; every participant does **both** conditions (within-subject) | PROPOSED |
| 2 | Participant population: **mostly able-bodied volunteers**; participants with motor impairment are welcome where they can be recruited, but are expected to be few | **WORKING DECISION** (owner, 2026-10-25); professor approval and ethics review of including participants with motor impairment still **PENDING** |
| 2b | Research-question framing with a mostly able-bodied sample plus a few motor-impaired participants | **WORKING DECISION**: keep the accessibility framing but limit every claim to what the sample supports (section 1); **PENDING PROFESSOR APPROVAL** |
| 3 | No gaze-dwell third condition, unless the professor requires it | PROPOSED (the professor may overrule) |
| 4 | Lab docking task (T2) kept, but **provisional** until its necessity is confirmed | PROPOSED, provisional |
| 5 | Fitts' Law Index of Difficulty: Shannon form `ID = log2(D/W + 1)` | **WORKING DECISION** (owner, 2026-10-25, on my recommendation, see 7.2); **conflicts with the dossier**, so professor confirmation is still needed |
| 6 | Participants identified only by pseudonymous IDs (P01, P02, ...); the developers' own test sessions are never presented as participant data | PROPOSED (rule already enforced in code and in section 9) |
| 7 | Sample size and ethics requirements | sample size N = 10: WORKING DECISION (owner, 2026-10-26); ethics: **PENDING PROFESSOR APPROVAL** |

All other items below (order of conditions, trial counts, questionnaires, session length, analysis details, and so on) are PROPOSED or UNRESOLVED as labelled where they appear.

---

## 0. Phase 03 closure check (done before starting this phase)

| Check | Result (2026-10-23) |
|---|---|
| Standalone tests | 384 passed, 0 failed |
| External compile check | 35 files, 3 of 3 configurations OK |
| Real voice CSVs in `...\LocalLow\DefaultCompany\Group_05_Sakshi_Aryan\VoiceVR\` | 5 files, 74 rows; every file has the exact 15-column header, 15 fields per row, `sequence_id` 1..n with no gaps, one session per file; no ignored row carries an outcome |
| Intents seen at runtime | Select, Move, Release, Cancel, and Grab (new commands, spoken) |
| Recognizer callbacks vs rows | 48 callbacks and 48 rows in the earlier cross-check; typed/keyboard commands write no voice row (checked) |
| Still unobserved | a `recognizer_error` row and a `parse_rejected` row at runtime (tests only); nothing about Quest hardware |

Phase 03 is closed with those limits recorded in `docs/ROADMAP_PHASE03_VOICE_TELEMETRY_CSV.md`.

## 1. Research question, outcomes and hypotheses

**Authorised title (dossier §1, from the faculty-approved title sheet):** "How can voice-driven spatial NLP commands in Unity VR reduce task completion latency and interaction failure rates for motor-impaired users facing physical controller barriers?" Its two dependent variables are therefore **task completion latency** and **interaction failure rate**; the dossier's §4 adds **throughput (bits/s)**, **command latency (ms)** and **WER** as the primary task metrics, with **SUS** and **NASA-TLX** as instruments (SSQ if a headset is used).

Working question for this study (PROPOSED): *Does voice-driven spatial interaction (gaze to point, voice to act) let users acquire and place virtual objects faster, with fewer failures and with less workload than a handheld controller, in a simulated lab?*

**Accessibility framing — WORKING DECISION (2026-10-25), PENDING PROFESSOR APPROVAL (D-05a).** The authorised title is about **motor-impaired** users. The sample will be **mostly able-bodied volunteers**, with **some participants with motor impairment** where they can be recruited (expected to be few). Working position: keep the accessibility framing, but every claim must match the sample. (i) The comparison of the two interaction methods is reported for the able-bodied group. (ii) Results from participants with motor impairment are reported **separately and descriptively** (individual or small-group results; no statistical test across groups unless the numbers genuinely support one). (iii) Reports say plainly that the evidence for motor-impaired users comes only from those few participants. (iv) Nothing from the able-bodied data is presented as evidence that the system reduces failure for motor-impaired users. This is option (b) below, extended with a small motor-impaired subgroup. The title is faculty-authorised, so whether this framing is acceptable, and any wording change, is the professor's decision; and the ethics route has to cover recruiting participants with motor impairment (section 5). Options as originally listed: (a) keep the framing and recruit mainly participants with motor impairment (not currently possible); (b) keep the framing but describe the able-bodied study as a first, preliminary step, with every claim limited to able-bodied users and accessibility listed as future work; (c) reword the question to interaction performance in general.

Outcomes (PROPOSED, UNRESOLVED until the professor agrees them; Q-01):

| Role | Outcome | Operational definition | Origin |
|---|---|---|---|
| Co-primary | Task completion latency | movement time (MT): trial start to the accepted selection, per trial, in seconds; for T2 also first grab to last dock | title; dossier §4 |
| Co-primary | Interaction failure rate | failed trials (wrong target, wrong dock, timeout, abandoned) divided by trials; for the voice condition also rejected commands divided by commands | title |
| Secondary | Throughput | `TP = ID / MT` in bits/s; the ID formula is the open question D-02 (section 7.2) | dossier §4; guide §1.1 |
| Secondary | Command latency | recognizer-reported speech end to callback, ms (not end-to-end; D-11) | dossier §4; guide §1.3 |
| Secondary | Workload, usability, sickness | NASA-TLX raw (6 subscales, 0–100), SUS (0–100), SSQ before and after | dossier §4 |
| Descriptive | Recognition quality | recognizer confidence ranks and ignore rate; accuracy for prompted phrases; WER only if free speech is added (D-08) | dossier §4 |

Hypotheses (PROPOSED; directional; to be fixed before data collection, not after):
- **H1** — task completion latency is shorter with gaze+voice than with the controller.
- **H2** — interaction failure rate is lower, or not higher, with gaze+voice.
- **H3** — throughput is higher with gaze+voice (conditional on the D-02 formula decision being applied the same way to both conditions).
- **H4** — perceived workload (NASA-TLX) is lower and usability (SUS) higher with gaze+voice.
- **H5 (descriptive, no test)** — recognition is reliable enough for the task; report confidence ranks, ignore rate and latency distribution, with the keyword-recogniser limits stated.

**Do not reuse the guide's or blueprint's numbers as baselines.** The guide (§1.1: slope b of about 0.58 s/bit, throughput about 1.22 bps for controllers and 3.84 bps for voice+gaze) and the manuscript blueprint's Table I (N = 50, p < 0.001, SUS 87.2, ...) do **not** come from any session recorded in this project. Treat them as unverified placeholders: label or remove them before any paper text is written. The guide's "< 350 ms" is an end-to-end target (D-11); the recognizer-reported latency measured so far in **developer test sessions** (74 callbacks, not participant data) ranges from 252 to 1614 ms with a median of 520 ms, and 10.8 % of callbacks were at or under 350 ms, so the target is **not demonstrated** by the project's current data.

## 2. Conditions (D-04 / D-06)

| Condition | Input | Status | Exists in the project today |
|---|---|---|---|
| C1 `controller` | tracked controller: ray and grab through the Interaction SDK | PROPOSED | scene has grab, hand/controller interactors on all nine items; no trial logic yet |
| C2 `gaze_voice` | gaze focus (head pose) to point; fixed spoken commands to act: select, move (left/right/up/down with optional distance, there), grab/carry/drop, release, cancel, reset, focus, help | PROPOSED | yes, working, with telemetry (voice CSV, gaze event log) |
| C3 `gaze_dwell` | gaze only, dwell to select | **NOT INCLUDED** unless the professor requires it (D-06: PROPOSED, professor may overrule) | no |

PROPOSED: the study compares C1 and C2, and **each participant completes both**. C3 is not added on our own initiative (it would add a third block per participant and lengthen every session); if the professor requires it, the design in section 4 changes to a 3 × 3 Latin square.

Fairness rules (PROPOSED): the same scene, targets, order of target presentation and time limit in both conditions; in C1 voice is disabled and in C2 controller selection is disabled, so each condition measures one input method; the experimenter reads the same instructions in both.

## 3. Tasks, parameters and trial counts

| Task | What | Status | Exists today |
|---|---|---|---|
| T1 Target acquisition (Fitts) | select targets of distance D and width W, ISO 9241-9 style | PROPOSED | maths exists (`FittsMath`, `TrialRecord`); no on-screen task generator or trial runner (Phase 05) |
| T2 Lab docking (**PROVISIONAL**) | take each of 9 items to its own dock; see `docs/LAB_DOCKS.md` | PROPOSED, provisional (Q-02) | exists in the scene, logic-tested in Edit Mode only; **no telemetry yet** |

PROPOSED: T1 gives the standardised, comparable number. **T2 is kept only as provisional until its necessity is confirmed**: nothing further is built for it (no telemetry, no scoring, no protocol text that depends on it) until the professor confirms the study needs an ecological task. If T2 is dropped, the study is T1 only and no other section changes.

### 3.1 T1 parameters and trial counts (PROPOSED numbers, UNRESOLVED; to be tuned in the pilot — Q-03)

The guide suggests D 0.5–2.0 m and W 0.08–0.30 m. Proposal: 3 distances × 3 widths = 9 D×W combinations.

| | W = 0.30 m | W = 0.15 m | W = 0.08 m |
|---|---|---|---|
| D = 0.5 m | 0.5 / 0.30 | 0.5 / 0.15 | 0.5 / 0.08 |
| D = 1.0 m | 1.0 / 0.30 | 1.0 / 0.15 | 1.0 / 0.08 |
| D = 2.0 m | 2.0 / 0.30 | 2.0 / 0.15 | 2.0 / 0.08 |

Per condition: **3 repetitions of each combination = 27 measured trials**, preceded by **9 practice trials** (one per combination, not analysed). Per participant: 2 conditions × (27 measured + 9 practice) = **54 measured + 18 practice trials**. Order of the 27 trials is randomised in blocks of 9 (each combination once per block), with the random seed recorded. Per-trial time limit and the definition of timeout and abandonment: UNRESOLVED (to be set from the pilot; for example 15 s).

Estimated time on task: about 4–6 s per trial gives roughly 5 minutes of measured trials per condition (an assumption, to be measured in the pilot, not a result).

## 4. Design and counterbalancing

| Option | Pro | Con |
|---|---|---|
| Within-subject, two conditions, order counterbalanced (AB / BA) | every participant is their own control; fewest participants needed; paired tests | order and fatigue effects (handled by counterbalancing and breaks) |
| Between-subject | no carry-over | needs about twice as many participants; individual differences swamp small effects |

PROPOSED (not approved): **within-subject, C1 vs C2, AB/BA counterbalanced**, with participants assigned alternately in the order they enrol (P01 = AB, P02 = BA, ...). With three conditions use a 3 × 3 Latin square. The assignment sheet must be written down before recruiting, not chosen per participant. The order scheme itself is UNRESOLVED until the professor agrees the design (the dossier §4 "Study design" field is still unselected).

## 5. Participants, sample size, ethics and consent

**Everything in this section is PENDING PROFESSOR APPROVAL.** The items are what has to be settled with the professor and the institution **before** anyone is recruited; nothing here is assumed or approved.

- **Population — WORKING DECISION (2026-10-25); PENDING PROFESSOR APPROVAL and ethics review (D-05, D-05a).** Mostly able-bodied volunteers, plus participants with motor impairment where available (expected few). Consequences: (1) the two-method comparison rests on the able-bodied group; (2) motor-impaired participants are analysed and reported separately and descriptively; (3) group membership is **self-reported, optional** health-related information, recorded only if the ethics board allows it (background questionnaire question 10 is the only place; without it every participant is analysed as one group); (4) recruiting participants with motor impairment needs extra care that must be part of the ethics submission: an accessible information sheet and consent (large print, read aloud, a person of their choice present), a session that can be adapted (seating, breaks, longer time limits where needed, using only the condition they can use), no pressure to complete either condition, and a clear right to stop; (5) every adaptation is recorded in the notes column and never silently dropped from the data. Whether this participant mix is acceptable, and how the research question and wording change, is the professor's decision.
- **Sample size — WORKING DECISION N = 10 (owner, 2026-10-26); professor approval PENDING (Q-07).** N = 10 is the dossier's minimum; it is not backed by a power calculation, so the report must say so and show confidence intervals. The text below predates this decision. The dossier (§4) states "Minimum N ≥ 10 real people (your PBL dossier recommends N = 16–20)", with voluntary adult participants, written consent and anonymised IDs (P01, P02...). Whether that applies to an able-bodied study, and a justification (effect size, power), are not settled. No sample size is claimed or assumed here. A power calculation needs an effect-size estimate; the only honest source is a pilot, and the guide's and blueprint's numbers are not usable (section 1).
- **Ethics — PENDING PROFESSOR APPROVAL (Q-07).** Which board or procedure applies (approval or a written exemption), and its requirements, must be confirmed by the professor before recruiting.
- Inclusion/exclusion (UNRESOLVED suggestion): adult (the dossier requires adult participants), normal or corrected vision, fluent in the command language, no condition that makes VR unsafe (for example photosensitive epilepsy, severe motion sickness history); recorded by the experimenter, not stored with names.
- Compensation, if any, and recruitment route (UNRESOLVED, Q-05).

**Consent and information materials (to be written only after the ethics confirmation, to fit those requirements).** Minimum content proposed: purpose and tasks; duration; voluntary participation and the right to stop at any time without reason; what is recorded (voice phrases from a fixed command list with confidence and timing, gaze and command events, trial times, questionnaire scores; **no audio and no video**); who sees the data; how long it is kept; how the pseudonym link is handled; contact for questions. The dossier §4 requires **written** consent and a declaration that "we hold the forms"; signed forms are stored by the experimenter, separate from the data files.

**Participant identity (PROPOSED, and already enforced in code — Q-06):** participants appear in every file only as a pseudonymous ID of the form P01, P02, ... The trial record rejects any other form for real data. No name, roll number or email goes into a data file. The link from ID to person, if kept at all, is held separately by the experimenter and destroyed on a date the ethics rules set.

**The developers' own test sessions are not participant data.** Sessions run by the project team on their own machines (including the voice CSVs recorded so far) show how the software behaves. They are never given a participant ID, never counted in N, and never reported as results.

What the software records (verified in code): per-utterance voice rows (phrase from the fixed grammar, confidence rank, parse result, outcome, timings), gaze focus/command events, and later trial rows. **No audio is recorded or saved.** The keyword recognizer returns only the matched grammar phrase.

Safety in VR (PROPOSED): session length cap, a rest between conditions, an easy way to stop at any time, and a simulator-sickness questionnaire (SSQ) before and after. A seated, stationary setup, because the tasks need no walking (Q-04).

## 6. Procedure for one session (PROPOSED; durations are estimates to be measured in the pilot)

1. Information sheet, written consent, pseudonym assigned (about 10 min).
2. Short background questionnaire (age band, VR experience, handedness, microphone/voice experience); no name or student number on it.
3. SSQ baseline.
4. Condition 1: explanation, practice (9 practice trials, not analysed), 27 measured T1 trials, then T2 (only if T2 is confirmed); NASA-TLX and SUS for that condition.
5. Rest (>= 5 min), SSQ.
6. Condition 2 as in step 4.
7. SSQ, short interview or free comments, debrief.

Target total: about 60–75 minutes per participant (an estimate, to be confirmed by the pilot).

## 7. Measures, instruments and the Fitts' Law formula

### 7.1 Measures and where each comes from

| Measure | Definition (PROPOSED) | Source today | Gap |
|---|---|---|---|
| Task completion latency (MT) | trial start to the accepted selection | `TrialRecord` fields exist | trial runner (Phase 05) |
| Failure rate | wrong target, missed target, timeout, wrong dock (T2); rejected commands | `TrialRecord.error_count`; command outcomes in the voice CSV and gaze log | live recording |
| Index of difficulty, throughput | see 7.2 | `FittsMath` (tested, Shannon form only) | formula decision (D-02); minimum-MT safeguard (D-03); nominal vs effective width (part of D-02) |
| Command count and success | commands issued, executed, rejected, ignored | voice CSV and gaze event log (working) | link to trial id |
| Recognition quality | recognizer confidence ranks, ignore rate; accuracy needs the *intended* phrase | voice CSV (working) | prompted tasks must log the intended phrase; WER is only meaningful if free speech is used (D-08), because a keyword recogniser can return only listed phrases |
| Command latency | recognizer-reported speech end to callback (not end-to-end; D-11) | voice CSV `speech_end_to_event_ms` | an end-to-end definition if one is required |
| SUS | 10 items, 1–5; odd items score − 1, even items 5 − score, sum × 2.5; the dossier names a target mean of at least 68 | `telemetry/sus_calculator.py` | questionnaire delivery and storage |
| NASA-TLX | raw, 6 subscales, 0–100 | `telemetry/nasa_tlx_calculator.py` | same |
| SSQ | before and after each session | `telemetry/kennedy_ssq_calculator.py` | same |
| Lab task time (T2, **provisional**) | first grab to ninth dock | `LabTaskManager` computes it on screen | write it to a file — **not to be built unless T2 is confirmed** |

All timestamps in UTC; all numbers with invariant culture (already the rule in the writers).

### 7.2 Fitts' Law Index of Difficulty — CONFLICT, final choice PENDING PROFESSOR APPROVAL (D-02)

Two formulas are in use in the project documents:

| Source | Formula | Where |
|---|---|---|
| Project guide | Shannon form `ID = log2(D/W + 1)`, `TP = ID / MT` | guide §1.1 ("ISO 9241-9 standard Shannon formulation") |
| Dossier | `ID = log2(2D/W)`, `TP = ID / MT (bits/s)` | dossier **§2** "Fundamental equations"; and **§1.1** claim table: "Throughput (TP) = log2(2D/W) / MT", which the faculty pre-audit marks "Not yet checked: verify" (the team's Keep/Correct/Delete choice and verified source are not yet filled in) |
| Code (`FittsMath`) | Shannon form, provisional | `Assets/Scripts/Telemetry/FittsMath.cs` |
| Decision register | D-02, decider: Professor; current implementation "Shannon (provisional)" | `docs/DECISION_REGISTER.md` |

The dossier's check was made directly in the file on 2026-10-23. The two forms give different values for the same D and W (the dossier form is always larger), so throughput numbers produced under one are **not comparable** with the other and must never be mixed in one analysis:

| D (m) | W (m) | D/W | Shannon `log2(D/W+1)` | Dossier `log2(2D/W)` | Difference |
|---|---|---|---|---|---|
| 0.5 | 0.30 | 1.67 | 1.42 | 1.74 | 0.32 |
| 0.5 | 0.15 | 3.33 | 2.12 | 2.74 | 0.62 |
| 0.5 | 0.08 | 6.25 | 2.86 | 3.64 | 0.79 |
| 1.0 | 0.30 | 3.33 | 2.12 | 2.74 | 0.62 |
| 1.0 | 0.15 | 6.67 | 2.94 | 3.74 | 0.80 |
| 1.0 | 0.08 | 12.50 | 3.75 | 4.64 | 0.89 |
| 2.0 | 0.30 | 6.67 | 2.94 | 3.74 | 0.80 |
| 2.0 | 0.15 | 13.33 | 3.84 | 4.74 | 0.90 |
| 2.0 | 0.08 | 25.00 | 4.70 | 5.64 | 0.94 |

Decision and status: **WORKING DECISION (project owner, 2026-10-25): use the Shannon form `ID = log2(D/W + 1)`** for all project work. Reasons: it is the form of the ISO 9241-9 formulation the project guide cites (guide §1.1: "ISO 9241-9 standard Shannon formulation"); it is what `FittsMath` already implements and tests; and it behaves sensibly at small D/W (it is never negative and is 0 only when D = 0), whereas `log2(2D/W)` is 0 at D = W/2 and **negative** when D is smaller than W/2. (I recommended the Shannon form to the owner on that basis; I have not checked these points against the text of the standard.) **This is not professor-approved:** the dossier, the faculty-supplied document, states `log2(2D/W)` and marks that claim "verify", so the professor must confirm or overrule, and any dossier figure computed with the other form must say so. Until the professor confirms:
1. every data file, figure and report states which formula produced its ID and throughput;
2. data collection stores the raw target distance D, width W and movement time per trial, so that ID and throughput can be recomputed under either formula without re-running anyone (the trial schema already has `target_distance_m`, `target_width_m` and the raw movement time; its `fitts_id_bits` column is the provisional Shannon value);
3. no code is changed (`FittsMath` already implements the working decision); adding a formula label to the trial schema is a Phase 05 schema decision.

Related open points under D-02: nominal width W versus effective width We (effective width is commonly used in ISO 9241-9-style analyses; not checked against the standard's text here), and the minimum-MT safeguard (D-03: 0.1 s in the starter code, 0.4 s in the figure generator).

## 8. Analysis plan (PROPOSED; to be written down and dated before any data collection)

- Unit of analysis: one value per participant per condition (median MT; failure rate; throughput under the chosen formula), so trials are not treated as independent.
- Confirmatory comparisons (C1 vs C2, paired): MT, failure rate (and throughput once D-02 is settled). Test choice by distribution: paired t-test if the paired differences are approximately normal, otherwise Wilcoxon signed-rank; report the effect size with a confidence interval. SUS and NASA-TLX: same paired approach, descriptive first.
- Multiple outcomes: state in advance which are confirmatory and which exploratory, and the correction method used (UNRESOLVED).
- Order effect: report condition order (AB vs BA) and check it; with a small N this is descriptive.
- Trial exclusion rules, written in advance: technical failure (recorded with a reason), experimenter error, participant stopped; no exclusion because of a "bad" result. Missing data is reported, not filled in.
- Tools: `analysis/stats.py`, `analysis/analyze_trials.py` exist; their tests passed on synthetic data only (Phase 06).
- Reporting: the dossier §4 table needs N, mean, SD and median per measure and a plot per condition, regenerated from the raw CSV in the repository.

## 9. Data integrity rules (non-negotiable)

- Every file and row carries its origin: `Real`, `Selftest` or `Simulated` (already in the trial and gaze schemas). Only `Real` may be reported as participant data. In the code today the gaze event log refuses `Real` outright (until a participant link is designed), and the trial record accepts `Real` only with a pseudonym like P01; the voice CSV has no origin column at all (it carries no participant id), so its files must be kept separate by session and labelled by the experimenter. Whether to add an origin column to the voice CSV is a schema decision for Phase 05.
- `telemetry/SIMULATED_*.csv` and `telemetry/selftest/` are not evidence and must not be counted as participants (D-18). The dossier §4 requires a signed provenance declaration: every Section 4 number comes from real people who tested the team's build; none from sample, scaffold or generated files (`sample_experiment_data.csv`, `*_benchmark.csv`, `generate_paper_figures.py`); every participant signed a consent form and the forms are held; the plots regenerate from the raw CSV in the repository. The study must be able to make each of those statements truthfully.
- The voice CSVs recorded so far came from the developers' own test sessions; they are evidence about the **software** (reliability, latency range), never participant data. They carry no participant ID, are not counted in N, and are not reported as study results.
- Participant IDs (P01, P02, ...) are issued only to real participants, in enrolment order, after the ethics requirements are confirmed. Team members who test or pilot the setup get no P-number; their data is labelled `Selftest`.
- Raw data location (PROPOSED, nothing created): a dedicated folder for real session files, separate from `telemetry/selftest/` and the `SIMULATED_*` files; the dossier asks for the raw CSV path in the repository, so the path must be fixed before data collection (UNRESOLVED).
- Placeholder or invented values in the manuscript blueprint and the guide (section 1) are labelled as such or removed before any paper text is written.

## 10. Pilot plan (before any real participant)

Two or three team members run the full procedure, with data labelled `Selftest` and no P-numbers. Goals: time the session, find confusing instructions, check every file is written, check the microphone and simulator setup on the lab machine, check that the counterbalancing sheet works, set the trial time limit and trial counts, get a first effect-size estimate for the sample-size justification, and rehearse the stop/rest rules. Fix, then freeze the protocol (v1.0). Pilot data is never merged with the study data.

## 11. Decisions: status, and what is still needed from the professor

Statuses are defined at the top of this file. **No row is CONFIRMED.** The same rows are in `docs/DECISION_REGISTER.md` (§1 and §1a).

| ID | Decision | Current position | Status |
|---|---|---|---|
| D-02 | Fitts formula | **WORKING DECISION (owner, 2026-10-25): Shannon `ID = log2(D/W + 1)`** (guide §1.1, code); the dossier states `log2(2D/W)` (§2; §1.1 flagged "verify"); conflict documented in 7.2 | WORKING DECISION; **conflict, professor confirmation PENDING** |
| D-03 | Minimum movement-time safeguard | keep raw MT always; apply the 0.1 s floor only for throughput | UNRESOLVED (suggestion) |
| D-04 | Conditions and design | C1 controller vs C2 gaze+voice; every participant does both; AB/BA order | PROPOSED |
| D-05 | Participants, sample size, ethics route | mostly able-bodied volunteers plus participants with motor impairment where available; dossier §4: minimum N ≥ 10, recommends 16–20; sample size and ethics nothing assumed | population: WORKING DECISION (2026-10-25); **professor approval and ethics PENDING** |
| D-05a | Research-question framing for a mostly able-bodied sample with a few motor-impaired participants | accessibility framing kept, every claim limited to the sample, motor-impaired results separate and descriptive (section 1) | WORKING DECISION (2026-10-25); **PENDING PROFESSOR APPROVAL** |
| D-06 | Gaze-dwell condition | not included unless the professor requires it | PROPOSED (professor may overrule) |
| D-08 | WER required? | report command accuracy for prompted phrases; WER only if free speech is added | UNRESOLVED (suggestion) |
| D-09 | Speech route (offline keyword recogniser vs cloud) | keep the offline keyword recogniser (the dossier's toolchain line allows Wit.ai or KeywordRecognizer / DictationRecognizer) | UNRESOLVED (suggestion) |
| D-11 | Latency definition | recognizer-reported plus a stated limitation; the "< 350 ms" target is end-to-end in the guide and is not demonstrated | UNRESOLVED (suggestion) |
| Q-01 | Primary outcome(s) | co-primary: task completion latency and failure rate (the authorised title's variables); throughput, command latency, SUS, NASA-TLX secondary | UNRESOLVED (suggestion) |
| Q-02 | T2 (lab docking) in or out, and how it is scored | kept, provisional; nothing more built until confirmed | PROPOSED, provisional |
| Q-03 | Trials per condition, practice trials, time limit | 9 D×W combinations × 3 repetitions = 27 measured + 9 practice per condition; limit to be set from the pilot | UNRESOLVED (suggestion) |
| Q-04 | Seated vs standing, simulator vs headset | seated; Meta XR Simulator for development; a headset for the study only if one is available (none today) | UNRESOLVED (suggestion) |
| Q-05 | Compensation and recruitment route | none | UNRESOLVED |
| Q-06 | Participant identity | pseudonymous IDs P01, P02, ...; team test sessions are never participant data | PROPOSED (enforced in code) |
| Q-07 | Sample size and ethics requirements | N = 10 (owner, 2026-10-26); ethics not assumed | sample size: WORKING DECISION; **professor approval and ethics PENDING** |

## 12. Phase 04 completion status

**The Phase 04 documents are complete; Phase 04 is BLOCKED on external approvals and is not closed.** Done: research question and outcomes, hypotheses, conditions, tasks with parameters and trial counts, design and counterbalancing, participant and consent requirements (as a checklist), instruments, the Fitts formula conflict (documented, not resolved), analysis plan, data integrity and provenance rules, pilot plan, and the decision register entries. Not done because only the professor can do it: every row marked PENDING PROFESSOR APPROVAL above.

**Supporting materials drafted 2026-10-24** (`docs/phase04_materials/`, all marked DRAFT, none approved): participant information sheet, consent form, background questionnaire, session script and checklists (including the pilot checklist), the counterbalancing sheet (20 enrolment slots), and the analysis plan. These cover the practical content of exit criteria 2, 4 and 5 below; they still need the professor's decisions, the ethics wording (`[TO BE FILLED]` markers) and approval before use. No participant, trial or result exists.

Exit criteria for closing Phase 04:
1. Every decision in section 11 is CONFIRMED by the professor (not merely proposed by us) and recorded in `docs/DECISION_REGISTER.md` with the date — in particular D-02 (formula conflict), D-05/D-05a (population and the accessibility question), Q-02 (is T2 needed) and Q-07 (sample size and ethics).
2. Protocol v1.0 written from this draft with the chosen values, and the consent form, information sheet and questionnaire set drafted.
3. Ethics approval or a written exemption obtained (or, if the study is only a software demonstration, a written statement that no participant study will be run).
4. Counterbalancing sheet and the analysis plan written and dated before any data collection.
5. Pilot plan agreed (the pilot itself belongs to Phase 05–06 once the trial manager exists).

**Status update 2026-10-25:** the project owner instructed that the Phase 04 documents be treated as complete for engineering purposes and that Phase 05 begin (`docs/ROADMAP_PHASE05_LIVE_TRIALS.md`). Phase 04 itself is **not** closed: its exit criteria still need the professor's decisions (D-02, D-05/D-05a, Q-02, Q-07) and the ethics route. Phase 05 is therefore provisional, records only `Selftest` data, and refuses `Real` data until the professor approves.

## 13. What this phase did not do

No code, scene, setting, telemetry file or participant data was changed or created for Phase 04. No approval is claimed: every item is labelled PROPOSED, PENDING PROFESSOR APPROVAL or UNRESOLVED. The manuscript blueprint, the guide, the dossier and every research result file were left untouched. Phase 05 (live trial manager and Fitts' telemetry) has not been started.

## Revision history

- v0.1 (2026-10-23): first draft.
- v0.2 (2026-10-23): decision status labels added, owner's seven positions recorded, accessibility framing and able-bodied population marked pending, T2 provisional, formula conflict flagged.
- v0.3 (2026-10-23): dossier checked directly (D-02 locations, §4 sample-size and provenance requirements); research question tied to the authorised title; outcomes and hypotheses defined; trial counts, analysis plan and consent checklist added; `P-01`..`P-07` renamed `Q-01`..`Q-07` to avoid confusion with participant IDs; register updated to match.
- v0.4 (2026-10-25): the project owner's working decisions recorded: Shannon formula; mostly able-bodied participants plus any motor-impaired volunteers (separate, descriptive reporting). New label WORKING DECISION (not an approval). Ethics considerations for motor-impaired participants added.
