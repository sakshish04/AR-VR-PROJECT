#if UNITY_EDITOR
using System.IO;
using Group05.VoiceVR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Builds the desktop test scene Assets/Scenes/VoiceVR_Main.unity (plain primitives, no packages).
    /// Safety: refuses to run if the scene already exists (never overwrites), never touches build settings,
    /// project settings or packages, and never edits the professor's scripts.
    /// Run from the menu: Group05 > VoiceVR > Build Main Scene.
    /// </summary>
    public static class VoiceVRSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/VoiceVR_Main.unity";
        const string MaterialFolder = "Assets/Scenes/VoiceVR_Materials";

        /// <summary>
        /// Whether the destination is safe to create. The builder proceeds ONLY when every independent indicator agrees
        /// that nothing is there. If any indicator reports something (or they disagree, e.g. the file is on disk but the
        /// AssetDatabase does not know it yet), the destination is treated as occupied/ambiguous and nothing is written.
        /// </summary>
        /// <param name="evidence">Human-readable list of the indicators that fired; empty when the destination is free.</param>
        public static bool IsDestinationFree(out string evidence)
        {
            var found = new System.Collections.Generic.List<string>();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) found.Add("AssetDatabase has a SceneAsset at the path");
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath))) found.Add("AssetDatabase has a GUID for the path");
            if (File.Exists(ScenePath)) found.Add("a file exists on disk");
            if (File.Exists(ScenePath + ".meta")) found.Add("a .meta file exists on disk");
            if (Directory.Exists(ScenePath)) found.Add("a directory with that name exists");
            evidence = string.Join("; ", found.ToArray());
            return found.Count == 0;
        }

        [MenuItem("Group05/VoiceVR/Build Main Scene")]
        public static void BuildFromMenu()
        {
            string evidence;
            if (!IsDestinationFree(out evidence))
            {
                EditorUtility.DisplayDialog("Scene not built",
                    ScenePath + " is already present or its state is unclear (" + evidence + "). Nothing was changed. " +
                    "Rename or move the existing scene yourself if you want a fresh build.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; // user cancelled; nothing touched
            if (!Build()) return;
            EditorUtility.DisplayDialog("Scene built", ScenePath + " was created. Press Play, then use the on-screen controls.", "OK");
        }

        /// <summary>For: Unity -batchmode -projectPath ... -executeMethod Group05.VoiceVR.EditorTools.VoiceVRSceneBuilder.BuildBatch -quit</summary>
        public static void BuildBatch()
        {
            // Exit code 0 = scene created; 2 = refused because the destination is present/ambiguous (nothing written).
            EditorApplication.Exit(Build() ? 0 : 2);
        }

        /// <summary>Creates the scene. Returns false, without writing anything, if the destination is occupied or ambiguous.</summary>
        static bool Build()
        {
            string evidence;
            if (!IsDestinationFree(out evidence))   // re-checked here so no caller can bypass it
            {
                Debug.LogError("[VoiceVRSceneBuilder] Refusing to build: " + ScenePath + " is present or its state is unclear (" + evidence + "). Nothing was changed.");
                return false;
            }
            BuildScene();
            return true;
        }

        static void BuildScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Scenes", "VoiceVR_Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera at roughly seated eye height, facing +Z.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f);
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<DesktopLookController>();

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;

            // Floor: has a collider but NO SpatialTarget, so it is never a valid target.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(1.5f, 1f, 1.5f);
            floor.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Floor", new Color(0.28f, 0.30f, 0.34f));

            // Distractor: solid collider that is deliberately NOT a target (tests "arbitrary colliders are not selectable").
            var distractor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            distractor.name = "Distractor_NotATarget";
            distractor.transform.position = new Vector3(2.6f, 0.6f, 3f);
            distractor.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);
            distractor.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Distractor", new Color(0.45f, 0.45f, 0.45f));

            // Three targets that differ in shape AND colour (shape is a non-colour cue).
            MakeTarget(PrimitiveType.Cube, "TargetA_RedCube", "red cube", new Vector3(-1.4f, 1.3f, 3f), Vector3.one * 0.5f, new Color(0.85f, 0.15f, 0.15f));
            MakeTarget(PrimitiveType.Sphere, "TargetB_BlueSphere", "blue sphere", new Vector3(0f, 1.3f, 3f), Vector3.one * 0.5f, new Color(0.15f, 0.35f, 0.9f));
            MakeTarget(PrimitiveType.Capsule, "TargetC_GreenCapsule", "green capsule", new Vector3(1.2f, 1.3f, 3f), new Vector3(0.4f, 0.4f, 0.4f), new Color(0.15f, 0.7f, 0.25f));

            // Reticle: visual only (collider removed so it can never block the gaze ray).
            var reticle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticle.name = "GazeReticle";
            Object.DestroyImmediate(reticle.GetComponent<Collider>());
            reticle.transform.localScale = Vector3.one * 0.06f;
            reticle.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Reticle", Color.white);

            var systems = new GameObject("VoiceVR_Systems");
            var voice = systems.AddComponent<DesktopVoiceInput>();
            var legacy = systems.AddComponent<SpatialVoiceIntentController>(); // professor's starter, unmodified; its logger is deliberately NOT added
            var gaze = systems.AddComponent<GazeFocusSystem>();
            gaze.gazeCamera = cam;
            gaze.reticle = reticle.transform;
            gaze.voiceInput = voice;
            gaze.legacyController = legacy;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[VoiceVRSceneBuilder] Saved " + ScenePath + ". Build settings were NOT changed.");
        }

        static void MakeTarget(PrimitiveType shape, string objectName, string spokenName, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = objectName;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MakeMaterial(objectName, color);
            var t = go.AddComponent<SpatialTarget>();
            t.displayName = spokenName;
        }

        static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            string path = AssetDatabase.GenerateUniqueAssetPath(MaterialFolder + "/" + name + ".mat");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
#endif
