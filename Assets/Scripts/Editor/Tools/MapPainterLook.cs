using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Weizza.EditorTools
{
    /// <summary>
    /// How the Map Painter looks: the header that is always in view, the thumbnail
    /// palette and the shortcut card. Kept apart from the window so the painting logic
    /// and the drawing of its panels can be read separately.
    ///
    /// Shaped by what the level tools people like have in common (Unity's Tile Palette,
    /// LDtk, Tiled): the active tool and the active layer are never more than a glance
    /// away, the palette is pictures not names, and the tool switch also lives in the
    /// Scene view where the work happens.
    /// </summary>
    internal static class MapPainterLook
    {
        private static readonly Color On = new(0.36f, 0.78f, 0.45f);
        private static readonly Color Off = new(0.92f, 0.62f, 0.3f);
        private static readonly Color Panel = new(0f, 0f, 0f, 0.18f);
        private static readonly Color Pick = new(0.4f, 0.85f, 0.5f);

        private static readonly string[] ToolNames = { "Paint", "Erase", "Pick" };
        private static readonly string[] ToolKeys = { "B", "E", "I" };
        private static readonly string[] ToolIcons = { "Grid.PaintTool", "Grid.EraserTool", "Grid.PickingTool" };

        private static GUIStyle _title, _chip, _small, _toolButton, _caption;

        private static GUIStyle Title => _title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        private static GUIStyle Chip => _chip ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        private static GUIStyle Small => _small ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
        private static GUIStyle Caption => _caption ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip };
        private static GUIStyle ToolButton => _toolButton ??= new GUIStyle(EditorStyles.miniButton)
        {
            fixedHeight = 34, fontSize = 11, imagePosition = ImagePosition.ImageLeft, alignment = TextAnchor.MiddleCenter,
        };

        internal static GUIContent ToolContent(int i) =>
            new($" {ToolNames[i]}  ({ToolKeys[i]})", EditorGUIUtility.IconContent(ToolIcons[i]).image, $"{ToolNames[i]} tool, key {ToolKeys[i]}");

        // A colour per band, so the chip and the layer rows can be matched at a glance.
        internal static Color BandColour(SceneryRole role)
        {
            var h = Mathf.Repeat((int)role * 0.137f + 0.55f, 1f);
            return Color.HSVToRGB(h, 0.5f, 0.85f);
        }

        // ---- Header -------------------------------------------------------------

        internal static void Header(MapPainterWindow w)
        {
            var area = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(new Rect(area.x - 4, area.y - 2, area.width + 8, area.height + 4), Panel);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Map Painter", Title);
            GUILayout.FlexibleSpace();
            var old = GUI.backgroundColor;
            GUI.backgroundColor = w.Painting ? On : Off;
            if (GUILayout.Button(w.Painting ? "  ON   (Esc stops)" : "  OFF   click to paint", GUILayout.Height(24), GUILayout.MinWidth(140)))
                w.Painting = !w.Painting;
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < ToolNames.Length; i++)
            {
                var live = w.Painting && w.ToolIndex == i;
                GUI.backgroundColor = live ? On : old;
                if (GUILayout.Toggle(live, ToolContent(i), ToolButton) && !live) w.ToolIndex = i;
            }
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            // What lands where: the band chip, then the piece in hand.
            var band = BandColour(w.Role);
            var chip = GUILayoutUtility.GetRect(new GUIContent(SceneryRoleData.Label(w.Role)), Chip, GUILayout.Height(20), GUILayout.MinWidth(110));
            EditorGUI.DrawRect(chip, band * 0.8f);
            GUI.Label(chip, SceneryRoleData.Label(w.Role), Chip);
            GUILayout.Space(6);

            var sprite = w.SwatchSprite(w.Chosen);
            var thumb = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
            EditorGUI.DrawRect(thumb, new Color(0f, 0f, 0f, 0.25f));
            if (sprite != null) DrawSprite(thumb, sprite);
            EditorGUILayout.BeginVertical();
            if (sprite != null)
            {
                var m = w.SwatchMetres(w.Chosen);
                GUILayout.Label($"{sprite.name}", EditorStyles.miniBoldLabel);
                GUILayout.Label($"{w.Chosen + 1} of {w.SwatchCount}   {m.x:0.0} x {m.y:0.0} m" + (w.RandomVariant ? "   random" : ""), EditorStyles.miniLabel);
            }
            else GUILayout.Label("No palette yet: pick a preset or drop a sheet below.", Small);
            var chunk = w.ChunkUnderCursor;
            if (!string.IsNullOrEmpty(chunk)) GUILayout.Label("Chunk: " + chunk, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label(w.Status, Small);
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }

        // ---- Palette grid --------------------------------------------------------

        internal static void SwatchGrid(MapPainterWindow w)
        {
            if (w.SwatchCount == 0)
            {
                EditorGUILayout.HelpBox("The palette is empty. Pick a preset above, or drop a sliced sheet or a folder of PNGs.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            w.swatchFilter = EditorGUILayout.TextField(w.swatchFilter, EditorStyles.toolbarSearchField);
            w.RandomVariant = GUILayout.Toggle(w.RandomVariant, new GUIContent("Random", "A different piece every time"), EditorStyles.miniButton, GUILayout.Width(58));
            w.thumbSize = GUILayout.HorizontalSlider(w.thumbSize, 44f, 128f, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            var size = w.thumbSize;
            var width = EditorGUIUtility.currentViewWidth - 36f;
            var columns = Mathf.Max(1, Mathf.FloorToInt(width / (size + 6f)));
            var filter = string.IsNullOrWhiteSpace(w.swatchFilter) ? null : w.swatchFilter.Trim().ToLowerInvariant();
            var shown = 0;
            var col = 0;
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < w.SwatchCount; i++)
            {
                var sprite = w.SwatchSprite(i);
                if (sprite == null) continue;
                if (filter != null && !sprite.name.ToLowerInvariant().Contains(filter)) continue;
                if (col == columns) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); col = 0; }
                Cell(w, i, sprite, size);
                col++;
                shown++;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (shown == 0) EditorGUILayout.HelpBox("Nothing matches the search.", MessageType.None);
        }

        private static void Cell(MapPainterWindow w, int i, Sprite sprite, float size)
        {
            var rect = GUILayoutUtility.GetRect(size, size + 14f, GUILayout.Width(size), GUILayout.Height(size + 14f));
            var box = new Rect(rect.x, rect.y, size, size);
            var chosen = w.Chosen == i;
            var hover = box.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(box, chosen ? Pick * 0.55f : hover ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.22f));
            DrawSprite(new Rect(box.x + 4, box.y + 4, box.width - 8, box.height - 8), sprite);
            if (chosen) Outline(box, Pick, 2f);
            if (w.SwatchAnimated(i)) GUI.Label(new Rect(box.x + 3, box.y + 2, 40, 14), "anim", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(box.xMax - 22, box.y + 2, 20, 14), (i + 1).ToString(), EditorStyles.miniLabel);
            var m = w.SwatchMetres(i);
            GUI.Label(new Rect(rect.x, box.yMax, size, 14), new GUIContent(sprite.name, $"{sprite.name}\n{m.x:0.0} x {m.y:0.0} m\nClick to paint with it"), Caption);
            if (Event.current.type == EventType.MouseDown && box.Contains(Event.current.mousePosition))
            {
                w.Choose(i);
                Event.current.Use();
            }
            if (hover) w.Repaint();
        }

        // Draws the sprite's own rect out of its atlas or sheet, kept in proportion.
        private static void DrawSprite(Rect into, Sprite sprite)
        {
            var tex = sprite.texture;
            if (tex == null) return;
            var r = sprite.textureRect;
            var uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
            var aspect = r.width / Mathf.Max(1f, r.height);
            var fit = into;
            if (aspect > into.width / into.height) { fit.height = into.width / aspect; fit.y += (into.height - fit.height) * 0.5f; }
            else { fit.width = into.height * aspect; fit.x += (into.width - fit.width) * 0.5f; }
            GUI.DrawTextureWithTexCoords(fit, tex, uv, true);
        }

        private static void Outline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // ---- Shortcuts -------------------------------------------------------------

        internal static void Shortcuts()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("In the Scene view", EditorStyles.miniBoldLabel);
            GUILayout.Label("B paint   E erase   I pick   Ctrl held: erase\n" +
                            "Left / Right arrows: next piece   [ ]: scale   F: flip   Esc: stop\n" +
                            "One drag is one undo.", Small);
            EditorGUILayout.EndVertical();
        }
    }

    /// <summary>
    /// The Map Painter's tools in the Scene view, where the painting happens, so switching
    /// tool never means going back to the window. Shows up in the Scene view's Overlays menu.
    /// </summary>
    [Overlay(typeof(SceneView), Id, "Map Painter")]
    [Icon("Grid.PaintTool")]
    internal sealed class MapPainterOverlay : ToolbarOverlay
    {
        public const string Id = "weizza-map-painter";
        public MapPainterOverlay() : base(MapPainterOnToggle.Id, MapPainterToolButton0.Id, MapPainterToolButton1.Id, MapPainterToolButton2.Id) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class MapPainterOnToggle : EditorToolbarToggle
    {
        public const string Id = "WeizzaMapPainter/On";

        public MapPainterOnToggle()
        {
            text = "Paint";
            tooltip = "Map Painter on or off. Opens the window if it is closed.";
            icon = EditorGUIUtility.IconContent("Grid.PaintTool").image as Texture2D;
            this.RegisterValueChangedCallback(e =>
            {
                if (MapPainterWindow.Current == null) MapPainterWindow.OpenWindow();
                if (MapPainterWindow.Current != null) MapPainterWindow.Current.Painting = e.newValue;
            });
            // Keep in step with the window, which can switch itself off (Esc, closing).
            schedule.Execute(() => SetValueWithoutNotify(MapPainterWindow.Current != null && MapPainterWindow.Current.Painting)).Every(250);
        }
    }

    internal abstract class MapPainterToolButton : EditorToolbarToggle
    {
        protected MapPainterToolButton(int tool, string iconName, string tip)
        {
            icon = EditorGUIUtility.IconContent(iconName).image as Texture2D;
            tooltip = tip;
            this.RegisterValueChangedCallback(e =>
            {
                if (MapPainterWindow.Current == null) MapPainterWindow.OpenWindow();
                if (MapPainterWindow.Current != null) MapPainterWindow.Current.ToolIndex = tool;
            });
            schedule.Execute(() =>
            {
                var w = MapPainterWindow.Current;
                SetValueWithoutNotify(w != null && w.Painting && w.ToolIndex == tool);
            }).Every(250);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class MapPainterToolButton0 : MapPainterToolButton
    {
        public const string Id = "WeizzaMapPainter/Paint";
        public MapPainterToolButton0() : base(0, "Grid.PaintTool", "Paint (B)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class MapPainterToolButton1 : MapPainterToolButton
    {
        public const string Id = "WeizzaMapPainter/Erase";
        public MapPainterToolButton1() : base(1, "Grid.EraserTool", "Erase (E)") { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class MapPainterToolButton2 : MapPainterToolButton
    {
        public const string Id = "WeizzaMapPainter/Pick";
        public MapPainterToolButton2() : base(2, "Grid.PickingTool", "Pick (I)") { }
    }
}
