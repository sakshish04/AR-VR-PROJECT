# Voice input routes — why the microphone stopped working in the Meta XR Simulator

**Status: implemented 2026-10-23; checked by tests and a start/stop test of the helper. Not yet checked by speaking into it with the simulator window focused (the next step is yours).**

## The problem (measured, not guessed)

Unity's own Windows keyword recognizer (`UnityEngine.Windows.Speech.KeywordRecognizer`) delivered phrases **only while the Unity window had keyboard focus**. Evidence from the Editor log: every logged recognition callback had `Application.isFocused = True` (four of four); after the Meta XR Simulator window took focus, the log kept showing `recognizerRunning=True phraseSystemStatus=Running state=Listening` every 5 s, and no phrase arrived. In the simulator you have to click the simulator window, so voice stopped. (The same signs were seen in the Phase 02 focus investigation, which was never closed.)

## Correction (2026-10-24): the first version of the helper could never report a phrase

The first helper (PowerShell `Register-ObjectEvent` handlers) started and printed READY, which is all that was tested, but its recognition handler never ran while the script waited, and it also used a property that does not exist (`AudioDuration`). It therefore never reported a single phrase. It was rewritten as a small compiled C# class with ordinary .NET event handlers (property `Duration`), the engine's rejection threshold was lowered so weak matches are reported as Low confidence instead of discarded, and speech that matches nothing is now shown in the HUD ("Heard something like ... but it did not match a command"). Still needed: a test with a real voice (laptop-speaker playback of synthesized speech was too unreliable to count as one).

## Update (2026-10-26): helper now uses the Windows Runtime recognizer

The System.Speech engine scored the developer's real voice very low even with a clean microphone signal. The helper process now defaults to the Windows Runtime recognizer (`Tools/VoiceHelper/WinRtSpeechHelper.cs`, built on demand into `Library/VoiceHelper` with the csc that ships with Windows), the same engine family as Unity's KeywordRecognizer. It listens to the Windows default microphone itself and does not need the Unity window focused. `DesktopVoiceInput.helperEngine` can switch back to System.Speech, which is also the automatic fallback if the build fails.

Verification: Voice Check (Edit Mode, 20 s) on 2026-10-26 recognised 5 spoken phrases (confidence Medium) with the Windows Runtime engine. Not yet verified: behaviour in the Meta XR Simulator window and a full spoken Play-mode session.

## Update (2026-10-25): the engine's own Windows microphone path was the weak link

With the helper working, real speech was still often "heard but not matched". The engine itself explained why: reading the Windows default capture path it reported **`PROBLEM|NoSignal`** continuously, while Unity's own microphone capture on the same device measured a clean signal. The same engine recognised synthesized speech fed from a file at 0.74–0.95 confidence, with or without the long phrase list, so the grammar size was not the cause.

Fix: by default (`helperAudioSource = UnityMicrophone`) Unity now captures the microphone (the device chosen in the dropdown, so the dropdown now matters for this route) and streams 16 kHz mono PCM to the helper's stdin; the helper feeds it to the engine. A gentle automatic gain raises quiet speech (at most `helperMaxGain` times, never clipped). `WindowsDefault` keeps the old path. The helper now also reports why audio could not be understood (`TooQuiet`, `TooLoud`, `TooNoisy`, `NoSignal`, ...) and the HUD says it in plain words, with the rejected best guess and its score in the Console. Checked: the pipe path recognised four synthesized phrases at 0.90–0.94; the full Unity chain starts, stays alive and reports no audio problem. **Not yet checked with a real voice.**

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
