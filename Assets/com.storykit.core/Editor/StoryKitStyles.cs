using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Shared visual language for every StoryKit inspector and window, so the
    /// whole package looks like one product instead of a pile of tools.
    /// Colours are tuned for Unity's dark skin and stay readable in light skin.
    /// </summary>
    public static class StoryKitStyles
    {
        // ---- palette ----
        public static readonly Color Accent = new(0.49f, 0.40f, 0.95f);   // violet
        public static readonly Color Success = new(0.30f, 0.78f, 0.48f);
        public static readonly Color Warning = new(0.95f, 0.72f, 0.30f);
        public static readonly Color Danger = new(0.90f, 0.38f, 0.38f);
        public static readonly Color Muted = new(0.62f, 0.67f, 0.75f);

        private static Texture2D _pixel;

        private static Texture2D Pixel
        {
            get
            {
                if (_pixel != null) return _pixel;
                _pixel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _pixel.SetPixel(0, 0, Color.white);
                _pixel.Apply();
                return _pixel;
            }
        }

        private static GUIStyle _header;
        private static GUIStyle _card;
        private static GUIStyle _pill;
        private static GUIStyle _summary;

        /// <summary>Section title with an accent bar.</summary>
        public static GUIStyle Header => _header ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            padding = new RectOffset(8, 4, 4, 4),
            normal = { textColor = new Color(0.88f, 0.90f, 0.95f) },
        };

        /// <summary>Rounded panel used to group related fields.</summary>
        public static GUIStyle Card => _card ??= new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 8, 8),
            margin = new RectOffset(0, 0, 4, 6),
        };

        /// <summary>Small coloured tag, e.g. AND / OR / Consequence.</summary>
        public static GUIStyle Pill => _pill ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 10,
            normal = { textColor = Color.white },
            padding = new RectOffset(6, 6, 1, 1),
        };

        /// <summary>Muted one-line explanation under a control.</summary>
        public static GUIStyle Summary => _summary ??= new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            fontStyle = FontStyle.Italic,
            normal = { textColor = Muted },
            padding = new RectOffset(4, 4, 0, 2),
        };

        /// <summary>Draws a section title with a coloured bar on the left.</summary>
        public static void SectionTitle(string title, Color color, string subtitle = null)
        {
            EditorGUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));

            var bar = new Rect(rect.x, rect.y + 2, 3, rect.height - 4);
            DrawRect(bar, color);

            var labelRect = new Rect(rect.x + 9, rect.y, rect.width - 9, rect.height);
            GUI.Label(labelRect, title, Header);

            if (!string.IsNullOrEmpty(subtitle))
                EditorGUILayout.LabelField(subtitle, Summary);
        }

        /// <summary>Coloured tag drawn inline. Returns the rect it used.</summary>
        public static Rect DrawPill(Rect rect, string text, Color color)
        {
            var faded = color;
            faded.a = 0.85f;
            DrawRoundedRect(rect, faded);
            GUI.Label(rect, text, Pill);
            return rect;
        }

        public static void DrawRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Pixel);
            GUI.color = previous;
        }

        /// <summary>Approximates rounded corners by insetting the edge rows.</summary>
        public static void DrawRoundedRect(Rect rect, Color color)
        {
            DrawRect(new Rect(rect.x + 1, rect.y, rect.width - 2, rect.height), color);
            DrawRect(new Rect(rect.x, rect.y + 1, rect.width, rect.height - 2), color);
        }

        /// <summary>Thin separator line.</summary>
        public static void Separator()
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            DrawRect(rect, new Color(1f, 1f, 1f, 0.07f));
        }

        /// <summary>Status strip: green when fine, amber when something is off.</summary>
        public static void StatusLine(bool ok, string okText, string problemText)
        {
            var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            var color = ok ? Success : Warning;

            var background = color;
            background.a = 0.14f;
            DrawRoundedRect(rect, background);
            DrawRect(new Rect(rect.x, rect.y, 3, rect.height), color);

            var labelRect = new Rect(rect.x + 10, rect.y, rect.width - 12, rect.height);
            var style = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = color },
                fontStyle = FontStyle.Bold,
                fontSize = 11,
            };
            GUI.Label(labelRect, (ok ? "✓  " : "⚠  ") + (ok ? okText : problemText), style);
        }
    }
}
