# Voice commands on the Meta Quest headset

**Status: code written and compiles; NOT yet tested on a headset.** The desktop voice path is unchanged.

## How it works

- On an Android (Quest) build, `QuestVoiceInput` (`Assets/Scripts/Voice/QuestVoiceInput.cs`) creates itself when the scene loads and creates a Meta Voice SDK dictation object. No scene change is needed, and it does nothing in the Unity Editor or on Windows, so the desktop project is not affected.
- Every full transcription goes through the same command pipeline as desktop speech (`DesktopVoiceInput.SubmitExternalTranscription` -> parser -> gaze system -> HUD), so "select that", "move left 30 cm", "grab this object", "drop it" behave the same.
- **Right controller button A** switches listening on and off. Listening starts automatically when the app starts.
- The dictation service uses the headset's own platform dictation where it exists, and **Wit.ai** (a Meta cloud service) otherwise. In this project the Platform SDK define is not set, so expect **Wit.ai**.

## One-time setup you must do (needs your own account)

1. Go to https://wit.ai and sign in with your Meta/Facebook account. Create a new app (any name, language English).
2. In the app, open **Settings** and copy the **Client Access Token** (NOT the Server Access Token).
3. In Unity, create the text file `Assets/Resources/wit_client_token.txt` containing only that token. It is ignored by git on purpose (`.gitignore`), so it is not pushed.
4. Build again (File > Build Profiles > Meta Quest > Build And Run).

## On the headset

1. The headset must be **online** (Wi-Fi). Without internet Wit.ai cannot transcribe.
2. The first time, accept the **microphone permission** prompt.
3. Say a command. If nothing happens, check the HUD status line: it names the problem (no token, no internet, dictation error).

## Privacy and the research protocol

Wit.ai receives the microphone audio. Participants must be told this in the information sheet and consent form (`docs/phase04_materials/`), and the professor should confirm it is acceptable (open question; not yet raised in the decision register). Do not use real participants with this path until that is decided.

## Known limits

- Dictation returns free text, not scored phrases, so every Quest utterance is logged as confidence "Medium" and no voice CSV row is written yet on Android.
- Not tested on a headset: latency, accuracy in a noisy room, the HUD position, comfort and frame rate.
- If the Wit.ai token is missing or wrong, the app still runs; only voice is unavailable (controller and gaze work).
