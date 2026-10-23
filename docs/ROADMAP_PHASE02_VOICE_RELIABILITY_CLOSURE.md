# Roadmap Phase 02 — Voice Input and Recognition Reliability: closure record

**Status: CLOSED WITH A DOCUMENTED, UNRESOLVED LIMITATION** (focus-dependent recognition). Closed on 2026-10-19.

> Numbering note. This file uses the *current roadmap* (Phase 00 baseline ... Phase 09 release). The older reports in this folder
> (`PHASE2_IMPLEMENTATION_REPORT.md`, `PHASE3_SCENE_AND_GAZE_REPORT.md`, `PHASE4_SPATIAL_ACTIONS_AND_HUD.md`) use an earlier numbering
> and describe different work. They are unchanged.

The roadmap has no separate checklist file; the acceptance criteria are the ones stated for Phase 02. Each is assessed below with its evidence.

## Acceptance criteria

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Recognition behaviour and supported grammar documented accurately | Met | The grammar table below, generated from `VoiceIntentParser.KeywordGrammar` (26 phrases) |
| 2 | Existing automated tests and compile checks pass | Met, one item carried | Standalone C#, compile check and Python analysis pass (counts below). The Unity batch self-test last passed 40/40 and has **not been re-run on the current tree** (Unity open). Carried to the Phase 03 verification gate. |
| 3 | Real microphone behaviour tested separately from synthetic evidence | Met | Real-microphone commands in retained Editor logs and 178 `Voice`-modality rows in `telemetry/selftest/gaze_events_*.csv`. Typed (F8) and keyboard input carry other modalities and are not counted. |
| 4 | Focus-loss behaviour understood, or documented as an unresolved limitation | Met as a **documented limitation** | See "Unresolved limitation". It is **not** diagnosed and **not** fixed. |
| 5 | No regression to the working laptop demo | Met for the evidence available | Live voice sessions after the latest changes executed commands; automated suites pass. The Unity self-test rerun (criterion 2) is still owed. |
| 6 | Existing project changes preserved | Met | Nothing reverted, staged, committed or pushed; `m_renderMode: 0` unchanged. |

## Voice pipeline (as implemented)

`Windows default microphone` → `UnityEngine.Windows.Speech.KeywordRecognizer` (grammar, created at confidence floor Low) →
`DesktopVoiceInput.OnPhraseRecognized` → `VoiceConfidencePolicy.Decide` → `VoiceIntentParser.Parse` →
`DesktopVoiceInput.UtteranceReceived` → `GazeFocusSystem.SubmitParsed` → `GazeInteractionModel.Handle` → action → HUD / event log.

* Windows only (`UNITY_STANDALONE_WIN` / `UNITY_EDITOR_WIN`). Android builds contain no speech recogniser; **native Quest voice input is not implemented and not tested.**
* The recogniser can only return phrases from its fixed grammar. Speech outside the grammar produces no callback.
* Typed commands (F8) and keys 1–7 enter at `Submit` with a different `InputModality` and are never speech data.
* Confidence: select and move below the configured floor (default Medium) are ignored with an on-screen notice; cancel, reset, release, help and focus are accepted at Low (`acceptLowConfidenceUndoCommands`).

## Supported voice grammar (26 phrases) and what each does

| Phrase(s) | Intent | With nothing focused | Focused, nothing selected | Target selected |
|---|---|---|---|---|
| select that, select this, grab this, select target | Select | rejected `no_focus` | selects the focused target | rejected `already_selected` |
| move left / right / up / down / forward / back | Move (0.5 m, camera-relative) | rejected `nothing_selected` | rejected `nothing_selected` | moves (clamped to the play area) |
| move there, move to gaze | Move to the gazed surface | rejected `nothing_selected` | rejected `nothing_selected` | moves, or rejects (other target, no surface, at limit) |
| release, release it, drop it, let go, put it down | Release | rejected `nothing_selected` | rejected `nothing_selected` | keeps position, deselects |
| cancel, cancel that, never mind | Cancel | rejected `nothing_selected` | rejected `nothing_selected` | undoes moves since selection, deselects |
| reset, start over | Reset | rejected `nothing_selected` | rejected `nothing_selected` | restores start positions |
| focus, what is this | Focus report | rejected `no_focus` | reports the target | reports the target |
| help, what can I say | Help | shows help | shows help | shows help |

Parser-only vocabulary (reachable by typed text, never by the recogniser): for example "stop", "undo", "pick up", "place", "put it there". Negated and multi-action
phrases are rejected by the parser. `select target` previously failed with `target_mismatch`; the word "target" is now generic (fixed in Phase 02).

## What real-microphone evidence covers

* **Phrase level (retained logs I inspected):** select that, move left, move right, move back, move there, drop it, release, cancel, help, what is this.
  Some of these come from earlier logs that Unity has since overwritten.
* **User-reported, no retained log:** select target, reset, start over, help and focus by voice.
* **Intent level (persisted CSV, `Voice` modality, 178 rows over all sessions):** Select 24 executed / 9 rejected, Move 100 executed (13 earlier `not_implemented` rows from before Move existed),
  Release 11, Cancel 3, Reset 3, Focus 6, Help 3.
* **No real-microphone evidence at all:** grab this, select this, move up, move down, move forward, move to gaze, release it, let go, put it down, cancel that, never mind, what can I say.
  They parse correctly and are covered by automated tests only.
* Recognizer-reported speech-end-to-event time over 24 retained events: min 307 ms, median 513 ms, max 719 ms (3 under 350 ms). This clock is unvalidated (D-11); the 350 ms target is **not** demonstrated.
* Some recognised phrases are ignored for Low confidence (2 of 26 retained events in the current log, all "select that"); no false-accept or miss rate has been measured, so reliability is characterised, not quantified.

## Unresolved limitation: recognition while another window has focus

**Observed (user report + logs):** voice commands work with the Unity window focused. With the Meta XR Simulator window focused, no commands were recognised.

**Verified facts**
* In every retained session, all recognition callbacks (15 at the time of closure) occurred with `isFocused=True`; none occurred while Unity was unfocused.
* While unfocused, the frame loop kept running (about 37 fps), and the diagnostics reported `recognizerRunning=True`, `phraseSystemStatus=Running`, `state=Listening` and one microphone device.
* The OpenXR session state did not change when Unity lost focus (one `FOCUSED` transition per Play session in the Simulator log).
* No local Simulator or Unity documentation says anything about focus, microphone capture or audio routing for these APIs.

**Not established (hypotheses only):** that Windows delivers speech results only to the foreground application; that the Simulator window affects the audio path; that audio arrives too quietly to match. The user reported the same commands working with Notepad focused, but that test is **not captured in any retained log**.

**Why it cannot be diagnosed from the existing logs:** the recogniser is created at floor Low, so a "heard but not matched" phrase raises no callback, and no audio level is logged, so "not hearing" and "not matching" look the same.

**Required test (not yet performed):** one Play Mode session with a different harmless phrase per window — Unity "what is this", Notepad "help", Simulator "what can I say", Unity again — with the Windows input-level bar visible.
The recognised phrase in the log then identifies which windows delivered speech.

**Practical procedure until resolved:** keep the Unity window focused while speaking. Voice and the Simulator's own keyboard controls cannot be used at the same moment.

**Not claimed:** that the Simulator captures the microphone; that Windows has a focus limitation; that `PhraseRecognitionSystem.Restart()` would help.

## Instrumentation kept

The temporary `[VoiceFocusDiag]` block in `Assets/Scripts/Voice/DesktopVoiceInput.cs` (focus and pause events, recognizer state heartbeat every 5 s, frame-stall lines, one line per recognition callback) **stays** until the limitation is diagnosed. It logs no audio and no speech.

## Test and compile results at closure

* Standalone C# suite: 282 passed, 0 failed (329 after the Phase 03 additions).
* `compile_check_unity_scripts.ps1`: exit 0, 3 configurations, 0 failed.
* Python analysis: 34 passed, 5 skipped (SciPy not installed).
* Unity batch self-test: last result 40/40, exit 0 — **not re-run on the current tree**.

## Settings state recorded at closure (Windows demo)

Color space Gamma; Android graphics APIs Vulkan only (manual); Windows OpenXR loader assigned, render mode Multi Pass (`m_renderMode: 0`); Android OpenXR configured, none of it tested on a headset.
