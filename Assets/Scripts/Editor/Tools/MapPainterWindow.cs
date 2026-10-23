using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Places finished pieces of scenery by clicking, or dragging, in the scene.
    ///
    /// Replaces the spline terrain approach for one reason: a pack of hand
    /// drawn art is a set of finished platforms, not a set of bands that tile.
    /// Every free pack on hand is drawn that way, and forcing one into strips
    /// costs a repaint of the whole set. Placing the pieces as drawn costs
    /// nothing and cannot come apart at a seam, because there is no seam.
    ///
    /// The layers panel is the part borrowed from LDtk, Tiled and Aseprite,
    /// which all solved the same problem the same way: a level is many bands
    /// stacked back to front, and the only way to work on one without fighting
    /// the others is to say which band is live, dim the rest, and be able to
    /// put everything back with one button. Painting always lands in the live
    /// band, so a background piece can never end up on the ground's Z by
    /// accident.
    /// </summary>
    public sealed class MapPainterWindow : OdinEditorWindow
    {
        /// <summary>Lets a Preset row reach back into the window that owns it.</summary>
        private static MapPainterWindow Active { get; set; }

        [MenuItem("Weizza/Map Painter %#m", priority = 3)]
        private static void Open()
        {
            var window = GetWindow<MapPainterWindow>();
            window.titleContent = new GUIContent("Map Painter");

            // Deliberately small. A tall minimum cannot be dragged smaller and
            // makes the window impossible to dock anywhere narrow, which is
            // what "I cannot resize it" turned out to mean. Odin scrolls the
            // content, so a short window loses nothing.
            window.minSize = new Vector2(330f, 260f);
            window.Show();
        }

        // ---- Layers ---------------------------------------------------------

        [TitleGroup("Layers", "Back to front, the order they draw in", TitleAlignments.Centered)]
        [InfoBox("$LiveBandVerdict", InfoMessageType.Info)]
        [ShowInInspector, HideLabel]
        [TableList(IsReadOnly = true, AlwaysExpanded = true, HideToolbar = true)]
        private List<LayerRow> layers = new();

        [TitleGroup("Layers")]
        [HorizontalGroup("Layers/Buttons")]
        [Button("Show all", ButtonSizes.Medium), GUIColor(0.55f, 0.75f, 0.95f)]
        private void ShowAll()
        {
            RestoreColours();
            isolating = false;
            Repaint();
            SceneView.RepaintAll();
        }

        [HorizontalGroup("Layers/Buttons")]
        [Button("Refresh counts", ButtonSizes.Medium)]
        private void Recount() => RefreshLayerRows();

        private string LiveBandVerdict
        {
            get
            {
                var n = SceneryRoleData.Get(role);
                var isolateNote = !isolating
                    ? "nothing is dimmed"
                    : $"isolating {SceneryRoleData.Label(isolatedRole)}";
                return $"Painting into {SceneryRoleData.Label(role)}\n" +
                       $"{n.Layer}, order {n.Order}, z {n.Z}   ({isolateNote})";
            }
        }

        // ---- Palette --------------------------------------------------------

        [TitleGroup("Palette", "What you are painting from", TitleAlignments.Centered)]
        [InfoBox("Drop a sliced sheet here. Every piece on it becomes a swatch " +
                 "below. Nothing is tiled or stretched, so a piece lands in the " +
                 "scene exactly as it was drawn.")]
        [OnValueChanged(nameof(LoadPalette))]
        [SerializeField] private Texture2D sheet;

        [TitleGroup("Palette")]
        [InfoBox("For art that never arrives as one sheet, a Plant Animations " +
                 "style folder of separate PNGs works the same way: every " +
                 "Sprite found inside becomes a swatch. Leave Sheet empty to " +
                 "use this instead.")]
        [OnValueChanged(nameof(LoadPalette))]
        [SerializeField] private DefaultAsset folder;

        [TitleGroup("Palette")]
        [InfoBox("$SheetVerdict", InfoMessageType.Warning, VisibleIf = "@ScaleWrong")]
        [Button(ButtonSizes.Large, Icon = SdfIconType.Rulers), GUIColor(0.4f, 0.8f, 1f)]
        [ShowIf("@ScaleWrong")]
        private void FixTheScale()
        {
            if (sheet == null) return;

            var path = AssetDatabase.GetAssetPath(sheet);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            importer.spritePixelsPerUnit = GamePixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            LoadPalette();
        }

        [TitleGroup("Palette")]
        [ShowInInspector, HideLabel]
        [PropertyOrder(10)]
        [TableList(IsReadOnly = true, AlwaysExpanded = true, ShowPaging = true, NumberOfItemsPerPage = 8)]
        private List<Swatch> swatches = new();

        // ---- Presets ----------------------------------------------------------

        [TitleGroup("Presets", "One click loads a whole layer: its pictures, band, scale and collider", TitleAlignments.Centered)]
        [OnInspectorGUI]
        private void DrawPresetButtons()
        {
            if (presets.Count == 0)
            {
                EditorGUILayout.HelpBox("No presets yet. Set up a palette below, name it and press Save current.", MessageType.Info);
                return;
            }

            // Buttons flow into as many columns as the window is wide, so the list stays
            // readable in a narrow docked panel instead of running off the edge as a table.
            var width = EditorGUIUtility.currentViewWidth - 30f;
            var columns = Mathf.Max(1, Mathf.FloorToInt(width / 200f));
            Preset remove = null;
            for (var i = 0; i < presets.Count; i += columns)
            {
                EditorGUILayout.BeginHorizontal();
                for (var c = 0; c < columns; c++)
                {
                    if (i + c >= presets.Count) { GUILayout.FlexibleSpace(); continue; }
                    var p = presets[i + c];
                    var live = folder != null && p.folder == folder && sheet == null || sheet != null && p.sheet == sheet;
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = live ? new Color(0.55f, 1f, 0.6f) : new Color(0.75f, 0.85f, 1f);
                    if (GUILayout.Button(p.name, GUILayout.Height(30), GUILayout.MinWidth(80))) ApplyPreset(p);
                    GUI.backgroundColor = new Color(1f, 0.6f, 0.55f);
                    if (GUILayout.Button("x", GUILayout.Width(22), GUILayout.Height(30)) &&
                        EditorUtility.DisplayDialog("Delete preset", "Delete the preset '" + p.name + "'? The pictures are not touched.", "Delete", "Keep"))
                        remove = p;
                    GUI.backgroundColor = old;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (remove != null) presets.Remove(remove);
        }

        [SerializeField, HideInInspector] private List<Preset> presets = new();

        private void ApplyPreset(Preset p)
        {
            sheet = p.sheet;
            folder = p.folder;
            role = p.role;
            pieceScale = p.pieceScale;
            solid = p.solid;
            colliderDepth = p.colliderDepth;
            sortingOrder = p.sortingOrder;
            alternateFlip = p.alternateFlip;
            grid = p.grid;
            LoadPalette();
            RefreshLayerRows();
            painting = true;
            Repaint();
            SceneView.RepaintAll();
        }

        [TitleGroup("Presets")]
        [HorizontalGroup("Presets/Save")]
        [LabelText("Name"), LabelWidth(40)]
        [SerializeField] private string presetName = "";

        [HorizontalGroup("Presets/Save", Width = 90)]
        [Button("Save current", ButtonSizes.Medium), GUIColor(0.5f, 0.8f, 0.5f)]
        private void SavePreset()
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                Debug.LogWarning("[Weizza] Name the preset first, or it is unfindable a week from now.");
                return;
            }

            var existing = presets.Find(p => p.name == presetName);
            var slot = existing ?? new Preset();
            slot.name = presetName;
            slot.sheet = sheet;
            slot.folder = folder;
            slot.role = role;
            slot.pieceScale = pieceScale;
            slot.solid = solid;
            slot.colliderDepth = colliderDepth;
            slot.sortingOrder = sortingOrder;
            slot.alternateFlip = alternateFlip;
            slot.grid = grid;

            if (existing == null) presets.Add(slot);
            presetName = "";
        }

        // ---- Tool -------------------------------------------------------------

        private enum Tool { Paint, Erase, Pick }

        [TitleGroup("Tool", "B paint, E erase, I pick. Hold Ctrl to erase while painting", TitleAlignments.Centered)]
        [InfoBox("$PaintVerdict", InfoMessageType.None)]
        [ToggleLeft, LabelText("Tools on in the Scene view")]
        [GUIColor("@painting ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.75f, 0.5f)")]
        [SerializeField] private bool painting;

        // Picking a tool is the clear sign someone wants to use it. The switch
        // used to live far below in Placing, and Erase or Pick with the switch
        // off did nothing at all, which read as a broken eraser.
        [TitleGroup("Tool")]
        [EnumToggleButtons, HideLabel]
        [OnValueChanged(nameof(TurnOn))]
        [SerializeField] private Tool tool = Tool.Paint;

        private void TurnOn()
        {
            painting = true;
            SceneView.RepaintAll();
        }

        [TitleGroup("Tool")]
        [ShowIf("@tool == Tool.Erase")]
        [PropertyRange(0f, 6f)]
        [Tooltip("0 erases the one piece under the cursor. Above 0, dragging sweeps a circle this many metres across and erases everything it touches.")]
        [SerializeField] private float eraseRadius;

        [TitleGroup("Tool")]
        [ToggleLeft]
        [Tooltip("Erase only pieces in the band you are painting into. Off erases whatever is on top under the cursor.")]
        [SerializeField] private bool eraseOnlyLiveBand;

        [TitleGroup("Tool")]
        [ToggleLeft]
        [Tooltip("Also erase sprites that were placed by hand or by an older Map Painter (no Painted Piece mark). The player, cameras and systems are never touched.")]
        [SerializeField] private bool eraseUnmarked = true;

        [TitleGroup("Tool")]
        [Button("Mark existing pieces in open scenes", ButtonSizes.Medium), GUIColor(0.55f, 0.75f, 0.95f)]
        [Tooltip("Gives every scenery sprite painted before this version its Painted Piece mark and band, so the eraser and eyedropper treat old and new pieces the same.")]
        private void MarkExisting()
        {
            var marked = 0;
            foreach (var r in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (r.sprite == null || IsExcluded(r.transform) || r.GetComponentInParent<PaintedPiece>() != null) continue;
                var piece = PieceOf(r);
                if (piece.GetComponent<PaintedPiece>() != null) continue;
                var mark = Undo.AddComponent<PaintedPiece>(piece);
                foreach (var candidate in SceneryRoleData.BackToFront)
                {
                    if (!SceneryRoleData.Matches(r, candidate)) continue;
                    mark.role = (int)candidate;
                    break;
                }
                mark.source = r.sprite.texture != null ? r.sprite.texture.name : "";
                EditorSceneManager.MarkSceneDirty(piece.scene);
                marked++;
            }
            Debug.Log($"[Map Painter] Marked {marked} existing piece(s).");
        }

        // ---- Placing --------------------------------------------------------

        [TitleGroup("Placing", "How a piece lands", TitleAlignments.Centered)]
        [ValueDropdown(nameof(RoleOptions))]
        [OnValueChanged(nameof(OnRoleChanged))]
        [LabelText("Paint into")]
        [SerializeField] private SceneryRole role = SceneryRole.GroundMoss;

        [TitleGroup("Placing")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Rounds where a piece lands. Zero drops it exactly under the " +
                 "cursor, which is right for scattered props and wrong for a " +
                 "run of platforms meant to read as one ledge.")]
        [SerializeField] private float grid = 0.5f;

        [TitleGroup("Placing")]
        [ToggleLeft]
        [Tooltip("Drag instead of clicking once per piece, the way a Tilemap " +
                 "brush works. A new piece drops whenever the cursor has moved " +
                 "far enough from the last one to not overlap it.")]
        [SerializeField] private bool dragPaints = true;

        [TitleGroup("Placing")]
        [ToggleLeft]
        [Tooltip("Picks a random swatch for every piece instead of the one " +
                 "chosen in the palette, and never repeats the swatch it just " +
                 "used. Off leaves the arrow keys and Use This button in charge.")]
        [SerializeField] private bool randomVariant;

        [TitleGroup("Placing")]
        [PropertyRange(0.25f, 3f)]
        [Tooltip("Scales every piece as it is placed. One is the size it was " +
                 "drawn at. A background band reads further away at 0.6 and a " +
                 "foreground shape reads closer at 1.4, without needing a " +
                 "second drawing of it.")]
        [SerializeField] private float pieceScale = 1f;

        [TitleGroup("Placing")]
        [ToggleLeft, LabelText("Sway in the wind")]
        [Tooltip("Plants, grass and vines bend gently, each on its own beat. Uses the " +
                 "Weizza/SpriteSway material. Vines hang from the top, everything else is " +
                 "planted at the bottom. Animated plants already move and skip this.")]
        [SerializeField] private bool sway;

        [TitleGroup("Placing")]
        [ToggleLeft]
        [Tooltip("Fits a box collider to the drawn pixels. Turn it off for " +
                 "plants and background pieces, which the player should walk " +
                 "straight through.")]
        [SerializeField] private bool solid = true;

        [TitleGroup("Placing")]
        [ShowIf(nameof(solid))]
        [PropertyRange(0f, 1f)]
        [Tooltip("How much of the drawn height the collider covers, measured " +
                 "down from the top. Ground only needs its surface to be solid, " +
                 "and a collider reaching the full way down catches the player " +
                 "on scenery hanging below the ledge. Push it to 1 for a rock " +
                 "that is meant to be solid all the way through.")]
        [SerializeField] private float colliderDepth = 0.35f;

        [TitleGroup("Placing")]
        [PropertyRange(-50, 50)]
        [Tooltip("Starts from the band's own order and can be nudged from " +
                 "there, for two pieces in the same band that still need one " +
                 "in front of the other.")]
        [SerializeField] private int sortingOrder = -5;

        [TitleGroup("Placing")]
        [ToggleLeft]
        [Tooltip("Mirrors every other piece as it is placed, so a long run does " +
                 "not read as the same shape stamped over and over.")]
        [SerializeField] private bool alternateFlip = true;

        [TitleGroup("Placing")]
        [ToggleLeft]
        [ShowIf(nameof(HasWorld))]
        [LabelText("Paint straight into chunks")]
        [Tooltip("On: every piece goes into the chunk scene under it. Off: everything goes into a Draft " +
                 "root, painted freely, and becomes chunks later with \"Make chunk from selection\" in World. " +
                 "Painting past the end of the line always drafts.")]
        [SerializeField] private bool paintIntoChunks = true;

        [TitleGroup("Placing")]
        [ShowIf(nameof(HasWorld))]
        [ShowInInspector, ReadOnly, LabelText("Chunk under cursor")]
        private string ChunkReadout => _lastChunk;

        private string _lastChunk = "move the mouse over the Scene view";

        private static bool HasWorld => ChunkPainting.Atlas != null;

        // ---- Ground paths -----------------------------------------------------

        [TitleGroup("Ground Path", "Hills, ledges and slopes, so the ground is not one flat line", TitleAlignments.Centered)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string GroundPathHelp =>
            "Makes a hill where the Scene view is looking, filed into that stretch's chunk.\n" +
            "Then drag the white points. Shift click the line to add a point, Ctrl click a point to remove it.\n" +
            "Keep slopes under 30 degrees for a natural walk; 40 is the most the player can climb.";

        [TitleGroup("Ground Path")]
        [Button("New Ground Path here", ButtonSizes.Large, Icon = SdfIconType.GraphUp), GUIColor(0.55f, 0.85f, 0.45f)]
        private void NewGroundPath() => GroundPathFactory.CreateAtSceneView();

        // ---- Chunks ---------------------------------------------------------

        [TitleGroup("Chunks", "Every stretch of the map, left to right", TitleAlignments.Centered, Order = -10)]
        [ShowIf(nameof(HasWorld))]
        [InfoBox("$ChunkVerdict", InfoMessageType.Info)]
        [ShowInInspector, HideLabel]
        [TableList(IsReadOnly = true, AlwaysExpanded = true, HideToolbar = true)]
        private List<ChunkRow> chunks = new();

        [TitleGroup("Chunks")]
        [ShowIf(nameof(HasWorld))]
        [HorizontalGroup("Chunks/Buttons")]
        [Button("Show every chunk", ButtonSizes.Medium), GUIColor(0.55f, 0.75f, 0.95f)]
        private void ShowEveryChunk()
        {
            soloChunk = "";
            var svm = SceneVisibilityManager.instance;
            foreach (var root in LoadedChunkRoots()) svm.Show(root.gameObject.scene.GetRootGameObjects(), true);
            SceneView.RepaintAll();
            Repaint();
        }

        [HorizontalGroup("Chunks/Buttons")]
        [Button("Refresh list", ButtonSizes.Medium)]
        private void RefreshChunkRows()
        {
            chunks.Clear();
            var atlas = ChunkPainting.Atlas;
            if (atlas == null) return;

            var ordered = new List<RoomDefinition>();
            foreach (var room in atlas.rooms) if (room != null) ordered.Add(room);
            ordered.Sort((a, b) => a.worldRect.xMin.CompareTo(b.worldRect.xMin));

            foreach (var def in ordered)
            {
                var root = ChunkPainting.FindLoaded(def);
                var count = root != null ? root.GetComponentsInChildren<SpriteRenderer>(true).Length : -1;
                chunks.Add(new ChunkRow(def, count, this));
            }
            Repaint();
        }

        // Kept in the window's own state so a recompile does not forget which
        // chunk was soloed. The hiding itself is Unity's Scene Visibility (the eye
        // in the Hierarchy), which never touches the scene files, so a forgotten
        // solo can never ship.
        [SerializeField, HideInInspector] private string soloChunk = "";

        private string ChunkVerdict => string.IsNullOrEmpty(soloChunk)
            ? "Edit jumps the Scene view to a chunk and paints into it. Solo hides every other chunk, the way Solo does for a layer."
            : $"Only {soloChunk} is showing. Press Show every chunk, or Unsolo on its row, to bring the rest back.";

        private static List<RoomRoot> LoadedChunkRoots()
        {
            var roots = new List<RoomRoot>();
            var atlas = ChunkPainting.Atlas;
            if (atlas == null) return roots;
            foreach (var def in atlas.rooms)
            {
                if (def == null) continue;
                var root = ChunkPainting.FindLoaded(def);
                if (root != null) roots.Add(root);
            }
            return roots;
        }

        private void SoloChunk(RoomDefinition def)
        {
            var target = ChunkPainting.FindLoaded(def);
            if (target == null)
            {
                var opened = ChunkPainting.ChunkAt(def.worldRect.center.x, openIfClosed: true);
                if (opened == null) return;
                target = opened;
            }

            var svm = SceneVisibilityManager.instance;
            foreach (var root in LoadedChunkRoots())
            {
                var roots = root.gameObject.scene.GetRootGameObjects();
                if (root == target) svm.Show(roots, true);
                else svm.Hide(roots, true);
            }
            soloChunk = def.id;
            EditChunk(def);
        }

        private void EditChunk(RoomDefinition def)
        {
            var root = ChunkPainting.FindLoaded(def) ?? ChunkPainting.ChunkAt(def.worldRect.center.x, openIfClosed: true);
            if (root == null) return;

            paintIntoChunks = true;
            Selection.activeGameObject = root.gameObject;
            var r = def.worldRect;
            SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(r.center.x, 3f, 0f), new Vector3(r.width, 12f, 1f)), false);
            SceneView.RepaintAll();
            Repaint();
        }

        // ---- World ----------------------------------------------------------

        [TitleGroup("World", "The map is a line of chunk scenes the game streams", TitleAlignments.Centered, Order = -20)]
        [ShowIf(nameof(HasWorld))]
        [InfoBox("Every chunk is its own scene, opened together here so the whole map shows. " +
                 "Paint anywhere and the piece goes into the chunk under the cursor. " +
                 "Need more map? Add a chunk to the end of the line and keep painting.")]
        [LabelText("Area id"), SerializeField] private string newChunkArea = "forest";

        [TitleGroup("World")]
        [ShowIf(nameof(HasWorld))]
        [PropertyRange(20f, 80f), LabelText("Chunk width (m)"), SerializeField]
        private float newChunkWidth = 40f;

        [TitleGroup("World")]
        [ShowIf(nameof(HasWorld))]
        [Button("$NewChunkLabel", ButtonSizes.Gigantic), GUIColor(0.5f, 0.85f, 0.55f)]
        private void AddChunk()
        {
            // Finishing a chunk and starting the next is one press: the one just
            // painted is saved, so nothing is lost if Unity closes, and the new
            // one is ready to paint into straight away.
            var trimmed = FitLastChunkToDrawing();
            EditorSceneManager.SaveOpenScenes();
            paintIntoChunks = true;
            painting = true;
            if (trimmed != null) Debug.Log("[Map Painter] " + trimmed);

            var def = WorldMapWindow.AppendChunk(newChunkArea, newChunkWidth);
            if (def == null)
            {
                Debug.LogWarning("[Map Painter] The World Atlas is not linear, add rooms from Weizza > World > World Map.");
                return;
            }
            ChunkEditorSync.OpenAll();
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(def.sceneName);
            var root = ChunkPainting.FindIn(scene);
            if (root != null) Selection.activeGameObject = root.gameObject;
            SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(def.worldRect.center.x, 3f, 0f), new Vector3(def.worldRect.width, 12f, 1f)), false);
            Debug.Log($"[Map Painter] Added chunk {def.id} at x {def.worldRect.xMin:0} to {def.worldRect.xMax:0}. Paint into it.");
            RefreshChunkRows();
        }

        /// <summary>
        /// A new chunk opens at the default width so there is room to paint, but
        /// the chunk should end where its drawing ends. Runs after every change in
        /// the Hierarchy (paint, erase, undo), so the last chunk grows and shrinks
        /// with what is drawn in it. Never below 2 m, and an empty chunk is left
        /// alone. Only the last chunk: one in the middle shares both edges with
        /// its neighbours, and moving an edge would open a gap or an overlap.
        /// </summary>
        private static string FitLastChunkToDrawing()
        {
            var atlas = ChunkPainting.Atlas;
            if (atlas == null) return null;
            RoomDefinition last = null;
            foreach (var room in atlas.rooms)
                if (room != null && (last == null || room.worldRect.xMax > last.worldRect.xMax)) last = room;
            if (last == null) return null;

            var root = ChunkPainting.FindLoaded(last);
            if (root == null) return null;

            var drawnEnd = float.MinValue;
            foreach (var r in root.GetComponentsInChildren<Renderer>()) drawnEnd = Mathf.Max(drawnEnd, r.bounds.max.x);
            foreach (var c in root.GetComponentsInChildren<Collider>())
                if (c.gameObject != root.gameObject && !c.isTrigger) drawnEnd = Mathf.Max(drawnEnd, c.bounds.max.x);
            if (drawnEnd == float.MinValue) return null;

            var r0 = last.worldRect;
            var width = Mathf.Max(2f, Mathf.Ceil(drawnEnd - r0.xMin));
            if (Mathf.Abs(width - r0.width) < 0.5f) return null;

            last.worldRect = new Rect(r0.xMin, r0.yMin, width, r0.height);
            EditorUtility.SetDirty(last);

            var box = root.GetComponent<BoxCollider>();
            box.size = new Vector3(width + 1f, box.size.y, box.size.z);
            box.center = new Vector3(width * 0.5f, box.center.y, box.center.z);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return $"{last.id} fitted to its drawing: x {r0.xMin:0} to {r0.xMin + width:0} ({width:0} m).";
        }

        /// <summary>
        /// Painting just past the end of the line keeps going into the last chunk,
        /// which stretches to fit, so the chunk being worked on is never "full".
        /// It is only the start of the next chunk (New chunk) that ends this one,
        /// and FitLastChunkToDrawing then trims it back to what was drawn.
        /// Painting before the start of the line still drafts.
        /// </summary>
        private static RoomRoot GrowLastChunkTo(float x)
        {
            var atlas = ChunkPainting.Atlas;
            if (atlas == null) return null;
            RoomDefinition last = null;
            foreach (var room in atlas.rooms)
                if (room != null && (last == null || room.worldRect.xMax > last.worldRect.xMax)) last = room;
            if (last == null || x < last.worldRect.xMax) return null;

            var root = ChunkPainting.FindLoaded(last);
            if (root == null) return null;

            var r0 = last.worldRect;
            var width = Mathf.Ceil(x + 10f - r0.xMin);
            Undo.RecordObject(last, "Grow chunk");
            last.worldRect = new Rect(r0.xMin, r0.yMin, width, r0.height);
            EditorUtility.SetDirty(last);

            var box = root.GetComponent<BoxCollider>();
            Undo.RecordObject(box, "Grow chunk");
            box.size = new Vector3(width + 1f, box.size.y, box.size.z);
            box.center = new Vector3(width * 0.5f, box.center.y, box.center.z);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return root;
        }

        private void OnHierarchyChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            FitLastChunkToDrawing();
            RefreshChunkRows();
        }

        private string NewChunkLabel
        {
            get
            {
                var atlas = ChunkPainting.Atlas;
                if (atlas == null) return "New chunk";
                var area = string.IsNullOrWhiteSpace(newChunkArea) ? "chunk" : newChunkArea.Trim().Replace(' ', '_');
                return $"New chunk  ({WorldMapWindow.NextChunkId(atlas, area)})";
            }
        }

        // ---- Draft ----------------------------------------------------------

        [TitleGroup("World")]
        [ShowIf(nameof(HasWorld))]
        [InfoBox("$DraftVerdict", InfoMessageType.None)]
        [ToggleLeft, LabelText("One chunk that fits the selection"), SerializeField]
        [Tooltip("On: the new chunk is exactly as wide as what you drew. Off: it is cut every Chunk width metres.")]
        private bool oneChunkForDraft = true;

        [TitleGroup("World")]
        [ShowIf(nameof(HasWorld))]
        [ToggleLeft, LabelText("Move it to the end of the line"), SerializeField]
        [Tooltip("On: the drawing slides along X so it starts where the map currently ends, so a draft drawn " +
                 "anywhere (even at x 0 in an empty scene) becomes the next stretch of the walk. Off: pieces keep " +
                 "their X and go into whatever chunk is already there.")]
        private bool slideDraftToLineEnd;

        [TitleGroup("World")]
        [ShowIf(nameof(HasWorld))]
        [Button("Make chunk from selection", ButtonSizes.Large), GUIColor(0.45f, 0.8f, 1f)]
        private void BakeDraft()
        {
            var pieces = DraftBaker.SelectedPieces();
            if (pieces.Count == 0) pieces = DraftBaker.FindPieces();
            if (pieces.Count == 0)
            {
                Debug.LogWarning("[Map Painter] No draft to bake. Turn off \"Paint straight into chunks\" and paint first.");
                return;
            }
            Debug.Log("[Map Painter] " + DraftBaker.Bake(pieces, newChunkArea, newChunkWidth, oneChunkForDraft, slideDraftToLineEnd));
            SceneView.RepaintAll();
        }

        private string DraftVerdict
        {
            get
            {
                var pieces = DraftBaker.SelectedPieces();
                var which = "selected";
                if (pieces.Count == 0) { pieces = DraftBaker.FindPieces(); which = "in the draft"; }
                if (!DraftBaker.Measure(pieces, out var a, out var b))
                    return "Draft: empty. Untick \"Paint straight into chunks\" to draw freely, then come back here.";
                return $"Draft: {pieces.Count} piece(s) {which}, x {a:0.#} to {b:0.#} ({b - a:0.#} m). " +
                       $"Select part of it (or nothing for all), set Area id, then make it a chunk.";
            }
        }

        // ---- Walk check -------------------------------------------------------

        [TitleGroup("Check", "Can the player actually walk it?", TitleAlignments.Centered)]
        [InfoBox("$WalkVerdict", "@_walkIssues.Count > 0", InfoMessageType = InfoMessageType.Warning)]
        [InfoBox("Sweeps the whole map every 0.2 m: gaps too wide to jump (over 3 m), steps too high to climb " +
                 "(over 2.2 m), ceilings too low to stand under (under 1.9 m). Problems show as red marks in the " +
                 "Scene view. A pit you meant to be a pit is fine, the list is advice.", "@_walkIssues.Count == 0")]
        [Button("Check the whole walk", ButtonSizes.Large), GUIColor(1f, 0.8f, 0.45f)]
        private void CheckWalk()
        {
            float from = -10f, to = 250f;
            var atlas = ChunkPainting.Atlas;
            if (atlas != null && atlas.rooms.Length > 0)
            {
                from = float.MaxValue;
                to = float.MinValue;
                foreach (var room in atlas.rooms)
                {
                    if (room == null) continue;
                    from = Mathf.Min(from, room.worldRect.xMin);
                    to = Mathf.Max(to, room.worldRect.xMax);
                }
            }
            // Stay clear of the end walls, which a downward ray would read as a cliff.
            _walkIssues = WalkCheck.Run(from + 0.6f, to - 0.6f);
            _walkChecked = true;
            foreach (var issue in _walkIssues) Debug.Log("[Walk Check] " + issue.text);
            if (_walkIssues.Count == 0) Debug.Log("[Walk Check] Clean: every gap, step and ceiling is within what the player can do.");
            SceneView.RepaintAll();
        }

        [TitleGroup("Check")]
        [ShowIf("@_walkIssues.Count > 0")]
        [Button("Clear the marks", ButtonSizes.Medium)]
        private void ClearWalkMarks()
        {
            _walkIssues.Clear();
            _walkChecked = false;
            SceneView.RepaintAll();
        }

        private List<WalkCheck.Issue> _walkIssues = new();
        private bool _walkChecked;

        private string WalkVerdict
        {
            get
            {
                if (_walkIssues.Count == 0) return _walkChecked ? "Clean." : "";
                var lines = new System.Text.StringBuilder($"{_walkIssues.Count} thing(s) to look at:\n");
                for (var i = 0; i < _walkIssues.Count && i < 12; i++) lines.AppendLine("- " + _walkIssues[i].text);
                if (_walkIssues.Count > 12) lines.Append("(more in the Console)");
                return lines.ToString().TrimEnd();
            }
        }

        // Drawn whether or not the painting tools are on, so the marks stay
        // visible while the problem is being fixed by hand.
        private void DrawWalkIssues(SceneView view)
        {
            if (_walkIssues.Count == 0 || Event.current.type != EventType.Repaint) return;
            foreach (var issue in _walkIssues)
            {
                Handles.color = issue.serious ? new Color(1f, 0.25f, 0.2f, 0.95f) : new Color(1f, 0.75f, 0.2f, 0.95f);
                Handles.DrawWireDisc(issue.at + Vector3.up * 0.5f, Vector3.forward, 0.6f, 3f);
                Handles.DrawLine(issue.at, issue.at + Vector3.up * 3f, 3f);
                Handles.Label(issue.at + Vector3.up * 3.3f, issue.text, Badge());
            }
        }

        // ---- Camera -----------------------------------------------------------

        [TitleGroup("Camera", "Line the Scene view up with the game", TitleAlignments.Centered)]
        [InfoBox("The Scene view free camera makes it easy to place a piece at " +
                 "an angle that looks right here and reads wrong in Game, " +
                 "because the pitch does not match. This puts the Scene camera " +
                 "at the exact angle ViewRig plays at, orthographic.")]
        [Button(ButtonSizes.Large, Icon = SdfIconType.Camera), GUIColor(0.5f, 0.8f, 0.5f)]
        private void LockCameraToGamePitch()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                Debug.LogWarning("[Weizza] No Scene view is open to lock.");
                return;
            }

            var rig = FindAnyObjectByType<ViewRig>();
            var pitch = rig != null ? rig.PitchDegrees : DefaultPitch;
            var size = rig != null ? rig.OrthographicSize : DefaultOrthoSize;

            view.orthographic = true;
            view.rotation = Quaternion.Euler(pitch, 0f, 0f);
            view.size = size;
            view.in2DMode = false;
            view.Repaint();
        }

        private const float DefaultPitch = 16f;
        private const float DefaultOrthoSize = 3.6f;

        // ---- State ----------------------------------------------------------

        private int chosen;
        private int lastPainted = -1;
        private int placed;
        private bool flipNext;
        private Vector3? lastPlacePosition;

        /// <summary>
        /// What every dimmed renderer looked like before it was dimmed.
        ///
        /// Serialized, and that is the whole point. A plain dictionary is wiped
        /// by the domain reload that follows every script recompile, and a
        /// scene dimmed by a dictionary that no longer exists can never be put
        /// back: the artist is left with a level at twelve percent alpha and
        /// nothing on screen explaining why. Unity keeps serialized EditorWindow
        /// state across a reload, scene object references included, so the way
        /// home survives with it.
        /// </summary>
        [SerializeField, HideInInspector] private List<DimRecord> dimmed = new();

        [SerializeField, HideInInspector] private bool isolating;
        [SerializeField, HideInInspector] private SceneryRole isolatedRole;

        private const float GamePixelsPerUnit = 150f;
        private const float DimAlpha = 0.12f;

        [System.Serializable]
        private sealed class DimRecord
        {
            public SpriteRenderer renderer;
            public Color original;
        }

        // ---- Readouts -------------------------------------------------------

        private static IEnumerable<ValueDropdownItem<SceneryRole>> RoleOptions()
        {
            foreach (var r in SceneryRoleData.BackToFront)
            {
                yield return new ValueDropdownItem<SceneryRole>(SceneryRoleData.Label(r), r);
            }
        }

        private bool ScaleWrong
        {
            get
            {
                if (swatches.Count == 0) return false;
                var widest = swatches.Max(s => s.Metres.x);

                // A piece wider than about six players is not scenery any more,
                // it is the whole screen, and the import is the only thing that
                // could have made it that way.
                return widest > 11f || widest < 0.4f;
            }
        }

        private string SheetVerdict
        {
            get
            {
                if (swatches.Count == 0) return string.Empty;
                var widest = swatches.Max(s => s.Metres.x);
                return $"The widest piece is {widest:0.0} m across and the player " +
                       $"is 1.8 m. This sheet was imported at " +
                       $"{swatches[0].sprite.pixelsPerUnit:0} pixels per unit; the " +
                       $"game is built for {GamePixelsPerUnit:0}.";
            }
        }

        private string PaintVerdict => painting
            ? $"On. B paint, E erase (red box shows what goes), I pick a piece to paint more of it, " +
              $"Ctrl held erases, arrows change piece, [ ] scale, F flip, Esc stops. " +
              $"One drag is one undo. {placed} placed so far."
            : "Off. Tick this or click a tool below, then work in the Scene view.";

        // ---- Palette loading ------------------------------------------------

        /// <summary>
        /// Sheet and folder are two answers to the same question, "what am I
        /// painting from", so only one is ever read. Sheet wins when both are
        /// set, since assigning a sheet is a more specific choice than a
        /// folder that happened to still be there from an earlier preset.
        /// </summary>
        private void LoadPalette()
        {
            swatches.Clear();
            chosen = 0;

            if (sheet != null) LoadFromSheet();
            else if (folder != null) LoadFromFolder();
        }

        private void LoadFromSheet()
        {
            var path = AssetDatabase.GetAssetPath(sheet);
            var found = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();

            if (found.Count == 0)
            {
                Debug.LogWarning($"[Weizza] {sheet.name} has no sprites. Slice it " +
                                 "first with Weizza > Sprite Slicer.");
                return;
            }

            // Left to right, top to bottom, the order a person reads the sheet
            // in, so the number on a swatch means something when talking about it.
            found.Sort((a, b) =>
            {
                var byRow = b.rect.yMax.CompareTo(a.rect.yMax);
                return byRow != 0 ? byRow : a.rect.xMin.CompareTo(b.rect.xMin);
            });

            for (var i = 0; i < found.Count; i++) swatches.Add(new Swatch(i, found[i], this));
        }

        /// <summary>
        /// For art that was never sliced onto a sheet at all: a folder of
        /// separate PNGs, each its own Sprite, the way Plant Animations ships.
        /// Every sprite anywhere inside the folder becomes one swatch, sorted
        /// by file name so a numbered set (leaf_01, leaf_02, ...) stays in order.
        /// </summary>
        private void LoadFromFolder()
        {
            var path = AssetDatabase.GetAssetPath(folder);
            if (LoadAnimatedPlants(path)) return;

            var guids = AssetDatabase.FindAssets("t:Sprite", new[] { path });
            var found = new List<Sprite>();

            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                found.AddRange(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>());
            }

            if (found.Count == 0)
            {
                Debug.LogWarning($"[Weizza] No sprites found under {path}. Each file needs " +
                                 "Texture Type set to Sprite before it shows up here.");
                return;
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (var i = 0; i < found.Count; i++) swatches.Add(new Swatch(i, found[i], this));
        }

        /// <summary>
        /// A folder whose own files are not sprites but whose subfolders each hold
        /// a run of frames is the Plant Animations layout: one subfolder is one
        /// plant. Each becomes a single swatch that paints a plant already
        /// animating, instead of a hundred loose frames to choose from.
        /// </summary>
        private bool LoadAnimatedPlants(string path)
        {
            if (AssetDatabase.FindAssets("t:Sprite", new[] { path })
                .Any(g => System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(g))?.Replace('\\', '/') == path))
                return false;

            var subs = AssetDatabase.GetSubFolders(path);
            System.Array.Sort(subs, string.CompareOrdinal);
            foreach (var sub in subs)
            {
                // Names that promise a mechanic the game does not have (the project notes,
                // Plant Animations): a player who sees a poison plant avoids it.
                var leaf = System.IO.Path.GetFileName(sub);
                if (leaf.Contains("Poison") || leaf.Contains("Jump")) continue;

                var frames = AssetDatabase.FindAssets("t:Sprite", new[] { sub })
                    .Select(g => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(s => s != null)
                    .OrderBy(s => s.name, System.StringComparer.Ordinal)
                    .ToList();
                if (frames.Count == 0) continue;
                swatches.Add(new Swatch(swatches.Count, frames[0], this, sub));
            }
            return swatches.Count > 0;
        }

        [System.Serializable]
        private sealed class Preset
        {
            [HorizontalGroup("Row", 0.3f), HideLabel]
            public string name;

            [HorizontalGroup("Row", 0.5f), HideLabel, DisplayAsString]
            private string Source => sheet != null ? sheet.name : folder != null ? folder.name : "(empty)";

            [HorizontalGroup("Row", Width = 55)]
            [Button("Load"), GUIColor(0.5f, 0.75f, 0.95f)]
            private void Load()
            {
                var window = MapPainterWindow.Active;
                if (window == null) return;

                window.sheet = sheet;
                window.folder = folder;
                window.role = role;
                window.pieceScale = pieceScale;
                window.solid = solid;
                window.colliderDepth = colliderDepth;
                window.sortingOrder = sortingOrder;
                window.alternateFlip = alternateFlip;
                window.grid = grid;
                window.LoadPalette();
                window.OnRoleChanged();
            }

            public Texture2D sheet;
            public DefaultAsset folder;
            public SceneryRole role;
            public float pieceScale;
            public bool solid;
            public float colliderDepth;
            public int sortingOrder;
            public bool alternateFlip;
            public float grid;
        }

        private void OnRoleChanged()
        {
            var n = SceneryRoleData.Get(role);
            solid = n.DefaultSolid;
            if (n.DefaultSolid) colliderDepth = n.DefaultColliderDepth;
            sortingOrder = n.Order;

            // Following the live band is the whole point of isolating: picking
            // a band to paint into and still looking at the previous one is how
            // a piece ends up in the wrong place.
            if (isolating) Isolate(role);

            RefreshLayerRows();
            Repaint();
        }

        // ---- Layer rows and isolation ---------------------------------------

        private void RefreshLayerRows()
        {
            layers.Clear();
            var renderers = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);

            foreach (var r in SceneryRoleData.BackToFront)
            {
                var count = 0;
                foreach (var sr in renderers)
                {
                    if (SceneryRoleData.Matches(sr, r)) count++;
                }

                layers.Add(new LayerRow(r, count, this));
            }

            Repaint();
        }

        private void Isolate(SceneryRole which)
        {
            // Always back to the real colours first. Dimming on top of dimming
            // multiplies the alpha down a second time and the way back is lost.
            RestoreColours();

            foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (SceneryRoleData.Matches(sr, which)) continue;

                dimmed.Add(new DimRecord { renderer = sr, original = sr.color });

                var colour = sr.color;
                colour.a *= DimAlpha;
                sr.color = colour;
            }

            isolating = true;
            isolatedRole = which;
            SceneView.RepaintAll();
            Repaint();
        }

        private void RestoreColours()
        {
            foreach (var record in dimmed)
            {
                if (record?.renderer != null) record.renderer.color = record.original;
            }

            dimmed.Clear();
            SceneView.RepaintAll();
        }

        // ---- Scene interaction ----------------------------------------------

        private void OnEnable()
        {
            Active = this;
            SceneView.duringSceneGui += OnScene;
            SceneView.duringSceneGui += DrawWalkIssues;
            if ((sheet != null || folder != null) && swatches.Count == 0) LoadPalette();

            // Two states to recover from after a domain reload. Still soloing:
            // OnDisable undimmed everything on the way out, so put it back.
            // Not soloing but holding records: something was interrupted, and
            // those records are the only way home for whatever is still dim.
            if (isolating) Isolate(isolatedRole);
            else if (dimmed.Count > 0) RestoreColours();

            RefreshLayerRows();
            RefreshChunkRows();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            SceneView.duringSceneGui -= OnScene;
            SceneView.duringSceneGui -= DrawWalkIssues;

            // Undim on the way out, always. This fires both when the window is
            // closed and on every script recompile, and a scene left dim by a
            // window that is no longer there has nothing on screen explaining
            // why half the level looks faded. The isolating flag is kept as it
            // was, so OnEnable knows whether to put the dimming back.
            RestoreColours();
        }

        private void OnScene(SceneView view)
        {
            if (!painting) return;

            // Erasing needs no palette at all, only placing does. Bailing out
            // here whenever no sheet or folder was ever dropped in meant
            // ctrl-click could never reach Erase() below, which read as the
            // eraser being broken when the real fault was nothing being
            // loaded yet.
            var canPlace = swatches.Count > 0;
            if (!canPlace && tool == Tool.Paint && !Event.current.control && Event.current.type != EventType.MouseDown
                           && Event.current.type != EventType.MouseDrag
                           && Event.current.type != EventType.KeyDown) return;

            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);

            // Takes the default handle away while painting, otherwise a click
            // meant to place a piece selects whatever is already under it.
            HandleUtility.AddDefaultControl(id);

            var where = PointerInScene(e.mousePosition);

            if (canPlace && tool == Tool.Paint && !e.control) DrawGhost(where);
            if (paintIntoChunks && HasWorld && e.type == EventType.Repaint) DrawChunkEdges(where.x);

            // What a click would do right now: Ctrl turns any tool into the eraser
            // for as long as it is held.
            var acting = e.control ? Tool.Erase : tool;

            // Show what the click would hit before it happens: red for erase,
            // blue for the eyedropper. Guessing which of three stacked pieces an
            // eraser will take is the main reason erasing felt unreliable.
            if (e.type == EventType.Repaint && acting != Tool.Paint)
                DrawHoverTargets(where, acting == Tool.Erase ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(0.4f, 0.8f, 1f, 0.95f),
                    acting == Tool.Erase ? eraseRadius : 0f);

            if (e.type == EventType.KeyDown && !e.control && !e.alt)
            {
                switch (e.keyCode)
                {
                    case KeyCode.B: tool = Tool.Paint; Repaint(); e.Use(); break;
                    case KeyCode.E: tool = Tool.Erase; Repaint(); e.Use(); break;
                    case KeyCode.I: tool = Tool.Pick; Repaint(); e.Use(); break;
                    case KeyCode.LeftBracket: pieceScale = Mathf.Max(0.25f, pieceScale - 0.1f); Repaint(); e.Use(); break;
                    case KeyCode.RightBracket: pieceScale = Mathf.Min(3f, pieceScale + 0.1f); Repaint(); e.Use(); break;
                    case KeyCode.F: flipNext = !flipNext; Repaint(); e.Use(); break;
                }
            }

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    painting = false;
                    lastPlacePosition = null;
                    Repaint();
                    e.Use();
                }
                else if (canPlace && e.keyCode is KeyCode.RightArrow or KeyCode.UpArrow)
                {
                    Step(1);
                    e.Use();
                }
                else if (canPlace && e.keyCode is KeyCode.LeftArrow or KeyCode.DownArrow)
                {
                    Step(-1);
                    e.Use();
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                // One stroke, one undo: everything painted or erased between
                // mouse down and mouse up comes back with a single Ctrl+Z.
                Undo.IncrementCurrentGroup();
                _strokeGroup = Undo.GetCurrentGroup();

                if (acting == Tool.Erase) Erase(RawPointer(e.mousePosition));
                else if (acting == Tool.Pick) Pick(RawPointer(e.mousePosition));
                else if (canPlace) Place(where);
                lastPlacePosition = where;
                e.Use();
            }

            if (e.type == EventType.MouseUp && e.button == 0 && _strokeGroup >= 0)
            {
                Undo.CollapseUndoOperations(_strokeGroup);
                Undo.SetCurrentGroupName(acting == Tool.Erase ? "Map Painter erase" : "Map Painter paint");
                _strokeGroup = -1;
            }

            // Drag painting fires from MouseDrag rather than every MouseMove, so
            // a piece is only considered once the mouse has actually moved with
            // the button held, not on every frame the cursor sits still.
            if (e.type == EventType.MouseDrag && e.button == 0 && !e.alt)
            {
                if (acting == Tool.Erase)
                {
                    Erase(RawPointer(e.mousePosition));
                }
                else if (acting == Tool.Paint && dragPaints && canPlace && FarEnoughFromLastPlacement(where))
                {
                    Place(where);
                }
                lastPlacePosition = where;
                e.Use();
            }

            if (e.type == EventType.MouseMove) view.Repaint();
        }

        /// <summary>
        /// A drag lays pieces edge to edge rather than piling a hundred of them
        /// on top of each other between two mouse move events. Half the current
        /// swatch's own width is the gap that keeps a fast drag from doubling
        /// up and a slow one from leaving holes.
        /// </summary>
        private bool FarEnoughFromLastPlacement(Vector3 where)
        {
            if (lastPlacePosition == null) return true;
            var width = swatches[chosen].Metres.x * pieceScale;
            var minGap = Mathf.Max(grid, width * 0.5f);
            return Vector3.Distance(lastPlacePosition.Value, where) >= minGap;
        }

        private void Step(int by)
        {
            chosen = (chosen + by + swatches.Count) % swatches.Count;
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Where the cursor is on the plane the game is played on.
        ///
        /// Cast against z = 0 rather than against whatever collider happens to
        /// be under the cursor, because a piece dropped onto the surface of the
        /// piece already there ends up at a different depth from its neighbours
        /// and the sorting order stops meaning anything. The band's own Z is
        /// applied afterwards, in Place, once the piece actually exists.
        /// </summary>
        private Vector3 PointerInScene(Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var plane = new Plane(Vector3.forward, Vector3.zero);

            var at = plane.Raycast(ray, out var distance)
                ? ray.GetPoint(distance)
                : Vector3.zero;

            return new Vector3(Snap(at.x), Snap(at.y), 0f);
        }

        private float Snap(float value) =>
            grid <= 0f ? value : Mathf.Round(value / grid) * grid;

        private void DrawGhost(Vector3 at)
        {
            var swatch = swatches[chosen];
            var size = swatch.Metres * pieceScale;

            Handles.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Handles.DrawWireCube(at + new Vector3(0f, size.y * 0.5f, 0f), size);

            Handles.BeginGUI();
            var screen = HandleUtility.WorldToGUIPoint(at);
            var label = $"{chosen + 1} of {swatches.Count}   {size.x:0.0} x {size.y:0.0} m\n" +
                        $"{SceneryRoleData.Label(role)}";
            GUI.Label(new Rect(screen.x + 12f, screen.y - 40f, 360f, 36f), label, Badge());
            Handles.EndGUI();
        }

        private static GUIStyle Badge() => new(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.6f, 0.95f, 1f) },
            fontStyle = FontStyle.Bold,
        };

        // ---- Placing --------------------------------------------------------

        public const string SwayMaterialPath = "Assets/GameData/Atmosphere/SpriteSway.mat";

        private static float DrawnBottom(Sprite sprite)
        {
            var v = sprite.vertices;
            if (v.Length == 0) return sprite.bounds.min.y;
            var min = float.MaxValue;
            foreach (var p in v) if (p.y < min) min = p.y;
            return min;
        }

        private void Place(Vector3 at)
        {
            if (randomVariant) PickRandomSwatch();

            var swatch = swatches[chosen];
            var sprite = swatch.sprite;
            var numbers = SceneryRoleData.Get(role);

            var go = new GameObject(PieceName(sprite));
            Undo.RegisterCreatedObjectUndo(go, "Paint piece");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = numbers.Layer;
            renderer.sortingOrder = sortingOrder;

            // The mark the eraser and eyedropper find pieces by.
            var mark = go.AddComponent<PaintedPiece>();
            mark.role = (int)role;
            mark.source = sheet != null ? sheet.name : folder != null ? folder.name : "";

            if (alternateFlip && flipNext) renderer.flipX = true;
            flipNext = !flipNext;

            go.transform.localScale = Vector3.one * pieceScale;

            // Sat on the click rather than centred on it, by the lowest drawn
            // pixel, whatever the pivot. Seating by the sprite rectangle floated
            // pieces by the artist's empty padding, up to 2.28 m on the Plant
            // Animations frames (the project notes, sprite.bounds against vertices).
            go.transform.position = new Vector3(at.x, at.y - DrawnBottom(sprite) * pieceScale, numbers.Z);
            go.transform.SetParent(RoleGroupAt(at.x), true);

            if (swatch.animFolder != null)
            {
                // A plant that is already animating. Its own rate, so a row of
                // them never sways in unison.
                var animator = go.AddComponent<SpriteAnimator>();
                animator.AddClipFromFolder(swatch.animFolder, "Idle", Random.Range(8f, 13f), true);
            }
            else if (sway)
            {
                var swayMat = AssetDatabase.LoadAssetAtPath<Material>(SwayMaterialPath);
                if (swayMat != null)
                {
                    renderer.sharedMaterial = swayMat;
                    var bend = go.AddComponent<SpriteSway>();
                    bend.Configure(role == SceneryRole.Vines, role == SceneryRole.Vines ? 0.22f : 0.08f);
                }
            }

            // Foreground slides past faster than the world, like Neva's black
            // foreground. It is never something to stand on.
            if (role == SceneryRole.ForegroundSilhouette) go.AddComponent<ForegroundDrift>();
            else if (solid && swatch.animFolder == null) AddCollider(go, renderer, swatch.Metres);

            // A piece painted while another band is soloed has to be dimmed
            // with the rest of its band, or the level slowly fills with bright
            // pieces that are not actually in the band being looked at.
            if (isolating && !SceneryRoleData.Matches(renderer, isolatedRole))
            {
                dimmed.Add(new DimRecord { renderer = renderer, original = renderer.color });

                var c = renderer.color;
                c.a *= DimAlpha;
                renderer.color = c;
            }

            Selection.activeGameObject = go;
            placed++;

            EditorSceneManager.MarkSceneDirty(go.scene);
            Repaint();
        }

        /// <summary>
        /// Roots the eraser never reaches into, no matter how close a click
        /// lands. The player's own body and the screen-building infrastructure
        /// are not scenery, and a stray ctrl-click that happened to land near
        /// the player should never be able to delete it.
        /// </summary>
        private static readonly string[] EraseExcludedRoots =
        {
            "Player", "Systems", "Dialogue Canvas", "EventSystem", "Main Camera",
            "CM_Follow", "View Rig", "Camera Feel", "Camera Anchor",
            "Encounter Director", "Atmosphere", "Stage", "HUD", "Pause Menu Canvas",
        };

        private int _strokeGroup = -1;
        private readonly List<SpriteRenderer> _hits = new();
        private readonly List<GameObject> _targets = new();

        private static Ray RawPointer(Vector2 mouse) => HandleUtility.GUIPointToWorldRay(mouse);

        /// <summary>
        /// Every sprite the mouse ray passes through, front most first.
        ///
        /// Tested against each sprite on its own Z plane, not against z = 0. The
        /// camera is pitched, so a background piece at Z 5 is drawn somewhere else
        /// on screen than its X and Y say; hitting it where it is drawn is what
        /// makes "click on it to erase it" work for every band. Whole drawn area,
        /// not distance to the pivot, so clicking anywhere on a wide platform hits it.
        /// </summary>
        private void HitTest(Ray ray, float radius, List<SpriteRenderer> into)
        {
            into.Clear();
            foreach (var r in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r.sprite == null || IsExcluded(r.transform)) continue;
                var marked = r.GetComponentInParent<PaintedPiece>() != null;
                if (!marked && !eraseUnmarked) continue;
                if (eraseOnlyLiveBand && !SceneryRoleData.Matches(r, role)) continue;

                var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, r.transform.position.z));
                if (!plane.Raycast(ray, out var distance)) continue;
                var p = ray.GetPoint(distance);
                var b = r.bounds;
                if (p.x < b.min.x - radius || p.x > b.max.x + radius || p.y < b.min.y - radius || p.y > b.max.y + radius) continue;
                into.Add(r);
            }

            // Front most first: sorting layer, then order, then closest to the camera.
            into.Sort((a, b) =>
            {
                var layer = SortingLayer.GetLayerValueFromID(b.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(a.sortingLayerID));
                if (layer != 0) return layer;
                var order = b.sortingOrder.CompareTo(a.sortingOrder);
                return order != 0 ? order : a.transform.position.z.CompareTo(b.transform.position.z);
            });
        }

        /// <summary>
        /// The object to delete for a hit sprite: the whole painted piece (its
        /// collider and any children go with it), never just the drawing, and never
        /// a folder or a chunk root above it.
        /// </summary>
        private static GameObject PieceOf(SpriteRenderer r)
        {
            var marked = r.GetComponentInParent<PaintedPiece>();
            if (marked != null) return marked.gameObject;

            var t = r.transform;
            while (t.parent != null && t.parent.parent != null &&
                   t.parent.GetComponents<Component>().Length > 1 &&
                   t.parent.GetComponent<RoomRoot>() == null)
            {
                t = t.parent;
            }
            return t.gameObject;
        }

        private void CollectTargets(Ray ray, float radius)
        {
            HitTest(ray, radius, _hits);
            _targets.Clear();
            foreach (var hit in _hits)
            {
                var piece = PieceOf(hit);
                if (!_targets.Contains(piece)) _targets.Add(piece);
                // A plain click takes the front most piece only. The radius
                // brush is the way to take several at once.
                if (radius <= 0f) break;
            }
        }

        private void Erase(Ray ray)
        {
            CollectTargets(ray, eraseRadius);
            if (_targets.Count == 0) return;

            foreach (var go in _targets)
            {
                if (go == null) continue;
                var scene = go.scene;
                Undo.DestroyObjectImmediate(go);
                EditorSceneManager.MarkSceneDirty(scene);
                placed = Mathf.Max(0, placed - 1);
            }
            _targets.Clear();
            Repaint();
        }

        /// <summary>
        /// Eyedropper: takes the piece under the cursor and makes it the brush, its
        /// sheet, its swatch, its band and its scale, then switches back to painting.
        /// "Paint more of that one" without hunting through the palette.
        /// </summary>
        private void Pick(Ray ray)
        {
            HitTest(ray, 0f, _hits);
            // While a band is soloed, only that band can be picked. The topmost hit used
            // to win, so picking a stone through a dimmed foreground plant picked the plant.
            if (isolating) _hits.RemoveAll(h => h == null || !SceneryRoleData.Matches(h, isolatedRole));
            if (_hits.Count == 0) return;
            var r = _hits[0];

            var index = swatches.FindIndex(s => s.sprite == r.sprite);
            if (index < 0)
            {
                var path = AssetDatabase.GetAssetPath(r.sprite.texture);
                var dir = string.IsNullOrEmpty(path) ? null : System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');

                // An art pack folder with a saved preset: load the whole preset, so the
                // palette is the folder and the band, scale and collider come with it.
                var preset = dir == null ? null : presets.Find(p => p.folder != null && AssetDatabase.GetAssetPath(p.folder) == dir);
                if (preset != null) ApplyPreset(preset);
                else if (!string.IsNullOrEmpty(path))
                {
                    // One picture per file means the folder is the palette, not the file.
                    var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count();
                    if (sprites <= 1 && dir != null) { sheet = null; folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(dir); }
                    else { sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(path); folder = null; }
                    LoadPalette();
                }
                index = swatches.FindIndex(s => s.sprite == r.sprite);
            }
            if (index >= 0) chosen = index;

            var marked = r.GetComponentInParent<PaintedPiece>();
            if (marked != null && System.Enum.IsDefined(typeof(SceneryRole), marked.role))
            {
                role = (SceneryRole)marked.role;
                OnRoleChanged();
            }
            pieceScale = Mathf.Clamp(Mathf.Abs(r.transform.lossyScale.x), 0.25f, 3f);
            tool = Tool.Paint;
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawHoverTargets(Vector3 where, Color colour, float radius)
        {
            CollectTargets(RawPointer(Event.current.mousePosition), radius);
            foreach (var go in _targets)
            {
                foreach (var r in go.GetComponentsInChildren<SpriteRenderer>())
                {
                    var b = r.bounds;
                    var z = r.transform.position.z;
                    var corners = new[]
                    {
                        new Vector3(b.min.x, b.min.y, z), new Vector3(b.min.x, b.max.y, z),
                        new Vector3(b.max.x, b.max.y, z), new Vector3(b.max.x, b.min.y, z),
                    };
                    Handles.DrawSolidRectangleWithOutline(corners, new Color(colour.r, colour.g, colour.b, 0.12f), colour);
                }
            }

            if (radius > 0f)
            {
                Handles.color = colour;
                Handles.DrawWireDisc(where, Vector3.forward, radius);
            }

            Handles.BeginGUI();
            var screen = HandleUtility.WorldToGUIPoint(where);
            var label = _targets.Count == 0 ? "nothing here"
                : _targets.Count == 1 ? _targets[0].name
                : $"{_targets.Count} pieces";
            GUI.Label(new Rect(screen.x + 12f, screen.y - 22f, 320f, 20f),
                (tool == Tool.Pick && !Event.current.control ? "Pick: " : "Erase: ") + label, Badge());
            Handles.EndGUI();
        }

        /// <summary>
        /// Checked at every level, not only the scene root: after a chunk split,
        /// Atmosphere sits under a chunk root, and a full screen light shaft in
        /// front of everything would otherwise be the first thing every erase hits.
        /// </summary>
        private static bool IsExcluded(Transform t)
        {
            for (var at = t; at != null; at = at.parent)
            {
                if (at.GetComponent<PaintedPiece>() != null) return false;
                foreach (var excluded in EraseExcludedRoots)
                    if (at.name == excluded) return true;
            }
            return false;
        }

        private void PickRandomSwatch()
        {
            if (swatches.Count <= 1) return;

            int next;
            do
            {
                next = Random.Range(0, swatches.Count);
            } while (next == lastPainted);

            chosen = next;
            lastPainted = next;
        }

        /// <summary>
        /// Boxes the top slice of the piece.
        ///
        /// Three dimensional on purpose. The player moves on a CharacterController,
        /// which is PhysX, and PhysX cannot see a 2D collider at all. A level built
        /// with 2D colliders looks finished in the scene view and drops the player
        /// through the floor on the first frame of play.
        ///
        /// Sized in the sprite's own local units, not in metres, because the
        /// collider is a child of a transform that may be scaled: passing the
        /// already scaled size would apply the scale twice.
        /// </summary>
        private void AddCollider(GameObject go, SpriteRenderer renderer, Vector2 localSize)
        {
            // A rock or a stump is not a rectangle: follow its drawn outline.
            // Flat strips fall through to the box below.
            if (OutlineColliders.Wants(renderer.sprite))
            {
                var mesh = go.AddComponent<MeshCollider>();
                mesh.sharedMesh = OutlineColliders.MeshForSprite(renderer.sprite, renderer.flipX);
                if (mesh.sharedMesh != null) return;
                DestroyImmediate(mesh);
            }

            var box = go.AddComponent<BoxCollider>();
            var depth = Mathf.Clamp01(colliderDepth);

            // Fitted to the drawn pixels (the sprite's tight mesh), not the sprite
            // rectangle. sprite.bounds includes the transparent padding the artist
            // left, which put the walkable top up to two metres above the moss
            // (the project notes, Plant Animations) and shifted it sideways on lopsided art.
            var drawn = TightBounds(renderer.sprite);
            var height = Mathf.Max(0.05f, drawn.size.y * depth);
            var top = drawn.max.y;

            box.size = new Vector3(drawn.size.x, height, 1f);
            // flipX mirrors the drawing but not the collider, so mirror the offset too.
            box.center = new Vector3(renderer.flipX ? -drawn.center.x : drawn.center.x, top - height * 0.5f, 0f);
        }

        /// <summary>Local bounds of the opaque mesh Unity cut around the drawing.</summary>
        private static Bounds TightBounds(Sprite sprite)
        {
            var v = sprite.vertices;
            if (v == null || v.Length == 0) return sprite.bounds;
            var min = v[0];
            var max = v[0];
            foreach (var p in v)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var b = new Bounds();
            b.SetMinMax(new Vector3(min.x, min.y, 0f), new Vector3(max.x, max.y, 0f));
            return b;
        }

        [TitleGroup("Tool")]
        [Button("Refit colliders of selected pieces", ButtonSizes.Medium)]
        [Tooltip("Rebuilds the box collider of every selected painted piece to its drawn pixels, using the Collider Depth above. For pieces painted before the tight fit existed.")]
        private void RefitSelected()
        {
            var count = 0;
            foreach (var go in Selection.gameObjects)
            {
                foreach (var r in go.GetComponentsInChildren<SpriteRenderer>())
                {
                    var box = r.GetComponent<BoxCollider>();
                    if (box == null || box.isTrigger || r.sprite == null) continue;
                    Undo.RecordObject(box, "Refit collider");
                    var drawn = TightBounds(r.sprite);
                    var height = Mathf.Max(0.05f, drawn.size.y * Mathf.Clamp01(colliderDepth));
                    box.size = new Vector3(drawn.size.x, height, box.size.z);
                    box.center = new Vector3(r.flipX ? -drawn.center.x : drawn.center.x, drawn.max.y - height * 0.5f, 0f);
                    count++;
                }
            }
            Debug.Log($"[Map Painter] Refit {count} collider(s).");
        }

        /// <summary>
        /// Chunk seams as tall lines with each chunk's id, and the one under the
        /// cursor highlighted, so the painter always knows which scene a click lands in.
        /// </summary>
        private void DrawChunkEdges(float cursorX)
        {
            var atlas = ChunkPainting.Atlas;
            var under = ChunkPainting.DefinitionAt(cursorX);
            _lastChunk = under != null ? under.id + (ChunkPainting.FindLoaded(under) != null ? "" : " (opens on first click)") : "outside every chunk";
            foreach (var room in atlas.rooms)
            {
                if (room == null) continue;
                var r = room.worldRect;
                var colour = room == under ? new Color(0.6f, 0.95f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.25f);
                Handles.color = colour;
                Handles.DrawLine(new Vector3(r.xMin, r.yMin - 20f, 0f), new Vector3(r.xMin, r.yMax + 20f, 0f));
                Handles.DrawLine(new Vector3(r.xMax, r.yMin - 20f, 0f), new Vector3(r.xMax, r.yMax + 20f, 0f));
                Handles.Label(new Vector3(r.xMin + 0.3f, r.yMax + 1f, 0f), room.id, room == under ? HighlightLabel : EditorStyles.whiteMiniLabel);
            }
        }

        private static GUIStyle _highlightLabel;
        private static GUIStyle HighlightLabel => _highlightLabel ??= new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = new Color(0.6f, 0.95f, 1f) },
        };

        /// <summary>
        /// The band group a piece painted at this X belongs in. With a world, that
        /// is inside the chunk scene covering X (parenting moves the piece into that
        /// scene), so the map is split for streaming as it is drawn.
        /// </summary>
        /// <summary>
        /// The folder each band lives in, the same layout every chunk already has
        /// (Level/Art for walkable ground, Scenery/1 Far Fog ... Scenery/6 Props),
        /// so a painted piece lands beside the pieces of its own kind instead of in
        /// a second folder of its own.
        /// </summary>
        public static string FolderFor(SceneryRole forRole) => forRole switch
        {
            SceneryRole.FarFog => "Scenery/1 Far Fog",
            SceneryRole.DarkThings => "Scenery/2 Dark Things",
            SceneryRole.EarthFill => "Scenery/3 Earth Fill",
            SceneryRole.ForegroundSilhouette => "Scenery/4 Foreground Silhouette",
            SceneryRole.Vines => "Scenery/5 Vines",
            SceneryRole.PlayerPlane => "Scenery/6 Props",
            SceneryRole.GroundMoss => "Level/Art",
            _ => "Scenery/Other",
        };

        private Transform RoleGroupAt(float x)
        {
            var path = FolderFor(role).Split('/');
            if (paintIntoChunks && HasWorld)
            {
                var chunk = ChunkPainting.ChunkAt(x, openIfClosed: true) ?? GrowLastChunkTo(x);
                _lastChunk = chunk != null && chunk.Definition != null ? chunk.Definition.id : "past the end of the line: painting into Draft";
                if (chunk != null)
                {
                    var at = chunk.transform;
                    foreach (var part in path) at = ChunkPainting.Child(at, part);
                    return at;
                }
            }

            // Drafting (chunks off, or painting past the end of the line): the same
            // folders under one Draft root in the active scene, ready to bake into
            // chunks from the World section once the stretch looks right.
            var scene = EditorSceneManager.GetActiveScene();
            Transform top = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == DraftBaker.DraftRoot) top = go.transform;
            if (top == null)
            {
                var root = new GameObject(DraftBaker.DraftRoot);
                Undo.RegisterCreatedObjectUndo(root, "Create draft");
                top = root.transform;
            }
            foreach (var part in path) top = ChunkPainting.Child(top, part);
            return top;
        }

        /// <summary>
        /// Names a piece by where it sat on the sheet.
        ///
        /// Needed because Unity's automatic slice gives every piece on a sheet
        /// the same name, so a hierarchy painted from one sheet is thirty objects
        /// called Mossy and nothing in it can be found again.
        /// </summary>
        private string PieceName(Sprite sprite) =>
            $"{sprite.name}_{chosen + 1:00}";

        // ---- Layer row ------------------------------------------------------

        [System.Serializable]
        private sealed class LayerRow
        {
            private readonly SceneryRole row;
            private readonly int count;
            private readonly MapPainterWindow owner;

            public LayerRow(SceneryRole row, int count, MapPainterWindow owner)
            {
                this.row = row;
                this.count = count;
                this.owner = owner;
            }

            [TableColumnWidth(230)]
            [ShowInInspector, DisplayAsString(EnableRichText = true), HideLabel]
            public string Band => owner.role == row
                ? $"<color=#7ad48c><b>{SceneryRoleData.Label(row)}</b></color>"
                : SceneryRoleData.Label(row);

            [TableColumnWidth(58, Resizable = false)]
            [ShowInInspector, DisplayAsString, HideLabel]
            public string Count => $"{count}";

            [TableColumnWidth(70, Resizable = false)]
            [Button("Paint"), GUIColor("PaintColour")]
            public void PaintHere()
            {
                owner.role = row;
                owner.OnRoleChanged();
                owner.painting = true;
                SceneView.RepaintAll();
            }

            [TableColumnWidth(70, Resizable = false)]
            [Button("$IsolateLabel"), GUIColor("IsolateColour")]
            public void ToggleIsolate()
            {
                if (owner.isolating && owner.isolatedRole == row) owner.ShowAll();
                else owner.Isolate(row);
            }

            private string IsolateLabel => owner.isolating && owner.isolatedRole == row ? "Unsolo" : "Solo";

            private Color PaintColour() => owner.role == row
                ? new Color(0.35f, 0.85f, 0.45f)
                : new Color(0.72f, 0.72f, 0.74f);

            private Color IsolateColour() => owner.isolating && owner.isolatedRole == row
                ? new Color(0.95f, 0.8f, 0.35f)
                : new Color(0.72f, 0.72f, 0.74f);
        }

        // ---- Chunk row ------------------------------------------------------

        private sealed class ChunkRow
        {
            private readonly RoomDefinition def;
            private readonly int count;
            private readonly MapPainterWindow owner;

            public ChunkRow(RoomDefinition def, int count, MapPainterWindow owner)
            {
                this.def = def;
                this.count = count;
                this.owner = owner;
            }

            private bool IsSolo => owner.soloChunk == def.id;

            [ShowInInspector, DisplayAsString(EnableRichText = true), HideLabel]
            public string Chunk => IsSolo ? $"<color=#e8c860><b>{def.id}</b></color>" : def.id;

            [TableColumnWidth(110, Resizable = false)]
            [ShowInInspector, DisplayAsString, HideLabel]
            public string Span => $"x {def.worldRect.xMin:0} to {def.worldRect.xMax:0}";

            [TableColumnWidth(70, Resizable = false)]
            [ShowInInspector, DisplayAsString, HideLabel]
            public string Pieces => count < 0 ? "closed" : $"{count} pcs";

            [TableColumnWidth(60, Resizable = false)]
            [Button("Edit")]
            public void Edit() => owner.EditChunk(def);

            [TableColumnWidth(70, Resizable = false)]
            [Button("$SoloLabel"), GUIColor("SoloColour")]
            public void ToggleSolo()
            {
                if (IsSolo) owner.ShowEveryChunk();
                else owner.SoloChunk(def);
            }

            private string SoloLabel => IsSolo ? "Unsolo" : "Solo";

            [TableColumnWidth(70, Resizable = false)]
            [Button("Delete"), GUIColor(1f, 0.55f, 0.5f)]
            public void Delete()
            {
                if (!EditorUtility.DisplayDialog("Delete chunk",
                        $"Delete {def.id} and everything painted in it? Its scene file and room are removed. Deleting the last chunk frees its number for the next one.",
                        "Delete", "Keep")) return;
                if (owner.soloChunk == def.id) owner.ShowEveryChunk();
                WorldMapWindow.DeleteChunk(def);
                owner.RefreshChunkRows();
            }

            private Color SoloColour() => IsSolo
                ? new Color(0.95f, 0.8f, 0.35f)
                : new Color(0.72f, 0.72f, 0.74f);
        }

        // ---- Swatch ---------------------------------------------------------

        [System.Serializable]
        private sealed class Swatch
        {
            [HideInInspector] public Sprite sprite;

            // Set when this swatch is a whole animated plant (one folder of
            // frames) rather than one still drawing.
            [HideInInspector] public string animFolder;

            private readonly int index;
            private readonly MapPainterWindow owner;

            public Swatch(int index, Sprite sprite, MapPainterWindow owner, string animFolder = null)
            {
                this.index = index;
                this.sprite = sprite;
                this.owner = owner;
                this.animFolder = animFolder;
            }

            public Vector2 Metres => sprite == null
                ? Vector2.zero
                : sprite.rect.size / sprite.pixelsPerUnit;

            [TableColumnWidth(70, Resizable = false)]
            [ShowInInspector, PreviewField(58, ObjectFieldAlignment.Center), HideLabel]
            public Sprite Preview => sprite;

            [TableColumnWidth(60, Resizable = false)]
            [ShowInInspector, DisplayAsString, HideLabel]
            public string Number => $"{index + 1}";

            [ShowInInspector, DisplayAsString, HideLabel]
            public string Size => (animFolder != null ? "animated  " : "") + $"{Metres.x:0.0} x {Metres.y:0.0} m";

            [TableColumnWidth(110, Resizable = false)]
            [Button("Use this"), GUIColor("Colour")]
            public void Choose()
            {
                owner.chosen = index;
                owner.painting = true;
                SceneView.RepaintAll();
            }

            private Color Colour() => owner.chosen == index
                ? new Color(0.35f, 0.85f, 0.45f)
                : new Color(0.7f, 0.7f, 0.72f);
        }
    }
}
