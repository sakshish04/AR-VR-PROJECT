# Roadmap Phase 05 — Live Trial Manager and Fitts' Law Telemetry

**Status: STARTED 2026-10-25. Core implemented and tested; runtime checks with a real controller and real voice still pending. PROVISIONAL: it rests on the Phase 04 protocol draft, which the professor has not approved.**

Why work started before Phase 04 was signed off: on 2026-10-25 the project owner instructed that the Phase 04 documents be treated as complete for engineering purposes and that Phase 05 begin. That does **not** close Phase 04 (its exit criteria need the professor's decisions and ethics clearance, see `ROADMAP_PHASE04_RESEARCH_PROTOCOL.md` §12). Everything built here is therefore labelled **Selftest** and cannot record participant data until the professor approves (see "Data guard").

Step-by-step instructions for running a session: `docs/PHASE05_RUNBOOK.md`. Phase 05 is **implemented**; it is only **closed** once the team has completed the real runs and checks listed under "Not done / not verified".

## What exists now

| Part | File | Tested how |
|---|---|---|
| Trial plan: 3 distances × 3 widths, 1 practice block + 3 measured blocks in seeded random order, each target exactly D from the previous one | `Assets/Scripts/Trials/FittsTrialPlan.cs` | standalone tests (counts, block contents, amplitude, bounds, determinism by seed) |
| Trial state machine and trial-record factory, session sidecar text | `Assets/Scripts/Trials/TrialEngine.cs` | standalone tests (success, wrong target, timeout, miss errors, abandon, record validity, separate practice file) |
| Live trial manager (Unity): spawns the goal and previous markers, reads selections, writes rows, HUD, keys | `Assets/Scripts/Trials/LiveTrialManager.cs` | Edit-Mode run in the real scene: full session with timeouts (9 practice + 9 measured rows written, sidecar, no leftover objects); gaze+voice path: a selected goal recorded as `success`, a selected previous marker as `wrong_target` with `error_count` 1 |
| Scene setup menu | `Assets/Editor/VoiceVRLiveTrialSetup.cs` (**Group05 > VoiceVR > Add Live Trial Manager**) | run once; the manager is in the scene |
| Observer hook on the gaze system (additive) | `GazeFocusSystem.CommandHandled` event | exercised by the test above |
| Headset panel row | `XrHudPanel` shows the trial status while a session runs | not yet seen on screen |

Standalone suite: **424 passed, 0 failed** (45 more than before Phase 05). External compile check: 40 files, 3 of 3 configurations.

**Change 2026-10-26 (project owner's request): the targets are larger and selection is easier.** Widths are now 0.45 / 0.32 / 0.22 m (were 0.30 / 0.15 / 0.08, which the protocol draft §Q-02 lists; the 0.08 m target was too small to hit comfortably). In the controller condition any of trigger, grip, A/B/X/Y selects; in Selftest, Space or a left mouse click also select. This changes the D×W table of the Phase 04 draft and needs the professor's confirmation (new open item); IDs are computed from the actual D and W, so nothing else changes.

## How a session works

1. Press **F5** (desktop Game view, not typing): a session starts after 2 s. **F6** aborts, **F7** switches condition while idle.
2. Practice: 9 trials (one per distance × width), then a pause, then 27 measured trials (3 blocks of the 9 combinations in random order). The seed is chosen from the clock if left at 0 and is always written to the session file.
3. Each trial shows a bright **goal** sphere (diameter = W) exactly D to the left or right of a dim **previous** marker. The first target of a file is measured from a home marker at the centre. Targets stay within ±2 m of the centre line, 2.5 m in front of the participant at eye height.
4. **Controller condition:** press the trigger while pointing at the goal (headset or simulator). Microphone is switched off. *Desk rehearsal only, Selftest only:* a left mouse click stands in for the controller and the row says `mouse_surrogate` in `command_text`.
5. **Gaze + voice condition:** look at the goal and say "select that". The microphone is switched on. Every Select result from the existing command system is one selection attempt.
6. A trial ends: **success** (goal selected), **failure `wrong_target`** (the previous marker or another target selected; counts one error), **failure `timeout`** (no accepted selection within the limit), or **cancelled** (abandoned). A selection attempt that hits nothing, or that the system rejects (no focus, gaze held too briefly), adds one error and the trial **continues**.
7. Movement time = from the moment the goal appears to the accepted selection (the limit for a timeout). It is stored raw; the existing record computes ID, throughput and the minimum-time safeguard exactly as before.

## Output (existing schemas, unchanged)

- `telemetry/selftest/trials_selftest_<session>.csv` — measured trials, the existing 25-column `TrialRecord` schema.
- `telemetry/selftest/trials_selftest_<session>-practice.csv` — practice trials, **never analysed**.
- `telemetry/selftest/session_info_<session>.txt` — formula, seed, task parameters, recognizer route, protocol status, start and finish time, summary.
- The voice CSV and the gaze event log keep working as before; join them to trials by the start and end timestamps.
- `fitts_id_bits` and throughput in the trial file use the **Shannon** form (working decision); the formula is named in the session file. Raw D, W and movement time are always present, so either formula can be applied later (decision D-02).

## Data guard (protocol §9)

- Default data origin is **Selftest**; the participant field is `SELFTEST`.
- Data origin **Real** is refused unless `Real Data Enabled` is ticked on the manager (it must stay off until the professor has approved the protocol and the ethics route, D-05 / Q-07) **and** a code like `P01` is entered; the record validation also rejects anything that is not a pseudonym. Real runs write to `telemetry/real/`, not `selftest/`. Tested: Real is refused while disabled.
- In Real mode only spoken commands count as selections; typed or keyboard stand-ins are ignored.

## Decisions this depends on (all still open or provisional)

- Trial counts and the 15 s time limit: **UNRESOLVED**, PROVISIONAL (Q-03); the error rules in step 6 are PROPOSED and need the pilot (what counts as an error and a timeout).
- Fitts formula: WORKING DECISION (Shannon), professor confirmation pending (D-02).
- Sample size, ethics, participant mix: pending the professor (D-05, D-05a, Q-07).
- Docking task as a measured task: not built into trials (Q-02, provisional).

## Not done / not verified

- Controller path on a real controller or the simulator's controller (the trigger and pose reading is written but only the mouse and gaze paths were exercised in Edit Mode).
- A real spoken session end to end (the gaze+voice path was driven with typed commands in Edit Mode, so rows show `TextFallback`).
- The HUD and the headset panel row on screen; Play-Mode behaviour.
- A pilot (team members, `Selftest`) to set the time limit, trial counts and error rules; the pilot checklist is in `docs/phase04_materials/04_session_script_and_checklist_DRAFT.md`.
- A `fitts_formula` column in the trial schema (a schema decision; the session file carries it for now).
- Linking voice CSV rows to trial ids: done after the session with `telemetry/link_voice_to_trials.py <trials.csv> <voice_telemetry.csv>` (timestamp window join, checked on synthetic rows only; not yet run on a real session).
- Phase 06 (analysis on real data) has not started.
