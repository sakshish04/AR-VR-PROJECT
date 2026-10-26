#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Adds the <see cref="LiveTrialManager"/> (Phase 05) to the open scene, once. It starts idle: nothing happens until F5 is pressed in Play Mode.
    /// The manager defaults to data origin Selftest and refuses Real data until the professor has approved the protocol.
    /// Menu: Group05 > VoiceVR > Add Live Trial Manager
    /// </summary>
    public static class VoiceVRLiveTrialSetup
    {
        [MenuItem("Group05/VoiceVR/Add Live Trial Manager")]
        public static void Run()
        {
            string report = Apply();
            Debug.Log("[VoiceVRLiveTrialSetup] " + report);
        }

        public static string Apply()
        {
            LiveTrialManager existing = Object.FindFirstObjectByType<LiveTrialManager>();
            if (existing != null) return "A LiveTrialManager already exists (" + existing.gameObject.name + "); nothing changed.";
            var go = new GameObject("LiveTrialManager");
            Undo.RegisterCreatedObjectUndo(go, "Create LiveTrialManager");
            go.AddComponent<LiveTrialManager>();
            EditorSceneManager.MarkAllScenesDirty();
            return "LiveTrialManager added (data origin Selftest, idle until F5). Save the scene (Ctrl+S).";
        }
    }
}
#endif
