#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Offline replacement for the Project Setup Tool's "Create the AI Agent provider profile" fix.
    /// Meta's fix looks the provider up in a registry it downloads at editor start; if that download failed the lookup
    /// throws "Sequence contains no matching element" and nothing is created. This creates the ElevenLabs provider assets
    /// directly (same fields Meta fills in) and assigns them to the Speech To Text / Text To Speech blocks in the open scene.
    /// Reflection only, so it compiles regardless of assembly references.
    /// Run from: Group05 > VoiceVR > Create Meta AI Provider Profiles. You still need to enter your ElevenLabs API key.
    /// </summary>
    public static class CreateMetaAIProviderProfiles
    {
        const string Dir = "Assets/MetaXR";

        [MenuItem("Group05/VoiceVR/Create Meta AI Provider Profiles")]
        public static void Run()
        {
            Type providerType = FindType("ElevenLabsProvider");
            if (providerType == null)
            {
                EditorUtility.DisplayDialog("Meta AI provider", "ElevenLabsProvider type not found. Is com.meta.xr.sdk.core installed?", "OK");
                return;
            }

            Directory.CreateDirectory(Dir);
            string report = "";
            int assigned = 0;

            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                bool stt = tn == "SpeechToTextAgent", tts = tn == "TextToSpeechAgent";
                if (!stt && !tts) continue;

                var so = new SerializedObject(mb);
                var prop = so.FindProperty("providerAsset");
                if (prop == null) { report += "\n- " + tn + ": no providerAsset field."; continue; }
                if (prop.objectReferenceValue != null) { report += "\n- " + tn + ": already has a provider, skipped."; continue; }

                string baseName = (stt ? "SpeechToText" : "TextToSpeech") + "_ElevenLabs_ProviderProfile";
                string path = AssetDatabase.GenerateUniqueAssetPath(Dir + "/" + baseName + ".asset");
                var asset = ScriptableObject.CreateInstance(providerType);
                var aso = new SerializedObject(asset);
                SetString(aso, "endpoint", "https://api.elevenlabs.io");
                SetString(aso, "model", stt ? "scribe_v1" : "eleven_flash_v2_5");
                SetString(aso, "voiceId", "21m00Tcm4TlvDq8ikWAM");
                SetString(aso, "sttLanguage", "");
                aso.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(asset, path);

                prop.objectReferenceValue = asset;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(mb);
                assigned++;
                report += "\n- " + tn + ": created " + path;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (assigned > 0) EditorSceneManager.MarkAllScenesDirty();
            EditorUtility.DisplayDialog("Meta AI provider profiles",
                (assigned > 0 ? "Assigned " + assigned + " provider profile(s). Save the scene (Ctrl+S).\n" : "Nothing assigned.\n") + report +
                "\n\nNext: select each profile in Assets/MetaXR and enter your ElevenLabs API key (or set it in Meta > Tools > AI credentials).", "OK");
        }

        static Type FindType(string name)
        {
            foreach (var t in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
                if (t.Name == name) return t;
            return null;
        }

        static void SetString(SerializedObject so, string field, string value)
        {
            var p = so.FindProperty(field);
            if (p != null && p.propertyType == SerializedPropertyType.String) p.stringValue = value;
        }
    }
}
#endif
