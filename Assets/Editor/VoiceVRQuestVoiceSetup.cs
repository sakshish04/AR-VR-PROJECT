#if UNITY_EDITOR
using Oculus.Voice.Dictation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Adds the headset voice objects to the open scene: a Meta Voice SDK dictation object (platform dictation on the Quest, Wit.ai as fallback) and
    /// <see cref="QuestVoiceInput"/>, which feeds the transcriptions into the existing command pipeline. Safe to run twice (it reuses what is there).
    /// Menu: Group05 > VoiceVR > Add Quest Voice (Meta Voice SDK).
    /// </summary>
    public static class VoiceVRQuestVoiceSetup
    {
        [MenuItem("Group05/VoiceVR/Add Quest Voice (Meta Voice SDK)")]
        public static void AddQuestVoice()
        {
            GameObject go = GameObject.Find("QuestVoice");
            if (go == null) go = new GameObject("QuestVoice");
            Undo.RegisterCreatedObjectUndo(go, "Add Quest Voice");

            var dictation = go.GetComponent<AppDictationExperience>();
            if (dictation == null) dictation = Undo.AddComponent<AppDictationExperience>(go);
            var so = new SerializedObject(dictation);
            SerializedProperty platform = so.FindProperty("usePlatformServices");
            if (platform != null) platform.boolValue = true;
            SerializedProperty log = so.FindProperty("enableConsoleLogging");
            if (log != null) log.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var quest = go.GetComponent<QuestVoiceInput>();
            if (quest == null) quest = Undo.AddComponent<QuestVoiceInput>(go);
            quest.dictation = dictation;
            quest.commandPipeline = Object.FindFirstObjectByType<DesktopVoiceInput>();
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            UnityEngine.Debug.Log("[QuestVoiceSetup] Added 'QuestVoice' (AppDictationExperience + QuestVoiceInput). Save the scene. On the headset, button A switches listening on and off. " +
                                  "If the headset has no platform dictation, assign a Wit.ai configuration on the AppDictationExperience (see docs/QUEST_VOICE.md).");
        }
    }
}
#endif
