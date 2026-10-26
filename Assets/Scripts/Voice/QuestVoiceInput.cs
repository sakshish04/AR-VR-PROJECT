using Meta.WitAi.Configuration;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.Dictation.Events;
using Oculus.Voice.Dictation;
using Oculus.Voice.Dictation.Configuration;
using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Voice input for the Meta Quest headset. The Windows recognisers in <see cref="DesktopVoiceInput"/> do not exist on Android, so this component takes the
    /// text from the Meta Voice SDK dictation service (<see cref="AppDictationExperience"/>) and hands every full transcription to the SAME command pipeline as desktop
    /// speech (parser -> gaze system -> HUD). Press controller button A to switch listening on and off.
    ///
    /// It does nothing in the Unity Editor or on Windows (the desktop recognisers work there), so adding it cannot disturb the desktop project. On an Android build it
    /// creates itself and its dictation object at start-up; no scene change is needed.
    /// The dictation service on a headset without platform dictation is Wit.ai (a Meta cloud service): it needs the headset online and a free Wit.ai client token, put in
    /// Assets/Resources/wit_client_token.txt (kept out of git) or in the field below. See docs/QUEST_VOICE.md. Audio goes to Wit.ai, so participants must be told.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestVoiceInput : MonoBehaviour
    {
        [Tooltip("The Meta Voice SDK dictation object. Created automatically on the headset if left empty.")]
        public AppDictationExperience dictation;
        [Tooltip("The scene's command pipeline. Found automatically if left empty.")]
        public DesktopVoiceInput commandPipeline;
        [Tooltip("Wit.ai CLIENT access token (not the server token). Leave empty to read Assets/Resources/wit_client_token.txt instead.")]
        public string witClientToken = "";
        [Tooltip("Start listening as soon as the scene starts on the headset.")]
        public bool autoStartOnHeadset = true;
        [Tooltip("Listen again after every utterance, so the user does not have to press anything between commands.")]
        public bool keepListening = true;
        [Tooltip("Run in the Unity Editor too (only useful with a Wit.ai token; the Windows recognisers are used in the Editor otherwise).")]
        public bool alsoRunInEditor = false;

        bool wantListening;
        bool hooked;
        float restartAt = -1f;
        float nextWatchdog;
        int inactiveChecks;
        float lastStartAt;
        float micPeak, micLogAt;
        string lastPartial = "";

        public bool WantListening { get { return wantListening; } }
        public string LastPartial { get { return lastPartial; } }

        bool Enabled
        {
            get
            {
#if UNITY_EDITOR
                return alsoRunInEditor;
#else
                return Application.platform == RuntimePlatform.Android;
#endif
            }
        }

        // On a headset build, make sure the component exists even though the scene does not contain it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (FindFirstObjectByType<QuestVoiceInput>() != null) return;
            new GameObject("QuestVoice (auto)").AddComponent<QuestVoiceInput>();
#endif
        }

        string ResolveToken()
        {
            if (!string.IsNullOrWhiteSpace(witClientToken)) return witClientToken.Trim();
            TextAsset t = Resources.Load<TextAsset>("wit_client_token");
            return t != null && !string.IsNullOrWhiteSpace(t.text) ? t.text.Trim() : "";
        }

        void Notice(string message)
        {
            UnityEngine.Debug.Log("[QuestVoiceInput] " + message);
            if (commandPipeline != null) commandPipeline.ExternalStatusMessage(message);
        }

        bool CreateDictation()
        {
            string token = ResolveToken();
            if (token.Length == 0)
            {
                Notice("Quest voice needs a Wit.ai client token (see docs/QUEST_VOICE.md). Controller and gaze still work.");
                return false;
            }
            var host = new GameObject("QuestDictation");
            host.transform.SetParent(transform, false);
            host.SetActive(false);                      // configure before the service starts
            var d = host.AddComponent<AppDictationExperience>();
            var cfg = ScriptableObject.CreateInstance<WitConfiguration>();
            cfg.SetClientAccessToken(token);
            // dictationConfiguration must NOT be null: the service reads dictationConfiguration.multiPhrase every time a session stops, and a null here threw a NullReferenceException
            // that left the next session deaf (seen on the headset: the first command worked, then nothing). multiPhrase makes the service open the next session by itself.
            d.RuntimeDictationConfiguration = new WitDictationRuntimeConfiguration { witConfiguration = cfg, dictationConfiguration = new DictationConfiguration { multiPhrase = true } };
            d.UsePlatformIntegrations = true;           // use the headset's own dictation when it exists, Wit.ai otherwise
            host.SetActive(true);
            dictation = d;
            return true;
        }

        void OnEnable()
        {
            if (!Enabled) return;
            if (commandPipeline == null) commandPipeline = FindFirstObjectByType<DesktopVoiceInput>();
            if (dictation == null) dictation = FindFirstObjectByType<AppDictationExperience>();
            if (dictation == null && !CreateDictation()) return;
            Hook();
            if (autoStartOnHeadset) StartListening();
        }

        void OnDisable()
        {
            Unhook();
        }

        void Hook()
        {
            if (hooked || dictation == null) return;
            dictation.DictationEvents.OnFullTranscription.AddListener(OnFull);
            dictation.DictationEvents.OnPartialTranscription.AddListener(OnPartial);
            dictation.DictationEvents.OnStoppedListening.AddListener(OnStopped);
            dictation.DictationEvents.OnError.AddListener(OnError);
            dictation.DictationEvents.OnMicLevelChanged.AddListener(OnMicLevel);
            hooked = true;
        }

        void Unhook()
        {
            if (!hooked || dictation == null) { hooked = false; return; }
            dictation.DictationEvents.OnFullTranscription.RemoveListener(OnFull);
            dictation.DictationEvents.OnPartialTranscription.RemoveListener(OnPartial);
            dictation.DictationEvents.OnStoppedListening.RemoveListener(OnStopped);
            dictation.DictationEvents.OnError.RemoveListener(OnError);
            dictation.DictationEvents.OnMicLevelChanged.RemoveListener(OnMicLevel);
            hooked = false;
        }

        public void StartListening()
        {
            if (!Enabled || dictation == null) return;
            wantListening = true;
            try { if (!dictation.Active) dictation.Activate(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[QuestVoiceInput] could not start dictation: " + ex.Message); }
            lastStartAt = Time.realtimeSinceStartup;
            if (commandPipeline != null) commandPipeline.ExternalListeningChanged(true, "Quest dictation");
        }

        public void StopListening()
        {
            wantListening = false;
            try { if (dictation != null && dictation.Active) dictation.Deactivate(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[QuestVoiceInput] could not stop dictation: " + ex.Message); }
            if (commandPipeline != null) commandPipeline.ExternalListeningChanged(false, "Quest dictation");
        }

        void Update()
        {
            if (!Enabled || dictation == null) return;
            if (OvrControllerReader.ToggleVoicePressed())
            {
                if (wantListening) StopListening(); else StartListening();
            }
            if (wantListening && restartAt > 0f && Time.realtimeSinceStartup >= restartAt)
            {
                restartAt = -1f;
                StartListening();
            }
            // Watchdog: if the user wants to listen but the service is not active (for example after the headset was taken off and put on again, which pauses the app),
            // open a new session. Seen on the headset: after a pause the dictation never started again.
            if (wantListening && keepListening && Time.realtimeSinceStartup >= nextWatchdog)
            {
                nextWatchdog = Time.realtimeSinceStartup + 2.5f;
                bool active = false;
                try { active = dictation.Active; } catch (System.Exception) { }
                inactiveChecks = active ? 0 : inactiveChecks + 1;
                if (inactiveChecks >= 2 && restartAt < 0f) { inactiveChecks = 0; StartListening(); }
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (!Enabled || dictation == null) return;
            if (paused) return;
            // Back from a pause (headset put on again): the previous session is dead, start a fresh one.
            if (wantListening)
            {
                try { if (dictation.Active) dictation.Deactivate(); } catch (System.Exception) { }
                restartAt = Time.realtimeSinceStartup + 0.8f;
            }
        }

        // Diagnostics: the loudest microphone level seen in each 3 s, in the device log ("[QuestVoiceInput] mic level"), so a silent microphone can be told from a service problem.
        void OnMicLevel(float level)
        {
            if (level > micPeak) micPeak = level;
            if (Time.realtimeSinceStartup < micLogAt) return;
            micLogAt = Time.realtimeSinceStartup + 3f;
            UnityEngine.Debug.Log("[QuestVoiceInput] mic level peak " + micPeak.ToString("F4") + " (last 3 s), listening=" + wantListening);
            micPeak = 0f;
        }

        void OnPartial(string text)
        {
            lastPartial = text ?? "";
        }

        void OnFull(string text)
        {
            lastPartial = "";
            if (string.IsNullOrWhiteSpace(text)) return;
            if (commandPipeline != null) commandPipeline.SubmitExternalTranscription(text.Trim(), "/QuestDictation");
        }

        void OnStopped()
        {
            // With multiPhrase on, the service opens the next session by itself; the watchdog in Update only steps in if it did not.
        }

        void OnError(string error, string message)
        {
            Notice("Dictation problem: " + error + " - " + message + ". Check that the headset is online and the Wit.ai token is right.");
            if (wantListening && keepListening) restartAt = Time.realtimeSinceStartup + 3.0f;
        }
    }
}
