using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace Group05.VoiceVR
{
    /// <summary>
    /// A small head-locked status panel that is part of the 3D scene, so the XR camera renders it. The existing HUD is IMGUI
    /// (OnGUI in GazeFocusSystem / DesktopVoiceInput, drawn with HudTheme): that is a 2D overlay on the desktop Game view and is
    /// never part of what a headset or the Meta XR Simulator shows. This panel is the minimal headset-visible counterpart.
    ///
    /// It holds NO state of its own. Every line is read live from the same sources the desktop HUD uses
    /// (GazeFocusSystem focus / selection / last result / CommandHints, and DesktopVoiceInput microphone state), so the two
    /// can not disagree. It is read-only: it never submits a command and never changes selection.
    ///
    /// Behaviour:
    ///  * Creates itself when the scene loads (no scene edit, no Inspector setup) and shows ONLY while an XR device is active.
    ///    The desktop view is unchanged (the IMGUI HUD stays the desktop display). Set <see cref="showOnDesktop"/> to preview it.
    ///  * Parented to the Main Camera, above the view centre (the targets sit at or below eye level), so it does not cover them. Head-locked panels can feel
    ///    sticky in a real headset; this is the minimal version, not a validated accessible UI.
    ///  * Built from TextMesh + one quad with its collider removed, so it can never intercept the gaze ray.
    ///  * Updates at 5 Hz, only rebuilding text that changed. Does not render depth-aware: it can be hidden by a wall if the
    ///    camera is closer than <see cref="distance"/> to one.
    /// The microphone keys (F9, F8, 1-7) still need the Unity window focused; this panel shows the state but cannot change it.
    /// </summary>
    [DisallowMultipleComponent]
    public class XrHudPanel : MonoBehaviour
    {
        [Tooltip("Metres in front of the camera.")]
        public float distance = 1.2f;
        [Tooltip("Metres above (+) or below (-) the view centre: the TOP edge of the panel. Positive keeps the panel above the targets, which sit at or below eye level.")]
        public float verticalOffset = 0.66f;
        [Tooltip("World height of one line of text, in metres (0.042 m at 1.2 m is about 2 degrees; 0.026 m was too small to read in the simulator).")]
        public float lineHeightMeters = 0.042f;
        [Tooltip("Preview the panel in the plain desktop view too (it is normally XR-only).")]
        public bool showOnDesktop = false;

        const int MaxChars = 42;
        const float UpdateInterval = 0.2f;
        const int RowCount = 6;
        // Max wrapped lines per row: mic, looking at, selected, last result, say / typed. Sum 11 keeps the panel inside the upper view.
        static readonly int[] RowMaxLines = { 2, 2, 2, 3, 2, 2 };

        // Transparent objects are sorted by distance from the camera, so without an explicit order the plate was drawn AFTER the lower
        // text rows (they are farther from the camera than the plate's centre) and dimmed them. A sorting order wins over distance.
        const int PlateSortingOrder = 100, TextSortingOrder = 101;
        // XR-only plate colour (the shared desktop HudTheme.PanelFill is left alone): more opaque, so the bright scene never shows through the text.
        static readonly Color PlateFill = new Color(0.03f, 0.05f, 0.09f, 0.94f);

        GazeFocusSystem gaze;
        DesktopVoiceInput voice;
        Transform root;
        Transform background;
        Material backgroundMaterial;
        readonly TextMesh[] rows = new TextMesh[RowCount];
        readonly MeshRenderer[] rowRenderers = new MeshRenderer[RowCount];
        readonly string[] lastText = new string[RowCount];
        readonly int[] rowLines = new int[RowCount];
        float lineUnits;          // height of one text line in the panel's own (unscaled) units, measured once
        bool calibrated;
        int calibrationTries;
        float nextUpdate;
        bool visible;
        bool reportedFirstShow;
        bool usedFallbackCalibration;
        Camera parentCamera;

        /// <summary>
        /// The one live panel. The panel's GameObject is HideFlags.DontSave, which FindFirstObjectByType does NOT return (the F10 probe
        /// reported "NOT created" for a panel that was visibly shown), so other code must use this instead of searching the scene.
        /// </summary>
        public static XrHudPanel Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("XrHudPanel (runtime, not saved)");
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<XrHudPanel>();
        }

        void Awake()
        {
            Instance = this;
            var rootGo = new GameObject("XR HUD Panel");
            root = rootGo.transform;
            root.SetParent(transform, false);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < RowCount; i++)
            {
                var go = new GameObject("Row" + i);
                go.transform.SetParent(root, false);
                var tm = go.AddComponent<TextMesh>();
                var mr = go.GetComponent<MeshRenderer>();
                if (font != null) { tm.font = font; mr.sharedMaterial = font.material; }
                tm.anchor = TextAnchor.UpperCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontSize = 96;   // raster size only: Calibrate() rescales the panel to lineHeightMeters, so a larger glyph atlas just means sharper text
                tm.characterSize = 0.1f;
                tm.text = "";
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sortingOrder = TextSortingOrder;
                rows[i] = tm;
                rowRenderers[i] = mr;
            }

            // Dark backing plate. The primitive's MeshCollider is removed immediately: a collider here would sit in front of the
            // scene and the gaze raycast (nearest collider wins) would hit the HUD instead of the targets behind it.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Background";
            Collider c = quad.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
            quad.transform.SetParent(root, false);
            background = quad.transform;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var qr = quad.GetComponent<MeshRenderer>();
            qr.sortingOrder = PlateSortingOrder;
            if (sh != null)
            {
                backgroundMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                backgroundMaterial.color = PlateFill;
                qr.sharedMaterial = backgroundMaterial;
                qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                qr.receiveShadows = false;
            }
            rootGo.SetActive(false);
            Debug.Log("[XrHudPanel] created. It stays hidden on the desktop and shows only while an XR device is active (XRSettings.isDeviceActive).");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (backgroundMaterial != null) Destroy(backgroundMaterial);
        }

        void Update()
        {
            bool shouldShow = XRSettings.isDeviceActive || showOnDesktop;
            if (shouldShow != visible)
            {
                visible = shouldShow;
                root.gameObject.SetActive(visible);
                if (visible) nextUpdate = 0f;
            }
            if (!visible) return;

            Camera cam = Camera.main;
            if (cam != null && cam != parentCamera)
            {
                parentCamera = cam;
                root.SetParent(cam.transform, false);
                root.localRotation = Quaternion.identity;
            }
            if (parentCamera == null) return;

            if (!calibrated) { Calibrate(); return; }
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + UpdateInterval;
            Refresh();
            if (!reportedFirstShow)
            {
                reportedFirstShow = true;   // ONE line, so the Console proves the panel really was shown, where, and whether it is in the camera's view
                Debug.Log("[XrHudPanel] shown in XR. " + DebugState());
            }
        }

        /// <summary>
        /// A one-line description of where the panel is and whether the camera can see it (also printed in the F10 report). "In view" is tested
        /// with the camera's frustum against the panel's real bounds, so a wrong parent, scale or offset shows up here instead of as a silent blank.
        /// </summary>
        public string DebugState()
        {
            if (root == null) return "not created";
            var sb = new StringBuilder();
            sb.Append("visible=").Append(visible).Append(" xrActive=").Append(XRSettings.isDeviceActive);
            sb.Append(" parent=").Append(root.parent != null ? root.parent.name : "none");
            sb.Append(" calibrated=").Append(calibrated).Append(usedFallbackCalibration ? "(FALLBACK size guess)" : "");
            sb.Append(" worldScale=").Append(root.lossyScale.x.ToString("F4"));
            sb.Append(" worldPos=").Append(root.position.ToString("F2"));
            if (parentCamera != null && background != null)
            {
                var r = background.GetComponent<Renderer>();
                if (r != null)
                {
                    Bounds b = r.bounds;
                    bool inFrustum = GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(parentCamera), b);
                    sb.Append(" panelSizeMetres=").Append(b.size.x.ToString("F2")).Append("x").Append(b.size.y.ToString("F2"));
                    sb.Append(" distanceFromCamera=").Append(Vector3.Distance(parentCamera.transform.position, b.center).ToString("F2"));
                    sb.Append(" inCameraFrustum=").Append(inFrustum);
                }
            }
            int nonEmpty = 0;
            for (int i = 0; i < RowCount; i++) if (rowLines[i] > 0) nonEmpty++;
            sb.Append(" textRows=").Append(nonEmpty).Append("/").Append(RowCount);
            return sb.ToString();
        }

        // TextMesh has no fixed relation between characterSize and metres, so measure one line once and scale the whole panel to the
        // wanted line height. (Falls back to a typical value if the renderer never reports a size.)
        void Calibrate()
        {
            root.localScale = Vector3.one;
            root.localPosition = new Vector3(0f, verticalOffset, distance);
            rows[0].text = "Mg";
            float h = rowRenderers[0].bounds.size.y;
            if (h <= 1e-5f && ++calibrationTries < 30) return;
            usedFallbackCalibration = h <= 1e-5f;
            lineUnits = h > 1e-5f ? h : 0.4f;
            root.localScale = Vector3.one * (lineHeightMeters / lineUnits);
            calibrated = true;
            rows[0].text = "";
            nextUpdate = 0f;
        }

        void Refresh()
        {
            if (gaze == null) gaze = FindFirstObjectByType<GazeFocusSystem>();
            if (voice == null) voice = FindFirstObjectByType<DesktopVoiceInput>();

            string text; Color color;

            // Row 0: microphone / input state (the desktop status bar equivalent)
            MicRow(out text, out color); SetRow(0, text, color);

            if (gaze != null)
            {
                float now = Time.realtimeSinceStartup;
                // Row 1: gaze focus (+ whether the dwell requirement is met, like the desktop READY/HOLD bar)
                if (gaze.FocusedTarget != null)
                {
                    bool ready = gaze.Model.MinFocusSeconds <= 0f || gaze.Model.FocusAge(now) >= gaze.Model.MinFocusSeconds;
                    SetRow(1, "LOOKING AT: " + gaze.FocusedTarget.DisplayName + (ready ? "  (ready)" : "  (hold...)"), HudTheme.Focus);
                }
                else SetRow(1, "LOOKING AT: nothing", HudTheme.TextDim);

                // Row 2: selection
                if (gaze.SelectedTarget != null) SetRow(2, "SELECTED: " + gaze.SelectedTarget.DisplayName + (gaze.Model.IsCarrying ? "  (carrying: say 'drop this object')" : ""), HudTheme.Selected);
                else SetRow(2, gaze.Model.MovedCount > 0 ? "SELECTED: none  (" + gaze.Model.MovedCount + " moved: say 'reset')" : "SELECTED: none", HudTheme.TextDim);

                // Row 3: the last command and what happened (same string the Console and desktop HUD use)
                if (gaze.HasLastResult)
                {
                    Color oc;
                    switch (gaze.LastResult.Outcome)
                    {
                        case CommandOutcome.Executed: oc = HudTheme.Success; break;
                        case CommandOutcome.Rejected: oc = HudTheme.Reject; break;
                        case CommandOutcome.Cancelled: oc = HudTheme.Warn; break;
                        default: oc = HudTheme.Info; break;
                    }
                    SetRow(3, "LAST: " + gaze.LastCommandLabel, oc);
                }
                else SetRow(3, "LAST: no command yet", HudTheme.TextDim);

                // Row 4: what to say now (the same CommandHints() the desktop HUD shows). While the F8 typed-command box is open it shows
                // the typed text instead: that box is IMGUI (desktop Game view only), so without this the headset view would show nothing.
                if (voice != null && voice.TextBoxOpen)
                    SetRow(4, "TYPED (SIMULATED): " + HudText.TypedBoxText(voice.TypedText, 18) + "  [Enter] send  [Esc] close", HudTheme.Warn);
                else
                    SetRow(4, "SAY: " + HudText.CommandHints(gaze.Model.HasSelection, true, gaze.Model.IsCarrying), HudTheme.Text);
            }
            else
            {
                SetRow(1, "No GazeFocusSystem in the scene.", HudTheme.Reject);
                SetRow(2, "", HudTheme.TextDim); SetRow(3, "", HudTheme.TextDim); SetRow(4, "", HudTheme.TextDim);
            }
            // Row 5: the lab task (docks): progress, and where the looked-at / selected item belongs
            LabTaskManager lab = LabTaskManager.Instance;
            if (lab != null && lab.Total > 0)
                SetRow(5, "LAB " + LabPlacementRules.Progress(lab.PlacedCount, lab.Total) + ": " + lab.StatusLine(), lab.Completed ? HudTheme.Success : HudTheme.Info);
            else SetRow(5, "", HudTheme.TextDim);
            Layout();
        }

        void MicRow(out string text, out Color color)
        {
            if (voice == null) { text = "VOICE: no DesktopVoiceInput in the scene"; color = HudTheme.Reject; return; }
            string status = voice.StatusMessage ?? "";
            switch (voice.State)
            {
                case VoiceInputState.Listening:
                    if (status.StartsWith("Heard")) { text = status; color = HudTheme.Warn; }
                    else { text = "MIC LIVE: " + voice.SelectedMicDevice + "  (controller A stops it)"; color = HudTheme.Success; }
                    break;
                case VoiceInputState.Idle:
                    text = "MIC OFF: press controller A (or F9 in Unity) to talk";
                    color = HudTheme.Warn;
                    break;
                case VoiceInputState.Unavailable:
                    text = "NO SPEECH ON THIS DEVICE: keys 1-7 / F8 typed commands only"; color = HudTheme.Warn;
                    break;
                default:
                    text = "MIC ERROR: " + status; color = HudTheme.Reject;
                    break;
            }
        }

        void SetRow(int i, string text, Color color)
        {
            // Capped so the worst case (a long rejection message) stays about 11 lines and the panel's bottom edge stays above the view centre.
            string wrapped = HudText.WrapLimited(text ?? "", MaxChars, RowMaxLines[i]);
            rows[i].color = color;
            if (lastText[i] == wrapped) return;
            lastText[i] = wrapped;
            rows[i].text = wrapped;
            rowLines[i] = HudText.CountLines(wrapped);
        }

        // Stacks the rows (each one or more lines) and sizes the backing plate to the real text size.
        void Layout()
        {
            float step = lineUnits * 1.25f;
            float gap = lineUnits * 0.25f;
            float y = 0f;
            for (int i = 0; i < RowCount; i++)
            {
                rows[i].transform.localPosition = new Vector3(0f, -y, 0f);
                if (rowLines[i] > 0) y += rowLines[i] * step + gap;
            }
            float scale = Mathf.Max(1e-5f, root.lossyScale.x);
            float widest = 0f;
            for (int i = 0; i < RowCount; i++)
                if (rowLines[i] > 0) widest = Mathf.Max(widest, rowRenderers[i].bounds.size.x / scale);
            float pad = lineUnits * 0.9f;
            float w = Mathf.Max(widest, lineUnits * 8f) + pad * 2f, h = Mathf.Max(y, step) + pad;
            background.localScale = new Vector3(w, h, 1f);
            background.localPosition = new Vector3(0f, -(y - gap) * 0.5f - lineUnits * 0.1f, 0.01f / scale);
        }
    }
}
