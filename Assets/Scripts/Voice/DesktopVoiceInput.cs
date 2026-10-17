using System;
using System.Diagnostics;
using UnityEngine;
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
        public float noSpeechHintAfterSeconds = 10f;

        [Header("Keyboard fallback (SIMULATED input, not speech)")]
        public bool enableKeyboardFallback = true;
        public KeyCode toggleTextBoxKey = KeyCode.F8;

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
        bool textBoxOpen;
        string typedText = "";
        string lastSummary = "(nothing received yet)";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        KeywordRecognizer recognizer;
        bool systemErrorHooked;
#endif

        void Start()
        {
            if (listenOnStart) StartListening();
            else if (!SpeechSupportedOnThisPlatform) { State = VoiceInputState.Unavailable; StatusMessage = "No speech recognizer on this platform. Keyboard fallback only."; }
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
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(toggleListeningKey))
            {
                if (State == VoiceInputState.Listening) StopListening(); else StartListening();
            }
            if (!enableKeyboardFallback) return;
            if (Input.GetKeyDown(toggleTextBoxKey)) textBoxOpen = !textBoxOpen;
            if (textBoxOpen) return; // typing must not trigger number-key commands
            for (int i = 0; i < VoiceIntentParser.FallbackKeyPhrases.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    Submit(VoiceIntentParser.FallbackKeyPhrases[i], InputModality.KeyboardFallback, null, float.NaN);
#endif
        }

        void OnDisable() { StopListening(); }
        void OnDestroy() { StopListening(); }

        /// <summary>Starts the Windows recognizer. Safe to call repeatedly; failures set State/StatusMessage instead of throwing.</summary>
        public void StartListening()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (State == VoiceInputState.Listening) return;
            try
            {
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
                recognizer = new KeywordRecognizer(VoiceIntentParser.KeywordGrammar, ToUnityConfidence(minimumRecognizerConfidence));
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
            float sinceSpeechEndMs = float.NaN;
            try
            {
                double d = (DateTime.Now - (args.phraseStartTime + args.phraseDuration)).TotalMilliseconds;
                if (d >= 0.0 && d < 60000.0) sinceSpeechEndMs = (float)d;
            }
            catch (Exception) { /* leave NaN: no trustworthy timing */ }
            Submit(args.text, InputModality.Voice, args.confidence.ToString(), sinceSpeechEndMs);
        }

        void OnSystemError(SpeechError error)
        {
            State = VoiceInputState.Error;
            StatusMessage = "Speech system error: " + error + ". Use the keyboard fallback.";
            UnityEngine.Debug.LogWarning("[DesktopVoiceInput] " + StatusMessage);
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
            lastSummary = "[" + label + "] '" + (text ?? "") + "' -> " + parsed.Status + "/" + parsed.Kind
                          + " | parse score " + parsed.ParseScore.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                          + " | recognizer confidence " + (recognizerConfidence ?? "n/a")
                          + " | parse " + parseMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ms"
                          + " | NOT EXECUTED (consumer not wired in this phase)";
            UnityEngine.Debug.Log("[DesktopVoiceInput] " + lastSummary);

            Action<UtteranceEvent> handlers = UtteranceReceived;
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
            GUILayout.BeginArea(new Rect(10, 10, 760, 260), GUI.skin.box);

            Color saved = GUI.color;
            GUI.color = State == VoiceInputState.Listening ? new Color(1f, 0.35f, 0.35f)
                      : State == VoiceInputState.Error ? new Color(1f, 0.8f, 0.2f) : Color.white;
            string mic = State == VoiceInputState.Listening ? "● LISTENING - MICROPHONE ON (" + toggleListeningKey + " to stop)"
                       : State == VoiceInputState.Idle ? "Microphone OFF (" + toggleListeningKey + " to start real voice input)"
                       : State.ToString().ToUpperInvariant();
            GUILayout.Label("VOICE INPUT: " + mic);
            GUI.color = saved;
            GUILayout.Label("Status: " + StatusMessage);

            if (State == VoiceInputState.Listening)
            {
                float quiet = Time.realtimeSinceStartup - Mathf.Max(listeningSince, lastUtteranceAt);
                if (quiet > noSpeechHintAfterSeconds) GUILayout.Label("No speech heard for " + Mathf.FloorToInt(quiet) + " s.");
                GUILayout.Label("Grammar: " + string.Join(", ", VoiceIntentParser.KeywordGrammar));
            }

            if (enableKeyboardFallback)
            {
                GUILayout.Label("KEYBOARD FALLBACK = SIMULATED INPUT, NOT SPEECH.  Keys: 1 select, 2 move, 3 release, 4 cancel, 5 reset, 6 focus, 7 help;  "
                                + toggleTextBoxKey + " = typed text");
                if (textBoxOpen)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Typed text (simulated, press Enter):", GUILayout.Width(250));
                    GUI.SetNextControlName("g5_text");
                    typedText = GUILayout.TextField(typedText, 80, GUILayout.Width(380));
                    GUILayout.EndHorizontal();
                    Event e = Event.current;
                    if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                        && GUI.GetNameOfFocusedControl() == "g5_text")
                    {
                        string submitted = typedText;
                        typedText = "";
                        e.Use();
                        Submit(submitted, InputModality.TextFallback, null, float.NaN);
                    }
                }
            }
            GUILayout.Label("Last: " + lastSummary);
            GUILayout.EndArea();
        }
    }
}
