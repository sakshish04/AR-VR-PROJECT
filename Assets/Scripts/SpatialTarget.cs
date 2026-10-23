using Group05.VoiceVR;
using UnityEngine;

/// <summary>
/// Marks a GameObject as a valid gaze/voice interaction target and draws its focus/selection feedback.
/// Feedback is never colour-only: the world-space label spells out [FOCUSED] / [SELECTED] / [REJECTED], and a lock-on ring
/// around the object changes colour, pulses while selected and flashes red when a command is rejected.
///
/// It also owns the target's pose bookkeeping for MOVE/RELEASE/CANCEL/RESET: the start pose, the pose at SELECT time (so
/// CANCEL can undo), and a short smooth glide when it is moved. The gaze system decides WHEN to move; this only moves.
/// </summary>
public class SpatialTarget : MonoBehaviour
{
    public enum VisualState { Normal, Focused, Selected }

    [Tooltip("Name spoken/shown for this target, e.g. 'red cube'.")]
    public string displayName = "";
    [Tooltip("Used only when 'Use Theme Colors' is off.")]
    public Color focusColor = new Color(1f, 0.92f, 0.1f);
    [Tooltip("Used only when 'Use Theme Colors' is off.")]
    public Color selectedColor = new Color(0.1f, 1f, 0.4f);
    [Tooltip("Focus = cyan, selected = gold, rejected = red (HudTheme), blended over the object's own colour so shape/colour stay recognisable.")]
    public bool useThemeColors = true;
    public bool showLabel = true;
    [Tooltip("Camera-facing ring around the object (focus/selection/rejection cue). Needs the Sprites/Default shader; falls back to Unlit/Color.")]
    public bool showRing = true;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    const int RingSegments = 48;
    const float FlashSeconds = 0.6f;

    Renderer[] renderers;
    // Per-renderer, so a layered asset keeps each part's own colour (and alpha) instead of every part taking renderers[0]'s colour.
    MaterialPropertyBlock[] blocks;
    int[] colorProperties;    // -1 = this renderer's shader has no _BaseColor/_Color (e.g. additive glows use _TintColor): left untouched
    Color[] baseColors;
    TextMesh label;
    Transform labelTransform;
    LineRenderer ring;
    Material ringMaterial;
    float ringAlpha;
    VisualState state = VisualState.Normal;
    float flashUntil;
    bool flashApplied;

    // pose bookkeeping
    Vector3 initialPosition;
    Vector3 selectionPosition;
    bool glideActive;
    Vector3 glideFrom, glideTo;
    float glideElapsed, glideDuration;

    public VisualState CurrentVisualState { get { return state; } }
    public string DisplayName { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } }
    public Vector3 InitialPosition { get { return initialPosition; } }
    /// <summary>Where the target is, or will be when a glide in progress finishes.</summary>
    public Vector3 ResolvedPosition { get { return glideActive ? glideTo : transform.position; } }
    public bool IsMoving { get { return glideActive; } }
    public bool HasMovedFromInitial { get { return (ResolvedPosition - initialPosition).sqrMagnitude > 1e-4f; } }
    public bool IsFlashing { get { return Time.unscaledTime < flashUntil; } }

    bool initialized;

    void Awake() { EnsureInitialized(); }

    /// <summary>Idempotent setup; also called by the gaze system so targets work even if Awake has not run (e.g. edit-mode tests).</summary>
    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        initialPosition = transform.position;
        selectionPosition = initialPosition;
        renderers = GetComponentsInChildren<Renderer>();
        blocks = new MaterialPropertyBlock[renderers.Length];
        colorProperties = new int[renderers.Length];
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            blocks[i] = new MaterialPropertyBlock();
            colorProperties[i] = -1;
            baseColors[i] = Color.white;
            Material m = renderers[i].sharedMaterial;
            if (m == null) continue;
            if (m.HasProperty(BaseColorId)) colorProperties[i] = BaseColorId;
            else if (m.HasProperty(ColorId)) colorProperties[i] = ColorId;
            if (colorProperties[i] >= 0) baseColors[i] = m.GetColor(colorProperties[i]);
        }
        if (renderers.Length == 0) Debug.LogWarning("[SpatialTarget] '" + name + "' has no Renderer; highlight disabled.", this);
        if (showLabel) CreateLabel();
        if (showRing) CreateRing();
        SetVisualState(VisualState.Normal);
    }

    void CreateLabel()
    {
        var go = new GameObject("TargetLabel");
        labelTransform = go.transform;
        labelTransform.SetParent(transform, false);
        label = go.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            label.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        label.fontStyle = FontStyle.Bold;
        label.characterSize = 0.04f;
        label.fontSize = 48;
        label.color = Color.white;
    }

    void CreateRing()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) { showRing = false; return; }
        var go = new GameObject("TargetRing");
        go.transform.SetParent(transform, false);
        ring = go.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = RingSegments;
        ring.widthMultiplier = 0.012f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ringMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        ring.sharedMaterial = ringMaterial;
        ring.enabled = false;
    }

    void OnDestroy()
    {
        if (ringMaterial == null) return;
        if (Application.isPlaying) Destroy(ringMaterial); else DestroyImmediate(ringMaterial);
    }

    // ---- per-frame: glide, flash expiry, label and ring ----

    void Update()
    {
        AdvanceGlide(Time.unscaledDeltaTime);
        bool flashing = IsFlashing;
        if (flashing != flashApplied) ApplyVisuals();   // flash started or ended
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        Bounds b = GetBounds();
        float radius = RingRadius(b);
        if (labelTransform != null)
        {
            // Above the ring so the label never sits inside it; the label is a child, so it inherits the parent's scale.
            labelTransform.position = new Vector3(b.center.x, b.center.y + radius + 0.06f, b.center.z);
            if (cam != null) labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - cam.transform.position);
        }
        UpdateRing(cam, b, radius);
    }

    static float RingRadius(Bounds b) { return b.extents.magnitude * 0.95f + 0.04f; }

    void UpdateRing(Camera cam, Bounds b, float radius)
    {
        if (ring == null) return;
        Color c;
        float targetAlpha;
        if (IsFlashing) { c = HudTheme.Reject; targetAlpha = 1f; }
        else if (state == VisualState.Selected) { c = SelectedColor; targetAlpha = 1f; }
        else if (state == VisualState.Focused) { c = FocusColor; targetAlpha = 0.85f; }
        else { c = FocusColor; targetAlpha = 0f; }

        ringAlpha = Mathf.MoveTowards(ringAlpha, targetAlpha, Time.unscaledDeltaTime * 8f);
        if (ringAlpha <= 0.01f || cam == null) { ring.enabled = false; return; }
        ring.enabled = true;

        c.a = ringAlpha;
        ring.startColor = c;
        ring.endColor = c;
        // Sprites/Default multiplies the vertex colour by _Color, so only the Unlit/Color fallback needs the material colour set.
        if (ringMaterial.shader.name != "Sprites/Default" && ringMaterial.HasProperty(ColorId)) ringMaterial.SetColor(ColorId, c);

        // Selected: a gentle pulse. Everything else: a steady ring.
        float pulse = state == VisualState.Selected && !IsFlashing ? 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
        ring.widthMultiplier = state == VisualState.Selected ? 0.018f : 0.012f;

        Vector3 forward = (b.center - cam.transform.position).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(forward, right);
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * (Mathf.PI * 2f / RingSegments);
            ring.SetPosition(i, b.center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * (radius * pulse));
        }
    }

    // ---- visual state ----

    Color FocusColor { get { return useThemeColors ? HudTheme.Focus : focusColor; } }
    Color SelectedColor { get { return useThemeColors ? HudTheme.Selected : selectedColor; } }

    public void SetVisualState(VisualState s)
    {
        state = s;
        ApplyVisuals();
    }

    /// <summary>Brief red flash on the object and its label: the last command was rejected. Never changes the focus/selection state.</summary>
    public void FlashRejected()
    {
        flashUntil = Time.unscaledTime + FlashSeconds;
        ApplyVisuals();
    }

    void ApplyVisuals()
    {
        bool flashing = IsFlashing;
        flashApplied = flashing;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || colorProperties[i] < 0) continue;
                Color bc = baseColors[i];
                Color c = bc;
                if (flashing) c = Color.Lerp(bc, HudTheme.Reject, 0.7f);
                else if (state == VisualState.Focused) c = Color.Lerp(bc, FocusColor, 0.55f);
                else if (state == VisualState.Selected) c = Color.Lerp(bc, SelectedColor, 0.65f);
                c.a = bc.a;   // keep a part's own transparency (e.g. a glass chamber)
                blocks[i].SetColor(colorProperties[i], c);
                renderers[i].SetPropertyBlock(blocks[i]);
            }
        }
        if (label != null)
        {
            string tag = flashing ? " [REJECTED]" : state == VisualState.Focused ? " [FOCUSED]" : state == VisualState.Selected ? " [SELECTED]" : "";
            label.text = DisplayName.ToUpperInvariant() + tag;
            label.color = flashing ? HudTheme.Reject : state == VisualState.Normal ? Color.white : (state == VisualState.Focused ? FocusColor : SelectedColor);
        }
    }

    // ---- pose bookkeeping and movement (callers decide when; this only moves) ----

    /// <summary>Called when the target is selected: CANCEL will return it to this pose.</summary>
    public void BeginManipulation() { selectionPosition = ResolvedPosition; }

    /// <summary>Moves to <paramref name="destination"/> with a short smooth glide; immediately when seconds &lt;= 0 or outside Play Mode.</summary>
    public void GlideTo(Vector3 destination, float seconds)
    {
        if (seconds <= 0f || !Application.isPlaying)
        {
            glideActive = false;
            transform.position = destination;
            Physics.SyncTransforms();
            return;
        }
        glideFrom = transform.position;
        glideTo = destination;
        glideElapsed = 0f;
        glideDuration = seconds;
        glideActive = true;
    }

    public void RestoreSelectionPose(float seconds) { GlideTo(selectionPosition, seconds); }
    public void ResetToInitialPose(float seconds) { GlideTo(initialPosition, seconds); }

    /// <summary>Advances a glide in progress. Public so it can be driven by a test clock.</summary>
    public void AdvanceGlide(float deltaTime)
    {
        if (!glideActive) return;
        glideElapsed += Mathf.Max(0f, deltaTime);
        float t = glideDuration <= 0f ? 1f : Mathf.Clamp01(glideElapsed / glideDuration);
        float eased = t * t * (3f - 2f * t);
        transform.position = Vector3.Lerp(glideFrom, glideTo, eased);
        if (t >= 1f) { glideActive = false; transform.position = glideTo; }
        Physics.SyncTransforms();
    }

    public Bounds GetBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool any = false;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                // The label/ring are children but must not inflate the target's own bounds.
                if (renderers[i] == null || !renderers[i].enabled || renderers[i].GetComponent<TextMesh>() != null || renderers[i] is LineRenderer) continue;
                if (!any) { b = renderers[i].bounds; any = true; }
                else b.Encapsulate(renderers[i].bounds);
            }
        }
        if (!any) b = new Bounds(transform.position, Vector3.one * 0.2f);
        return b;
    }

    /// <summary>Target "width" for Fitts' Law: the larger horizontal extent of the bounds (metres).</summary>
    public float Width { get { Vector3 s = GetBounds().size; return Mathf.Max(s.x, s.z); } }
}
