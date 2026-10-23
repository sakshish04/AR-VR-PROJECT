# Voice input routes — why the microphone stopped working in the Meta XR Simulator

**Status: implemented 2026-10-23; checked by tests and a start/stop test of the helper. Not yet checked by speaking into it with the simulator window focused (the next step is yours).**

## The problem (measured, not guessed)

Unity's own Windows keyword recognizer (`UnityEngine.Windows.Speech.KeywordRecognizer`) delivered phrases **only while the Unity window had keyboard focus**. Evidence from the Editor log: every logged recognition callback had `Application.isFocused = True` (four of four); after the Meta XR Simulator window took focus, the log kept showing `recognizerRunning=True phraseSystemStatus=Running state=Listening` every 5 s, and no phrase arrived. In the simulator you have to click the simulator window, so voice stopped. (The same signs were seen in the Phase 02 focus investigation, which was never closed.)

## Correction (2026-10-24): the first version of the helper could never report a phrase

The first helper (PowerShell `Register-ObjectEvent` handlers) started and printed READY, which is all that was tested, but its recognition handler never ran while the script waited, and it also used a property that does not exist (`AudioDuration`). It therefore never reported a single phrase. It was rewritten as a small compiled C# class with ordinary .NET event handlers (property `Duration`), the engine's rejection threshold was lowered so weak matches are reported as Low confidence instead of discarded, and speech that matches nothing is now shown in the HUD ("Heard something like ... but it did not match a command"). Still needed: a test with a real voice (laptop-speaker playback of synthesized speech was too unreliable to count as one).

## The fix

A second route, the **speech helper**: `Tools/VoiceHelper/SystemSpeechHelper.ps1`, a small PowerShell script started by Unity as a hidden child process. It uses Windows' own `System.Speech` engine (already part of Windows, nothing installed) on the default microphone, with the same fixed phrase list, and prints one line per recognized phrase. Unity reads those lines and sends each phrase through exactly the same code as before (confidence rule, parser, command, voice CSV row). The helper does not depend on window focus.

- `DesktopVoiceInput.recognizerRoute`: **Auto** (default) uses the helper whenever an XR device (Meta XR Simulator, headset) is active and Unity's recognizer on the desktop; `UnityKeywordRecognizer` or `SystemSpeechHelper` force one route.
- If the helper cannot start, Unity's recognizer is used and the Console says why.
- The helper exits when Unity exits and is killed when the microphone is switched off.
- No audio is recorded or saved; only the matched grammar phrase is printed.
- Line protocol and confidence mapping: `Assets/Scripts/Voice/ExternalRecognizerProtocol.cs` (unit tested).

## Things to know

- **Confidence is mapped, not identical.** System.Speech reports a number from 0 to 1; Unity's recognizer reports High/Medium/Low. Provisional mapping: 0.50 and above High, 0.20 and above Medium, below that Low (default minimum is Medium). These thresholds are **not calibrated**: the first live session showed a correctly spoken "select that" scoring 0.22, so the original 0.80 / 0.60 guesses rejected every real utterance and were lowered.
- **Voice CSV:** the schema is unchanged. Rows from the helper carry `platform` = `WindowsEditor/SystemSpeech` so they stay distinguishable from Unity-recognizer rows (`WindowsEditor`). Confidence ranks from the two recognizers must not be pooled in an analysis without saying so.
- Both routes listen to the **Windows default** microphone (the dropdown in the desktop view only tests devices).
- Startup takes about 1–3 s ("Starting the speech engine..."), then the status reads "Listening".
- This is a Windows-only desktop/simulator route. A Quest build has no Windows speech engine; it would need a different recognizer.
- Nothing here changes the research protocol: the recognizer used must be stated in any report (decision D-09 stays open).
