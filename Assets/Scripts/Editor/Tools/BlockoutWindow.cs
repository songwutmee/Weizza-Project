using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Draws a level as flat coloured boxes with real collision, before any
    /// art exists.
    ///
    /// This is LDtk's own workflow, read out of its installed platformer
    /// sample rather than guessed at: that project's layer stack is an
    /// Entities layer, a Wall_shadows layer at 0.17 opacity, a **Collisions
    /// IntGrid painted as dirt, stone, ladder and walls**, and a Bg_textures
    /// layer. The IntGrid carries the whole design. Tiles are laid over it
    /// afterwards by rules, and can be replaced without the level changing
    /// shape. The same split is what this project already decided for itself
    /// under "Art and collision are two separate layers".
    ///
    /// So: block a level out here, walk it, fix it, and hand the artist a
    /// screenshot of coloured shapes plus the brief this window writes. The
    /// boxes stay as collision when the art arrives; only their renderers get
    /// switched off.
    ///
    /// Everything is rectangles on purpose. A platformer blockout is ledges
    /// and walls, and a drag that produces one box is both faster to draw and
    /// far cheaper in the scene than a grid of painted cells.
    /// </summary>
    public sealed class BlockoutWindow : OdinEditorWindow
    {
        // No keyboard shortcut. %#b collided with Unity's own Build Profiles and
        // %#l collided with Generate Lighting, both discovered the hard way as a
        // modal dialog that freezes the whole Editor until a human clicks it.
        // Every unclaimed-looking combo in this Editor turned out already owned
        // by Unity itself, so this stays menu-only rather than guessing a third time.
        [MenuItem("Weizza/Blockout", priority = 2)]
        private static void Open()
        {
            var window = GetWindow<BlockoutWindow>();
            window.titleContent = new GUIContent("Blockout");
            window.minSize = new Vector2(320f, 250f);
            window.Show();
        }

        private enum Tool
        {
            Rect,
            Ledge,
            Ramp,
            Eraser,
            Picker,
        }

        // ---- Tool ------------------------------------------------------------

        [TitleGroup("Tool", "What the mouse does in the scene", TitleAlignments.Centered)]
        [InfoBox("$ToolVerdict")]
        [HorizontalGroup("Tool/Row", Width = 0.2f)]
        [Button("Rect", ButtonSizes.Large), GUIColor("@ToolColour(Tool.Rect)")]
        private void PickRect() => Use(Tool.Rect);

        [HorizontalGroup("Tool/Row", Width = 0.2f)]
        [Button("Ledge", ButtonSizes.Large), GUIColor("@ToolColour(Tool.Ledge)")]
        private void PickLedge() => Use(Tool.Ledge);

        [HorizontalGroup("Tool/Row", Width = 0.2f)]
        [Button("Ramp", ButtonSizes.Large), GUIColor("@ToolColour(Tool.Ramp)")]
        private void PickRamp() => Use(Tool.Ramp);

        [HorizontalGroup("Tool/Row", Width = 0.2f)]
        [Button("Eraser", ButtonSizes.Large), GUIColor("@ToolColour(Tool.Eraser)")]
        private void PickEraser() => Use(Tool.Eraser);

        [HorizontalGroup("Tool/Row", Width = 0.2f)]
        [Button("Picker", ButtonSizes.Large), GUIColor("@ToolColour(Tool.Picker)")]
        private void PickPicker() => Use(Tool.Picker);

        private void Use(Tool next)
        {
            tool = next;
            active = true;
            SceneView.RepaintAll();
            Repaint();
        }

        private Color ToolColour(Tool which) => tool == which && active
            ? new Color(0.4f, 0.85f, 0.55f)
            : new Color(0.72f, 0.72f, 0.74f);

        [TitleGroup("Tool")]
        [ToggleLeft, LabelText("Painting in the scene")]
        [SerializeField] private bool active = true;

        private string ToolVerdict => !active
            ? "Off. Pick a tool above to start."
            : tool switch
            {
                Tool.Rect => "Drag a rectangle. It becomes one box with collision. Hold shift to " +
                             "ignore the grid.",
                Tool.Ledge => "Drag sideways. It becomes a platform one Ledge Thickness tall, " +
                              "sitting on the line you dragged.",
                Tool.Ramp => "Drag from the low end to the high end. It becomes a sloped platform " +
                             "with its own rotated collider, the drag line is the surface you walk " +
                             "on. Past 45 degrees the player's own slope limit stops them climbing it.",
                Tool.Eraser => "Click or drag over blockout boxes to delete them. Art is never touched.",
                Tool.Picker => "Click a box to make its kind the live one.",
                _ => string.Empty,
            };

        // ---- Palette ---------------------------------------------------------

        [TitleGroup("Palette", "What the box means", TitleAlignments.Centered)]
        [InfoBox("$KindVerdict")]
        [HorizontalGroup("Palette/A", Width = 0.333f)]
        [Button("Ground", ButtonSizes.Large), GUIColor("@KindColour(BlockoutKind.Ground)")]
        private void PickGround() => UseKind(BlockoutKind.Ground);

        [HorizontalGroup("Palette/A", Width = 0.333f)]
        [Button("Ledge", ButtonSizes.Large), GUIColor("@KindColour(BlockoutKind.Ledge)")]
        private void PickKindLedge() => UseKind(BlockoutKind.Ledge);

        [HorizontalGroup("Palette/A", Width = 0.333f)]
        [Button("Wall", ButtonSizes.Large), GUIColor("@KindColour(BlockoutKind.Wall)")]
        private void PickWall() => UseKind(BlockoutKind.Wall);

        [HorizontalGroup("Palette/B", Width = 0.333f)]
        [Button("Hazard", ButtonSizes.Large), GUIColor("@KindColour(BlockoutKind.Hazard)")]
        private void PickHazard() => UseKind(BlockoutKind.Hazard);

        [HorizontalGroup("Palette/B", Width = 0.333f)]
        [Button("Water", ButtonSizes.Large), GUIColor("@KindColour(BlockoutKind.Water)")]
        private void PickWater() => UseKind(BlockoutKind.Water);

        [HorizontalGroup("Palette/B", Width = 0.333f)]
        [Button("Note", ButtonSizes.Large), GUIColor("@KindColour(BlockoutKind.Note)")]
        private void PickNote() => UseKind(BlockoutKind.Note);

        private void UseKind(BlockoutKind next)
        {
            kind = next;
            if (tool == Tool.Eraser || tool == Tool.Picker) tool = Tool.Rect;
            active = true;
            SceneView.RepaintAll();
            Repaint();
        }

        private Color KindColour(BlockoutKind which)
        {
            var look = BlockoutKindData.Get(which).Colour;
            return kind == which ? look : new Color(look.r * 0.55f, look.g * 0.55f, look.b * 0.55f);
        }

        private string KindVerdict
        {
            get
            {
                var look = BlockoutKindData.Get(kind);
                var solid = look.Solidity switch
                {
                    BlockoutSolidity.Solid => "solid collider",
                    BlockoutSolidity.Trigger => "trigger, never blocks",
                    _ => "drawn only, no collider",
                };
                return $"{look.Label}: {look.Note}  ({solid})";
            }
        }

        // ---- Settings --------------------------------------------------------

        [TitleGroup("Settings")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Corners snap to this. Hold shift while dragging to ignore it " +
                 "for one box.")]
        [SerializeField] private float grid = 0.5f;

        [TitleGroup("Settings")]
        [PropertyRange(0.1f, 2f)]
        [Tooltip("How tall the Ledge tool draws. The player capsule is 1.70 m " +
                 "and a ledge only needs a surface, so this is thin on purpose.")]
        [SerializeField] private float ledgeThickness = 0.4f;

        [TitleGroup("Settings")]
        [PropertyRange(0.2f, 1f)]
        [Tooltip("How solid the coloured boxes look. Lower lets the art behind " +
                 "them read through, which is what a blockout over existing " +
                 "scenery wants.")]
        [SerializeField] private float boxOpacity = 0.55f;

        [TitleGroup("Settings")]
        [ToggleLeft]
        [Tooltip("Writes the size in metres on every box in the scene view. " +
                 "Reading a jump distance off the level is the entire reason to " +
                 "block out rather than guess.")]
        [SerializeField] private bool showSizes = true;

        [TitleGroup("Settings")]
        [Tooltip("Everything painted lives under this object, so the whole " +
                 "blockout can be switched off in one click when art arrives.")]
        [SerializeField] private string rootName = "Blockout";

        // ---- View ------------------------------------------------------------

        [TitleGroup("View", "Get the art out of the way while designing", TitleAlignments.Centered)]
        [InfoBox("Uses Unity's own scene visibility, the eye column in the " +
                 "Hierarchy. Nothing is modified and nothing is saved into the " +
                 "scene, so this can never leave the level looking wrong.")]
        [HorizontalGroup("View/Row")]
        [Button("Hide art", ButtonSizes.Medium)]
        private void HideArt()
        {
            foreach (var root in ArtRoots())
            {
                SceneVisibilityManager.instance.Hide(root, true);
            }

            SceneView.RepaintAll();
        }

        [HorizontalGroup("View/Row")]
        [Button("Show art", ButtonSizes.Medium), GUIColor(0.55f, 0.75f, 0.95f)]
        private void ShowArt()
        {
            SceneVisibilityManager.instance.ExitIsolation();
            SceneVisibilityManager.instance.ShowAll();
            SceneView.RepaintAll();
        }

        [HorizontalGroup("View/Row")]
        [Button("Camera to game pitch", ButtonSizes.Medium)]
        private void LockCamera()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;

            var rig = FindAnyObjectByType<ViewRig>();
            view.orthographic = true;
            view.rotation = Quaternion.Euler(rig != null ? rig.PitchDegrees : 16f, 0f, 0f);
            view.size = rig != null ? rig.OrthographicSize : 3.6f;
            view.in2DMode = false;
            view.Repaint();
        }

        private static IEnumerable<GameObject> ArtRoots()
        {
            foreach (var name in new[] { "Level", "Scenery", "Atmosphere" })
            {
                var found = GameObject.Find(name);
                if (found != null) yield return found;
            }
        }

        // ---- Brief -----------------------------------------------------------

        [TitleGroup("Hand off", "What the artist gets", TitleAlignments.Centered)]
        [InfoBox("Writes every box out as a table, sorted left to right, next " +
                 "to the scene file. Paired with a screenshot of the coloured " +
                 "shapes it is a complete brief: this is where the ground is, " +
                 "this is how wide the gap is, draw over it.")]
        [Button(ButtonSizes.Large, Icon = SdfIconType.FileEarmarkText), GUIColor(0.5f, 0.8f, 0.5f)]
        private void WriteArtBrief()
        {
            var root = GameObject.Find(rootName);
            if (root == null)
            {
                Debug.LogWarning("[Weizza] Nothing blocked out yet, so there is no brief to write.");
                return;
            }

            var boxes = new List<Transform>();
            Collect(root.transform, boxes);
            boxes.Sort((a, b) => a.position.x.CompareTo(b.position.x));

            var text = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            text.AppendLine($"Blockout brief for {scene.name}");
            text.AppendLine($"{boxes.Count} boxes, measured in metres, sorted left to right.");
            text.AppendLine("The player is 1.8 m tall, runs at 5.2 m/s and jumps 3.13 m across.");
            text.AppendLine();
            text.AppendLine("kind          x from    x to     top y    width   height");

            foreach (var box in boxes)
            {
                var size = box.localScale;
                var kindName = box.parent != null ? box.parent.name : "?";
                var ramp = box.GetComponent<BlockoutRamp>();

                if (ramp != null)
                {
                    var angle = Vector2.SignedAngle(Vector2.right, ramp.to - ramp.from);
                    text.AppendLine(
                        $"{kindName,-12}  {ramp.from.x,7:0.00}  {ramp.to.x,7:0.00}  " +
                        $"{ramp.from.y,7:0.00}->{ramp.to.y,5:0.0}  {Vector2.Distance(ramp.from, ramp.to),6:0.00}  " +
                        $"{angle,4:0} deg");
                    continue;
                }

                var half = size.x * 0.5f;
                text.AppendLine(
                    $"{kindName,-12}  {box.position.x - half,7:0.00}  {box.position.x + half,7:0.00}  " +
                    $"{box.position.y + size.y * 0.5f,7:0.00}  {size.x,6:0.00}  {size.y,6:0.00}");
            }

            var path = System.IO.Path.ChangeExtension(scene.path, null) + "_blockout_brief.txt";
            System.IO.File.WriteAllText(path, text.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"[Weizza] Wrote {boxes.Count} boxes to {path}");
            EditorUtility.RevealInFinder(path);
        }

        private static void Collect(Transform parent, List<Transform> into)
        {
            foreach (Transform child in parent)
            {
                if (child.GetComponent<SpriteRenderer>() != null) into.Add(child);
                Collect(child, into);
            }
        }

        // ---- State -----------------------------------------------------------

        [SerializeField, HideInInspector] private Tool tool = Tool.Rect;
        [SerializeField, HideInInspector] private BlockoutKind kind = BlockoutKind.Ground;

        private Vector3? dragStart;
        private Vector3 dragNow;
        private Sprite fill;

        private const string FillPath = "Assets/GameData/Atmosphere/SolidFill.png";

        // ---- Scene -----------------------------------------------------------

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnScene;
            fill = AssetDatabase.LoadAssetAtPath<Sprite>(FillPath);
        }

        private void OnDisable() => SceneView.duringSceneGui -= OnScene;

        private void OnScene(SceneView view)
        {
            if (!active) return;

            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);

            DrawExistingLabels();

            var at = PointerInScene(e.mousePosition, e.shift);

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    if (tool == Tool.Eraser) EraseAt(at);
                    else if (tool == Tool.Picker) PickAt(at);
                    else dragStart = at;
                    dragNow = at;
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && !e.alt:
                    dragNow = at;
                    if (tool == Tool.Eraser) EraseAt(at);
                    view.Repaint();
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && !e.alt:
                    if (dragStart != null && tool is Tool.Rect or Tool.Ledge or Tool.Ramp)
                    {
                        Create(dragStart.Value, at);
                    }
                    dragStart = null;
                    e.Use();
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    dragStart = null;
                    active = false;
                    Repaint();
                    e.Use();
                    break;

                case EventType.MouseMove:
                    view.Repaint();
                    break;
            }

            DrawPreview(at);
        }

        private Vector3 PointerInScene(Vector2 mouse, bool freehand)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            var at = plane.Raycast(ray, out var distance) ? ray.GetPoint(distance) : Vector3.zero;

            if (freehand || grid <= 0f) return new Vector3(at.x, at.y, 0f);
            return new Vector3(
                Mathf.Round(at.x / grid) * grid,
                Mathf.Round(at.y / grid) * grid,
                0f);
        }

        private void DrawPreview(Vector3 at)
        {
            var look = BlockoutKindData.Get(kind);

            if (dragStart != null && tool == Tool.Ramp)
            {
                var from = dragStart.Value;
                var to = dragNow;
                var angle = Vector2.SignedAngle(Vector2.right, to - from);

                Handles.color = new Color(look.Colour.r, look.Colour.g, look.Colour.b, 0.9f);
                Handles.DrawLine(from, to, 4f);

                Handles.BeginGUI();
                var screen = HandleUtility.WorldToGUIPoint((from + to) * 0.5f);
                var steep = Mathf.Abs(angle) > 45f && Mathf.Abs(angle) < 135f;
                var note = steep ? "  TOO STEEP, player's slope limit is 45" : string.Empty;
                GUI.Label(new Rect(screen.x - 60f, screen.y - 10f, 320f, 20f),
                    $"{Vector2.Distance(from, to):0.0} m at {angle:0} deg{note}",
                    Badge(steep ? Color.red : look.Colour));
                Handles.EndGUI();
                return;
            }

            if (dragStart != null && tool is Tool.Rect or Tool.Ledge)
            {
                var (centre, size) = Shape(dragStart.Value, dragNow);
                Handles.color = new Color(look.Colour.r, look.Colour.g, look.Colour.b, 0.9f);
                Handles.DrawWireCube(centre, new Vector3(size.x, size.y, 1f));

                Handles.BeginGUI();
                var screen = HandleUtility.WorldToGUIPoint(centre);
                GUI.Label(new Rect(screen.x - 40f, screen.y - 10f, 200f, 20f),
                    $"{size.x:0.0} x {size.y:0.0} m", Badge(look.Colour));
                Handles.EndGUI();
                return;
            }

            Handles.color = new Color(look.Colour.r, look.Colour.g, look.Colour.b, 0.8f);
            var tick = Mathf.Max(0.15f, grid * 0.5f);
            Handles.DrawLine(at + Vector3.left * tick, at + Vector3.right * tick);
            Handles.DrawLine(at + Vector3.down * tick, at + Vector3.up * tick);

            Handles.BeginGUI();
            var cursor = HandleUtility.WorldToGUIPoint(at);
            GUI.Label(new Rect(cursor.x + 12f, cursor.y - 24f, 260f, 20f),
                $"{tool}   {look.Label}   {at.x:0.0}, {at.y:0.0}", Badge(look.Colour));
            Handles.EndGUI();
        }

        private void DrawExistingLabels()
        {
            if (!showSizes) return;

            var root = GameObject.Find(rootName);
            if (root == null) return;

            var boxes = new List<Transform>();
            Collect(root.transform, boxes);

            Handles.BeginGUI();
            foreach (var box in boxes)
            {
                var screen = HandleUtility.WorldToGUIPoint(box.position);
                if (screen.x < -80f || screen.y < -40f) continue;

                var ramp = box.GetComponent<BlockoutRamp>();
                var label = ramp != null
                    ? $"{Vector2.Distance(ramp.from, ramp.to):0.0}m ramp"
                    : $"{box.localScale.x:0.0} x {box.localScale.y:0.0}";

                GUI.Label(new Rect(screen.x - 34f, screen.y - 8f, 160f, 18f), label, Badge(Color.white));
            }
            Handles.EndGUI();
        }

        private static GUIStyle Badge(Color colour) => new(EditorStyles.miniLabel)
        {
            normal = { textColor = colour },
            fontStyle = FontStyle.Bold,
        };

        /// <summary>
        /// Rect takes both corners. Ledge takes the drag as the top surface and
        /// hangs its own thickness below it, because a platform is placed by
        /// where its surface goes, the same rule Map Painter follows.
        /// </summary>
        private (Vector3 centre, Vector2 size) Shape(Vector3 from, Vector3 to)
        {
            if (tool == Tool.Ledge)
            {
                var width = Mathf.Max(grid, Mathf.Abs(to.x - from.x));
                var midX = (from.x + to.x) * 0.5f;
                return (new Vector3(midX, from.y - ledgeThickness * 0.5f, 0f),
                        new Vector2(width, ledgeThickness));
            }

            var size = new Vector2(
                Mathf.Max(grid, Mathf.Abs(to.x - from.x)),
                Mathf.Max(grid, Mathf.Abs(to.y - from.y)));
            var centre = new Vector3((from.x + to.x) * 0.5f, (from.y + to.y) * 0.5f, 0f);
            return (centre, size);
        }

        private void Create(Vector3 from, Vector3 to)
        {
            if (tool == Tool.Ramp)
            {
                CreateRamp(from, to);
                return;
            }

            var (centre, size) = Shape(from, to);
            var look = BlockoutKindData.Get(kind);

            var go = new GameObject($"{look.Label}_{centre.x:0.0}_{centre.y:0.0}");
            Undo.RegisterCreatedObjectUndo(go, "Blockout box");
            go.transform.SetParent(KindGroup(look.Label), true);
            go.transform.position = centre;

            // SolidFill is one metre square at its import settings, so the
            // scale is the size in metres with no conversion anywhere.
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = fill;
            renderer.color = new Color(look.Colour.r, look.Colour.g, look.Colour.b, boxOpacity);

            // Drawn over everything, including the real foreground band, because
            // a plan the art hides is not a plan.
            renderer.sortingLayerName = "Foreground";
            renderer.sortingOrder = 100;

            if (look.Solidity != BlockoutSolidity.None)
            {
                var box = go.AddComponent<BoxCollider>();

                // Size is one, because the transform's own scale already carries
                // the metres. A collider sized in metres here would be squared.
                box.size = Vector3.one;
                box.isTrigger = look.Solidity == BlockoutSolidity.Trigger;
            }

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        /// <summary>
        /// A ramp is the same rotated box every other piece here already is,
        /// nothing new: a Sprite Shape asset needs its own edge and fill
        /// sprites to look like anything, and a blockout has neither. Reading
        /// its real source in `Library/PackageCache/com.unity.2d.spriteshape`
        /// confirmed there is no plain solid colour path, and its own
        /// generated collider is 2D besides, the exact trap TerrainSpline was
        /// written to avoid. The drag line becomes the walkable surface, the
        /// same convention Ledge already uses, just tilted to the drag angle
        /// instead of held flat.
        /// </summary>
        private void CreateRamp(Vector3 from, Vector3 to)
        {
            var diff = to - from;
            var length = diff.magnitude;
            if (length < 0.05f) return;

            var angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            var midpoint = (from + to) * 0.5f;
            var look = BlockoutKindData.Get(kind);

            var go = new GameObject($"Ramp_{midpoint.x:0.0}_{midpoint.y:0.0}");
            Undo.RegisterCreatedObjectUndo(go, "Blockout ramp");
            go.transform.SetParent(KindGroup(look.Label + " Ramp"), true);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);

            // The drag line is the walked-on surface, so the box hangs its own
            // thickness below that line along the box's own tilted down axis,
            // not world down, or the surface would not sit where it was dragged.
            go.transform.position = midpoint - (Vector3)(go.transform.up * (ledgeThickness * 0.5f));
            go.transform.localScale = new Vector3(length, ledgeThickness, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = fill;
            renderer.color = new Color(look.Colour.r, look.Colour.g, look.Colour.b, boxOpacity);
            renderer.sortingLayerName = "Foreground";
            renderer.sortingOrder = 100;

            if (look.Solidity != BlockoutSolidity.None)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                box.isTrigger = look.Solidity == BlockoutSolidity.Trigger;
            }

            var marker = go.AddComponent<BlockoutRamp>();
            marker.from = from;
            marker.to = to;

            if (Mathf.Abs(angle) > 45f && Mathf.Abs(angle) < 135f)
            {
                Debug.LogWarning($"[Weizza] Ramp at {midpoint} is {angle:0} degrees, past the " +
                    "player's 45 degree slope limit. It will read as a wall, not a slope.");
            }

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private Transform KindGroup(string label)
        {
            var root = GameObject.Find(rootName);
            if (root == null)
            {
                root = new GameObject(rootName);
                Undo.RegisterCreatedObjectUndo(root, "Blockout root");
            }

            var existing = root.transform.Find(label);
            if (existing != null) return existing;

            var group = new GameObject(label);
            Undo.RegisterCreatedObjectUndo(group, "Blockout group");
            group.transform.SetParent(root.transform, false);
            return group.transform;
        }

        private void EraseAt(Vector3 at)
        {
            var found = BoxUnder(at);
            if (found == null) return;

            Undo.DestroyObjectImmediate(found.gameObject);
            SceneView.RepaintAll();
        }

        private void PickAt(Vector3 at)
        {
            var found = BoxUnder(at);
            if (found == null || found.parent == null) return;

            var isRamp = found.GetComponent<BlockoutRamp>() != null;
            var groupName = found.parent.name;
            var lookupLabel = isRamp ? groupName.Substring(0, groupName.Length - " Ramp".Length) : groupName;

            foreach (var candidate in BlockoutKindData.All)
            {
                if (BlockoutKindData.Get(candidate).Label != lookupLabel) continue;
                kind = candidate;
                tool = isRamp ? Tool.Ramp : Tool.Rect;
                Repaint();
                return;
            }
        }

        /// <summary>
        /// Only ever finds boxes under the blockout root, so the eraser can
        /// never take a piece of real scenery with it.
        ///
        /// Tests in the box's own local space rather than world space, or a
        /// tilted Ramp piece would answer to a hit test shaped like its own
        /// unrotated bounding box instead of the shape actually on screen.
        /// </summary>
        private Transform BoxUnder(Vector3 at)
        {
            var root = GameObject.Find(rootName);
            if (root == null) return null;

            var boxes = new List<Transform>();
            Collect(root.transform, boxes);

            foreach (var box in boxes)
            {
                var size = box.localScale;
                var local = Quaternion.Inverse(box.rotation) * (at - box.position);
                if (Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f)
                {
                    return box;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Remembers the two points a Ramp piece was dragged from, since its
    /// rotated transform alone cannot be read back into "x from, x to" for the
    /// art brief the way an axis-aligned box's `localScale` can.
    /// </summary>
    internal sealed class BlockoutRamp : MonoBehaviour
    {
        public Vector3 from;
        public Vector3 to;
    }
}
