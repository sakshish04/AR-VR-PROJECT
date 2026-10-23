using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// One designated place ("dock") for one lab item: a low plinth with a glowing pad and a floating label. The item that belongs here is
    /// <see cref="target"/>. The rules (when an item counts as placed, snapping, messages, progress) live in <see cref="LabTaskManager"/>;
    /// this component only holds the data and draws the dock: dim accent colour while idle, pulsing while its item is being looked at,
    /// held or selected ("this is where it goes"), green when docked, red for a moment when the wrong item was put here.
    /// The visuals are normal scene objects built by <see cref="BuildVisuals"/> (called by the editor builder), so they can be seen and edited
    /// in the Scene view without pressing Play.
    /// </summary>
    [DisallowMultipleComponent]
    public class LabStation : MonoBehaviour
    {
        public enum Visual { Idle, Hint, Filled, Wrong }

        [Header("Which item belongs here")]
        public SpatialTarget target;
        [Tooltip("Short name shown on the label, for example REACTOR DOCK.")]
        public string stationName = "DOCK";
        public Color accent = new Color(0.25f, 0.85f, 1f, 1f);
        [Tooltip("The item counts as placed when its bottom is over the pad and within this distance of the pad centre (metres).")]
        public float snapRadius = 0.40f;

        [Header("Built by the editor builder")]
        public Transform pad;
        public Renderer padRenderer;
        public Renderer ringRenderer;
        public TextMesh label;

        /// <summary>1-based position in the row, left to right as the player faces it.</summary>
        public int number;
        public bool IsFilled { get; internal set; }

        // used by the task manager while it watches the item settle
        internal Vector3 lastPos;
        internal float stillFor;
        internal bool wrongAnnounced;

        bool hint;
        float wrongUntil;
        MaterialPropertyBlock block;
        string shownLabel;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color FilledColor = new Color(0.25f, 0.95f, 0.65f, 1f);
        static readonly Color WrongColor = new Color(1f, 0.35f, 0.4f, 1f);

        public Vector3 PadTop { get { return pad != null ? pad.position : transform.position; } }
        public string ItemName { get { return target != null ? target.DisplayName : "item"; } }

        /// <summary>Where the item's pivot goes so that its bottom rests on the pad.</summary>
        public Vector3 SnapPositionFor(SpatialTarget t)
        {
            Bounds b = t.GetBounds();
            Vector3 pivotOffset = t.transform.position - b.center;
            return PadTop + pivotOffset + Vector3.up * (b.extents.y + 0.005f);
        }

        public void SetHint(bool on) { hint = on; }
        public void ShowWrong(float seconds) { wrongUntil = Time.unscaledTime + seconds; }

        void Awake() { if (block == null) block = new MaterialPropertyBlock(); }

        void LateUpdate()
        {
            Visual v = Time.unscaledTime < wrongUntil ? Visual.Wrong : IsFilled ? Visual.Filled : hint ? Visual.Hint : Visual.Idle;
            Color c;
            switch (v)
            {
                case Visual.Filled: c = FilledColor; break;
                case Visual.Wrong: c = WrongColor; break;
                case Visual.Hint:
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                    c = Color.Lerp(accent, Color.white, pulse * 0.6f);
                    break;
                default: c = accent * 0.7f; c.a = 1f; break;
            }
            Apply(padRenderer, c);
            Apply(ringRenderer, v == Visual.Idle ? accent * 0.45f : c);

            if (label != null)
            {
                string text = stationName + "\n" + (IsFilled ? "DOCKED" : "(" + ItemName + ")");
                if (text != shownLabel) { shownLabel = text; label.text = text; }
                label.color = v == Visual.Idle ? Color.Lerp(accent, Color.white, 0.55f) : c;
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 away = label.transform.position - cam.transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude > 1e-4f) label.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);   // faces the player, stays upright
                }
            }
        }

        void Apply(Renderer r, Color c)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            c.a = 1f;
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }

        /// <summary>
        /// (Re)creates the dock's visible parts under this object: plinth (with the collider the gaze ray hits), pad, ring, label.
        /// Idempotent: earlier parts with the same names are removed first. Works in Edit Mode.
        /// </summary>
        public void BuildVisuals(Material plinthMaterial, Material glowMaterial, Material ringMaterial)
        {
            string[] names = { "Plinth", "Pad", "Ring", "Label" };
            foreach (string n in names)
            {
                Transform old = transform.Find(n);
                while (old != null) { DestroyImmediate(old.gameObject); old = transform.Find(n); }
            }

            // plinth: 0.20 m tall, radius 0.31; its top is at local y = 0.20
            GameObject plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plinth.name = "Plinth";
            plinth.transform.SetParent(transform, false);
            plinth.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            plinth.transform.localScale = new Vector3(0.62f, 0.10f, 0.62f);
            Collider pc = plinth.GetComponent<Collider>();
            if (pc != null) DestroyImmediate(pc);
            plinth.AddComponent<BoxCollider>();               // flat top: a gaze ray and "move there" land on it
            if (plinthMaterial != null) plinth.GetComponent<Renderer>().sharedMaterial = plinthMaterial;

            // ring (slightly wider, sits just under the pad) and pad (glowing disc on top)
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.2005f, 0f);
            ring.transform.localScale = new Vector3(0.64f, 0.0015f, 0.64f);
            DestroyImmediate(ring.GetComponent<Collider>());
            if (ringMaterial != null) ring.GetComponent<Renderer>().sharedMaterial = ringMaterial;

            GameObject padGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            padGo.name = "Pad";
            padGo.transform.SetParent(transform, false);
            padGo.transform.localPosition = new Vector3(0f, 0.2025f, 0f);
            padGo.transform.localScale = new Vector3(0.50f, 0.0015f, 0.50f);
            DestroyImmediate(padGo.GetComponent<Collider>());
            if (glowMaterial != null) padGo.GetComponent<Renderer>().sharedMaterial = glowMaterial;

            foreach (GameObject g in new[] { plinth, ring, padGo })
            {
                Renderer r = g.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            // label, floating above the pad
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, number % 2 == 0 ? 0.88f : 0.70f, 0f);   // alternate heights so neighbouring labels never overlap
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { tm.font = font; labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = 0.0115f;
            tm.text = stationName + "\n(" + ItemName + ")";
            tm.color = Color.Lerp(accent, Color.white, 0.55f);

            pad = padGo.transform;
            padRenderer = padGo.GetComponent<Renderer>();
            ringRenderer = ring.GetComponent<Renderer>();
            label = tm;
        }
    }
}
