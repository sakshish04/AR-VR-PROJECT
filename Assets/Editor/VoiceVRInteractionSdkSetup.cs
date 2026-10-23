#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Adds the Meta Interaction SDK grab interactions to every <see cref="SpatialTarget"/> in the open scene, using the SDK's own Quick Action wizards
    /// (the same ones as right-click > Interaction SDK > Add Grab Interaction / Add Distance Grab Interaction), with their default settings:
    ///  * Grab: Grabbable + kinematic Rigidbody + HandGrabInteractable (tracked hands) + GrabInteractable (controllers), and the grab interactors on the rig if missing;
    ///  * Distance grab: DistanceHandGrabInteractable + DistanceGrabInteractable, so an object can be pulled from across the room.
    /// A target that already has a Grabbable is skipped, so the menu item is safe to run again. Nothing else in the scene is touched.
    /// The wizards are internal to the SDK, so they are called by reflection. Undo (Ctrl+Z) works for the added components.
    /// Menu: Group05 > VoiceVR > Add Interaction SDK Grab To All Targets
    /// </summary>
    public static class VoiceVRInteractionSdkSetup
    {
        const string WizardBase = "Oculus.Interaction.Editor.QuickActions.QuickActionsWizard";
        const string GrabWizard = "Oculus.Interaction.Editor.QuickActions.GrabWizard";
        const string DistanceGrabWizard = "Oculus.Interaction.Editor.QuickActions.DistanceGrabWizard";

        [MenuItem("Group05/VoiceVR/Add Interaction SDK Grab To All Targets")]
        public static void Run()
        {
            string report = Apply();
            Debug.Log("[VoiceVRInteractionSdkSetup] " + report);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("Interaction SDK on all targets", report + "\n\nSave the scene (Ctrl+S).", "OK");
        }

        public static string Apply()
        {
            Type baseType = FindType(WizardBase), grabType = FindType(GrabWizard), distType = FindType(DistanceGrabWizard);
            if (baseType == null || grabType == null || distType == null)
                return "The Interaction SDK quick-action wizards were not found (is com.meta.xr.sdk.interaction installed?). Nothing changed.";

            MethodInfo create = baseType.GetMethod("CreateWithDefaults", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (create == null) return "QuickActionsWizard.CreateWithDefaults not found in this SDK version. Nothing changed.";

            var targets = new List<SpatialTarget>(UnityEngine.Object.FindObjectsByType<SpatialTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            targets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            int done = 0, skipped = 0, failed = 0;
            var names = new List<string>();

            foreach (SpatialTarget t in targets)
            {
                if (HasGrabbable(t.gameObject)) { skipped++; continue; }
                try
                {
                    Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "Add Interaction SDK grab to " + t.name);
                    // fixOptionals=false, no injections: exactly the wizards' defaults.
                    create.MakeGenericMethod(grabType).Invoke(null, new object[] { t.gameObject, false, null });
                    create.MakeGenericMethod(distType).Invoke(null, new object[] { t.gameObject, false, null });
                    EditorUtility.SetDirty(t.gameObject);
                    done++;
                    names.Add(t.name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Debug.LogWarning("[VoiceVRInteractionSdkSetup] " + t.name + ": " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                }
            }

            if (done > 0) EditorSceneManager.MarkAllScenesDirty();
            return "Targets found: " + targets.Count + ", grab added: " + done + ", already had it: " + skipped + ", failed: " + failed + (names.Count > 0 ? " (" + string.Join(", ", names.ToArray()) + ")" : "") + ".";
        }

        static bool HasGrabbable(GameObject go)
        {
            foreach (Component c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == "Grabbable") return true;
            return false;
        }

        static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
#endif
