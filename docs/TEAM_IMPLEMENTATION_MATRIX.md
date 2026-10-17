# VoiceVR — Team Implementation Matrix

**Team:** Sakshi Sharma (A057) · Aryan Kanungo (I077) · **Date:** 2026-10-17 · **Branch:** `dev/group-05-voicevr` (HEAD `3eda462`)

This matrix **does not assign work to either member**. It records (1) what the professor's documents assign, (2) what exists, and (3) what is unconfirmed. Contribution and authorship tables (dossier §2.1, §3.2) must be completed by the team from real effort and real Git history.

## 1. What the evidence does and does not show about authorship

| Evidence | Finding |
|---|---|
| Git history | One commit (`3eda462`, professor's initial source). **No per-member commit history exists**, so ownership cannot be inferred from `git log`. |
| `docs/TEAM_ROSTER.json` | Roles: Sakshi = "XR Systems Architect & OpenXR Gaze Binding Lead"; Aryan = "Spatial NLP Engine & Ergonomic Telemetry Lead". `github_username` fields are empty. |
| Starter-script TODO tags | Sakshi: gaze raycast, outline highlight, soft magnetic snap, procedural Fitts array. Aryan: phoneme/intent classification, vocabulary parser, Fitts ID/TP. |
| `RESEARCH_AND_IMPLEMENTATION_GUIDE.md` §2 table | Assigns `FittsTargetTelemetryLogger.cs` to **Sakshi** and `SpatialVoiceIntentController.cs` to **Aryan** — while §2.2 and the README give the Fitts ID/TP logger to **Aryan**. **Conflict, unresolved.** |
| Work done in this repository session | Written by Claude Code at the user's direction, as the team's AI-assisted implementation. It is **not** evidence that either member authored a module. Dossier §5.1/§6 ask for honest AI-use disclosure; this belongs there. |

## 2. Responsibility matrix

`Original (source)` = what the professor's file assigns. `Status` uses the project vocabulary; only evidence cited in `REQUIREMENTS_TRACEABILITY.md` counts.

| Module | Original assignment (source) | Current implementation | Missing | Interfaces / dependencies | Verification | Confirmed owner |
|---|---|---|---|---|---|---|
| XR workspace, OpenXR scene | Sakshi (guide §2.1) | Desktop scene `VoiceVR_Main.unity` | XR rig, packages, OpenXR | Unity XR packages (gate G2) | Simulator run: NOT TESTED | UNCONFIRMED |
| ISO 9241-9 Fitts target array | Sakshi (guide §2.1; logger TODO) | None (3 hand-placed targets) | Generator, counts, scene wiring | `FittsMath`, `TrialRecord` | NOT TESTED | UNCONFIRMED |
| Visual cues: reticle, outline, snapping | Sakshi (guide §2.1; controller TODO) | Tint, label, reticle | Snapping, outline shader | `SpatialTarget`, `GazeFocusSystem` | Visual: NOT TESTED | UNCONFIRMED |
| Gaze raycast / deictic binding | Sakshi (controller TODO) | `GazeFocusSystem`, `GazeInteractionModel` | Eye tracking (not applicable on simulator) | Camera, `SpatialTarget` | Automated PASS; Play Mode NOT TESTED | UNCONFIRMED |
| Speech capture | Aryan (guide §2.2) | `DesktopVoiceInput` (Windows `KeywordRecognizer`) | Real-mic test; Quest path | Windows speech; `VoiceIntentParser` | NOT TESTED | UNCONFIRMED |
| Intent parser / vocabulary | Aryan (guide §2.2) | `VoiceIntentParser` | Vocabulary sign-off | `GazeInteractionModel` | Automated PASS | UNCONFIRMED |
| Fitts ID/TP + CSV logger | Aryan (README, guide §2.2) **vs** Sakshi (guide §2 table) | `FittsMath`, `TrialRecord`, `TrialCsvWriter`, `SessionCsvFile`, `GazeEventLog`; starter logger unchanged | Live trial loop; 90 Hz | Schema (proposed) | Automated PASS | **CONFLICT** |
| Statistics (t-test, d, Wilcoxon) | Aryan (guide §2.2) | `analysis/stats.py`, `analyze_trials.py` | Variant confirmation | Trial CSV schema | Automated PASS (fixtures only) | UNCONFIRMED |
| WER | Dossier §4 (no owner) | `analysis/wer.py` | Real transcripts | — | Automated PASS | UNCONFIRMED |
| Python ↔ Unity voice transport | None in any professor source | Nothing exists | Entire module (decision D-14) | — | — | UNCONFIRMED |
| Telemetry safety (no overwrite, provenance) | Dossier §4 data integrity (shared) | `SessionCsvFile`, `data_origin`, analyzer guard | — | — | Automated PASS | UNCONFIRMED |
| Dossier / video / release | Shared (dossier) | Not started | All | — | — | Shared |

## 3. Shared interface contract (as implemented)

| Interface | Definition | Where |
|---|---|---|
| Intents | `Select, Move, Release, Cancel, Reset, Focus, Help` | `Assets/Scripts/Voice/VoiceIntentParser.cs` |
| Parse result | `Ok / Empty / Unknown / Negated / Ambiguous`; only `Ok` is actionable | same |
| Input modality | `Voice, KeyboardFallback, TextFallback, Controller` | same |
| Command result | `Executed / Rejected / Cancelled / NotImplemented` + reason code | `GazeInteractionModel.cs` |
| Trial row | 25-column proposed schema (PENDING CONFIRMATION) | `TrialRecord.cs`; read by `analysis/analyze_trials.py` (header cross-checked in E5) |
| Gaze event row | 12-column proposed schema (PENDING CONFIRMATION) | `GazeEventLog.cs` |
| Target identity | Object carrying `SpatialTarget`; spoken name = `displayName` | `SpatialTarget.cs` |

Both team members' modules are meant to integrate through these types. No module depends on a transport that does not yet exist.

## 4. To resolve before the dossier is filled

1. Who wrote or will defend each module (honest authorship table, dossier §2.1).
2. The logger-ownership conflict (guide §2 table vs §2.2/README).
3. Each member's real GitHub username and commit history.
