using System.Collections.Generic;
using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Shared look for the on-screen HUD (IMGUI). A dark navy / cyan sci-fi style laid out in a virtual 1280x720 space.
    ///
    /// Scaling: layouts are written in virtual units, but every rectangle and every font size is converted to WHOLE SCREEN PIXELS
    /// when it is drawn (see <see cref="Px"/>). GUI.matrix is deliberately not scaled: scaling the matrix resamples the glyph
    /// textures at fractional sizes, which is what made the text look soft and distorted. With pixel-snapped rectangles and an
    /// integer font size the text is rasterised at its real size and stays sharp at any resolution.
    ///
    /// Everything draws with FIXED rectangles (GUI.*, never GUILayout), so the number of controls cannot differ between
    /// IMGUI passes and "Invalid GUILayout state" cannot occur. Desktop Play Mode only: IMGUI is a screen overlay and is NOT
    /// visible in a headset. Textures are generated in code (no assets, no packages).
    /// </summary>
    public static class HudTheme
    {
        public const float VirtualWidth = 1280f, VirtualHeight = 720f;

        // ---- palette (restrained cyan/blue on dark navy) ----
        public static readonly Color PanelFill = new Color(0.04f, 0.06f, 0.10f, 0.82f);
        public static readonly Color PanelBorder = new Color(0.20f, 0.62f, 0.78f, 0.60f);
        public static readonly Color Cyan = new Color(0.25f, 0.85f, 1.00f, 1f);
        public static readonly Color Text = new Color(0.92f, 0.96f, 1.00f, 1f);
        public static readonly Color TextDim = new Color(0.66f, 0.76f, 0.88f, 1f);   // still > 7:1 against PanelFill
        public static readonly Color Success = new Color(0.25f, 0.95f, 0.65f, 1f);
        public static readonly Color Reject = new Color(1.00f, 0.42f, 0.48f, 1f);
        public static readonly Color Warn = new Color(1.00f, 0.78f, 0.25f, 1f);
        public static readonly Color Info = new Color(0.66f, 0.58f, 1.00f, 1f);
        /// <summary>Target feedback colours (also used by SpatialTarget): focus = cyan, selected = gold, rejected = red.</summary>
        public static readonly Color Focus = Cyan;
        public static readonly Color Selected = new Color(1.00f, 0.80f, 0.20f, 1f);

        // ---- typography (virtual px at 1280x720; converted to real px by Scale) ----
        public const int FontSmall = 12, FontBody = 14, FontLead = 15, FontTitle = 19;

        static float scale = 1f;
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        static Texture2D dotTexture;
        static GUIStyle boxStyle, labelStyle, fieldStyle;

        /// <summary>
        /// Pixels per layout unit. Never below 1: on a Game view smaller than 1280x720 the text keeps its real size and the layout
        /// switches to <see cref="IsCompact"/> instead of shrinking everything (shrunken fonts hit the minimum font size while the
        /// rectangles kept shrinking, which is what made small Game views overlap and look cramped). Above 1280x720 it grows with the view.
        /// </summary>
        public static float Scale { get { return Mathf.Clamp(Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight), 1f, 2.5f); } }

        /// <summary>True when the available layout area is too small for the full HUD. Secondary hints are dropped; primary text is never shrunk.</summary>
        public static bool IsCompact(Vector2 v) { return v.y < 600f || v.x < 1000f; }

        /// <summary>Heights shared by the HUD pieces so the typed-text box can sit exactly above the bottom bar.</summary>
        public static float BottomHeight(Vector2 v) { return IsCompact(v) ? 80f : 104f; }
        public static float ResultHeight(Vector2 v) { return IsCompact(v) ? 112f : 150f; }
        public const float TopBarHeight = 44f, EdgeMargin = 12f, ResultTop = 62f;

        /// <summary>Starts a HUD pass. Returns the virtual screen size (the width/height available at the current aspect ratio).</summary>
        public static Vector2 Begin()
        {
            scale = Scale;
            return new Vector2(Screen.width / scale, Screen.height / scale);
        }

        /// <summary>Ends a HUD pass started with <see cref="Begin"/>. Nothing global is changed by Begin, so there is nothing to restore.</summary>
        public static void End() { }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>Virtual rectangle to whole screen pixels. Sizes never collapse to zero, so 1-unit rules stay visible.</summary>
        public static Rect Px(Rect r)
        {
            float x = Mathf.Round(r.x * scale), y = Mathf.Round(r.y * scale);
            float w = Mathf.Max(1f, Mathf.Round(r.width * scale)), h = Mathf.Max(1f, Mathf.Round(r.height * scale));
            return new Rect(x, y, w, h);
        }

        static int FontPx(int size) { return Mathf.Max(9, Mathf.RoundToInt(size * scale)); }

        // ---- drawing ----

        public static void Panel(Rect r, Color fill, Color border)
        {
            EnsureStyles();
            boxStyle.normal.background = Rounded(fill, border);
            GUI.Box(Px(r), GUIContent.none, boxStyle);
        }

        public static void Panel(Rect r) { Panel(r, PanelFill, PanelBorder); }

        /// <summary>Restrained sci-fi framing: four small corner ticks just outside a panel. Flat rectangles only.</summary>
        public static void Brackets(Rect r, Color color, float len = 12f, float t = 2f)
        {
            Color c = WithAlpha(color, 0.9f);
            Rule(new Rect(r.x - 1f, r.y - 1f, len, t), c);              Rule(new Rect(r.x - 1f, r.y - 1f, t, len), c);
            Rule(new Rect(r.xMax - len + 1f, r.y - 1f, len, t), c);     Rule(new Rect(r.xMax - t + 1f, r.y - 1f, t, len), c);
            Rule(new Rect(r.x - 1f, r.yMax - t + 1f, len, t), c);       Rule(new Rect(r.x - 1f, r.yMax - len + 1f, t, len), c);
            Rule(new Rect(r.xMax - len + 1f, r.yMax - t + 1f, len, t), c); Rule(new Rect(r.xMax - t + 1f, r.yMax - len + 1f, t, len), c);
        }

        /// <summary>Small rounded tag with tinted fill and coloured text.</summary>
        public static void Chip(Rect r, string text, Color color, int size = FontSmall)
        {
            Panel(r, WithAlpha(color, 0.20f), WithAlpha(color, 0.90f));
            Label(r, text, size, color, true, TextAnchor.MiddleCenter);
        }

        public static void Dot(Vector2 center, float radius, Color color)
        {
            if (dotTexture == null) dotTexture = Circle(32);
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(Px(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f)), dotTexture);
            GUI.color = saved;
        }

        /// <summary>Flat filled rectangle (thin dividers, accent bars, progress bars). No rounded corners, so it is safe at any size.</summary>
        public static void Rule(Rect r, Color color)
        {
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(Px(r), Texture2D.whiteTexture);
            GUI.color = saved;
        }

        public static void Bar(Rect r, float fraction, Color color)
        {
            fraction = Mathf.Clamp01(fraction);
            Rule(r, WithAlpha(Color.white, 0.12f));
            if (fraction > 0.01f) Rule(new Rect(r.x, r.y, r.width * fraction, r.height), WithAlpha(color, 0.95f));
        }

        public static void Label(Rect r, string text, int size, Color color, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
        {
            EnsureStyles();
            labelStyle.fontSize = FontPx(size);
            labelStyle.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            labelStyle.alignment = anchor;
            labelStyle.wordWrap = wrap;
            labelStyle.clipping = TextClipping.Clip;
            labelStyle.normal.textColor = color;
            GUI.Label(Px(r), text ?? "", labelStyle);
        }

        /// <summary>
        /// Single-line text field in the HUD style. Uses the same pixel-snapped rectangle and font as the labels, so typed text is as
        /// sharp as everything else. The caller sets GUI.SetNextControlName first if it needs to focus the field.
        /// </summary>
        public static string Field(Rect r, string text, int maxLength, int size)
        {
            EnsureStyles();
            fieldStyle.fontSize = FontPx(size);
            int pad = Mathf.RoundToInt(10f * scale);
            fieldStyle.padding = new RectOffset(pad, pad, 0, 0);
            return GUI.TextField(Px(r), text ?? "", maxLength, fieldStyle);
        }

        /// <summary>Truncates to maxChars with "..." so a long message can never spill out of its panel.</summary>
        public static string Fit(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\n', ' ');
            return s.Length <= maxChars ? s : s.Substring(0, Mathf.Max(0, maxChars - 3)) + "...";
        }

        // ---- procedural textures ----

        const int RoundedSize = 32, RoundedRadius = 10;

        static void EnsureStyles()
        {
            if (boxStyle == null) boxStyle = new GUIStyle { border = new RectOffset(RoundedRadius, RoundedRadius, RoundedRadius, RoundedRadius) };
            if (labelStyle == null) labelStyle = new GUIStyle { richText = false, padding = new RectOffset(0, 0, 0, 0) };
            if (fieldStyle == null)
            {
                var edge = new RectOffset(RoundedRadius, RoundedRadius, RoundedRadius, RoundedRadius);
                fieldStyle = new GUIStyle
                {
                    border = edge, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false,
                    fontStyle = FontStyle.Normal, richText = false
                };
                Texture2D idle = Rounded(new Color(0.02f, 0.03f, 0.06f, 0.95f), WithAlpha(PanelBorder, 0.9f));
                Texture2D active = Rounded(new Color(0.02f, 0.04f, 0.08f, 0.98f), Cyan);
                fieldStyle.normal.background = idle; fieldStyle.hover.background = idle;
                fieldStyle.focused.background = active; fieldStyle.active.background = active;
                fieldStyle.normal.textColor = Text; fieldStyle.hover.textColor = Text;
                fieldStyle.focused.textColor = Text; fieldStyle.active.textColor = Text;
            }
        }

        static Texture2D Rounded(Color fill, Color border)
        {
            string key = ColorUtility.ToHtmlStringRGBA(fill) + "/" + ColorUtility.ToHtmlStringRGBA(border);
            Texture2D tex;
            if (Textures.TryGetValue(key, out tex) && tex != null) return tex;

            tex = new Texture2D(RoundedSize, RoundedSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color[RoundedSize * RoundedSize];
            float half = RoundedSize * 0.5f;
            for (int y = 0; y < RoundedSize; y++)
            {
                for (int x = 0; x < RoundedSize; x++)
                {
                    // Signed distance to a rounded rectangle centred in the texture (negative inside).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - RoundedRadius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - RoundedRadius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float sd = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - RoundedRadius;
                    float coverage = Mathf.Clamp01(0.5f - sd);
                    float edge = Mathf.Clamp01((sd + 1.6f) / 1.2f);   // 0 inside, 1 on the border band
                    Color c = Color.Lerp(fill, border, edge);
                    c.a *= coverage;
                    px[y * RoundedSize + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            Textures[key] = tex;
            return tex;
        }

        static Texture2D Circle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - c, y + 0.5f - c).magnitude;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(c - d));
                }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
