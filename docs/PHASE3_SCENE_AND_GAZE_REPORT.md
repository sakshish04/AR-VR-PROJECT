# Phase 3 Report — Scene and Gaze Loop (Group 05 VoiceVR)

**Team:** Sakshi Sharma (A057), Aryan Kanungo (I077) · **Branch:** `dev/group-05-voicevr` (base `3eda462`) · **Date:** 2026-10-17
**Builds on:** `docs/PHASE2_IMPLEMENTATION_REPORT.md` (not modified).

**Honest summary (updated 2026-10-17 after Unity batch verification):** the gaze-loop logic is implemented and **automated logic tests pass**: 161/161 standalone tests, and 26/26 Unity edit-mode self-tests run in Unity batch mode (0 failed, 0 blocked). The project compiled in Unity batch mode and the scene `Assets/Scenes/VoiceVR_Main.unity` was built and exists on disk. **Automated logic tests are not visual verification.** Nothing has been seen on screen: tint changes, text labels, reticle rendering, the two on-screen panels, Play Mode keyboard input, real microphone speech, and headset/Quest behaviour are all **unverified**. A person must do that manually (§5).

Attribution is unchanged: this report does not assign any file to either team member. Per-member contribution tables must be completed by the team from the real history.

---

## Actual results as of 2026-10-17

| Stage | Result | Evidence |
|---|---|---|
| Standalone unit tests (pure C#) | **PASS: 161 passed, 0 failed**, exit code 0 | `powershell -NoProfile -ExecutionPolicy Bypass -File Tests\Standalone\run_standalone_tests.ps1` (re-run after the self-test repair) |
| External C# compile (15 files, 3 configurations) | **PASS**, 0 failed | `Tests\Standalone\compile_check_unity_scripts.ps1` |
| **Unity compilation (batch mode)** | **Succeeded.** `*** Tundra build success`, no `error CS` lines; the Editor assembly compiled and loaded | `Unity.exe -batchmode -nographics -projectPath … -executeMethod …` runs below. First run: "0 items updated" (reused cache from the earlier interactive compile); self-test rerun: "3 items updated" (the edited self-test script was recompiled) |
| **Scene build (batch)** | **Succeeded, exit code 0** (10.9 s). Log: `[VoiceVRSceneBuilder] Saved Assets/Scenes/VoiceVR_Main.unity. Build settings were NOT changed.` | `-executeMethod Group05.VoiceVR.EditorTools.VoiceVRSceneBuilder.BuildBatch`. On disk: `VoiceVR_Main.unity` (31,335 B) + 6 materials. Build Settings unchanged (`m_Scenes: []`) |
| **Unity edit-mode self-tests, first attempt** | **Did not run (exit code 1).** `InvalidOperationException: Cannot create a new scene additively with an untitled scene unsaved.` thrown at `VoiceVRGazeSelfTests.cs:97`, before any check. 0 of 26 checks recorded; no results file | A defect in the self-test setup, not a failed check. Treated as BLOCKED/INCOMPLETE, not a pass |
| Repair | Scene setup moved inside the `try`; batch mode uses `NewSceneMode.Single`; a setup failure is recorded as BLOCKED (exit 3) | Only `Assets/Editor/VoiceVRGazeSelfTests.cs` was edited; the 26 checks and the Passed/Failed/Blocked accounting are unchanged |
| **Unity edit-mode self-tests, rerun** | **PASS: Passed 26, Failed 0, Blocked 0, recorded 26 of 26 expected. Exit code 0** | `-executeMethod Group05.VoiceVR.EditorTools.VoiceVRGazeSelfTests.RunBatch` (8.8 s). Results file: `telemetry/selftest/unity_gaze_selftest_20261017T142019Z.txt` (git-ignored). Log summary line: `Passed: 26 Failed: 0 Blocked: 0 (recorded 26 of 26 expected checks) | Verdict: ALL PASSED | exit code 0` |

### Automated logic tests versus visual verification

| Verified by automated tests | NOT verified (needs a person looking at the running scene) |
|---|---|
| Gaze at each of the three targets focuses the right one; switching leaves exactly one focused (by internal state) | That the **tint** is actually visible on screen, and distinguishable |
| The floor, a plain collider without `SpatialTarget`, and empty space are not targets | That the **`[FOCUSED]` / `[SELECTED]` text labels** render at all (the self-tests disable labels with `showLabel = false`; font `LegacyRuntime.ttf` is unproven in Unity 6.3) |
| Leaving a target restores its internal visual state | **Reticle rendering**: position, size, colour change |
| SELECT is rejected right after focus, on a name mismatch, when negated, when ambiguous; focus is unchanged by rejections | The **two on-screen `OnGUI` panels**: legibility, overlap, clipping at small Game-view sizes |
| An explicit SELECT after a held gaze selects the focused target; a second SELECT is rejected | **Play Mode keyboard input**: keys 1–7, F8 text box, F9 toggle, arrow-key and right-mouse look |
| MOVE and RELEASE return NotImplemented and change nothing; CANCEL clears the selection | The scene running without Console errors or missing references **in Play Mode** |
| Unknown commands, a destroyed target and a missing camera are handled without exceptions | **Real microphone speech** (Windows KeywordRecognizer), including mic permission and recognition behaviour |
| | **Headset / Quest behaviour** of any kind (no XR packages are installed) |
| | Any claim that this is an accessible interaction: the cue is a plain tint/label/reticle and is **not a validated accessibility feature** |

The scene file itself was inspected as text: it contains Main Camera (tag `MainCamera`), Directional Light, Floor, `Distractor_NotATarget`, the three targets, `GazeReticle` and `VoiceVR_Systems`; `GazeFocusSystem` has serialized references to the camera, reticle, `DesktopVoiceInput` and the professor's controller; three `SpatialTarget` components carry the names "red cube", "blue sphere", "green capsule". That shows the wiring exists, not that it behaves correctly on screen.

**Files produced by the batch runs (all untracked):** the scene, its `.meta`, 6 `.mat` + `.meta` files and two folder `.meta` files under `Assets/Scenes/`; the self-test results `.txt`; and `ProjectSettings/SceneTemplateSettings.json`, a default-valued file Unity generated by itself during the first batch run. No existing file was modified (checked by SHA-256 against a pre-run baseline). While in Play Mode the scene's `GazeFocusSystem` will also write a `gaze_events_selftest_<session>.csv` under git-ignored `telemetry/selftest/` (`writeEventLog` is on; origin `Selftest`).

---

## Correction notice (added after the Phase 3 integrity audit)

An earlier version of this report said the external compile check "includes `Assets/Editor` scripts" and compiled "13 source files". **That was inaccurate.** The original `compile_check_unity_scripts.ps1` collected sources with a glob over `Assets\Scripts` only, so it compiled **13** of the **15** `.cs` files and **never compiled** `Assets/Editor/VoiceVRSceneBuilder.cs` or `Assets/Editor/VoiceVRGazeSelfTests.cs`. Its Editor-assembly references were present, but no Editor source was ever passed to the compiler, and it judged success by compiler exit code alone, so the gap was invisible.

Three separate things are therefore reported, and must not be conflated:

| What was run | Covered | Result |
|---|---|---|
| **Original compile script** (before the audit) | 13 files in `Assets\Scripts` only; **no** Editor scripts | Exit code 0 in all 3 configurations. This says nothing about the Editor scripts |
| **Ad-hoc compile during the audit** (manual command, not a repo script, output in `%TEMP%`) | All 15 files under `Assets/`, warning level 4 | Windows Editor: exit 0, no warnings. Android: exit 0, one CS0649 warning. This was the first time the two Editor scripts were compiled anywhere |
| **Corrected compile script** (this revision) | An explicit allow-list of the 15 intended files; fails before compiling if a listed file is missing or an unlisted `.cs` appears in `Assets/Scripts` or `Assets/Editor`; prints the exact files compiled and each configuration's exit code; fails if no output dll is produced | Exit code 0: 15 files, 3 configurations, 0 failed (details in §4). Guard behaviour was checked on a throw-away temp copy: an extra file → exit 3, a missing file → exit 3, a real compile error → FAIL in all 3 configurations |

**Still true, unchanged:** this is an *external* Roslyn compile against Unity's assemblies. It is not the Unity Editor's own compile. Unity Editor compilation of the current scripts, running the Unity self-tests, building and running the scene, and the visual behaviour of focus/selection remain **unverified**. All pending professor confirmations (§9 and `PHASE2_IMPLEMENTATION_REPORT.md` §13) are unchanged.

---

## 1. Phase 3A — scene audit (read-only, done before any change)

| Question | Finding |
|---|---|
| Scenes | **None.** No `.unity`, `.prefab`, `.asmdef`, `.mat` or `.inputactions` asset under `Assets/`. `EditorBuildSettings.m_Scenes` is empty. The Editor shows an unsaved "Untitled" scene |
| Cameras / lights / colliders / targets | None (no scene) |
| XR origin, interaction manager, input components | None; no XR, Input System or XRI package in `Packages/manifest.json` (built-in modules only) |
| Input | Legacy Input Manager (`activeInputHandler: 0`) |
| Pipeline | Built-in RP (`m_CustomRenderPipeline: {fileID: 0}`) |
| Desktop run possible? | Yes in principle: primitives + Built-in RP + legacy input need no new package |
| Professor scene data at risk | None (no professor scene exists) |
| Automation | Unity is open (PID 18376); no Unity automation tool was available. Batch-mode runs are impossible while the Editor holds the project |

**Smallest safe change chosen:** one new scene file, built by an Editor menu command that refuses to overwrite, plus purely additive runtime scripts. No packages, no project settings, no build settings, no edit to any professor file.

## 2. What was implemented (3B)

### Runtime (additive)
| File | Role |
|---|---|
| `Assets/Scripts/Interaction/GazeInteractionModel.cs` | **Pure C#.** Focus, selection and command outcome as separate states; the confirmation rules |
| `Assets/Scripts/Interaction/GazeFocusSystem.cs` | Unity component: camera ray, valid-target check, focus cue, reticle, status panel, event log, optional bridge |
| `Assets/Scripts/Interaction/DesktopLookController.cs` | Desktop stand-in for head gaze (right-mouse drag / arrow keys); rotates only; off when an XR device is active |
| `Assets/Scripts/Telemetry/SessionCsvFile.cs` | **Pure C#.** Shared never-overwrite CSV file logic |
| `Assets/Scripts/Telemetry/GazeEventLog.cs` | **Pure C#.** Separate proposed gaze-event schema + writer |
| `Assets/Scripts/Telemetry/TrialCsvWriter.cs` | **Refactored** onto `SessionCsvFile`; public API, behaviour and the 25-column trial schema are unchanged (the existing writer tests still pass) |
| `Assets/Scripts/SpatialTarget.cs` | Added idempotent `EnsureInitialized()` (so targets work where `Awake` has not run) |

### Editor-only (additive)
| File | Role |
|---|---|
| `Assets/Editor/VoiceVRSceneBuilder.cs` | Menu **Group05 › VoiceVR › Build Main Scene** creates `Assets/Scenes/VoiceVR_Main.unity` (+ 6 materials under `Assets/Scenes/VoiceVR_Materials/`). Proceeds **only if every indicator agrees the destination is free**: `AssetDatabase.LoadAssetAtPath<SceneAsset>`, `AssetDatabase.AssetPathToGUID`, `File.Exists` for the scene and its `.meta`, and `Directory.Exists`. If any indicator fires, or they disagree, it stops without writing anything (the check is repeated inside `Build()` so no caller can bypass it; batch exit code 2 = refused). Does not touch build/project settings or packages. **Run once in batch mode: built the scene, exit code 0** (see "Actual results"). The overwrite refusal path itself has not been exercised |
| `Assets/Editor/VoiceVRGazeSelfTests.cs` | Menu **Run Gaze Self-Tests** / batch `RunBatch`. Edit-mode tests in a throwaway additive scene; writes a unique results file under git-ignored `telemetry/selftest/`. Passed, failed and blocked checks are counted separately and all appear in the console, menu dialog and results file. **Exit code 0 only when all 26 expected checks ran and passed**; 1 = a check failed; 3 = blocked or incomplete (never shown as "All passed"). **Run in batch mode: 26/26 passed, exit code 0** (after the setup repair described in "Actual results") |

### Scene contents (built in batch mode; file inspected, not yet viewed)
Main Camera at (0, 1.6, 0) facing +Z with `DesktopLookController`; directional light; `Floor` (collider, **no** `SpatialTarget`); `Distractor_NotATarget` (solid collider, **no** `SpatialTarget`); three targets differing in shape and colour — **red cube**, **blue sphere**, **green capsule** — each with `SpatialTarget`; a collider-less `GazeReticle`; a `VoiceVR_Systems` object holding `DesktopVoiceInput`, `GazeFocusSystem` and the professor's `SpatialVoiceIntentController` (unmodified). The professor's `FittsTargetTelemetryLogger` is **deliberately not in the scene** because on `Start` it opens its CSV with overwrite semantics.

### Behaviour (decision logic verified by automated tests; on-screen rendering NOT yet seen)
1. The ray starts at the viewing camera (`gazeCamera`, else `Camera.main`). No camera → warning logged once, focus empty, no exception.
2. The **nearest** collider decides. Only an object with `SpatialTarget` (found with `GetComponentInParent`) is a valid target; floor, walls and the distractor are not. A wall in front of a target blocks it.
3. Focus tints the target, adds a `[FOCUSED]` text label (non-colour cue), and colours the reticle. Leaving restores the previous appearance; only one target is focused at a time. This is a plain cue and **not a validated accessibility feature**.
4. **Focus never executes anything.**
5. Selection requires an explicit utterance that parsed as an actionable **SELECT** (not negated, not ambiguous), a valid focused target held for at least `minFocusSeconds` (0.3 s, PROVISIONAL), nothing already selected, and a spoken target name (if any) matching the focused target. Otherwise it is rejected with a reason (`no_focus`, `focus_too_recent`, `target_mismatch`, `already_selected`, `parse_negated`, `parse_ambiguous`, `parse_unknown`, `parse_empty`).
6. `CANCEL`/`RESET` clear the selection. `FOCUS` reports the focus. `HELP` shows text.
7. **`MOVE` and `RELEASE` return `NotImplemented` and change nothing.** No snapping, no object movement, no release behaviour, no XR controller handling.
8. Destroyed focused/selected targets are detected via Unity's null check and cleared with a logged `target_lost` event.
9. Focus, selection and command outcome are separate fields; the input modality (`Voice`, `KeyboardFallback`, `TextFallback`) is shown in the panel and logged.
10. Events go to a **separate** log `gaze_events_<origin>_<session>.csv` (default `telemetry/selftest/`, git-ignored; never overwrites, `_2`, `_3` on collision). The canonical trial schema is unchanged. `Real` origin is refused by the event schema (no participant link exists yet).
11. Optional bridge: `GazeFocusSystem` writes **only valid focus** into the professor controller's public `focusedSpatialObject` in `LateUpdate`. The professor's file is untouched; see §6 for the proper diff proposal.
12. No microphone audio is recorded here; no participant identifier is handled.

## 3. Proposed event schema (PENDING CONFIRMATION)
`schema_version, data_origin, session_id, event_index, realtime_s, utc, event_type, target_name, input_modality, intent, outcome, reason`
`event_type` ∈ `focus_enter | focus_exit | command | target_lost | camera_missing`. If the team wants gaze events inside the trial file, that is a schema change and needs approval; none was made.

## 4. Test results (3C)

| Category | Command | Result |
|---|---|---|
| **Standalone unit tests** (pure C#) | `powershell -NoProfile -ExecutionPolicy Bypass -File Tests\Standalone\run_standalone_tests.ps1` | **PASS — 161 passed, 0 failed, exit code 0** (up from 123; +38 covering the model, rejections, event log) |
| **External C# compile** against Unity's managed assemblies (Roslyn, C# 9) — *original script, superseded* | `... Tests\Standalone\compile_check_unity_scripts.ps1` (before the audit) | Exit 0 in 3 configurations, but covered **13 files in `Assets\Scripts` only**; the Editor scripts were **not** compiled (see the correction notice) |
| **External C# compile — corrected script** | `powershell -NoProfile -ExecutionPolicy Bypass -File Tests\Standalone\compile_check_unity_scripts.ps1` | **PASS — 15 files** (13 runtime + 2 `Assets/Editor`, including both unmodified professor scripts), explicit allow-list validated. Windows Editor/Standalone: exit 0. Android with legacy input: exit 0. Android without legacy input: exit 0. Output dll produced in each. Only CS0649 unassigned-field warnings (Windows-only fields) in the Android configurations. Script exit code 0, 0 configurations failed |
| **Unity compilation (batch mode)** *(supersedes the earlier "not verified" row)* | `Unity.exe -batchmode -nographics -projectPath D:\arvr\Group_05_Sakshi_Aryan -executeMethod … -quit` | **PASS.** `Tundra build success`, no `error CS`, assemblies reloaded, both `executeMethod` entry points ran |
| **Unity scene build (batch)** | `… -executeMethod Group05.VoiceVR.EditorTools.VoiceVRSceneBuilder.BuildBatch -quit` | **PASS, exit code 0.** Scene + 6 materials on disk; Build Settings unchanged |
| **Unity edit-mode self-tests (batch), first attempt** | `… -executeMethod Group05.VoiceVR.EditorTools.VoiceVRGazeSelfTests.RunBatch -quit` | **Not a pass: exit code 1, 0/26 checks recorded** (setup exception, described above). Reported as BLOCKED/INCOMPLETE |
| **Unity edit-mode self-tests (batch), after repair** | same command | **PASS: 26 passed, 0 failed, 0 blocked, 26/26 expected; exit code 0**. Results file `telemetry/selftest/unity_gaze_selftest_20261017T142019Z.txt` |
| **Scene opened and run in Play Mode** | — | **NOT TESTED** (manual; §5) |
| **Gaze focus / focus loss seen visually** | — | **NOT TESTED** (tint, labels, reticle, panels: manual; §5) |
| **Play Mode keyboard input (keys 1–7, F8, F9, arrow keys, right-mouse look)** | — | **NOT TESTED** |
| **Valid target confirmed without unintended action, as experienced in Play Mode** | — | **NOT TESTED visually.** The rules are covered by the 26 edit-mode Unity checks and the standalone tests |
| Real microphone speech | — | **NOT TESTED** |
| Headset / Quest | — | **NOT TESTED** (no XR packages installed) |

What the 161 standalone tests do show: the focus/selection/confirmation rules behave as specified (no SELECT without a held valid focus; named-target mismatch, negation, ambiguity, unknown and empty input are rejected without state change; MOVE/RELEASE change nothing; CANCEL/RESET clear only the selection; destroyed targets clear state), plus the CSV/event-log safety properties. They do **not** test raycasting, rendering, `OnGUI`, `SpatialTarget` visuals or any Unity API.

## 5. Manual visual verification (a person at the Editor) — NOT YET PERFORMED
The scene already exists; **do not run "Build Main Scene" again** (it will refuse). Open `Assets/Scenes/VoiceVR_Main.unity`, press Play, and check, in the Game view with the Game view focused and at least 1920×1080 or Maximize On Play:
1. Console shows no red errors on entering Play Mode; no "missing script/reference" warnings.
2. Two translucent panels are readable (top-left: voice/fallback status; below it: gaze loop panel). The top panel must say the keyboard fallback is SIMULATED, not speech, and the microphone is OFF.
3. Right-mouse drag or arrow keys turn the view. Looking at the red cube, blue sphere and green capsule each shows a yellow tint, a `[FOCUSED]` label and a yellow reticle; looking away restores the original colour and clears the label state. The grey cube (distractor) and the floor must never highlight.
4. Hold the gaze on one target ≥ 0.5 s, press **1** (simulated "select that"): it turns green with `[SELECTED]` and the panel says Executed. Pressing **1** immediately after moving onto a target is rejected (`focus_too_recent`). **4** cancels. **2** and **3** say not available and move nothing.
5. Record what was actually seen (below). Only then may any "NOT TESTED" row be changed.

| Item | Observed by a person? | Notes |
|---|---|---|
| Play Mode starts without Console errors | not yet | |
| Both panels legible | not yet | |
| Tint on focus / restore on leaving | not yet | |
| `[FOCUSED]` / `[SELECTED]` labels render | not yet | |
| Reticle visible, scales, changes colour | not yet | |
| Floor and grey distractor never highlight | not yet | |
| Keys 1, 4, 2, 3 behave as described | not yet | |
| Real microphone (F9) | not tested | out of scope for this step |
| Headset / Quest | not tested | no XR packages |

## 6. Proposed diff to a professor file — NOT APPLIED, needs approval
The starter controller treats any collider as focus, including the floor. Minimal proposed change in `Assets/Scripts/SpatialVoiceIntentController.cs`, `UpdateSpatialGazeTarget()`:

```diff
         if (Physics.Raycast(gazeRay, out hit, 10f))
         {
-            focusedSpatialObject = hit.collider.gameObject;
+            var target = hit.collider.GetComponentInParent<SpatialTarget>();
+            focusedSpatialObject = target != null ? target.gameObject : null;
             // TODO [A057 - Sakshi Sharma]: Apply accessible outline highlight shader to indicate spatial focus.
         }
```
Until approved, the scene relies on the non-invasive `LateUpdate` bridge above.
Not proposed here, but known: `FittsTargetTelemetryLogger` overwrites its CSV each run and formats numbers with the current culture; its fixes need separate approval.

## 7. Safety checks
No commit, push, tag, merge, `git add`, package install/update, LFS change, build, or scene overwrite. `Packages/manifest.json` and `ProjectSettings/` unchanged. No professor file modified. No participant data, no audio, no identifiers.

## 8. Limitations and blockers
- **Resolved:** the scene was built and the Unity self-tests were run in batch mode with the Editor closed (see "Actual results"). **Remaining blocker:** nothing has been viewed on screen; manual Play Mode verification (§5) is outstanding.
- The edit-mode self-tests run with `showLabel = false` and no rendering, so they say nothing about labels or panels.
- The focus cue's appearance is unseen. In particular the label uses `LegacyRuntime.ttf` via `Resources.GetBuiltinResource`; if that fails in Unity 6.3, labels may not render (tint and reticle should still work). The label is a child object and inherits parent scale.
- `OnGUI` panels (two of them) overlap nothing by design (y=10 and y=280) but have not been viewed.
- The status panel and keyboard fallback exist only in the desktop path; Android builds have no on-screen controls.
- The gaze ray uses camera forward only (no eye tracking, no cone, no snapping).
- `minFocusSeconds` = 0.3 s is a provisional anti-accident safeguard, not a validated value.
- The `GazeFocusSystem` consumes utterances from any modality, including real speech via `DesktopVoiceInput`, but real speech has not been tested and this phase did **not** wire speech to the professor's controller.
- The event schema is a proposal; `Real` data is deliberately refused.
- Scene is not added to Build Settings (a project-setting change needing approval).

## 9. Approvals still needed
1. The §6 diff to the professor's controller (or confirmation to keep the bridge only).
2. Adding `VoiceVR_Main` to Build Settings.
3. Keeping or changing the event schema; whether gaze events belong in the trial file.
4. `minFocusSeconds` default and the SELECT-as-confirmation rule (versus a separate "confirm" command).
5. Everything listed in `PHASE2_IMPLEMENTATION_REPORT.md` §13 remains PENDING CONFIRMATION (Unity 6.3 vs 2022.3, Shannon formula and MT safeguard, study protocol, recruitment/ethics, gaze-dwell, canonical schema, Quest/OpenXR target, cloud vs offline speech, latency definition, A-Frame artefact).

## 10. Next steps
1. Perform the manual Play Mode verification (§5) and record the real observations in the table there. Until then no visual claim is made.
2. After review: approved professor-file diffs; then, only if approved, voice-to-controller integration; XR packages remain deferred.
