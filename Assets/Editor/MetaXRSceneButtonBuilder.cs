#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Adds a world-space "Meta XR" button to the OPEN scene, clickable with the controller rays (XR Interaction Toolkit UI).
    /// Also adds an EventSystem + XRUIInputModule if missing and turns on UI interaction for the existing ray interactors.
    /// Run from: Group05 > VoiceVR > Add Meta XR Button To Scene. Idempotent: does nothing if the button already exists.
    /// </summary>
    public static class MetaXRSceneButtonBuilder
    {
        const string RootName = "MetaXR_ButtonCanvas";

        [MenuItem("Group05/VoiceVR/Add Meta XR Button To Scene")]
        public static void Add()
        {
            if (GameObject.Find(RootName) != null)
            {
                EditorUtility.DisplayDialog("Meta XR button", "The button already exists in this scene. Nothing was changed.", "OK");
                return;
            }

            // Canvas (world space, 0.5 m x 0.25 m)
            var root = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root, "Add Meta XR button");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(500f, 250f);
            rt.localScale = Vector3.one * 0.001f;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

            // Place to the left of the user, facing the origin
            rt.position = new Vector3(-1.2f, 1.4f, 1.5f);
            rt.rotation = Quaternion.LookRotation(rt.position - new Vector3(0f, 1.4f, 0f));

            // Button
            var btnGo = new GameObject("MetaXR_Button", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(root.transform, false);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(480f, 230f);
            var img = btnGo.GetComponent<Image>();
            img.color = new Color(0.05f, 0.55f, 0.75f, 1f);
            var btn = btnGo.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.4f, 0.9f, 1f, 1f);
            colors.pressedColor = new Color(1f, 0.2f, 0.8f, 1f);
            btn.colors = colors;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<Text>();
            label.text = "Meta XR";
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 72;
            label.color = Color.white;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.raycastTarget = false;

            // Click behaviour: persistent listener so it survives in the scene file
            var handler = root.AddComponent<MetaXRButtonHandler>();
            handler.label = label;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, handler.OnClicked);

            // EventSystem with the XR UI input module (needed for ray -> UI clicks)
            var es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                Undo.RegisterCreatedObjectUndo(esGo, "Add EventSystem");
            }
            else if (es.GetComponent<XRUIInputModule>() == null)
            {
                var old = es.GetComponent<BaseInputModule>();
                if (old != null) Object.DestroyImmediate(old);
                es.gameObject.AddComponent<XRUIInputModule>();
            }

            // Existing ray interactors had UI interaction off
            int rays = 0;
            foreach (var ray in Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ray.enableUIInteraction = true;
                EditorUtility.SetDirty(ray);
                rays++;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            EditorUtility.DisplayDialog("Meta XR button added",
                "Added '" + RootName + "' (world space, left of the user) and enabled UI on " + rays + " ray interactor(s).\n" +
                "Save the scene (Ctrl+S). Not tested on a headset.", "OK");
        }
    }

    /// <summary>Runtime click handler for the Meta XR button.</summary>
    public class MetaXRButtonHandler : MonoBehaviour
    {
        public Text label;
        int clicks;

        public void OnClicked()
        {
            clicks++;
            if (label != null) label.text = "Meta XR  x" + clicks;
            Debug.Log("[MetaXR] Button clicked (" + clicks + ")");
        }
    }
}
#endif
