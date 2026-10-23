#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Removes the two Meta XR AI building blocks ('[BuildingBlock] Speech To Text' and '[BuildingBlock] Text To Speech',
    /// both set to ElevenLabs with no provider profile) from the open scene. Nothing in Assets/Scripts references them,
    /// and they are what makes the Project Setup Tool report "AI Agent component is missing its provider profile".
    /// Run from: Group05 > VoiceVR > Remove Unused Meta AI Blocks. Undo (Ctrl+Z) works.
    /// </summary>
    public static class RemoveUnusedMetaAIBlocks
    {
        static readonly string[] Names = { "[BuildingBlock] Speech To Text", "[BuildingBlock] Text To Speech" };

        [MenuItem("Group05/VoiceVR/Remove Unused Meta AI Blocks")]
        public static void Run()
        {
            int removed = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go == null || System.Array.IndexOf(Names, go.name) < 0) continue;
                Undo.DestroyObjectImmediate(go);
                removed++;
            }
            if (removed > 0) EditorSceneManager.MarkAllScenesDirty();
            EditorUtility.DisplayDialog("Meta AI blocks",
                removed > 0 ? "Removed " + removed + " object(s). Save the scene (Ctrl+S), then reopen Meta > Tools > Project Setup Tool."
                            : "None found in the open scene. Nothing changed.", "OK");
        }
    }
}
#endif
