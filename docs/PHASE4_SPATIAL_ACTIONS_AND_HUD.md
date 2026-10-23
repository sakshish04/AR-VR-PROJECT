# Phase 4 — Spatial actions (MOVE / RELEASE) and sci-fi HUD

**Status:** implemented in code and covered by automated logic tests; **not yet seen in Unity.** Everything below that talks about
appearance, glide animation, the ring, and the Unity edit-mode self-tests is **unverified until run in the Editor.**
No headset was used and none is claimed: the HUD is an IMGUI overlay that is **desktop Play Mode only** and is not visible in a headset.

## Command semantics (design choices — change them if the study protocol says otherwise)

| Command | Needs | Effect | Rejected when |
|---|---|---|---|
| SELECT | a valid focused target, held >= `minFocusSeconds` | grabs it; remembers its pose (for CANCEL) | no focus, too recent, name mismatch, already selected |
| MOVE `left/right/up/down/forward/back` | a selection | steps the selected target `moveStepMeters` (0.5 m); left/right/forward/back are relative to the camera's horizontal facing | nothing selected; named a different target; **at_limit** (play-area edge / floor) |
| MOVE `there` / `here` / `to gaze` | a selection | places the target on the surface the gaze ray hits (ignoring the target itself), resting against that surface | nothing selected; **no_gaze_point**; **gaze_on_other_target**; at_limit |
| MOVE `to <name>` | — | **not supported** (no named destinations exist) | **unknown_destination** |
| RELEASE | a selection | keeps the target where it is, deselects it | nothing_selected |
| CANCEL | a selection | deselects **and returns the target to where it was when it was selected** (`revertOnCancel`) | nothing_selected |
| RESET | something selected or moved | deselects and returns **every** moved target to its start position | nothing selected and nothing moved |

Only a `SpatialTarget` can ever be selected, so the floor and the grey distractor can never be moved. Moves are clamped to a play area
(`arenaCenter`, `arenaHalfExtent` = 7 m, `maxHeightAboveFloor` = 3 m); a target's bottom never goes below the floor. Moves glide for
`moveGlideSeconds` (0.3 s; 0 = instant).

**Decision for you:** CANCEL *undoes* the moves since SELECT, while RELEASE *commits* them. Previously CANCEL only cleared the
selection (there was nothing to undo). If you want CANCEL to keep moved positions, set `revertOnCancel` off on `GazeFocusSystem`.

## Known limitations
- No collision avoidance between targets (a target can be moved into another one or into the distractor), no rotation, no snapping.
- Gaze is camera-forward only (no eye tracking). Speech results arrive about a second after you stop talking, so the gaze may have moved.
- The ring uses the `Sprites/Default` shader (fallback `Unlit/Color`); a player build might need it added to the included shaders.
- Keys 2 and 3 send the fixed phrases "move there" and "release" (SIMULATED input); other directions need speech or typed text (F8).

## Test status
- Standalone logic tests: 181 (up from 161) — model, parser and grammar for the new behaviour; the old "MOVE/RELEASE are NotImplemented"
  assertions were **replaced** by assertions of the new behaviour, not deleted or relaxed.
- Unity edit-mode self-tests: 40 checks expected (was 26): the two NotImplemented checks were replaced by 16 checks that move real objects
  (camera-relative step, floor clamp, at_limit, gaze placement, rejections, RELEASE, CANCEL undo, RESET, floor/distractor never move).
  **Not run yet.** With `VoiceVR_Main` open the missing-camera check stays BLOCKED, so expect 39 passed / 0 failed / 1 blocked.
