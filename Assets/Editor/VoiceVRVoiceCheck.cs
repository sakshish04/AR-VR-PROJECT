#if UNITY_EDITOR && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using UnityEditor;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Voice Check: 20 seconds that answer "does my voice reach the program?" without Play Mode, the simulator or a scene. It switches the microphone on through the
    /// speech helper, and every few seconds prints (in the Console, no dialogs) the microphone level and what the speech engine reported, then a verdict.
    /// Say the phrases while it runs: "select that", "move left", "drop it". Nothing is executed (no object is in focus in Edit Mode); rows may appear in the voice CSV as
    /// the developers' own test data.
    /// Menu: Group05 > VoiceVR > Voice Check (20 s, no Play needed)
    /// </summary>
    public static class VoiceVRVoiceCheck
    {
        const float Seconds = 20f;
        static DesktopVoiceInput voice;
        static DesktopVoiceInput.RecognizerRoute savedRoute;
        static double startedAt, nextReport;
        static float peakSeen;
        static bool running;

        [MenuItem("Group05/VoiceVR/Voice Check (20 s, no Play needed)")]
        public static void Run()
        {
            if (running) { Debug.Log("[VoiceCheck] already running."); return; }
            if (EditorApplication.isPlaying) { Debug.Log("[VoiceCheck] Stop Play Mode first (the check uses the Edit Mode microphone)."); return; }
            voice = Object.FindFirstObjectByType<DesktopVoiceInput>();
            if (voice == null) { Debug.LogWarning("[VoiceCheck] no DesktopVoiceInput in the open scene."); return; }
            savedRoute = voice.recognizerRoute;
            voice.recognizerRoute = DesktopVoiceInput.RecognizerRoute.SystemSpeechHelper;
            voice.StopListening();
            voice.ResetHelperCounters();
            voice.StartListening();
            peakSeen = 0f;
            startedAt = EditorApplication.timeSinceStartup;
            nextReport = startedAt + 3.0;
            running = true;
            EditorApplication.update += Tick;
            Debug.Log("[VoiceCheck] STARTED. For the next " + Seconds + " s say clearly, about 30-50 cm from the laptop: \"select that\"... \"move left\"... \"drop it\". Watch this Console.");
        }

        static void Tick()
        {
            if (voice == null) { Finish("the voice component disappeared"); return; }
            voice.SendMessage("PumpHelper");
            peakSeen = Mathf.Max(peakSeen, voice.HelperMicLevel);
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextReport)
            {
                nextReport = now + 3.0;
                Debug.Log("[VoiceCheck] t=" + (now - startedAt).ToString("0") + " s  mic level now " + voice.HelperMicLevel.ToString("0.000") + " (loudest so far " + peakSeen.ToString("0.000")
                          + ")  engine: recognised " + voice.HelperPhraseCount + ", heard-but-no-match " + voice.HelperHeardNoMatchCount + ", thinking " + voice.HelperHypothesisCount
                          + (voice.HelperLastHypothesis.Length > 0 ? " (last '" + voice.HelperLastHypothesis + "')" : "") + ", audio problems " + voice.HelperProblemCount
                          + (voice.HelperLastProblem.Length > 0 ? " (last: " + voice.HelperLastProblem + ")" : "") + "  status: " + voice.StatusMessage);
            }
            if (now - startedAt >= Seconds) Finish(null);
        }

        static void Finish(string abortReason)
        {
            EditorApplication.update -= Tick;
            running = false;
            string verdict;
            if (abortReason != null) verdict = "stopped: " + abortReason;
            else if (voice != null && !voice.HelperRunning) verdict = "FAIL: the speech helper is not running (" + voice.StatusMessage + ").";
            else if (voice.HelperMicLevelAvailable && peakSeen < 0.01f) verdict = "FAIL: the microphone level stayed near zero (loudest " + peakSeen.ToString("0.000") + "). Your voice is not reaching Unity: check the Windows default input device, mute switches and the dropdown choice.";
            else if (voice.HelperPhraseCount > 0) verdict = "OK: the engine (" + voice.HelperEngineName + ") recognised " + voice.HelperPhraseCount + " phrase(s). Voice input works.";
            else if (voice.HelperHeardNoMatchCount > 0) verdict = "PARTIAL: the engine (" + voice.HelperEngineName + ") hears speech but it matched no command. Say the phrases exactly as listed, a little slower and clearly" + (voice.HelperLastProblem.Length > 0 ? "; the engine reported: " + voice.HelperLastProblem : "") + ".";
            else verdict = "PARTIAL: the engine (" + voice.HelperEngineName + ") reported no speech at all" + (voice.HelperMicLevelAvailable ? " (microphone loudest " + peakSeen.ToString("0.000") + ")" : "") + ". Did you speak during the check?" + (voice.HelperLastProblem.Length > 0 ? " Engine reported: " + voice.HelperLastProblem : "");
            if (voice != null) { voice.StopListening(); voice.recognizerRoute = savedRoute; }
            Debug.Log("[VoiceCheck] RESULT: " + verdict);
        }
    }
}
#endif
