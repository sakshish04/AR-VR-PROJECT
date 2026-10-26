using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace Group05.VoiceVR
{
    public enum VoiceInputState
    {
        /// <summary>Microphone is NOT in use.</summary>
        Idle,
        /// <summary>Recognizer is running and the microphone is in use.</summary>
        Listening,
        /// <summary>No supported speech recognizer on this platform; keyboard fallback only.</summary>
        Unavailable,
        /// <summary>Starting or running the recognizer failed (see <see cref="DesktopVoiceInput.StatusMessage"/>).</summary>
        Error
    }

    /// <summary>What happened to an utterance after it was parsed. This component never executes anything.</summary>
    public enum ExecutionStatus { NotAttempted, Executed, Rejected }

    /// <summary>
    /// One received utterance. The three quality notions are deliberately separate:
    /// recognizer confidence (only for real speech, categorical, supplied by the recogniser),
    /// parse result (<see cref="Parsed"/>, including its heuristic ParseScore), and execution status.
    /// </summary>
    public readonly struct UtteranceEvent
    {
        public readonly string Text;
        public readonly InputModality Modality;
        public readonly ParsedIntent Parsed;
        /// <summary>"High"/"Medium"/"Low" from the recognizer; null for fallback input (no recognizer involved).</summary>
        public readonly string RecognizerConfidence;
        public readonly float ReceivedRealtimeS;
        public readonly DateTime ReceivedUtc;
        /// <summary>Measured wall time spent in VoiceIntentParser.Parse.</summary>
        public readonly float ParseDurationMs;
        /// <summary>
        /// Recognizer-reported end of speech to event delivery. NaN for fallback input. This is NOT an end-to-end latency
        /// measurement (no audio-capture start is observed) and the recognizer clock is not independently validated.
        /// </summary>
        public readonly float SpeechEndToEventMs;
        public readonly ExecutionStatus Execution;

        public UtteranceEvent(string text, InputModality modality, ParsedIntent parsed, string recognizerConfidence,
                              float receivedRealtimeS, DateTime receivedUtc, float parseDurationMs, float speechEndToEventMs)
        {
            Text = text;
            Modality = modality;
            Parsed = parsed;
            RecognizerConfidence = recognizerConfidence;
            ReceivedRealtimeS = receivedRealtimeS;
            ReceivedUtc = receivedUtc;
            ParseDurationMs = parseDurationMs;
            SpeechEndToEventMs = speechEndToEventMs;
            Execution = ExecutionStatus.NotAttempted;
        }
    }

    /// <summary>
    /// Development-time input for the desktop (Windows Editor / standalone). Additive: it does not touch the
    /// professor's scripts and does not move or select anything; it only raises <see cref="UtteranceReceived"/>.
    /// A consumer MUST require <c>Parsed.IsActionable</c> AND a confirmed gaze target before acting.
    ///
    /// Two input paths, always labelled:
    ///  * Real speech: Windows KeywordRecognizer over a fixed phrase grammar. The microphone starts only when you
    ///    press the toggle key (or enable "Listen On Start") and a banner shows while it is on. No audio is recorded or saved.
    ///  * Fallback (SIMULATED, NOT SPEECH): number keys 1-7 and an optional typed-text box (F8).
    /// Neither path is evidence about Quest voice functionality; Android builds contain only the fallback.
    /// </summary>
    [DisallowMultipleComponent]
    public class DesktopVoiceInput : MonoBehaviour
    {
        public enum RecognizerConfidenceFloor { Low, Medium, High }

        [Header("Microphone (OFF until you start it)")]
        [Tooltip("Start the Windows recognizer automatically when the scene starts. Leave off to keep the microphone idle.")]
        public bool listenOnStart = false;
        public KeyCode toggleListeningKey = KeyCode.F9;
        [Tooltip("Phrases below this recognizer confidence are ignored by the recognizer itself.")]
        public RecognizerConfidenceFloor minimumRecognizerConfidence = RecognizerConfidenceFloor.Medium;
        [Tooltip("The recognizer itself is created at its LOWEST floor so that nothing is discarded invisibly. Phrases below 'Minimum Recognizer Confidence' " +
                 "then follow this rule: undo/info commands (cancel, reset, release, help, focus) are still accepted; select and move are NOT executed " +
                 "and the status bar says what was heard so you can repeat it.")]
        public bool acceptLowConfidenceUndoCommands = true;
        public float noSpeechHintAfterSeconds = 10f;

        [Header("Keyboard fallback (SIMULATED input, not speech)")]
        public bool enableKeyboardFallback = true;
        public KeyCode toggleTextBoxKey = KeyCode.F8;

        public enum RecognizerRoute
        {
            /// <summary>Unity's own recognizer on the desktop; the focus-independent speech helper while an XR device (Meta XR Simulator, headset) is active.</summary>
            Auto,
            /// <summary>Always Unity's KeywordRecognizer (delivers phrases only while the Unity window has focus).</summary>
            UnityKeywordRecognizer,
            /// <summary>Always the helper process (works while another window, such as the Meta XR Simulator, has focus).</summary>
            SystemSpeechHelper
        }

        [Header("Recognizer route")]
        [Tooltip("Unity's keyword recognizer was observed to deliver phrases only while the Unity window has focus. In the Meta XR Simulator the simulator window has " +
                 "focus, so Auto switches to the speech helper (Tools/VoiceHelper/SystemSpeechHelper.ps1, Windows System.Speech) whenever an XR device is active. " +
                 "If the helper cannot start, the Unity recognizer is used and the status says so.")]
        public RecognizerRoute recognizerRoute = RecognizerRoute.Auto;

        public enum HelperAudioSource
        {
            /// <summary>Unity captures the microphone (the device chosen in the dropdown) and streams it to the speech engine. Measured to give a clean signal.</summary>
            UnityMicrophone,
            /// <summary>The speech engine opens the Windows default microphone itself (older path; recognized far less reliably on the test laptop).</summary>
            WindowsDefault
        }

        public enum HelperEngine
        {
            /// <summary>Windows Runtime speech recognizer (Windows.Media.SpeechRecognition): what Unity's own recognizer uses, and what recognised the developers' voices well. Built on first use.</summary>
            WindowsRuntime,
            /// <summary>The older System.Speech desktop engine (PowerShell script). Scored real voices much lower on the test laptop.</summary>
            SystemSpeech
        }

        [Tooltip("Which speech engine runs inside the helper process. WindowsRuntime is the same engine family as Unity's own recognizer; SystemSpeech is the older fallback. " +
                 "WindowsRuntime listens to the Windows default microphone itself (the dropdown and the gain settings below only apply to SystemSpeech).")]
        public HelperEngine helperEngine = HelperEngine.WindowsRuntime;

        [Tooltip("Where the speech helper gets its audio. UnityMicrophone uses the device chosen in the dropdown; WindowsDefault lets Windows' own capture path feed the engine.")]
        public HelperAudioSource helperAudioSource = HelperAudioSource.UnityMicrophone;
        [Tooltip("OFF by default: in the first live check automatic gain made the engine report the signal as 'TooNoisy' (the gain swings when speech starts). Only switch on if the engine keeps reporting 'TooQuiet'.")]
        public bool helperAutoGainEnabled = false;
        [Range(1f, 12f)] public float helperMaxGain = 6f;

        [Header("Headset / Meta XR Simulator")]
        [Tooltip("When an XR device (Quest, Meta XR Simulator) becomes active, start the microphone once automatically. In the simulator the Unity window " +
                 "does not have keyboard focus, so F9 cannot be pressed there.")]
        public bool autoStartInXR = true;
        [Tooltip("Controller A / X button (primary button) toggles the microphone while an XR device is active. Works in the simulator and on Quest.")]
        public bool xrControllerToggle = true;
        [Tooltip("Keep the player loop and the recognizer running while another window (the Meta XR Simulator) has focus.")]
        public bool runInBackground = true;

        [Header("Display")]
        public bool showOnScreenStatus = true;

        /// <summary>Raised on the main thread for every received utterance, accepted or not.</summary>
        public event Action<UtteranceEvent> UtteranceReceived;

        public VoiceInputState State { get; private set; } = VoiceInputState.Idle;
        public string StatusMessage { get; private set; } = "Microphone off.";
        public bool HasLastUtterance { get; private set; }
        public UtteranceEvent LastUtterance { get; private set; }

        float listeningSince;
        float lastUtteranceAt = -1f;
        float transientStatusUntil;        // a short-lived status message (e.g. "heard X, ignored") that must not be overwritten at once
        bool textBoxOpen;
        string typedText = "";
        string lastSummary = "(nothing received yet)";

        // Layout-pass snapshot used by OnGUI so every IMGUI pass of a frame sees the same control set.
        bool guiListening, guiShowQuietHint, guiTextBox, guiMicStalled;

        // Typed-text box state. The focus requests are consumed on a Repaint pass, after the field has been drawn.
        const string TypedControlName = "g5_text";
        bool focusTextBoxPending, releaseTextFocusPending;
        string typedFeedback = "";
        Color typedFeedbackColor = Color.white;
        int guiQuietSeconds;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        KeywordRecognizer recognizer;
        bool systemErrorHooked;

        // focus-independent speech helper (separate process, see ExternalRecognizerProtocol)
        Process helperProcess;
        readonly ConcurrentQueue<string> helperLines = new ConcurrentQueue<string>();
        bool usingHelper;
        bool xrRouteSwitchTried;
        AudioClip helperMicClip;
        string helperMicDevice;
        int helperReadPos;
        float helperEnvelope;
        System.Threading.Thread helperWriter;
        readonly ConcurrentQueue<byte[]> helperAudioQueue = new ConcurrentQueue<byte[]>();
        readonly System.Threading.AutoResetEvent helperAudioSignal = new System.Threading.AutoResetEvent(false);
        volatile bool helperWriterRun;
        bool helperFedByUnity;
        // live diagnostics: what the microphone and the speech engine are doing, visible on the HUD and written to a small log file
        float helperPeakWindow, helperLevelNow, helperDiagNextAt, helperDiagStartedAt;
        long helperBytesSent;
        int helperEvPhrases, helperEvHeard, helperEvProblems;
        string helperLastProblem = "";
        string helperLastHypothesis = "";
        bool helperIsWinRt;
        float helperStoppedAt = -100f;
        int engineMode;
        bool helperReadySeen;
        float helperStartedAt;
        bool winRtUnusable;   // the Windows Runtime engine never answered in this Unity session (seen with an XR runtime active): use System.Speech fed by Unity's microphone
        int helperClipFreq = 16000;
        long helperSampleCounter;
        float[] helperTail = new float[0];
        static float[] helperFir;
        int helperEvHypotheses;
        const string HelperPlatformSuffix = "/SystemSpeech";
#endif

        // ---- TEMPORARY focus diagnostics (remove after the "commands wait until Unity is focused" investigation) ----
        // Logs only timing, thread id and focus/pause state, never speech content. Every line starts with [VoiceFocusDiag].
        const bool FocusDiagnostics = true;
        int diagMainThreadId = -1;

        static string DiagStamp()
        {
            return "t=" + Time.realtimeSinceStartup.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                 + " frame=" + Time.frameCount + " utc=" + DateTime.UtcNow.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
                 + " isFocused=" + Application.isFocused;
        }

        // Is the recognizer object itself still running, and what does Unity's phrase system report? Separates "Unity stopped it when focus
        // was lost" from "it keeps running but receives no phrases".
        string DiagRecognizerState()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return "route=" + (usingHelper ? "helper helperAlive=" + HelperAlive : "unity") + " recognizerRunning=" + (recognizer != null && recognizer.IsRunning) + " phraseSystemStatus=" + PhraseRecognitionSystem.Status
                   + " state=" + State + " micDevices=" + Microphone.devices.Length;
#else
            return "recognizer not available on this platform";
#endif
        }

        float diagNextState;

        void OnApplicationFocus(bool hasFocus)
        {
            if (FocusDiagnostics) UnityEngine.Debug.Log("[VoiceFocusDiag] OnApplicationFocus(" + hasFocus + ") " + DiagStamp() + " " + DiagRecognizerState());
        }

        void OnApplicationPause(bool paused)
        {
            if (FocusDiagnostics) UnityEngine.Debug.Log("[VoiceFocusDiag] OnApplicationPause(" + paused + ") " + DiagStamp());
        }
        // ---- end TEMPORARY focus diagnostics ----

        // ---- Voice telemetry CSV (Phase 03): one row per RECOGNIZER event; typed (F8) and keyboard input never write a row ----
        VoiceTelemetryCsvWriter voiceTelemetry;
        int voiceTelemetryDropped;                   // rows that could not be written this session (reported, never hidden)
        string pendingActionOutcome;                 // set synchronously by the consumer while Submit dispatches
#pragma warning disable 0649, 0414                    // only used by the Windows recognizer path
        volatile bool pendingRecognizerError;        // set by OnSystemError (its thread is not verified); written from Update on the main thread
#pragma warning restore 0649, 0414

        /// <summary>Full path of this session's voice telemetry file once the first row is written; null before that.</summary>
        public string VoiceTelemetryPath { get { return voiceTelemetry != null && voiceTelemetry.RowsWritten > 0 ? voiceTelemetry.FilePath : null; } }

        /// <summary>
        /// Called by the command consumer (GazeFocusSystem) from inside its handler, so the telemetry row can record what the command
        /// did. Synchronous: the outcome is whatever was observable when the handler returned. Ignored for typed or keyboard input.
        /// </summary>
        public void ReportActionOutcome(string outcomeCode) { pendingActionOutcome = outcomeCode; }

        // A failed write never throws into the interaction. It is counted and reported (first failure, then every 25th), and the NEXT event
        // tries again, so a transient problem (for example the file open in a viewer that locks it) does not silently end the session's data.
        void ReportTelemetryDrop(string error)
        {
            voiceTelemetryDropped++;
            if (voiceTelemetryDropped == 1 || voiceTelemetryDropped % 25 == 0)
                UnityEngine.Debug.LogWarning("[VoiceTelemetry] row not written (" + voiceTelemetryDropped + " so far this session): " + error);
        }

        void WriteVoiceTelemetry(VoiceTelemetryRecord row)
        {
            try
            {
                if (voiceTelemetry == null)
                {
                    string folder = System.IO.Path.Combine(Application.persistentDataPath, "VoiceVR");
                    string id = VoiceTelemetryCsvWriter.NewSessionId(DateTime.UtcNow, Guid.NewGuid().ToString("N").Substring(0, 4));
                    voiceTelemetry = new VoiceTelemetryCsvWriter(folder, id);
                }
                if (!voiceTelemetry.TryWrite(row)) { ReportTelemetryDrop(voiceTelemetry.LastError); return; }
                if (voiceTelemetry.RowsWritten == 1) UnityEngine.Debug.Log("[VoiceTelemetry] writing to " + voiceTelemetry.FilePath);
            }
            catch (Exception ex)
            {
                ReportTelemetryDrop(ex.Message);
            }
        }

        // ---- Microphone device list, test meter and headset controls ----
        // NOTE: Unity's KeywordRecognizer always listens to the Windows DEFAULT recording device; it has no device parameter. The dropdown therefore
        // chooses the device for the level test and shows which device to make the Windows default (Settings > System > Sound > Input).
        const string MicPrefKey = "g5_mic_device";
        string[] micDevices = new string[0];
        float nextDeviceRefresh;
        bool micMenuOpen;
        AudioClip testClip;
        string testDevice;
        float testUntil;
        float testPeak;
        string testResult = "";
        bool xrAutoStartDone;
        bool prevXrButton;

        /// <summary>Microphone chosen in the dropdown (empty when the machine has none).</summary>
        public string SelectedMicDevice { get; private set; } = "";
        public int MicDeviceCount { get { return micDevices.Length; } }

        void RefreshMicDevices()
        {
            nextDeviceRefresh = Time.realtimeSinceStartup + 2f;
            try { micDevices = Microphone.devices ?? new string[0]; }
            catch (Exception) { micDevices = new string[0]; }
            if (micDevices.Length == 0) { SelectedMicDevice = ""; return; }
            if (Array.IndexOf(micDevices, SelectedMicDevice) >= 0) return;
            string saved = PlayerPrefs.GetString(MicPrefKey, "");
            SelectedMicDevice = Array.IndexOf(micDevices, saved) >= 0 ? saved : micDevices[0];
        }

        void SelectMic(string device)
        {
            SelectedMicDevice = device;
            PlayerPrefs.SetString(MicPrefKey, device);
            testResult = "";
        }

        void StartMicTest()
        {
            if (State == VoiceInputState.Listening) { testResult = "Stop the microphone first (F9 / controller A), then test."; return; }
            if (string.IsNullOrEmpty(SelectedMicDevice)) { testResult = "No microphone found."; return; }
            try
            {
                testDevice = SelectedMicDevice;
                testClip = Microphone.Start(testDevice, true, 1, 16000);
                testUntil = Time.realtimeSinceStartup + 4f;
                testPeak = 0f;
                testResult = "Testing '" + testDevice + "': speak now (4 s)...";
            }
            catch (Exception ex) { testResult = "Test failed: " + ex.Message; testClip = null; }
        }

        void UpdateMicTest()
        {
            if (testClip == null) return;
            if (State == VoiceInputState.Listening) { EndMicTest("Test cancelled: microphone started."); return; }
            int pos = Microphone.GetPosition(testDevice);
            if (pos > 256)
            {
                var buf = new float[256];
                testClip.GetData(buf, pos - 256);
                float sum = 0f;
                for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
                testPeak = Mathf.Max(testPeak * 0.95f, Mathf.Sqrt(sum / buf.Length));
            }
            if (Time.realtimeSinceStartup >= testUntil)
                EndMicTest(testPeak > 0.01f ? "'" + testDevice + "' works: sound was detected."
                                            : "No sound detected on '" + testDevice + "'. Check mute / privacy settings or pick another device.");
        }

        void EndMicTest(string result)
        {
            try { if (Microphone.IsRecording(testDevice)) Microphone.End(testDevice); } catch (Exception) { }
            testClip = null;
            testResult = result;
        }

        static bool ReadXrButton(XRNode node)
        {
            InputDevice dev = InputDevices.GetDeviceAtXRNode(node);
            bool pressed;
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.primaryButton, out pressed) && pressed;
        }

        void ToggleListening()
        {
            if (RecognizerRunning) StopListening();
            else { StopListening(); StartListening(); }
        }

        // The Meta XR Simulator window owns the keyboard, so F9 in Unity is unreachable there. These two paths need no keyboard.
        void UpdateXrControls()
        {
            if (!XRSettings.isDeviceActive) { prevXrButton = false; return; }
            if (autoStartInXR && !xrAutoStartDone && State == VoiceInputState.Idle)
            {
                xrAutoStartDone = true;
                StartListening();
            }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // The microphone was started on the desktop (Unity recognizer) before the XR device became active: switch to the helper once.
            if (recognizerRoute == RecognizerRoute.Auto && !xrRouteSwitchTried && State == VoiceInputState.Listening && !usingHelper)
            {
                xrRouteSwitchTried = true;
                StopListening();
                StartListening();
            }
#endif
            if (!xrControllerToggle) return;
            bool pressed = ReadXrButton(XRNode.RightHand) || ReadXrButton(XRNode.LeftHand);
            if (pressed && !prevXrButton) ToggleListening();
            prevXrButton = pressed;
        }

        void Start()
        {
            if (runInBackground) Application.runInBackground = true;
            RefreshMicDevices();
            if (FocusDiagnostics) diagMainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (listenOnStart) StartListening();
            else if (!SpeechSupportedOnThisPlatform) { State = VoiceInputState.Unavailable; StatusMessage = "No speech recognizer on this platform. Keyboard fallback only."; }
        }

        /// <summary>True only while State is Listening AND the recognizer object is really running.</summary>
        bool RecognizerRunning
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return State == VoiceInputState.Listening && (usingHelper ? HelperAlive : (recognizer != null && recognizer.IsRunning));
#else
                return false;
#endif
            }
        }

        static bool SpeechSupportedOnThisPlatform
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        void Update()
        {
            // TEMPORARY: a frame that took more than half a second means the player loop was not running (throttled or paused).
            if (FocusDiagnostics && Time.unscaledDeltaTime > 0.5f)
                UnityEngine.Debug.Log("[VoiceFocusDiag] frame stall: previous frame took " + Time.unscaledDeltaTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " s. " + DiagStamp());
            // TEMPORARY: while the microphone is on, report the recognizer state every 5 s so changes show up even without a focus event.
            if (FocusDiagnostics && State == VoiceInputState.Listening && Time.realtimeSinceStartup >= diagNextState)
            {
                diagNextState = Time.realtimeSinceStartup + 5f;
                UnityEngine.Debug.Log("[VoiceFocusDiag] heartbeat " + DiagStamp() + " " + DiagRecognizerState());
            }
            if (pendingRecognizerError)
            {
                pendingRecognizerError = false;
                WriteVoiceTelemetry(new VoiceTelemetryRecord
                {
                    TimestampUtc = DateTime.UtcNow,
                    RealtimeS = Time.realtimeSinceStartup,
                    Platform = Application.platform.ToString(),
                    MinimumConfidenceRank = minimumRecognizerConfidence.ToString(),
                    Decision = VoiceTelemetryRecord.DecisionError,
                    Reason = VoiceTelemetryRecord.ReasonRecognizerError
                });
            }
            if (Time.realtimeSinceStartup >= nextDeviceRefresh) RefreshMicDevices();
            UpdateMicTest();
            UpdateXrControls();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            PumpHelper();
            KeepUnityRecognizerAlive();
#endif
            IsTypingText = textBoxOpen && enableKeyboardFallback;
            if (transientStatusUntil > 0f && Time.realtimeSinceStartup > transientStatusUntil)
            {
                transientStatusUntil = 0f;
                if (State == VoiceInputState.Listening) StatusMessage = "Listening. Say one of the grammar phrases.";
            }
#if ENABLE_LEGACY_INPUT_MANAGER
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (Input.GetKeyDown(KeyCode.F10))
            {
                // Pick whichever engine hears you best, live, in the same session: 1 = Windows engine in a helper process (works while the simulator is focused),
                // 2 = older engine fed with Unity's microphone, 3 = Unity's own recognizer (needs the Unity window focused).
                engineMode = (engineMode + 1) % 3;
                if (engineMode == 0) { recognizerRoute = RecognizerRoute.SystemSpeechHelper; helperEngine = HelperEngine.WindowsRuntime; winRtUnusable = false; }
                else if (engineMode == 1) { recognizerRoute = RecognizerRoute.SystemSpeechHelper; helperEngine = HelperEngine.SystemSpeech; }
                else recognizerRoute = RecognizerRoute.UnityKeywordRecognizer;
                StopListening();
                StartListening();
                StatusMessage = "Voice engine " + (engineMode + 1) + "/3 (F10 switches): " + (engineMode == 0 ? "Windows engine in a helper" : engineMode == 1 ? "older engine with Unity's microphone" : "Unity recognizer (Unity window must be focused)") + ".";
            }
            if (Input.GetKeyDown(KeyCode.F11))
            {
                // Compare the two recognizers in the same session: F11 switches between the speech helper and Unity's own recognizer.
                recognizerRoute = usingHelper ? RecognizerRoute.UnityKeywordRecognizer : RecognizerRoute.SystemSpeechHelper;
                StopListening();
                StartListening();
                StatusMessage = "Voice route: " + (usingHelper ? "speech helper (works while the simulator is focused)" : "Unity recognizer (needs the Unity window focused)") + ". " + StatusMessage;
            }
#endif
            if (Input.GetKeyDown(toggleListeningKey))
            {
                // A recognizer that stopped behind our back leaves State at Listening (the bar then says "MIC STOPPED ... press F9 to restart").
                // One press must restart it, so only a recognizer that is really running is stopped by the toggle.
                if (RecognizerRunning) StopListening();
                else { StopListening(); StartListening(); }
            }
            if (!enableKeyboardFallback) return;
            if (Input.GetKeyDown(toggleTextBoxKey)) SetTextBox(!textBoxOpen);
            if (textBoxOpen) return; // typing must not trigger number-key commands
            for (int i = 0; i < VoiceIntentParser.FallbackKeyPhrases.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    Submit(VoiceIntentParser.FallbackKeyPhrases[i], InputModality.KeyboardFallback, null, float.NaN);
#endif
        }

        /// <summary>
        /// True while the typed-text box is open. Other keyboard-driven scripts (arrow-key look) check this so typing never
        /// turns the camera. Static because there is one HUD per scene; it is cleared when this component is disabled.
        /// </summary>
        public static bool IsTypingText { get; private set; }

        /// <summary>
        /// True while the F8 typed-command box is open. Read-only: lets the headset panel (XrHudPanel) mirror the box, because this IMGUI box is
        /// drawn only in the desktop Game view and is NOT visible in a headset or the Meta XR Simulator view.
        /// </summary>
        public bool TextBoxOpen { get { return textBoxOpen && enableKeyboardFallback; } }

        /// <summary>The text typed in the F8 box so far. It is only submitted by Enter; this getter never submits anything.</summary>
        public string TypedText { get { return typedText ?? ""; } }

        void SetTextBox(bool open)
        {
            textBoxOpen = open && enableKeyboardFallback;
            IsTypingText = textBoxOpen;
            if (textBoxOpen)
            {
                focusTextBoxPending = true;
                SetTypedFeedback("Type a command, then press Enter to send. Esc closes. No microphone needed.", HudTheme.TextDim);
            }
            else
            {
                releaseTextFocusPending = true;   // drop keyboard focus so number keys / arrows work again
            }
        }

        void SetTypedFeedback(string message, Color color) { typedFeedback = message; typedFeedbackColor = color; }

        void OnDisable()
        {
            IsTypingText = false;
            StopListening();
            // Rows are appended and closed one at a time, so nothing is buffered and nothing is lost here. The next enable starts a NEW
            // telemetry session (new session_id, new file), and no flag from the old session may leak into it.
            voiceTelemetry = null;
            voiceTelemetryDropped = 0;
            pendingRecognizerError = false;
            pendingActionOutcome = null;
        }
        void OnDestroy() { StopListening(); }

        /// <summary>Starts the Windows recognizer. Safe to call repeatedly; failures set State/StatusMessage instead of throwing.</summary>
        public void StartListening()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (State == VoiceInputState.Listening) return;
            try
            {
                bool wantHelper = recognizerRoute == RecognizerRoute.SystemSpeechHelper
                                  || (recognizerRoute == RecognizerRoute.Auto && XRSettings.isDeviceActive);
                if (wantHelper)
                {
                    string why;
                    if (TryStartHelper(out why))
                    {
                        listeningSince = Time.realtimeSinceStartup;
                        State = VoiceInputState.Listening;
                        StatusMessage = "Starting the speech engine (works while the simulator window has focus)...";
                        return;
                    }
                    UnityEngine.Debug.LogWarning("[DesktopVoiceInput] Speech helper unavailable (" + why + "); using Unity's recognizer, which only hears you while the Unity window has focus.");
                }
                if (!PhraseRecognitionSystem.isSupported)
                {
                    State = VoiceInputState.Unavailable;
                    StatusMessage = "Windows speech recognition is not supported/enabled here. Use the keyboard fallback.";
                    return;
                }
                if (Microphone.devices.Length == 0)
                {
                    State = VoiceInputState.Error;
                    StatusMessage = "No microphone detected (or access denied). Use the keyboard fallback.";
                    return;
                }
                // Lowest floor on purpose: Unity silently drops anything below the floor, so a Medium floor made misses invisible.
                // Our own floor (minimumRecognizerConfidence) is applied in OnPhraseRecognized, where the result can be shown.
                recognizer = new KeywordRecognizer(VoiceIntentParser.KeywordGrammar, ConfidenceLevel.Low);
                recognizer.OnPhraseRecognized += OnPhraseRecognized;
                if (!systemErrorHooked) { PhraseRecognitionSystem.OnError += OnSystemError; systemErrorHooked = true; }
                recognizer.Start();
                listeningSince = Time.realtimeSinceStartup;
                State = VoiceInputState.Listening;
                StatusMessage = "Listening. Say one of the grammar phrases.";
            }
            catch (Exception ex)
            {
                ReleaseRecognizer();
                State = VoiceInputState.Error;
                StatusMessage = "Could not start speech recognition: " + ex.Message;
                UnityEngine.Debug.LogWarning("[DesktopVoiceInput] " + StatusMessage);
            }
#else
            State = VoiceInputState.Unavailable;
            StatusMessage = "No speech recognizer on this platform. Keyboard fallback only.";
#endif
        }

        /// <summary>
        /// Entry for a recogniser that lives outside this component (the Quest headset's dictation, see <see cref="QuestVoiceInput"/>): the text goes through the same
        /// judge/execute/log path as Windows speech. Dictation gives no per-phrase confidence, so the phrase is treated as Medium; text that matches no command is
        /// reported by the parser exactly like any other unmatched utterance.
        /// </summary>
        public void SubmitExternalTranscription(string text, string platformSuffix)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            HandleRecognizedPhrase(text, 1, "Medium", float.NaN, platformSuffix ?? "");
#else
            // Android: the command executes through Submit; voice-CSV rows for the headset are not written yet (see docs/QUEST_VOICE.md).
            Submit(text, InputModality.Voice, "Medium", float.NaN);
#endif
        }

        /// <summary>Shows a message from an external recogniser in the HUD status line.</summary>
        public void ExternalStatusMessage(string message)
        {
            StatusMessage = message;
            transientStatusUntil = Time.realtimeSinceStartup + 10f;
        }

        /// <summary>Lets an external recogniser show its state in the HUD (it replaces the "no recogniser on this platform" notice).</summary>
        public void ExternalListeningChanged(bool listening, string engineName)
        {
            State = listening ? VoiceInputState.Listening : VoiceInputState.Idle;
            StatusMessage = listening ? "Listening (" + engineName + "). Say a command." : "Microphone off.";
        }

        public void StopListening()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            ReleaseRecognizer();
            if (systemErrorHooked) { PhraseRecognitionSystem.OnError -= OnSystemError; systemErrorHooked = false; }
            if (State == VoiceInputState.Listening) { State = VoiceInputState.Idle; StatusMessage = "Microphone off."; }
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        static ConfidenceLevel ToUnityConfidence(RecognizerConfidenceFloor f)
        {
            switch (f)
            {
                case RecognizerConfidenceFloor.High: return ConfidenceLevel.High;
                case RecognizerConfidenceFloor.Low: return ConfidenceLevel.Low;
                default: return ConfidenceLevel.Medium;
            }
        }

        void ReleaseRecognizer()
        {
            StopHelper();
            if (recognizer == null) return;
            try
            {
                recognizer.OnPhraseRecognized -= OnPhraseRecognized;
                if (recognizer.IsRunning) recognizer.Stop();
                recognizer.Dispose();
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[DesktopVoiceInput] Error while releasing recognizer: " + ex.Message); }
            recognizer = null;
        }

        void OnPhraseRecognized(PhraseRecognizedEventArgs args)
        {
            if (FocusDiagnostics)
                UnityEngine.Debug.Log("[VoiceFocusDiag] recognition callback " + DiagStamp() + " onMainThread="
                                      + (System.Threading.Thread.CurrentThread.ManagedThreadId == diagMainThreadId)
                                      + " confidence=" + args.confidence);
            float sinceSpeechEndMs = float.NaN;
            try
            {
                double d = (DateTime.Now - (args.phraseStartTime + args.phraseDuration)).TotalMilliseconds;
                if (d >= 0.0 && d < 60000.0) sinceSpeechEndMs = (float)d;
            }
            catch (Exception) { /* leave NaN: no trustworthy timing */ }

            // ConfidenceLevel is ordered High(0) < Medium(1) < Low(2) < Rejected(3): a LARGER number means LESS confident.
            HandleRecognizedPhrase(args.text, (int)args.confidence, args.confidence.ToString(), sinceSpeechEndMs, "");
        }

        /// <summary>
        /// The one place a recognized phrase is judged, executed and logged, whichever recognizer produced it (Unity's keyword recognizer or the speech helper).
        /// <paramref name="platformSuffix"/> is appended to the platform column so rows from the helper stay distinguishable from Unity-recognizer rows.
        /// </summary>
        void HandleRecognizedPhrase(string text, int rank, string rankName, float sinceSpeechEndMs, string platformSuffix)
        {
            float callbackRealtime = Time.realtimeSinceStartup;   // telemetry timestamps are taken at callback entry
            DateTime callbackUtc = DateTime.UtcNow;

            // The accept/ignore rule lives in VoiceConfidencePolicy (Unity-independent, unit tested).
            int floorRank = (int)ToUnityConfidence(minimumRecognizerConfidence);
            long parseStart = Stopwatch.GetTimestamp();
            ParsedIntent preParsed = VoiceIntentParser.Parse(text);
            float parseMs = (float)((Stopwatch.GetTimestamp() - parseStart) * 1000.0 / Stopwatch.Frequency);
            ConfidenceDecision decision = VoiceConfidencePolicy.Decide(rank, floorRank, preParsed.Kind, acceptLowConfidenceUndoCommands);
            if (decision == ConfidenceDecision.IgnoreWithNotice)
            {
                StatusMessage = "Heard '" + text + "' with " + rankName + " confidence: NOT executed. Say it again, a little clearer.";
                transientStatusUntil = Time.realtimeSinceStartup + 6f;
                UnityEngine.Debug.Log("[DesktopVoiceInput] IGNORED (low recognizer confidence " + rankName + "): '" + text + "'");
                WriteRecognitionRow(text, rankName, platformSuffix, callbackUtc, callbackRealtime, rank, floorRank, preParsed, decision, sinceSpeechEndMs, parseMs, null);
                return;
            }
            pendingActionOutcome = null;   // the consumer reports the outcome while Submit dispatches
            Submit(text, InputModality.Voice, rankName, sinceSpeechEndMs);
            WriteRecognitionRow(text, rankName, platformSuffix, callbackUtc, callbackRealtime, rank, floorRank, preParsed, decision, sinceSpeechEndMs, parseMs, pendingActionOutcome);
        }

        // One telemetry row for a recognized phrase. The decision and reason come from VoiceTelemetryRecord.Classify (Unity-independent, tested).
        void WriteRecognitionRow(string text, string rankName, string platformSuffix, DateTime utc, float realtime, int rank, int floorRank, ParsedIntent parsed,
                                 ConfidenceDecision decision, float speechEndMs, float parseMs, string actionOutcome)
        {
            string decisionText, reason;
            VoiceTelemetryRecord.Classify(rank, floorRank, parsed, decision, out decisionText, out reason);
            WriteVoiceTelemetry(new VoiceTelemetryRecord
            {
                TimestampUtc = utc,
                RealtimeS = realtime,
                Platform = Application.platform.ToString() + platformSuffix,
                RecognizedPhrase = text,
                ConfidenceRank = rankName,
                MinimumConfidenceRank = minimumRecognizerConfidence.ToString(),
                Intent = VoiceTelemetryRecord.IntentText(parsed),
                Decision = decisionText,
                Reason = reason,
                SpeechEndToEventMs = speechEndMs,
                ParseDurationMs = parseMs,
                ActionOutcome = actionOutcome
            });
        }

        // ---- focus-independent speech helper ----

        bool HelperAlive
        {
            get
            {
                if (externalAttached) return ExternalEngineAlive();
                try { return helperProcess != null && !helperProcess.HasExited; }
                catch (Exception) { return false; }
            }
        }

        bool TryStartHelper(out string why)
        {
            why = "";
            try
            {
                string script = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Tools", "VoiceHelper", "SystemSpeechHelper.ps1"));
                if (!System.IO.File.Exists(script)) { why = "script not found: " + script; return false; }
                string grammar = System.IO.Path.Combine(Application.temporaryCachePath, "voicevr_grammar.txt");
                System.IO.File.WriteAllLines(grammar, VoiceIntentParser.KeywordGrammar, new System.Text.UTF8Encoding(false));

                // Give the operating system a moment to release the microphone after the previous engine process ended.
                float sinceStop = Time.realtimeSinceStartup - helperStoppedAt;
                if (sinceStop >= 0f && sinceStop < 2.5f) System.Threading.Thread.Sleep((int)((2.5f - sinceStop) * 1000f));
                // An engine started BEFORE Play mode (Group05 > VoiceVR > Start Voice Engine) keeps hearing through Play, while an engine launched during Play often hears
                // nothing; if one is running, listen to its output instead of starting another.
                if (helperEngine == HelperEngine.WindowsRuntime && TryAttachExternalEngine())
                {
                    helperIsWinRt = true; helperFedByUnity = false; helperReadySeen = true; usingHelper = true;
                    UnityEngine.Debug.Log("[DesktopVoiceInput] attached to the voice engine that was started before Play mode (it listens to the Windows default microphone).");
                    return true;
                }
                string winRtExe = null, winRtWhy = "";
                helperIsWinRt = false;
                helperReadySeen = false; helperStartedAt = Time.realtimeSinceStartup;
                // With an XR runtime active (Meta XR Simulator / headset) the Windows Runtime engine starts but hears nothing (verified 2026-10-26), so use the engine that takes Unity's own microphone audio there.
                if (helperEngine == HelperEngine.WindowsRuntime && !winRtUnusable)
                {
                    if (TryPrepareWinRtHelperStatic(out winRtExe, out winRtWhy)) helperIsWinRt = true;
                    else UnityEngine.Debug.LogWarning("[DesktopVoiceInput] Windows Runtime speech helper unavailable (" + winRtWhy + "); using the System.Speech helper instead.");
                }

                // Audio source: Unity's own microphone capture (clean signal) streamed to the engine, or the engine's own Windows capture.
                bool useUnityMic = !helperIsWinRt && helperAudioSource == HelperAudioSource.UnityMicrophone && Microphone.devices.Length > 0;
                if (useUnityMic)
                {
                    string dev = string.IsNullOrEmpty(SelectedMicDevice) ? null : SelectedMicDevice;
                    // Capture at 48 kHz where the device allows it and convert to 16 kHz here with a proper low-pass filter. Letting Unity resample straight to 16 kHz
                    // folds high frequencies back into the speech band, and the recognizer scored real voices far lower than clean test audio.
                    int fmin, fmax;
                    Microphone.GetDeviceCaps(dev, out fmin, out fmax);
                    helperClipFreq = (fmax == 0 || (48000 >= fmin && 48000 <= fmax)) ? 48000 : 16000;
                    helperMicClip = Microphone.Start(dev, true, 2, helperClipFreq);
                    helperSampleCounter = 0; helperTail = new float[0];
                    if (helperMicClip == null) { useUnityMic = false; UnityEngine.Debug.LogWarning("[DesktopVoiceInput] Unity could not open the microphone; the helper will use the Windows default capture."); }
                    else { helperMicDevice = dev; helperReadPos = 0; helperEnvelope = 0f; }
                }
                helperFedByUnity = useUnityMic;

                var psi = new ProcessStartInfo
                {
                    FileName = helperIsWinRt ? winRtExe : "powershell.exe",
                    Arguments = helperIsWinRt
                        ? "-GrammarFile \"" + grammar + "\" -ParentPid " + Process.GetCurrentProcess().Id
                        : "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -GrammarFile \"" + grammar + "\" -ParentPid " + Process.GetCurrentProcess().Id
                                + (useUnityMic ? " -AudioSource stdin" : " -AudioSource default"),
                    RedirectStandardInput = useUnityMic,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new System.Text.UTF8Encoding(false)
                };
                while (helperLines.TryDequeue(out _)) { }
                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (sender, e) => { if (e.Data != null) helperLines.Enqueue(e.Data); };
                proc.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) helperLines.Enqueue("ERROR|" + e.Data); };
                if (!proc.Start()) { why = "process did not start"; return false; }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                if (useUnityMic) StartHelperWriter(proc);
                UnityEngine.Debug.Log("[DesktopVoiceInput] speech helper engine: " + (helperIsWinRt ? "Windows Runtime" : "System.Speech") + "; audio source: "
                                      + (useUnityMic ? "Unity microphone '" + (helperMicDevice ?? "default") + "'" : "Windows default capture"));
                helperProcess = proc;
                usingHelper = true;
                return true;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                StopHelper();
                return false;
            }
        }

        float nextKeepAlive;
        int keepAliveRestarts;

        /// <summary>
        /// Unity's recognizer runs inside the (signed) Unity process, so Windows' app control never blocks it, but Unity shuts its phrase system down when the Unity window loses focus
        /// (for example when the Meta XR Simulator window is in front). While listening, check once a second and start it again if it was stopped.
        /// </summary>
        void KeepUnityRecognizerAlive()
        {
            if (usingHelper || recognizer == null || State != VoiceInputState.Listening) return;
            if (Time.realtimeSinceStartup < nextKeepAlive) return;
            nextKeepAlive = Time.realtimeSinceStartup + 1f;
            try
            {
                if (PhraseRecognitionSystem.Status != SpeechSystemStatus.Running) { PhraseRecognitionSystem.Restart(); keepAliveRestarts++; }
                if (!recognizer.IsRunning) { recognizer.Start(); keepAliveRestarts++; }
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[DesktopVoiceInput] could not keep the recognizer running: " + ex.Message); }
        }

        // ---- speech engine started before Play mode (see StartExternalEngine) ----
        bool externalAttached;
        long externalPos;

        static string ExternalDir { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "VoiceHelper")); } }
        static string ExternalOutFile { get { return System.IO.Path.Combine(ExternalDir, "external_out.txt"); } }
        static string ExternalPidFile { get { return System.IO.Path.Combine(ExternalDir, "external.pid"); } }

        public static bool ExternalEngineAlive()
        {
            try
            {
                if (!System.IO.File.Exists(ExternalPidFile)) return false;
                int pid = int.Parse(System.IO.File.ReadAllText(ExternalPidFile).Trim());
                Process p = Process.GetProcessById(pid);
                return !p.HasExited && p.ProcessName.IndexOf("WinRtSpeechHelper", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception) { return false; }
        }

        bool TryAttachExternalEngine()
        {
            if (!ExternalEngineAlive()) return false;
            try { externalPos = System.IO.File.Exists(ExternalOutFile) ? new System.IO.FileInfo(ExternalOutFile).Length : 0; }
            catch (Exception) { externalPos = 0; }
            externalAttached = true;
            return true;
        }

        void ReadExternal()
        {
            try
            {
                string f = ExternalOutFile;
                if (!System.IO.File.Exists(f)) return;
                using (var fs = new System.IO.FileStream(f, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                {
                    if (fs.Length < externalPos) externalPos = 0;
                    if (fs.Length == externalPos) return;
                    fs.Seek(externalPos, System.IO.SeekOrigin.Begin);
                    var buf = new byte[fs.Length - externalPos];
                    int n = fs.Read(buf, 0, buf.Length);
                    int last = Array.LastIndexOf(buf, (byte)'\n', n - 1);
                    if (last < 0) return;                       // no complete line yet
                    externalPos += last + 1;
                    string text = new System.Text.UTF8Encoding(false).GetString(buf, 0, last + 1);
                    foreach (string line in text.Split('\n')) { string t = line.TrimEnd('\r'); if (t.Length > 0) helperLines.Enqueue(t); }
                }
            }
            catch (Exception) { /* try again next frame */ }
        }

        /// <summary>
        /// Starts the Windows Runtime speech engine as an independent process that outlives Play mode and writes what it hears to Library/VoiceHelper/external_out.txt.
        /// Menu: Group05 > VoiceVR > Start Voice Engine (do this BEFORE pressing Play). Returns a message for the user.
        /// </summary>
        public static string StartExternalEngine()
        {
            try
            {
                string exe, why;
                if (!TryPrepareWinRtHelperStatic(out exe, out why)) return "Could not build the engine: " + why;
                StopExternalEngine();
                System.IO.Directory.CreateDirectory(ExternalDir);
                string grammar = System.IO.Path.Combine(ExternalDir, "external_grammar.txt");
                System.IO.File.WriteAllLines(grammar, VoiceIntentParser.KeywordGrammar, new System.Text.UTF8Encoding(false));
                try { System.IO.File.Delete(ExternalOutFile); } catch (Exception) { }
                // cmd.exe (signed by Windows) redirects the engine's output into a file; the engine program itself is the unchanged, already-allowed build.
                var psi = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    Arguments = "/c \"\"" + exe + "\" -GrammarFile \"" + grammar + "\" -ParentPid " + Process.GetCurrentProcess().Id + " > \"" + ExternalOutFile + "\" 2>&1\"",
                    UseShellExecute = false, CreateNoWindow = true
                };
                Process cmd = Process.Start(psi);
                if (cmd == null) return "The engine did not start.";
                System.Threading.Thread.Sleep(1500);
                Process engine = null;
                foreach (Process q in Process.GetProcessesByName("WinRtSpeechHelper")) { engine = q; break; }
                if (engine == null) return "The engine program did not start (Windows may have blocked it: see the Console).";
                System.IO.File.WriteAllText(ExternalPidFile, engine.Id.ToString());
                return "Voice engine started (process " + engine.Id + "). Now press Play; the project attaches to it automatically.";
            }
            catch (Exception ex) { return "Could not start the engine: " + ex.Message; }
        }

        public static string StopExternalEngine()
        {
            try
            {
                if (System.IO.File.Exists(ExternalPidFile))
                {
                    int pid = int.Parse(System.IO.File.ReadAllText(ExternalPidFile).Trim());
                    try { Process p = Process.GetProcessById(pid); if (p.ProcessName.IndexOf("WinRtSpeechHelper", StringComparison.OrdinalIgnoreCase) >= 0) { p.Kill(); p.WaitForExit(2000); } } catch (Exception) { }
                    System.IO.File.Delete(ExternalPidFile);
                }
                return "Voice engine stopped.";
            }
            catch (Exception ex) { return "Could not stop the engine: " + ex.Message; }
        }

        // Compiles Tools/VoiceHelper/WinRtSpeechHelper.cs with the C# compiler that ships with Windows (nothing to install) into Library/VoiceHelper, when missing or older than the source.
        static bool TryPrepareWinRtHelperStatic(out string exePath, out string why)
        {
            exePath = null; why = "";
            try
            {
                string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Tools", "VoiceHelper", "WinRtSpeechHelper.cs"));
                if (!System.IO.File.Exists(src)) { why = "source not found: " + src; return false; }
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "VoiceHelper"));
                System.IO.Directory.CreateDirectory(dir);
                string exe = System.IO.Path.Combine(dir, "WinRtSpeechHelper.exe");
                bool fresh = System.IO.File.Exists(exe) && System.IO.File.GetLastWriteTimeUtc(exe) >= System.IO.File.GetLastWriteTimeUtc(src);
                if (!fresh)
                {
                    string win = Environment.GetEnvironmentVariable("windir") ?? "C:\\Windows";
                    string fw = System.IO.Path.Combine(win, "Microsoft.NET", "Framework64", "v4.0.30319");
                    string csc = System.IO.Path.Combine(fw, "csc.exe");
                    string wm = System.IO.Path.Combine(win, "System32", "WinMetadata");
                    if (!System.IO.File.Exists(csc)) { why = "the Windows C# compiler was not found at " + csc; return false; }
                    var psi = new ProcessStartInfo
                    {
                        FileName = csc,
                        Arguments = "-nologo -out:\"" + exe + "\" -r:\"" + System.IO.Path.Combine(wm, "Windows.Media.winmd") + "\" -r:\"" + System.IO.Path.Combine(wm, "Windows.Foundation.winmd")
                                    + "\" -r:\"" + System.IO.Path.Combine(wm, "Windows.Globalization.winmd") + "\" -r:\"" + System.IO.Path.Combine(fw, "System.Runtime.dll")
                                    + "\" -r:\"" + System.IO.Path.Combine(fw, "System.Runtime.InteropServices.WindowsRuntime.dll") + "\" \"" + src + "\"",
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    using (Process c = Process.Start(psi))
                    {
                        string output = c.StandardOutput.ReadToEnd() + c.StandardError.ReadToEnd();
                        if (!c.WaitForExit(60000)) { try { c.Kill(); } catch (Exception) { } why = "the helper build timed out"; return false; }
                        if (c.ExitCode != 0 || !System.IO.File.Exists(exe)) { why = "the helper build failed: " + output; return false; }
                    }
                    UnityEngine.Debug.Log("[DesktopVoiceInput] built the Windows Runtime speech helper: " + exe);
                }
                exePath = exe;
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        void StopHelper()
        {
            if (externalAttached) { externalAttached = false; usingHelper = false; return; }   // the externally started engine keeps running
            usingHelper = false;
            StopHelperWriter();
            if (helperProcess == null) return;
            try { if (!helperProcess.HasExited) helperProcess.Kill(); } catch (Exception) { }
            // Wait until the old engine process is really gone: a Windows Runtime engine started while the previous one still holds the microphone starts normally
            // ("ready") but then hears nothing (seen 2026-10-26: it worked right after Play and stayed deaf after every restart).
            try { helperProcess.WaitForExit(2000); } catch (Exception) { }
            try { helperProcess.Dispose(); } catch (Exception) { }
            helperProcess = null;
            helperStoppedAt = Time.realtimeSinceStartup;
        }

        // Runs on the main thread (called from Update): the helper's lines arrive on a reader thread and are queued, so no Unity API is touched off-thread.
        // ---- Unity microphone -> speech helper (16 kHz, 16-bit, mono PCM on the helper's stdin) ----

        void StartHelperWriter(Process proc)
        {
            while (helperAudioQueue.TryDequeue(out _)) { }
            helperWriterRun = true;
            helperWriter = new System.Threading.Thread(() =>
            {
                try
                {
                    System.IO.Stream stdin = proc.StandardInput.BaseStream;
                    byte[] chunk;
                    while (helperWriterRun)
                    {
                        helperAudioSignal.WaitOne(200);
                        while (helperWriterRun && helperAudioQueue.TryDequeue(out chunk)) { stdin.Write(chunk, 0, chunk.Length); }
                        stdin.Flush();
                    }
                }
                catch (Exception) { /* the helper ended: Update notices that the process is gone */ }
            });
            helperWriter.IsBackground = true;
            helperWriter.Start();
        }

        void StopHelperWriter()
        {
            helperWriterRun = false;
            helperAudioSignal.Set();
            if (helperMicClip != null)
            {
                try { Microphone.End(helperMicDevice); } catch (Exception) { }
                helperMicClip = null;
            }
            helperWriter = null;
            helperFedByUnity = false;
        }

        // 15-tap Hamming-windowed sinc low-pass, cutoff about 7 kHz at 48 kHz, for a clean 3:1 decimation to 16 kHz.
        static float[] FirTaps()
        {
            if (helperFir != null) return helperFir;
            const int taps = 15;
            var h = new float[taps];
            double fc = 7000.0 / 48000.0, sum = 0.0;
            for (int k = 0; k < taps; k++)
            {
                double x = k - (taps - 1) / 2.0;
                double sinc = Math.Abs(x) < 1e-9 ? 2.0 * fc : Math.Sin(2.0 * Math.PI * fc * x) / (Math.PI * x);
                double w = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * k / (taps - 1));
                h[k] = (float)(sinc * w);
                sum += h[k];
            }
            for (int k = 0; k < taps; k++) h[k] = (float)(h[k] / sum);
            helperFir = h;
            return h;
        }

        void FeedHelperAudio()
        {
            if (!helperFedByUnity || helperMicClip == null) return;
            int total = helperMicClip.samples;
            int pos = Microphone.GetPosition(helperMicDevice);
            if (pos < 0 || pos == helperReadPos) return;
            int n = pos - helperReadPos;
            if (n < 0) n += total;
            if (n > helperClipFreq) { helperReadPos = pos; helperTail = new float[0]; return; }   // fell far behind (a long frame): skip rather than send stale audio

            var raw = new float[n];
            int first = Mathf.Min(n, total - helperReadPos);
            var part = new float[first];
            helperMicClip.GetData(part, helperReadPos);
            Array.Copy(part, 0, raw, 0, first);
            if (first < n)
            {
                var rest = new float[n - first];
                helperMicClip.GetData(rest, 0);
                Array.Copy(rest, 0, raw, first, n - first);
            }
            helperReadPos = pos;

            // to 16 kHz
            float[] samples;
            if (helperClipFreq == 48000)
            {
                float[] fir = FirTaps();
                int hist = fir.Length - 1;
                var ext = new float[helperTail.Length + n];
                Array.Copy(helperTail, 0, ext, 0, helperTail.Length);
                Array.Copy(raw, 0, ext, helperTail.Length, n);
                var outList = new System.Collections.Generic.List<float>(n / 3 + 1);
                for (int i = 0; i < n; i++)
                {
                    long g = helperSampleCounter + i;
                    if (g % 3 != 0) continue;
                    int e = helperTail.Length + i;          // index of the newest sample in ext
                    if (e - hist < 0) { outList.Add(raw[i]); continue; }
                    float acc = 0f;
                    for (int k = 0; k < fir.Length; k++) acc += fir[k] * ext[e - k];
                    outList.Add(acc);
                }
                helperSampleCounter += n;
                int keep = Math.Min(ext.Length, hist);
                var tail = new float[keep];
                Array.Copy(ext, ext.Length - keep, tail, 0, keep);
                helperTail = tail;
                samples = outList.ToArray();
            }
            else samples = raw;
            int m = samples.Length;
            if (m == 0) return;

            float gain = 1f;
            float peak = 0f;
            for (int i = 0; i < m; i++) { float a2 = Mathf.Abs(samples[i]); if (a2 > peak) peak = a2; }
            helperPeakWindow = Mathf.Max(helperPeakWindow, peak);
            helperLevelNow = Mathf.Max(peak, helperLevelNow * 0.9f);
            if (helperAutoGainEnabled)
            {
                helperEnvelope = Mathf.Max(peak, helperEnvelope * 0.995f);
                gain = Mathf.Clamp(0.30f / Mathf.Max(helperEnvelope, 0.02f), 1f, helperMaxGain);
            }
            var bytes = new byte[m * 2];
            for (int i = 0; i < m; i++)
            {
                float v = Mathf.Clamp(samples[i] * gain, -0.98f, 0.98f);
                short s16 = (short)Mathf.RoundToInt(v * 32767f);
                bytes[2 * i] = (byte)(s16 & 0xFF);
                bytes[2 * i + 1] = (byte)((s16 >> 8) & 0xFF);
            }
            helperBytesSent += bytes.Length;
            helperAudioQueue.Enqueue(bytes);
            helperAudioSignal.Set();
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>Read-only live numbers for the Voice Check editor tool: microphone level (0..1, decaying peak) and what the speech engine has reported.</summary>
        public float HelperMicLevel { get { return helperLevelNow; } }
        public int HelperPhraseCount { get { return helperEvPhrases; } }
        public int HelperHeardNoMatchCount { get { return helperEvHeard; } }
        public int HelperProblemCount { get { return helperEvProblems; } }
        public string HelperLastProblem { get { return helperLastProblem; } }
        public string HelperLastHypothesis { get { return helperLastHypothesis; } }
        public int HelperHypothesisCount { get { return helperEvHypotheses; } }
        public bool HelperRunning { get { return usingHelper && HelperAlive; } }
        /// <summary>True when Unity itself captures the microphone for the helper (so HelperMicLevel is meaningful); false for the Windows Runtime engine, which listens to the microphone itself.</summary>
        public bool HelperMicLevelAvailable { get { return helperFedByUnity; } }
        public string HelperEngineName { get { return helperIsWinRt ? "Windows Runtime" : "System.Speech"; } }
        /// <summary>Clears the counters (Voice Check calls this before it listens).</summary>
        public void ResetHelperCounters() { helperEvPhrases = 0; helperEvHeard = 0; helperEvProblems = 0; helperEvHypotheses = 0; helperLastProblem = ""; helperLastHypothesis = ""; helperPeakWindow = 0f; helperLevelNow = 0f; }
#endif

        // One line every 2 s into telemetry/selftest/voice_mic_diag.log: microphone peak, how much audio reached the engine, and what the engine reported.
        void WriteHelperDiag()
        {
            float now = Time.realtimeSinceStartup;
            if (now < helperDiagNextAt) return;
            helperDiagNextAt = now + 2f;
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "telemetry", "selftest"));
                System.IO.Directory.CreateDirectory(dir);
                string line = DateTime.UtcNow.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                    + " source=" + (helperIsWinRt ? "winrt_default_mic" : helperFedByUnity ? "unity_mic" : "windows_default")
                    + " micPeak2s=" + helperPeakWindow.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                    + " bytesSent=" + helperBytesSent + " helperAlive=" + HelperAlive + " focused=" + Application.isFocused
                    + " phrases=" + helperEvPhrases + " heardNoMatch=" + helperEvHeard + " hypotheses=" + helperEvHypotheses + (helperLastHypothesis.Length > 0 ? " lastHyp='" + helperLastHypothesis + "'" : "") + " problems=" + helperEvProblems + (helperLastProblem.Length > 0 ? " lastProblem=" + helperLastProblem : "");
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "voice_mic_diag.log"), line + "\n");
            }
            catch (Exception) { /* diagnostics must never disturb the interaction */ }
            helperPeakWindow = 0f;
        }

        void PumpHelper()
        {
            if (!usingHelper) return;
            if (externalAttached) ReadExternal();
            // The Windows Runtime engine can hang while starting when an XR runtime (the Meta XR Simulator) is active and never reports READY. Do not leave the user
            // staring at "Starting the speech engine": after 6 s switch to the System.Speech engine fed by Unity's own microphone, which works in that situation.
            if (helperIsWinRt && !helperReadySeen && Time.realtimeSinceStartup - helperStartedAt > 6f)
            {
                UnityEngine.Debug.LogWarning("[DesktopVoiceInput] Windows Runtime speech engine did not start within 6 s; switching to the System.Speech engine with Unity's microphone.");
                winRtUnusable = true;
                StopHelper();
                string whyFallback;
                if (!TryStartHelper(out whyFallback)) { StatusMessage = "Speech helper could not start: " + whyFallback; State = VoiceInputState.Error; return; }
                StatusMessage = "Starting the speech engine (fallback)...";
                return;
            }
            FeedHelperAudio();
            WriteHelperDiag();
            string line;
            int handled = 0;
            while (handled < 8 && helperLines.TryDequeue(out line))
            {
                handled++;
                ExternalRecognizerProtocol.Message m = ExternalRecognizerProtocol.Parse(line);
                switch (m.Kind)
                {
                    case ExternalRecognizerProtocol.Kind.Ready:
                        helperReadySeen = true;
                        StatusMessage = "Listening. Say one of the grammar phrases.";
                        UnityEngine.Debug.Log("[DesktopVoiceInput] speech helper ready (engine " + m.Text + "); it hears you even while another window has focus.");
                        break;
                    case ExternalRecognizerProtocol.Kind.Phrase:
                        if (FocusDiagnostics)
                            UnityEngine.Debug.Log("[VoiceFocusDiag] recognition callback (helper) " + DiagStamp() + " confidence=" + m.Confidence.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                        helperEvPhrases++;
                        int rank = ExternalRecognizerProtocol.RankFromConfidence(m.Confidence);
                        HandleRecognizedPhrase(m.Text, rank, ExternalRecognizerProtocol.RankName(rank),
                                               ExternalRecognizerProtocol.SinceSpeechEndMs(m, DateTime.UtcNow), HelperPlatformSuffix);
                        break;
                    case ExternalRecognizerProtocol.Kind.Hypothesis:
                        helperEvHypotheses++;
                        helperLastHypothesis = m.Text + " (" + m.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")";
                        break;
                    case ExternalRecognizerProtocol.Kind.Problem:
                        helperEvProblems++;
                        helperLastProblem = m.Text;
                        StatusMessage = ExternalRecognizerProtocol.ProblemAdvice(m.Text);
                        transientStatusUntil = Time.realtimeSinceStartup + 6f;
                        UnityEngine.Debug.Log("[DesktopVoiceInput] speech helper: audio problem " + m.Text);
                        break;
                    case ExternalRecognizerProtocol.Kind.Heard:
                        helperEvHeard++;
                        // A "rejected" guess that still matches a command is accepted as a Low-confidence phrase: the engine only ever picks from our fixed list, and in a
                        // noisy room or with a quiet voice many correct phrases are scored below its own threshold.
                        if (!string.IsNullOrEmpty(m.Text) && VoiceIntentParser.Parse(m.Text).Status == ParseStatus.Ok)
                        {
                            HandleRecognizedPhrase(m.Text, 2, "Low", float.NaN, HelperPlatformSuffix);
                            break;
                        }
                        // Speech was detected but matched no command: say so (and log it), otherwise a failure looks identical to "not listening".
                        StatusMessage = m.Text.Length > 0
                            ? "Heard something like '" + m.Text + "' but it did not match a command. Say it again, clearly."
                            : "Heard speech but could not match a command. Say it again, clearly.";
                        transientStatusUntil = Time.realtimeSinceStartup + 6f;
                        UnityEngine.Debug.Log("[DesktopVoiceInput] speech helper: heard speech, no match (best guess '" + m.Text + "', score "
                                              + (float.IsNaN(m.Confidence) ? "n/a" : m.Confidence.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)) + ")");
                        break;
                    case ExternalRecognizerProtocol.Kind.Error:
                        State = VoiceInputState.Error;
                        StatusMessage = "Speech helper error: " + m.Text + ". Press " + toggleListeningKey + " to retry, or use the keyboard fallback.";
                        UnityEngine.Debug.LogWarning("[DesktopVoiceInput] " + StatusMessage);
                        pendingRecognizerError = true;
                        StopHelper();
                        return;
                }
            }
            if (State == VoiceInputState.Listening && !HelperAlive)
            {
                StatusMessage = "The speech helper stopped. Press " + toggleListeningKey + " to restart it.";
            }
        }

        void OnSystemError(SpeechError error)
        {
            State = VoiceInputState.Error;
            StatusMessage = "Speech system error: " + error + ". Use the keyboard fallback.";
            UnityEngine.Debug.LogWarning("[DesktopVoiceInput] " + StatusMessage);
            pendingRecognizerError = true;   // the telemetry row is written from Update (main thread); the error detail stays in this log line
        }
#endif

        /// <summary>
        /// Parses text and raises <see cref="UtteranceReceived"/>. Text is only ever matched against the fixed
        /// vocabulary; it is never evaluated or executed.
        /// </summary>
        public void Submit(string text, InputModality modality, string recognizerConfidence, float speechEndToEventMs)
        {
            long t0 = Stopwatch.GetTimestamp();
            ParsedIntent parsed = VoiceIntentParser.Parse(text);
            float parseMs = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);

            var ev = new UtteranceEvent(text, modality, parsed, recognizerConfidence,
                                        Time.realtimeSinceStartup, DateTime.UtcNow, parseMs, speechEndToEventMs);
            LastUtterance = ev;
            HasLastUtterance = true;
            lastUtteranceAt = Time.realtimeSinceStartup;

            string label = modality == InputModality.Voice ? "VOICE" : "SIMULATED (" + modality + ") - NOT SPEECH";
            Action<UtteranceEvent> handlers = UtteranceReceived;
            int consumers = handlers == null ? 0 : handlers.GetInvocationList().Length;
            lastSummary = "[" + label + "] '" + (text ?? "") + "' -> " + parsed.Status + "/" + parsed.Kind
                          + " | parse score " + parsed.ParseScore.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                          + " | recognizer confidence " + (recognizerConfidence ?? "n/a")
                          + " | parse " + parseMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ms"
                          + (float.IsNaN(speechEndToEventMs) ? "" : " | recognizer speech-end to event " + speechEndToEventMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " ms")
                          + (consumers > 0 ? " | sent to " + consumers + " consumer(s); the outcome is shown in the result panel (top right)"
                                           : " | NOT EXECUTED (no consumer is subscribed)");
            UnityEngine.Debug.Log("[DesktopVoiceInput] " + lastSummary);

            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<UtteranceEvent>)d)(ev); }
                catch (Exception ex) { UnityEngine.Debug.LogError("[DesktopVoiceInput] Utterance subscriber threw: " + ex); }
            }
        }

        void OnGUI()
        {
            if (!showOnScreenStatus) return;

            // The HUD is drawn with fixed rectangles only (no GUILayout), so the control set cannot change between IMGUI passes.
            // The few conditions that decide WHICH controls exist are still decided once, on the Layout pass, so the typed-text
            // field keeps a stable control id for the whole frame.
            if (Event.current.type == EventType.Layout)
            {
                // "Listening" is only shown while the recognizer is really running; if it stopped behind our back the bar says so.
                bool wantsListening = State == VoiceInputState.Listening;
                bool running = wantsListening;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                running = wantsListening && (usingHelper ? HelperAlive : (recognizer != null && recognizer.IsRunning));
#endif
                guiMicStalled = wantsListening && !running;
                guiListening = running;
                guiShowQuietHint = guiListening && Time.realtimeSinceStartup - Mathf.Max(listeningSince, lastUtteranceAt) > noSpeechHintAfterSeconds;
                guiQuietSeconds = guiListening ? Mathf.FloorToInt(Time.realtimeSinceStartup - Mathf.Max(listeningSince, lastUtteranceAt)) : 0;
                guiTextBox = enableKeyboardFallback && textBoxOpen;
            }

            Vector2 v = HudTheme.Begin();
            DrawStatusBar(v);
            DrawMicDropdown();
            DrawMicLevel();
            if (guiTextBox) DrawTypedTextBox(v);
            HudTheme.End();
        }

        // Top bar: microphone / recognizer state on the left, status text in the middle, input modality of the last utterance on the right.
        void DrawStatusBar(Vector2 v)
        {
            var bar = new Rect(12f, 10f, v.x - 24f, 44f);
            HudTheme.Panel(bar);

            Color micColor = guiListening ? HudTheme.Success
                           : guiMicStalled ? HudTheme.Warn
                           : State == VoiceInputState.Error ? HudTheme.Reject
                           : State == VoiceInputState.Unavailable ? HudTheme.Warn : HudTheme.TextDim;
            string mic = guiListening ? "MIC LIVE"
                       : guiMicStalled ? "MIC STOPPED"
                       : State == VoiceInputState.Idle ? "MIC OFF"
                       : State == VoiceInputState.Unavailable ? "MIC N/A"
                       : State.ToString().ToUpperInvariant();
            string micHint = guiListening ? "[" + toggleListeningKey + "] stop"
                           : (guiMicStalled || State == VoiceInputState.Idle || State == VoiceInputState.Error) ? "[" + toggleListeningKey + "] start" : "";
            // A live microphone gets a slow pulse so it is never mistaken for "off".
            float pulse = guiListening ? 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
            float midY = bar.y + bar.height * 0.5f;
            HudTheme.Dot(new Vector2(bar.x + 22f, midY), 6f, HudTheme.WithAlpha(micColor, pulse));
            HudTheme.Label(new Rect(bar.x + 38f, bar.y, 124f, bar.height), mic, HudTheme.FontLead, micColor, true, TextAnchor.MiddleLeft);
            HudTheme.Label(new Rect(bar.x + 166f, bar.y, 96f, bar.height), micHint, HudTheme.FontSmall, HudTheme.TextDim, false, TextAnchor.MiddleLeft);

            HudTheme.Rule(new Rect(bar.x + 268f, bar.y + 10f, 1f, bar.height - 20f), HudTheme.WithAlpha(Color.white, 0.16f));

            string status = guiMicStalled ? "The speech recognizer is not running. Press " + toggleListeningKey + " to restart it, or use the keyboard fallback."
                          : guiShowQuietHint ? "No speech heard for " + guiQuietSeconds + " s." : StatusMessage;
            bool narrow = v.x < 1000f;
            string chipText = !HasLastUtterance ? "" : LastUtterance.Modality == InputModality.Voice ? (narrow ? "VOICE" : "LAST INPUT: VOICE")
                                                                                                         : (narrow ? "SIMULATED" : "LAST: SIMULATED, NOT SPEECH");
            float chipW = narrow ? 118f : 236f;
            float rightW = HasLastUtterance ? chipW + 20f : 0f;
            HudTheme.Label(new Rect(bar.x + 284f, bar.y, Mathf.Max(60f, bar.width - 284f - rightW - 12f), bar.height),
                           HudTheme.Fit(status, 110), HudTheme.FontBody, HudTheme.Text, false, TextAnchor.MiddleLeft);

            if (HasLastUtterance)
            {
                bool voice = LastUtterance.Modality == InputModality.Voice;
                HudTheme.Chip(new Rect(bar.xMax - 12f - chipW, bar.y + 10f, chipW, bar.height - 20f),
                              chipText, voice ? HudTheme.Cyan : HudTheme.Warn, HudTheme.FontSmall);
            }
        }

        // Live microphone level (only meaningful on the helper route, where Unity captures the audio): shows at once whether your voice reaches the program.
        void DrawMicLevel()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!guiListening || !usingHelper || !helperFedByUnity) return;
            var r = new Rect(400f, 66f, 190f, 14f);
            float f = Mathf.Clamp01(helperLevelNow * 4f);
            HudTheme.Bar(r, f, f > 0.9f ? HudTheme.Warn : HudTheme.Success);
            HudTheme.Label(new Rect(r.xMax + 8f, r.y - 4f, 150f, 20f), "mic level", HudTheme.FontSmall, HudTheme.TextDim, false, TextAnchor.MiddleLeft);
#endif
        }

        // Microphone device dropdown, directly under the status bar (left). Click the header to open it.
        void DrawMicDropdown()
        {
            var head = new Rect(12f, 60f, 380f, 28f);
            string name = string.IsNullOrEmpty(SelectedMicDevice) ? "(no microphone found)" : HudTheme.Fit(SelectedMicDevice, 36);
            if (GUI.Button(head, "MIC INPUT:  " + name + (micMenuOpen ? "   [close]" : "   [change]")))
            {
                micMenuOpen = !micMenuOpen;
                if (micMenuOpen) RefreshMicDevices();
            }
            if (!micMenuOpen) return;

            float y = head.yMax + 2f;
            int shown = Mathf.Min(micDevices.Length, 8);
            for (int i = 0; i < shown; i++)
            {
                bool sel = micDevices[i] == SelectedMicDevice;
                if (GUI.Button(new Rect(head.x, y, head.width, 26f), (sel ? "> " : "   ") + HudTheme.Fit(micDevices[i], 44)))
                    SelectMic(micDevices[i]);
                y += 27f;
            }
            if (GUI.Button(new Rect(head.x, y, 186f, 26f), "Test microphone (4 s)")) StartMicTest();
            if (GUI.Button(new Rect(head.x + 194f, y, 186f, 26f), "Windows sound settings")) Application.OpenURL("ms-settings:sound");
            y += 30f;
            if (testClip != null) HudTheme.Bar(new Rect(head.x, y, head.width, 8f), Mathf.Clamp01(testPeak * 6f), HudTheme.Success);
            y += 12f;
            HudTheme.Label(new Rect(head.x, y, head.width, 22f), HudTheme.Fit(testResult, 60), HudTheme.FontSmall, HudTheme.Text, false, TextAnchor.UpperLeft);
            HudTheme.Label(new Rect(head.x, y + 22f, head.width, 44f),
                "Speech recognition listens to the Windows DEFAULT input device. To change it: Windows sound settings > Input.",
                HudTheme.FontSmall, HudTheme.TextDim, false, TextAnchor.UpperLeft, true);
        }

        // Shown only while the typed-text box is open (F8): a simulated, non-speech input path that works without the microphone.
        void DrawTypedTextBox(Vector2 v)
        {
            float w = Mathf.Min(700f, v.x - 2f * HudTheme.EdgeMargin), h = 82f;
            var r = new Rect((v.x - w) * 0.5f, v.y - HudTheme.EdgeMargin - HudTheme.BottomHeight(v) - HudTheme.EdgeMargin - h, w, h);
            HudTheme.Panel(r, HudTheme.PanelFill, HudTheme.Warn);
            HudTheme.Label(new Rect(r.x + 16f, r.y + 10f, 180f, 34f), "TYPED (SIMULATED)", HudTheme.FontBody, HudTheme.Warn, true, TextAnchor.MiddleLeft);

            Event e = Event.current;
            // Enter / Escape are read BEFORE the text field is drawn: the field may consume the key event, and a check made
            // after it would then never fire. KeyDown arrives once per key press, so this cannot submit twice.
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    SubmitTypedText();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    e.Use();
                    SetTextBox(false);
                    SetTypedFeedback("", HudTheme.TextDim);
                }
            }

            GUI.SetNextControlName(TypedControlName);
            typedText = HudTheme.Field(new Rect(r.x + 196f, r.y + 10f, w - 212f, 34f), typedText, 80, HudTheme.FontLead + 2);

            // Focus changes are applied after the field exists in this pass (Repaint), never in the Layout pass.
            if (e.type == EventType.Repaint)
            {
                if (focusTextBoxPending) { GUI.FocusControl(TypedControlName); focusTextBoxPending = false; }
                else if (releaseTextFocusPending) { GUI.FocusControl(null); releaseTextFocusPending = false; }
            }

            HudTheme.Label(new Rect(r.x + 16f, r.y + 50f, w - 32f, 24f), HudTheme.Fit(typedFeedback, Mathf.Max(20, Mathf.FloorToInt((w - 32f) / 7.5f))), HudTheme.FontBody, typedFeedbackColor, false, TextAnchor.MiddleLeft);
        }

        void SubmitTypedText()
        {
            string text = (typedText ?? "").Trim();
            if (text.Length == 0)
            {
                SetTypedFeedback("Nothing to send. Type a command such as 'select that', then press Enter.", HudTheme.Warn);
                return;
            }
            typedText = "";
            try
            {
                Submit(text, InputModality.TextFallback, null, float.NaN);
                ParsedIntent p = LastUtterance.Parsed;
                if (UtteranceReceived == null)
                    SetTypedFeedback("Sent '" + text + "' but nothing is listening for commands (no GazeFocusSystem in the scene).", HudTheme.Reject);
                else if (!p.IsActionable)
                    SetTypedFeedback("Sent '" + text + "' (TYPED, simulated). Not understood: " + (p.Error ?? "unknown command") + ". See the result panel.", HudTheme.Warn);
                else
                    SetTypedFeedback("Sent '" + text + "' (TYPED, simulated). The outcome is in the result panel.", HudTheme.Success);
            }
            catch (Exception ex)
            {
                SetTypedFeedback("Could not send: " + ex.Message, HudTheme.Reject);
                UnityEngine.Debug.LogError("[DesktopVoiceInput] Typed submission failed: " + ex);
            }
            focusTextBoxPending = true;   // keep typing: the field stays focused for the next command
        }
    }
}
