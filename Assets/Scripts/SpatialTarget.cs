using UnityEngine;

/// <summary>
/// Marks a GameObject as a valid gaze/voice interaction target and draws its focus/selection feedback.
/// Feedback is never colour-only: the world-space label also spells out [FOCUSED] / [SELECTED].
/// </summary>
public class SpatialTarget : MonoBehaviour
{
    public enum VisualState { Normal, Focused, Selected }

    [Tooltip("Name spoken/shown for this target, e.g. 'red cube'.")]
    public string displayName = "";
    public Color focusColor = new Color(1f, 0.92f, 0.1f);
    public Color selectedColor = new Color(0.1f, 1f, 0.4f);
    public bool showLabel = true;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    Renderer[] renderers;
    MaterialPropertyBlock block;
    int colorProperty;
    Color baseColor = Color.white;
    TextMesh label;
    Transform labelTransform;
    VisualState state = VisualState.Normal;

    public VisualState CurrentVisualState { get { return state; } }
    public string DisplayName { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } }

    bool initialized;

    void Awake() { EnsureInitialized(); }

    /// <summary>Idempotent setup; also called by the gaze system so targets work even if Awake has not run (e.g. edit-mode tests).</summary>
    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        if (renderers.Length > 0 && renderers[0].sharedMaterial != null)
        {
            Material m = renderers[0].sharedMaterial;
            colorProperty = m.HasProperty(BaseColorId) ? BaseColorId : ColorId;
            if (m.HasProperty(colorProperty)) baseColor = m.GetColor(colorProperty);
        }
        else
        {
            colorProperty = ColorId;
            if (renderers.Length == 0) Debug.LogWarning("[SpatialTarget] '" + name + "' has no Renderer; highlight disabled.", this);
        }
        if (showLabel) CreateLabel();
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
        label.characterSize = 0.04f;
        label.fontSize = 48;
        label.color = Color.white;
    }

    void LateUpdate()
    {
        if (labelTransform == null) return;
        Camera cam = Camera.main;
        // Keep the label just above the object's bounds and facing the viewer. Not verified in a running scene;
        // the label is a child, so it inherits the parent's scale.
        Bounds b = GetBounds();
        labelTransform.position = new Vector3(b.center.x, b.max.y + 0.12f, b.center.z);
        if (cam != null) labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - cam.transform.position);
    }

    public void SetVisualState(VisualState s)
    {
        state = s;
        if (renderers != null)
        {
            Color c = s == VisualState.Focused ? focusColor : s == VisualState.Selected ? selectedColor : baseColor;
            block.SetColor(colorProperty, c);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].SetPropertyBlock(block);
        }
        if (label != null)
        {
            string tag = s == VisualState.Focused ? " [FOCUSED]" : s == VisualState.Selected ? " [SELECTED]" : "";
            label.text = DisplayName.ToUpperInvariant() + tag;
            label.color = s == VisualState.Normal ? Color.white : (s == VisualState.Focused ? focusColor : selectedColor);
        }
    }

    public Bounds GetBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool any = false;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
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
