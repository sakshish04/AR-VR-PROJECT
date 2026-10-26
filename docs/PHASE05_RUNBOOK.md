# Phase 05 Runbook — running a live trial session (Selftest)

This is the step-by-step for the team's own rehearsal and pilot runs. Everything here writes **Selftest** data to `telemetry/selftest/`. Selftest data is never presented as participant data. Real participant runs stay blocked until the professor approves the protocol and ethics route (D-05, Q-07).

## A. One-time preparation

1. Open the project in Unity. Wait for the Console to finish compiling; it must show no red errors.
2. Open scene `Assets/Scenes/VoiceVR_Main.unity`. The `LiveTrialManager` object is already in it (if not: **Group05 > VoiceVR > Add Live Trial Manager**).
3. Select the `LiveTrialManager` object and check in the Inspector: Data Origin = **Selftest**, Real Data Enabled = **off**, Condition = as you need, Repetitions = 3, Time Limit = 15, Seed = 0 (0 means "pick from the clock; it is written to the session file").
4. Windows: Sound settings, your microphone volume at 100 (raise Microphone Boost if offered).
5. Optional microphone check without Play: **Group05 > VoiceVR > Voice Check (20 s, no Play needed)**; say "select that", "move left", "drop it". Verdict should say OK.

## B. Session 1 — desktop, gaze + voice condition

1. Press **Play**. The Game view must have focus (click once in it).
2. Press **F9** to start the microphone (the HUD says "Listening"). Skip this if the microphone auto-starts.
3. Press **F7** until the HUD shows condition **gaze_voice** (F7 only works while idle, before F5).
4. Press **F5**. After 2 s the first goal appears (a bright sphere with a dim "previous" marker beside it).
5. Look at the bright goal sphere (move the mouse/gaze cursor onto it) and say **"select that"**. The trial ends as success; the next goal appears after a short pause.
6. Do the 9 **practice** trials, wait for the pause, then the 27 **measured** trials. Speak at your normal volume; quiet speech is accepted.
7. If something goes wrong, press **F6** to abort (the trial is recorded as cancelled).
8. When the HUD says the session is finished, press **Stop**.

## C. Session 2 — controller condition (simulator or headset)

1. Start the Meta XR Simulator, then Play. (Close the simulator window **before** pressing Stop; with 207.0 pressing Stop with it open can crash Unity.)
2. Press **F7** to switch to **controller** (while idle). The microphone is switched off in this condition.
3. Press **F5**, point the controller ray at the bright goal and pull the **trigger**. Desk rehearsal without a controller (Selftest only): press **Space** while the Unity Game window has focus; it fires along the camera's forward direction and the row says `keyboard_surrogate`. A left mouse click does the same when no controller is detected (`mouse_surrogate`).
4. Run the same 9 + 27 trials.

## D. What to check afterwards

1. Open `telemetry/selftest/`:
   - `trials_selftest_<session>.csv` — 27 measured rows (25 columns).
   - `trials_selftest_<session>-practice.csv` — 9 practice rows (never analysed).
   - `session_info_<session>.txt` — formula (Shannon), seed, parameters, start/finish, summary.
   - the gaze event log and the voice telemetry CSV of the same session.
2. Sanity: every row has `success`, `failure/wrong_target` or `failure/timeout`; `movement_time_raw_s` is between 0 and 15; `input_modality` is `Voice` or the controller value.
3. Link voice rows to trials:
   ```
   python telemetry/link_voice_to_trials.py telemetry/selftest/trials_selftest_<session>.csv telemetry/selftest/voice_telemetry_<session>.csv
   ```
   It writes `..._linked.csv` with a `trial_id` column.
4. Report to the team: what the miss/timeout rate was, whether 15 s felt right, whether the "previous" marker confused you, and which spoken phrases failed. These findings set the time limit, trial counts and error rules (Q-03).

## E. Troubleshooting

| Symptom | What to do |
|---|---|
| F5 does nothing | Click in the Game view first; the session only starts while idle and not while typing in a field. |
| "Heard but could not match" | Say the phrase again clearly; check the Voice Check verdict; make sure only one Unity/helper process is running. |
| No goal visible | Check the Game view camera is the gaze camera and the object `LiveTrialManager` is active. |
| Real mode refused | Expected: Real needs Real Data Enabled and a code like `P01`, which must stay off until the professor approves. |
| Unity crashes on Stop | Close the simulator window first; upgrade the Simulator to 207.1. |
