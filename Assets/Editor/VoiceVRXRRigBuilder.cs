#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Adds a reduced, controller-only XR rig to Assets/Scenes/VoiceVR_Main.unity and registers the scene in Build Settings.
    /// The EXISTING Main Camera (tag, AudioListener, DesktopLookController) is reparented under the rig, never duplicated.
    /// No locomotion, teleportation, grabbing, Rigidbody, EventSystem or controller colliders are added.
    /// Safety: refuses (writing nothing) if an XR Origin already exists, if the camera/AudioListener counts are not exactly one,
    /// if the Main Camera is not a root object, or if the XRI Default Input Actions asset has not been imported.
    /// Run from the menu: Group05 > VoiceVR > Add XR Rig.
    /// </summary>
    public static class VoiceVRXRRigBuilder
    {
        const string ScenePath = VoiceVRSceneBuilder.ScenePath;
        const string InputActionsFileName = "XRI Default Input Actions.inputactions";
        const float EyeHeight = 1.6f;

        /// <summary>Result of the pre-flight checks. Nothing is written unless Ok.</summary>
        public class Preflight
        {
            public bool Ok;
            public readonly List<string> Problems = new List<string>();
            public readonly List<string> Info = new List<string>();
            public InputActionAsset Actions;
            public Camera MainCamera;
        }

        [MenuItem("Group05/VoiceVR/Add XR Rig")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string report;
            bool ok = Build(out report);
            EditorUtility.DisplayDialog(ok ? "XR rig added" : "XR rig NOT added", report, "OK");
        }

        /// <summary>For: Unity -batchmode -projectPath ... -executeMethod Group05.VoiceVR.EditorTools.VoiceVRXRRigBuilder.BuildBatch -quit</summary>
        public static void BuildBatch()
        {
            // Exit code 0 = rig added; 2 = refused by a pre-flight check (nothing written).
            string report;
            bool ok = Build(out report);
            Debug.Log("[VoiceVRXRRigBuilder] " + report);
            EditorApplication.Exit(ok ? 0 : 2);
        }

        /// <summary>Opens the scene, runs the pre-flight checks and, only if they all pass, adds the rig and saves.</summary>
        public static bool Build(out string report)
        {
            if (!File.Exists(ScenePath)) { report = ScenePath + " does not exist. Nothing was changed."; return false; }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Preflight pf = Check(scene);
            if (!pf.Ok)
            {
                report = "Refused. Nothing was changed.\n- " + string.Join("\n- ", pf.Problems.ToArray());
                Debug.LogError("[VoiceVRXRRigBuilder] " + report);
                return false;
            }

            AddRig(pf);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            bool added = EnsureInBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            report = "Added XR Origin + 2 controller ray interactors; reparented the existing Main Camera. " +
                     (added ? "Added the scene to Build Settings." : "Scene was already in Build Settings.") + "\nSaved " + ScenePath +
                     ". NOT tested in Play Mode and NOT verified on a headset.";
            return true;
        }

        /// <summary>Read-only inspection of the open scene and project. Public so tests can call it.</summary>
        public static Preflight Check(UnityEngine.SceneManagement.Scene scene)
        {
            var pf = new Preflight();

            var origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (origins.Length > 0) pf.Problems.Add("An XR Origin already exists (" + origins.Length + " found, first: '" + origins[0].name + "'). Not adding another.");

            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (cams.Length != 1) pf.Problems.Add("Expected exactly one Camera, found " + cams.Length + ".");
            if (listeners.Length != 1) pf.Problems.Add("Expected exactly one AudioListener, found " + listeners.Length + ".");
            if (cams.Length == 1)
            {
                pf.MainCamera = cams[0];
                if (!cams[0].CompareTag("MainCamera")) pf.Problems.Add("The camera is not tagged MainCamera.");
                if (cams[0].transform.parent != null) pf.Problems.Add("The camera is not a root object (parent '" + cams[0].transform.parent.name + "'); it may already be rigged.");
            }

            pf.Actions = FindInputActions();
            if (pf.Actions == null)
                pf.Problems.Add("'" + InputActionsFileName + "' was not found under Assets/. Import it first: Package Manager > XR Interaction Toolkit > Samples > Starter Assets > Import.");
            else if (FindReference(pf.Actions, "XRI Right Interaction/Select") == null)
                pf.Problems.Add("The imported input actions do not expose InputActionReference sub-assets (expected 'XRI Right Interaction/Select').");

            pf.Ok = pf.Problems.Count == 0;
            return pf;
        }

        static InputActionAsset FindInputActions()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:InputActionAsset", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == InputActionsFileName) return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            }
            return null;
        }

        static InputActionReference FindReference(InputActionAsset asset, string mapSlashAction)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset)))
            {
                var r = o as InputActionReference;
                if (r != null && r.action != null && r.action.actionMap != null && r.action.actionMap.name + "/" + r.action.name == mapSlashAction) return r;
            }
            return null;
        }

        static void AddRig(Preflight pf)
        {
            InputActionAsset a = pf.Actions;
            Camera cam = pf.MainCamera;

            var rig = new GameObject("XR Origin (XR Rig)");
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(rig.transform, false);
            offset.transform.localPosition = new Vector3(0f, EyeHeight, 0f); // same world height the desktop camera already had

            // Reparent the EXISTING camera, keeping its world pose, so DesktopLookController and the gaze system are unchanged.
            cam.transform.SetParent(offset.transform, true);

            var origin = rig.AddComponent<XROrigin>();
            origin.Origin = rig;
            origin.CameraFloorOffsetObject = offset;
            origin.Camera = cam;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = EyeHeight; // used only if the runtime reports Device mode

            var manager = rig.AddComponent<InputActionManager>();
            manager.actionAssets = new List<InputActionAsset> { a };

            var head = cam.gameObject.AddComponent<TrackedPoseDriver>();
            ConfigurePose(head, a, "XRI Head");

            BuildController(offset.transform, "Left Controller", a, "XRI Left", "XRI Left Interaction", InteractorHandedness.Left);
            BuildController(offset.transform, "Right Controller", a, "XRI Right", "XRI Right Interaction", InteractorHandedness.Right);
        }

        static void ConfigurePose(TrackedPoseDriver d, InputActionAsset a, string map)
        {
            d.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            d.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            d.positionInput = new InputActionProperty(FindReference(a, map + "/Position"));
            d.rotationInput = new InputActionProperty(FindReference(a, map + "/Rotation"));
            d.trackingStateInput = new InputActionProperty(FindReference(a, map + "/Tracking State"));
        }

        static void BuildController(Transform parent, string name, InputActionAsset a, string poseMap, string interactionMap, InteractorHandedness hand)
        {
            var controller = new GameObject(name);
            controller.transform.SetParent(parent, false);
            ConfigurePose(controller.AddComponent<TrackedPoseDriver>(), a, poseMap);

            // Ray only: no colliders are added anywhere, so it can never block the camera's gaze raycast.
            var rayGo = new GameObject(name.Replace(" Controller", "") + " Ray Interactor");
            rayGo.transform.SetParent(controller.transform, false);
            var ray = rayGo.AddComponent<XRRayInteractor>();
            ray.handedness = hand;
            ray.enableUIInteraction = false; // no UI in the scene, so no EventSystem is needed

            var so = new SerializedObject(ray);
            Bind(so, "m_SelectInput", FindReference(a, interactionMap + "/Select"), FindReference(a, interactionMap + "/Select Value"));
            Bind(so, "m_ActivateInput", FindReference(a, interactionMap + "/Activate"), FindReference(a, interactionMap + "/Activate Value"));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Bind(SerializedObject so, string field, InputActionReference performed, InputActionReference value)
        {
            SerializedProperty p = so.FindProperty(field + ".m_InputActionReferencePerformed");
            SerializedProperty v = so.FindProperty(field + ".m_InputActionReferenceValue");
            if (p == null || v == null) { Debug.LogError("[VoiceVRXRRigBuilder] Could not find serialized reader fields on " + field); return; }
            p.objectReferenceValue = performed;
            v.objectReferenceValue = value;
        }

        /// <summary>Appends the scene to Build Settings if missing; every other entry is kept as is. Returns true if added.</summary>
        public static bool EnsureInBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            foreach (var s in existing) if (s.path == scenePath) return false;
            var list = new List<EditorBuildSettingsScene>(existing) { new EditorBuildSettingsScene(scenePath, true) };
            EditorBuildSettings.scenes = list.ToArray();
            return true;
        }
    }
}
#endif
