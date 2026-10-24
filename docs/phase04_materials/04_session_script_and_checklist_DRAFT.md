# Session script and checklists — DRAFT

**DRAFT. Not approved.** Durations and trial counts are the PROPOSED values from the protocol (Q-03) and are to be confirmed in the pilot. The same wording is read to every participant in both conditions.

## A. Before the participant arrives (equipment checklist)

- [ ] Unity project opens without errors; scene `VoiceVR_Main`; Console is clear.
- [ ] Microphone checked: Windows Settings > System > Sound > Input shows the test bar moving; correct device is the Windows default (the recognizer listens to the default device).
- [ ] Voice route noted for the session (Auto; the speech helper is used while the simulator or headset is active). Recognizer used is written on the session sheet (D-09).
- [ ] Meta XR Simulator version noted. **Close the simulator window before pressing Stop in Unity** (known crash on shutdown with 207.0).
- [ ] Output folder for this session created and empty; session file labelled with the data origin (`Selftest` for pilots, `Real` only after approval).
- [ ] Questionnaire sheets, consent forms, pens, water, a clock. Chair placed; headset or screen set up per Q-04.
- [ ] Counterbalancing slot looked up in `05_counterbalancing_sheet.md`.

## B. Welcome and consent (about 10 min)

1. Give the information sheet; answer questions.
2. Check the exclusion questions (background sheet, questions 8 and 9) **before** anything else.
3. Signed consent form (kept separately). Only then issue the next free participant code (P01, P02, ...) and write it on the background sheet and the session sheet.
4. Say: "You can stop at any time, for any reason. This tests our software, not you."

## C. Task instructions (read aloud)

*Controller condition:* "Use the controller to point at a target and press the grip/trigger to select it. Select each highlighted target as quickly and accurately as you can."

*Gaze + voice condition:* "Look at a target and hold your gaze on it. Then say a command from the card: for example 'select that'. To move a selected object say 'move left', 'move right', 'move up', 'move down', or add a distance such as 'move left thirty centimeters'. Say 'grab this object' to carry it and 'drop this object' to put it down. Say 'cancel' to undo, 'help' for the list. Speak at normal volume, clearly, about arm's length from the microphone. Shouting makes recognition worse."

Give a printed **command card** with the exact grammar phrases (from `Assets/Scripts/Voice/VoiceIntentParser.cs`, `KeywordGrammar`) so participants use the words the recogniser knows.

## D. Per condition (about 20–25 min each)

1. Explain the condition (text above). Demonstrate once.
2. Practice: 9 trials (one per distance × width combination), not analysed (Q-03, PROPOSED).
3. Measured trials: 27 (3 repetitions of each of the 9 combinations), order randomised in blocks of 9; **record the random seed** shown by the software.
4. (Only if the docking task is confirmed, Q-02) the docking task: nine items to nine docks.
5. Questionnaires for this condition: NASA-TLX (raw, six subscales 0–100) and SUS (ten items, 1–5).
6. Note for the session sheet: technical problems, restarts, anything that affected a trial (with the time).

## E. Between conditions

Rest at least 5 minutes, water, SSQ (simulator-sickness questionnaire, 16 symptoms, 0–3). Ask how they feel. If they report more than mild symptoms, offer to stop (see F).

## F. Stop rules (any one ends the session or the block)

- The participant asks to stop (no questions asked).
- Reports nausea, dizziness, headache or eye strain beyond mild, or looks unwell.
- Equipment or software failure that cannot be fixed within a few minutes (note the reason).
- The experimenter judges it unsafe.
Record the stop time and reason. Data already collected stays labelled with the reason; it is not silently dropped or kept without a note.

## G. End of session (about 10 min)

1. Final SSQ. Short free comments (what was easy, what was hard).
2. Debrief: explain what the study measures; no results are given until the study is finished.
3. Save and copy the session folder; check the files exist (voice CSV, gaze event CSV, trial CSV once it exists). **Do not edit any data file by hand.**
4. Store the consent form separately from the data. Reset the scene.

## H. Pilot checklist (team members only, data labelled `Selftest`, no P-number)

- [ ] Time every step; compare with the estimates (60–75 min; 5 min measured trials per condition).
- [ ] Is every instruction understood without extra explanation? Reword the card and script if not.
- [ ] Does every file get written, and is the origin label correct?
- [ ] How many voice phrases are ignored or mis-heard per 27 trials? (Feeds the confidence-threshold calibration; the current thresholds are provisional.)
- [ ] Set the per-trial time limit and the definition of timeout.
- [ ] Does the counterbalancing sheet work in practice?
- [ ] Rehearse the stop rules and the shutdown order (close simulator, then Stop).
- [ ] Record a first effect-size estimate for the sample-size justification. **Pilot data is never merged with study data.**
