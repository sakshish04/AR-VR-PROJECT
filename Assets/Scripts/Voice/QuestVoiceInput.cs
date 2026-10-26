using Meta.WitAi.Dictation.Events;
using Oculus.Voice.Dictation;
using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Voice input for the Meta Quest headset. The Windows recognisers in <see cref="DesktopVoiceInput"/> do not exist on Android, so this component takes the
    /// text from the Meta Voice SDK dictation service (<see cref="AppDictationExperience"/>: the headset's own platform dictation, or Wit.ai as a fallback)
    /// and hands every full transcription to the SAME command pipeline as desktop speech (parser -> gaze system -> HUD -> voice CSV).
    /// Press controller button A to switch listening on and off. Nothing is recognised in the Unity Editor/Simulator by this component; the desktop
    /// recognisers keep doing that there.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestVoiceInput : MonoBehaviour
    {
        [Tooltip("The Meta Voice SDK dictation object (Oculus.Voice.Dictation.AppDictationExperience). Found automatically if left empty.")]
        public AppDictationExperience dictation;
        [Tooltip("The scene's command pipeline. Found automatically if left empty.")]
        public DesktopVoiceInput commandPipeline;
        [Tooltip("Start listening as soon as the scene starts on the headset.")]
        public bool autoStartOnHeadset = true;
        [Tooltip("Listen again after every utterance, so the user does not have to press anything between commands.")]
        public bool keepListening = true;
        [Tooltip("Run in the Unity Editor too (only useful when a Wit.ai app is configured; the Windows recognisers are used in the Editor otherwise).")]
        public bool alsoRunInEditor = false;

        bool wantListening;
        bool hooked;
        float restartAt = -1f;
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

        void OnEnable()
        {
            if (!Enabled) return;
            if (dictation == null) dictation = FindFirstObjectByType<AppDictationExperience>();
            if (commandPipeline == null) commandPipeline = FindFirstObjectByType<DesktopVoiceInput>();
            if (dictation == null) { UnityEngine.Debug.LogWarning("[QuestVoiceInput] No AppDictationExperience in the scene: run Group05 > VoiceVR > Add Quest Voice (Meta Voice SDK)."); return; }
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
            hooked = true;
        }

        void Unhook()
        {
            if (!hooked || dictation == null) { hooked = false; return; }
            dictation.DictationEvents.OnFullTranscription.RemoveListener(OnFull);
            dictation.DictationEvents.OnPartialTranscription.RemoveListener(OnPartial);
            dictation.DictationEvents.OnStoppedListening.RemoveListener(OnStopped);
            dictation.DictationEvents.OnError.RemoveListener(OnError);
            hooked = false;
        }

        public void StartListening()
        {
            if (!Enabled || dictation == null) return;
            wantListening = true;
            try { if (!dictation.Active) dictation.Activate(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[QuestVoiceInput] could not start dictation: " + ex.Message); }
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
            if (!Enabled) return;
            if (OvrControllerReader.ToggleVoicePressed())
            {
                if (wantListening) StopListening(); else StartListening();
            }
            if (wantListening && restartAt > 0f && Time.realtimeSinceStartup >= restartAt)
            {
                restartAt = -1f;
                StartListening();
            }
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
            // The dictation session ends after a pause in speech; open the next one a moment later.
            if (wantListening && keepListening) restartAt = Time.realtimeSinceStartup + 0.4f;
        }

        void OnError(string error, string message)
        {
            UnityEngine.Debug.LogWarning("[QuestVoiceInput] dictation error: " + error + " - " + message);
            if (wantListening && keepListening) restartAt = Time.realtimeSinceStartup + 2.0f;
        }
    }
}
