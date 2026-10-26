#if UNITY_EDITOR_WIN
using UnityEditor;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Starts the speech engine BEFORE Play mode. An engine launched during Play (especially with the Meta XR Simulator) often starts but hears nothing; one that is already running
    /// keeps listening, and the project attaches to it when Play starts. Menu: Group05 > VoiceVR > Start Voice Engine (before Play) / Stop Voice Engine.
    /// </summary>
    public static class VoiceVRVoiceEngineMenu
    {
        [MenuItem("Group05/VoiceVR/Start Voice Engine (before Play)")]
        public static void Start() { UnityEngine.Debug.Log("[VoiceEngine] " + DesktopVoiceInput.StartExternalEngine()); }

        [MenuItem("Group05/VoiceVR/Stop Voice Engine")]
        public static void Stop() { UnityEngine.Debug.Log("[VoiceEngine] " + DesktopVoiceInput.StopExternalEngine()); }
    }
}
#endif
