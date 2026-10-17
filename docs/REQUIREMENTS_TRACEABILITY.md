# VoiceVR — Master Requirements Traceability Matrix

**Project:** Voice-Driven Spatial NLP for Accessible Virtual Reality (IVRAR Group 05) · **Team:** Sakshi Sharma (A057), Aryan Kanungo (I077)
**Repository:** `D:\arvr\Group_05_Sakshi_Aryan` · **Branch:** `dev/group-05-voicevr` · **HEAD:** `3eda462` · **Unity:** `6000.3.23f1` · **Date:** 2026-10-17

Statuses: `PASS` (tested, evidence cited) · `FAIL` · `BLOCKED` (identified prerequisite/approval) · `NOT TESTED` · `NOT APPLICABLE`.
Where a row has both an automated result and a visual/runtime result they are shown separately; **automated results are never used as visual or device evidence.**

**Source classes.** *Professor* = a requirement in a professor-supplied file. *Team* = stated by the team (the user) in this project, **not** found in any professor document. *Eng* = my engineering recommendation. Only *Professor* rows are course requirements.

Evidence references (all re-run 2026-10-17 unless noted):
- **E1** `Tests\Standalone\run_standalone_tests.ps1` → 161 passed, 0 failed, exit 0 (pure C#; **not** Unity tests).
- **E2** `Tests\Standalone\compile_check_unity_scripts.ps1` → 15 files, 3 configurations, exit 0 (external Roslyn compile; **not** the Editor's compile).
- **E3** Unity batch compile: `Tundra build success`, no `error CS` (log `scratchpad\unity_build_batch.log`, 19:43; second batch 19:50).
- **E4** Unity batch edit-mode self-tests: 26 passed / 0 failed / 0 blocked of 26 expected, exit 0 (`telemetry/selftest/unity_gaze_selftest_20261017T142019Z.txt`). Edit mode, no rendering, no input.
- **E5** `python -m unittest discover -s analysis` → 34 passed, exit 0 (`analysis/`; scipy cross-check).
- **E6** `python telemetry/test_evaluation_tools.py` → `[SUCCESS]`, exit 0 (professor's own psychometric calculators; runs built-in demo data).

## 1. Sources inspected / not available

Inspected in full: `README.md`, `RESEARCH_AND_IMPLEMENTATION_GUIDE.md`, `docs/README.md`, `docs/LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md`, `docs/RESEARCH_PAPER_MANUSCRIPT_BLUEPRINT.md`, `docs/TEAM_ROSTER.json`, `docs/toolchain_spec.md`, `docs/figures/README.md`, both starter scripts, `Assets/index.html`, all `telemetry/*.py` and `*.csv`, the dossier text (`docs/IVRAR_Group_05_Technical_Dossier.docx`, extracted from XML: no layout/images).

**Not available (requirements depending on them cannot be confirmed):** the four `docs/figures/*.png` are Git LFS pointer files (objects not fetched); `IVRAR_LinkedIn_and_GitHub_Showcase_Guide.pdf` (referenced by `docs/README.md`, outside the repo); the dossier "title sheet"; any rubric / mark chart (the dossier header gives only the split SBL 10 / DBL 5 / Showcase+Viva 5 = 20); `telemetry/assistive_vr_economics.py`, `VoiceAccessibilityLogger.cs` and the "automated compliance audit script" (referenced, absent).

## 2. Course deliverables (Professor)

| ID | Requirement | Source | Owner | Existing implementation | Missing | Acceptance | Test | Evidence | Status |
|---|---|---|---|---|---|---|---|---|---|
| DEL-01 | Fill all 6 dossier sections, export PDF, commit `.docx` + `.pdf` to `docs/` | `docs/README.md:3`; dossier "Submit" | Shared (UNCONFIRMED split) | Template only | All content; PDF | Both files committed | Manual | — | NOT TESTED (not started) |
| DEL-02 | PDF turned in on MS Teams by **every** member by Mon 26-Oct-2026 23:59 IST | `docs/README.md:12`; dossier footer | Each member | — | Submission | Teams receipt | Manual | — | NOT TESTED |
| DEL-03 | LinkedIn video 60–90 s, every member appears and speaks; URL in dossier §5 and `TEAM_ROSTER.json` `linkedin_url` | README "Project Demonstration"; roster `project_showcase`; dossier §5 | Shared | `linkedin_url = PENDING_SUBMISSION` | Recording, post | Public URL recorded | Manual | — | NOT TESTED |
| DEL-04 | Release tag `group-05-v1.0`; Quest `.apk` **or** PCVR `.exe`; build link; installs on clean device; no build in Git history | Dossier §3 | Shared | `.gitignore` blocks `*.apk/*.aab/Build(s)/` | A build, tag, link | Clean-device install | Manual | — | BLOCKED (no XR packages/approval; no build attempted) |
| DEL-05 | Git LFS tracks binary types; `git lfs ls-files` lists them | Dossier §3 | Shared | Figure PNGs are LFS **pointers**; no `.gitattributes` here | LFS setup | `git lfs ls-files` output | Manual | — | BLOCKED (LFS deferred by decision) |
| DEL-06 | Real human trials N ≥ 10 (16–20 recommended), voluntary adults, written consent, anonymised IDs, raw CSV in repo | Dossier §4; `docs/figures/README.md` | Shared | Tooling only (`TrialRecord`, `TrialCsvWriter`, `analysis/`) | Participants, consent, ethics, trials | CSV of real sessions | Manual | — | BLOCKED (recruitment feasibility unknown) |
| DEL-07 | SUS (10 items, mean target ≥ 68) + NASA-TLX (or latency profile); SSQ if headset | Dossier §4 | Shared | Professor calculators run on built-in demo data only (E6) | Real responses; CSV input path | Per-participant scores | Manual | E6 (tooling only) | NOT TESTED |
| DEL-08 | Data-provenance declaration; no simulated numbers as human data | Dossier §4; `generate_paper_figures.py:1-7` | Shared | `data_origin` field + analyzer refusal of non-real data (E5) | Team declaration | 4 provenance boxes ticked truthfully | Review | E5 | PASS (tooling guard) / NOT TESTED (declaration) |
| DEL-09 | Authorship table, ≤25-line key snippet, derivation + worked example | Dossier §2.1–2.3 | Shared | — | Entries | Filled table | Manual | — | NOT TESTED |
| DEL-10 | Claim-verification table (XAUR, Fitts formula, < 350 ms) with primary sources | Dossier §1.1 | Shared | — | Sources recorded | Table filled | Manual | — | NOT TESTED |
| DEL-11 | Three AI-error audit entries | Dossier §5.1 | Shared | — | Entries | Filled | Manual | — | NOT TESTED |
| DEL-12 | Peer review of Group 06 and Group 14 videos | Dossier §5.2 | Each | — | Their videos | Scored | Manual | — | BLOCKED (external dependency) |
| DEL-13 | Individual learning record + sign-off | Dossier §6 | Each | — | Entries | Filled | Manual | — | NOT TESTED |
| DEL-14 | 4-page IEEE paper (Track A) | Dossier "Track A"; guide Sprint 4; manuscript blueprint | Shared | Blueprint with synthetic starter numbers | Real data, text | PDF | Manual | — | NOT TESTED |
| DEL-15 | Re-plot figures from real CSV; replace starter figures | `docs/figures/README.md` | Shared | Generator does **not** read any CSV; PNGs are LFS pointers | New plotting code, real data | Regenerable plots | Manual | — | BLOCKED (no real data; LFS) |
| DEL-16 | Tell Dr. Nanade by 20-Oct of any wrong name/ID/role | Dossier note | Each | — | Message | Sent | Manual | — | NOT TESTED |

## 3. Technical requirements (Professor)

| ID | Requirement | Source | Owner (per source) | Existing implementation | Missing | Acceptance | Test | Evidence | Status |
|---|---|---|---|---|---|---|---|---|---|
| TEC-01 | Accessible ergonomic workspace in Unity with OpenXR | Guide §2.1; README | Sakshi (guide §2.1) | `Assets/Scenes/VoiceVR_Main.unity` (desktop; no XR) | XR packages/config/rig | Runs in XR session | Simulator / device | E3 (scene builds) | BLOCKED (XR packages need approval) |
| TEC-02 | Procedural ISO 9241-9 target array, D ∈ [0.5, 2.0] m, W ∈ [0.08, 0.30] m | Guide §2.1; README; manuscript II.C | Sakshi (guide §2.1; logger TODO) | 3 hand-placed targets; no generator | Layout generator + scene wiring; target count undefined | Targets spawned within ranges | Play Mode | — | NOT TESTED (not implemented) |
| TEC-03 | Accessible cues: magnetic snapping, dynamic focus reticles, high-contrast outline shaders | Guide §2.1; controller TODO | Sakshi | Tint + `[FOCUSED]` label + reticle (`SpatialTarget`, `GazeFocusSystem`) | Snapping; outline shader | Visible cues | Play Mode | E4 (state only) | Visual: NOT TESTED; snapping/outline: NOT IMPLEMENTED |
| TEC-04 | Head/eye gaze raycast to bind deictic reference | Controller TODO; guide §1.2 | Sakshi (controller TODO) | `GazeFocusSystem` (valid-target-only, nearest-collider) | — | Correct target under gaze | Automated + Play Mode | E4 | PASS (automated, edit mode) / NOT TESTED (Play Mode, XR) |
| TEC-05 | Continuous microphone, sub-350 ms | Guide §2.2; README | Aryan | `DesktopVoiceInput` (Windows `KeywordRecognizer`, mic off by default) | Real-mic test; latency method | Measured latency | Real mic | E2 (compiles) | NOT TESTED |
| TEC-06 | Grammar parser → intent tuples ⟨Action, Target, Destination⟩ | Guide §2.2; manuscript II.B | Aryan | `VoiceIntentParser` | Vocabulary sign-off ("reset", "push", "shelf") | Correct intents; unsafe input rejected | Automated | E1 | PASS (automated) |
| TEC-07 | Act only above confidence 0.75 | Controller `confidenceThreshold`; manuscript | Aryan | Starter field only; `ParseScore` is **not** acoustic confidence | Defined recognizer confidence policy | Documented behaviour | Real mic | — | NOT TESTED / DEFINITION PENDING |
| TEC-08 | State machine Listening → Intent parsed → Gaze target confirmed → Action executed/Rejected | Dossier §2 | Shared | `GazeInteractionModel` + `DesktopVoiceInput` states | MOVE/RELEASE; real "Listening" end-to-end | Transitions logged | Automated + Play Mode | E1, E4 | PASS (automated, partial: MOVE/RELEASE NotImplemented) |
| TEC-09 | Logger computes ID/TP, 90 Hz CSV | Guide §2.2, Sprint 2; README; logger | Aryan (README/guide §2.2) / Sakshi (guide §2 table) — **conflict** | `FittsMath`, `TrialRecord`, `TrialCsvWriter` (proposed schema); starter logger unchanged | Wiring to a live trial loop; 90 Hz not verified | CSV with ID/TP | Automated | E1 | PASS (math/CSV, automated) / NOT TESTED (live trial, 90 Hz) |
| TEC-10 | `trial_<id>.csv`, ≥ 60 Hz; completion time, path deviation, error rate, collision events | `docs/toolchain_spec.md` | — | Different schema (`trials_<origin>_<session>.csv`) | Reconcile | — | — | — | UNRESOLVED (spec vs schema) |
| TEC-11 | Student's t-test, Cohen's d, Wilcoxon signed-rank | Guide §2.2 | Aryan | `analysis/stats.py`, `analysis/analyze_trials.py` | Variant confirmation (paired/d_z vs d_av) | Matches reference | Automated | E5 (scipy cross-check) | PASS (tool, on test fixtures only) |
| TEC-12 | Conditions: controller vs gaze+voice (dossier); manuscript lists gaze-dwell too | Dossier §2; manuscript Table I | Shared | Schema accepts `controller`, `gaze_voice` only | Protocol, order, trial counts | Documented protocol | Review | — | PENDING CONFIRMATION |
| TEC-13 | Speech latency < 350 ms (XAUR) | Guide §1.3; controller field | Aryan | `SpeechEndToEventMs` (recognizer-reported, unvalidated) | Defined measurement; real mic | Measured distribution | Real mic | — | NOT TESTED |
| TEC-14 | OpenXR ≥ 1.8.0, XRI ≥ 2.5.2/3.0.0, URP; Quest 2/3/Pro | `docs/toolchain_spec.md` ("Recommended") | Sakshi | None installed (manifest has only built-in modules) | All | Packages resolved | Static | — | BLOCKED (approval gate G2) |
| TEC-15 | Meta Voice SDK (Wit.ai) **or** Unity KeywordRecognizer/DictationRecognizer | Dossier §2 | Aryan | `KeywordRecognizer` (Windows only) | Quest path | Working recognizer | Real mic / device | — | NOT TESTED |
| TEC-16 | "Automated compliance audit script … zero defects" | Guide Sprint 4 | — | Not in repo; contents unspecified | Definition | — | — | — | BLOCKED (unspecified) |
| TEC-17 | WER = (S + D + I) / N as a primary metric | Dossier §2, §4 | Aryan | `analysis/wer.py` | Reference transcripts from real sessions | Matches hand-computed cases | Automated | E5 | PASS (tool) / NOT TESTED (no real transcripts) |
| TEC-18 | Fitts: Shannon ID = log₂(D/W+1), TP = ID/MT | Guide §1.1; manuscript II.C; logger (dossier prints `log₂(2D/W)`) | Aryan | `FittsMath` (+ explicit MT safeguard) | Professor confirmation of formula and safeguard | Unit tests | Automated | E1 | PASS (as implemented) / FORMULA PENDING CONFIRMATION |

## 4. Team-stated goals (not found in professor documents)

| ID | Goal | Source | Owner | Existing | Missing | Acceptance | Test | Evidence | Status |
|---|---|---|---|---|---|---|---|---|---|
| USR-01 | Head-pose gaze in the standalone Meta XR Simulator (no Quest) | User prompt | UNCONFIRMED | Simulator v207.0 installed + active OpenXR runtime (registry) | XR packages/config/rig in Unity | Head pose moves camera; focus works | Simulator | — | BLOCKED (gates G1–G6) |
| USR-02 | Left/right simulated controllers: ray, grab, release | User prompt | UNCONFIRMED | None | XR Origin, interactors, grabbables | Each controller grabs/releases | Simulator | — | BLOCKED (G2–G7) |
| USR-03 | Python ↔ Unity voice pipeline | User prompt (Python "part of architecture") | UNCONFIRMED | **None** (no transport/speech-service code) | Transport, router | End-to-end command | Mock + integration | — | BLOCKED (architecture decision D-14) |
| USR-04 | Desktop keyboard/mouse fallback preserved | User prompt | — | `DesktopVoiceInput`, `DesktopLookController` | — | Works after XR added | Play Mode | E4 (state only) | Visual: NOT TESTED |
| USR-05 | Mock transcription for repeatable tests | User prompt | — | Fallback keys/typed text (labelled SIMULATED) | Python mock sender | Deterministic runs | Automated | E1 | PARTIAL |
| USR-06 | Preserve scene/professor code/Build Settings | User prompt | — | Verified unchanged | — | Hashes match | Static | hash baseline | PASS |

## 5. Gaze / interaction acceptance criteria (Phase 3 report)

| ID | Criterion | Automated (E4, edit mode) | Play Mode (visual) |
|---|---|---|---|
| GZ-01 | Each valid target focuses correctly | PASS | NOT TESTED |
| GZ-02 | Looking away clears focus and restores appearance | PASS (state) | NOT TESTED (appearance) |
| GZ-03 | Floor, plain collider, empty space never targets | PASS | NOT TESTED |
| GZ-04 | SELECT rejected before hold time / on name mismatch / negated / ambiguous | PASS | NOT TESTED |
| GZ-05 | Explicit SELECT after held gaze selects; second SELECT rejected | PASS | NOT TESTED |
| GZ-06 | CANCEL clears selection; MOVE/RELEASE NotImplemented, nothing moves | PASS | NOT TESTED |
| GZ-07 | Destroyed target / missing camera handled | PASS | NOT TESTED |
| GZ-08 | Tint, `[FOCUSED]`/`[SELECTED]` labels, reticle, two panels render and are legible | — | NOT TESTED |
| GZ-09 | Keyboard 1–7, F8 text box, F9 toggle, look controls work in Play Mode | — | NOT TESTED |
| GZ-10 | Real microphone speech | — | NOT TESTED |
| GZ-11 | Headset / Quest | — | NOT TESTED (no hardware) |

## 6. Verification coverage by level

| Level | State |
|---|---|
| Static / source inspection | Done (this document) |
| Pure-C# standalone | E1 PASS |
| External compile | E2 PASS |
| Unity batch compile + edit-mode self-tests | E3, E4 PASS |
| Python analysis tooling | E5 PASS |
| Unity Play Mode, visual | NOT TESTED (user attempted manual test; no observations supplied yet) |
| Meta XR Simulator session | NOT TESTED |
| Real microphone | NOT TESTED |
| Physical headset | NOT TESTED (none available) |
