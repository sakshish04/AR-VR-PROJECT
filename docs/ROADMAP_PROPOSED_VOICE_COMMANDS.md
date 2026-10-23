# Roadmap — Quantified Move and Grab-Carry-Drop Voice Commands

**Status: IMPLEMENTED 2026-10-23 (after Phase 03 was closed). Checked with automated tests and a Play Mode run of the real scene using typed (simulated) input; NOT yet checked by speaking into a microphone, and not on a headset.**

This is a backlog feature, not a numbered phase: the agreed phase sequence (0-9) is unchanged. The original proposal text is kept below for reference; the section "What was built" comes first.

## What was built

| Command | Behaviour |
|---|---|
| `move left / right / up / down` (no distance) | Unchanged: moves the selected object by the default step, **0.5 m** (`GazeFocusSystem.moveStepMeters`). |
| `move left thirty centimeters`, `move left 30 cm`, `move this object thirty centimeters to the left`, `move up 1 meter`, `move left half a meter`, `move right 0.5 m` | Moves by the spoken distance (centimetres, millimetres or metres; digits or number words). Works for all four directions. |
| `grab this object` (also `carry this object`, `pick up this object`) | Selects the object you are looking at (same focus and dwell rules as `select that`) and **carries it**: it follows you (position and head turn, not head tilt) until you drop it. If an object is already selected it carries that one. |
| `drop this object` (also `drop it`, `release`) | Ends the carry and leaves the object where it is. |
| `cancel` / `reset` while carrying | End the carry; `cancel` also returns the object to where it was grabbed. |

Unchanged on purpose: every old command and phrase. `grab this` and `pick up the cube` still mean Select (no carry); only the longer "... object" phrases carry.

**Safety rules (reject, never guess):** a distance of 0, or more than 10 m, is rejected; a distance without a direction ("move there 30 centimeters") is rejected; a voice move while carrying is rejected ("drop this object first"); a second grab while carrying is rejected; one sentence with grab AND drop is rejected as ambiguous. Moves and carrying stay inside the play area (the same arena and floor limits as before); if the area cuts a move short, the result says how far it actually moved.

**Keyword grammar:** the recognizer can only return listed phrases, so distances are an explicit list: ten, twenty, thirty, forty, fifty centimeters and one meter, for each of the four directions, in two sentence forms (48 phrases), plus `grab this object`, `carry this object`, `pick up this object`, `drop this object`. Other distances work from typed input, but not from the microphone until they are added to the list (decision V-01 chose enumerated phrases; free numbers would need a different recognizer).

**Also fixed while testing:** the scene's `Gaze Camera` field pointed at the old, disabled `Main Camera` fixed at the origin, so in the XR view the gaze ray did not follow the head. `GazeFocusSystem` now uses that camera only if it is active, otherwise the headset camera (`Camera.main`). No scene or project setting was changed.

**Telemetry:** the voice CSV schema is unchanged (15 columns). `intent` can now read `Grab`; the spoken distance is not recorded. Adding it would be a deliberate schema change (V-08).

**Evidence:** standalone suite 375 passed, 0 failed (44 new checks); compile check 30 files, 3 of 3 configurations OK. In Play Mode (typed input in the real scene): default moves were exactly 0.5 m in all four directions; spoken distances were exact (0.3, 0.1, 1.0, 0.2, 0.5 m); invalid ones were rejected with a reason; a grabbed object followed a 1.5 m sideways walk of the rig and stayed put after `drop this object`.

**Still to check:** the same commands spoken into the microphone (recognition accuracy of the new phrases is untested), and anything on a real headset.

---

## 1. The two requested features

| # | Example utterance | What it should do |
|---|---|---|
| A | "move this object 30 cm to the left" | Move the gaze-focused (or already selected) object by a stated distance in a stated direction. |
| B | "grab this object, carry it, and drop it there" | Pick up the focused object, let it follow the user while carried, then place it where the user is looking when told to drop it. |

## 2. What exists today (verified from the current source)

- Grammar: `VoiceIntentParser.KeywordGrammar` is a **fixed list of phrases** ("select that", "grab this", "move left", "move there", "release", "drop it", "cancel", "reset", ...). The Windows `KeywordRecognizer` only returns phrases from that list.
- Intents: `Focus, Select, Move, Release, Cancel, Reset, Help`. "grab" is already a synonym of Select. There is **no distance parameter**, no separate Grab or Carry state, and no multi-step command.
- A move today is a fixed step in a direction, or "move there" to the gaze point.
- Each recognizer event writes one row to the voice telemetry CSV (15 columns, schema version 1, pinned by tests).

## 3. Feature A — "move this object 30 cm to the left"

**Why it is not a one-line change.** A `KeywordRecognizer` cannot recognise a free number. "30" and "centimetres" must be in the phrase list, so every allowed distance has to be listed.

Options (decision needed):

| Option | How | Trade-off |
|---|---|---|
| A1. Enumerated phrases | Generate phrases such as "move left 10 centimeters" for a fixed set of distances and directions. | Simplest and most reliable on the current recognizer. Limited to the listed values. |
| A2. SRGS grammar (`GrammarRecognizer`) | A grammar file with a number rule. | Flexible numbers on Windows. Needs verification that it works in this project. |
| A3. Dictation (`DictationRecognizer`) | Free text, parsed by the existing parser. | Most flexible; least precise, needs network on Windows, and is a different accuracy and privacy profile (see decision D-09). |

Design points to settle:
- Units: centimetres only, or also metres and inches. Spoken forms ("thirty", "30", "three zero").
- Direction frame: relative to the user's view (what "left" means now) or world axes. Must match the existing "move left" behaviour.
- Limits: smallest and largest allowed distance, and behaviour at room bounds or the floor.
- Target: gaze-focused object, or the currently selected one when both exist.
- Parser: extend `ParsedIntent` with distance and unit without breaking the existing grammar tests.
- Feedback: the HUD and XR panel show the interpreted distance and direction before or as it executes, so a mishearing ("13" vs "30") is visible.
- Confidence: a wrong number is worse than a wrong direction. Decide whether quantified moves need a higher confidence floor than plain moves.

## 4. Feature B — "grab this object, carry it, and drop it there"

**Key distinction.** This is a *sequence* over time, not one command. The words "carry it" and "drop it there" happen while the object moves, and "there" must be resolved **at the moment of the drop** from where the user is looking, not at the time of the first phrase.

Two ways to deliver it (decision needed):

| Option | Behaviour | Trade-off |
|---|---|---|
| B1. Three separate utterances | "grab this" → (object follows gaze) → "drop it there". | Fits the current keyword recognizer and the existing select/move/release model. Least risky. |
| B2. One compound utterance | The whole sentence is parsed into a plan: grab, carry, drop. | Needs a plan executor and a compound grammar; harder to cancel and to test. |

Design points to settle:
- New state "Carrying" between Selected and Released; what is allowed while carrying (cancel, reset, drop only?).
- How the carried object follows: attached to the gaze point at a fixed distance, or to the controller or hand later.
- "There": the gaze hit point at the moment of "drop", with a rule for no hit (reject, do not guess).
- Cancel mid-carry returns the object to where it was picked up. Timeout if the user never says drop.
- Collisions and placement on surfaces versus in mid-air.
- Interaction with the existing gaze dwell requirement and confirmed-target rule (an action needs a confirmed gaze target).

## 5. Telemetry and research impact

- The voice CSV schema is pinned at version 1 (15 columns). Distance, direction and carry-phase need new fields or a new schema version. That is a schema decision to make deliberately, with tests updated, not a silent change.
- Quantified moves open an accuracy measure (stated versus executed distance) that could be useful for the study, but only if the protocol (Phase 04) says so.
- Nothing here changes what has been validated so far; treat the features as unvalidated until tested with a real microphone, and later on a real headset.

## 6. Open decisions (owner to decide before implementation)

| ID | Question |
|---|---|
| V-01 | Recognizer route for numbers: A1 enumerated, A2 SRGS grammar, or A3 dictation? |
| V-02 | Supported units and number words. |
| V-03 | Direction frame: view-relative or world axes. |
| V-04 | Min / max move distance and out-of-bounds behaviour. |
| V-05 | Grab-carry-drop: B1 three utterances or B2 one compound utterance? |
| V-06 | Carry mechanic: follows gaze only, or controller / hand later. |
| V-07 | Confidence floor for quantified and carry commands. |
| V-08 | Voice telemetry schema change (new columns or version 2). |
| V-09 | Which roadmap phase owns this work. |

## 7. Acceptance checks (to write when work starts)

- Existing grammar, parser and 331 standalone checks still pass unchanged.
- Every new phrase parses to the intended intent and distance (a standalone test, like the existing grammar test).
- A misheard or out-of-range number is rejected with a visible reason, never silently executed.
- Cancel works at every step of a carry.
- Runtime checks in Play Mode and the Meta XR Simulator with a real microphone; physical headset testing stays a separate, later item.

## 8. Not decided and not done

No implementation, no grammar change, no scene change, no setting change, no test added. This document only records the idea so it is not lost.
