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

        [TitleGroup("Presets", "Stop re-dragging the same sheet in every session", TitleAlignments.Centered)]
        [InfoBox("A preset remembers the palette source and every setting below " +
                 "it: role, scale, collider, sorting, flip. Saved presets stay " +
                 "in this window's own state, the same way the last sheet you " +
                 "used already does, so they survive closing Unity.")]
        [ShowInInspector, HideLabel]
        [TableList(IsReadOnly = false, AlwaysExpanded = true, HideToolbar = true)]
        private List<Preset> presets = new();

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

        // ---- Placing --------------------------------------------------------

        [TitleGroup("Placing", "How a piece lands", TitleAlignments.Centered)]
        [InfoBox("$PaintVerdict", InfoMessageType.None)]
        [ToggleLeft]
        [SerializeField] private bool painting;

        [TitleGroup("Placing")]
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
        [Tooltip("Everything painted goes under this object plus the band name, " +
                 "so a level can be hidden or moved one band at a time.")]
        [SerializeField] private string groupName = "Map";

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
            ? (dragPaints
                ? $"Painting. Hold and drag to lay down a run, arrow keys change " +
                  $"piece, ctrl-click erases, escape stops. {placed} placed so far."
                : $"Painting. Click in the scene to place, arrow keys change piece, " +
                  $"ctrl-click erases, escape stops. {placed} placed so far.")
            : "Tick this, then click in the scene view to place pieces.";

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
            if ((sheet != null || folder != null) && swatches.Count == 0) LoadPalette();

            // Two states to recover from after a domain reload. Still soloing:
            // OnDisable undimmed everything on the way out, so put it back.
            // Not soloing but holding records: something was interrupted, and
            // those records are the only way home for whatever is still dim.
            if (isolating) Isolate(isolatedRole);
            else if (dimmed.Count > 0) RestoreColours();

            RefreshLayerRows();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnScene;

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
            if (!canPlace && Event.current.type != EventType.MouseDown
                           && Event.current.type != EventType.MouseDrag
                           && Event.current.type != EventType.KeyDown) return;

            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);

            // Takes the default handle away while painting, otherwise a click
            // meant to place a piece selects whatever is already under it.
            HandleUtility.AddDefaultControl(id);

            var where = PointerInScene(e.mousePosition);

            if (canPlace) DrawGhost(where);

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
                if (e.control) Erase(where);
                else if (canPlace) Place(where);
                lastPlacePosition = where;
                e.Use();
            }

            // Drag painting fires from MouseDrag rather than every MouseMove, so
            // a piece is only considered once the mouse has actually moved with
            // the button held, not on every frame the cursor sits still.
            if (dragPaints && e.type == EventType.MouseDrag && e.button == 0 && !e.alt)
            {
                if (e.control)
                {
                    Erase(where);
                }
                else if (canPlace && FarEnoughFromLastPlacement(where))
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

            if (alternateFlip && flipNext) renderer.flipX = true;
            flipNext = !flipNext;

            go.transform.localScale = Vector3.one * pieceScale;

            // Sat on the click rather than centred on it. A piece of ground is
            // placed by where its surface goes, and centring means every click
            // has to be aimed half a platform too high.
            var size = swatch.Metres * pieceScale;
            go.transform.position = new Vector3(at.x, at.y + size.y * 0.5f, numbers.Z);
            go.transform.SetParent(RoleGroup(), true);

            if (solid) AddCollider(go, renderer, swatch.Metres);

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
        /// Removes the nearest piece this window painted, within one grid cell
        /// of the cursor. Searches every "<Group> (Role)" band, not only the
        /// one Paint Into currently points at.
        ///
        /// Used to only look inside the live band's own group, on the reasoning
        /// that erasing a ground piece should never reach into the background
        /// group sitting behind it on screen. In practice that meant a piece
        /// painted under a different Role than the one currently selected could
        /// not be erased at all, silently: ctrl-click did nothing and looked
        /// like a bug. Depth is still respected, just picked automatically
        /// instead of demanded up front: the nearest piece across every band
        /// wins, and two pieces stacked at the same spot are exactly the case
        /// where a screen click cannot tell which one was meant anyway.
        /// </summary>
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
            "Encounter Director",
        };

        private void Erase(Vector3 at)
        {
            // Earlier versions of this looked only inside a "Map (Role)"
            // group, on the assumption that everything the eraser needs to
            // reach was placed by this same tool this same session. That
            // stopped being true once the scene's scenery lived under its own
            // hand built "Scenery" root instead: the eraser was searching a
            // group that had nothing in it while real pieces sat untouched a
            // few objects over, and ctrl-click looked broken with no error to
            // explain why. There is no reliable "this tool placed it" marker
            // to search by, so the honest fix is to look at every
            // SpriteRenderer in the scene and let distance from the cursor
            // decide, the same way a person pointing at the screen would.
            Transform nearest = null;
            var nearestDistance = float.MaxValue;
            var reach = Mathf.Max(grid, 0.1f) + 0.5f;
            var cursor = new Vector2(at.x, at.y);

            var renderers = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(
                FindObjectsSortMode.None);

            foreach (var renderer in renderers)
            {
                var child = renderer.transform;
                if (IsExcluded(child)) continue;

                var flat = new Vector2(child.position.x, child.position.y);
                var distance = Vector2.Distance(flat, cursor);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = child;
                }
            }

            if (nearest == null || nearestDistance > reach) return;

            Undo.DestroyObjectImmediate(nearest.gameObject);
            placed = Mathf.Max(0, placed - 1);
            Repaint();
        }

        private static bool IsExcluded(Transform t)
        {
            var root = t.root.name;
            foreach (var excluded in EraseExcludedRoots)
                if (root == excluded) return true;
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
            var box = go.AddComponent<BoxCollider>();
            var depth = Mathf.Clamp01(colliderDepth);

            var height = Mathf.Max(0.05f, localSize.y * depth);
            var top = renderer.sprite.bounds.max.y;

            box.size = new Vector3(localSize.x, height, 1f);
            box.center = new Vector3(0f, top - height * 0.5f, 0f);
        }

        private string GroupNameFor(SceneryRole forRole) => $"{groupName} ({forRole})";

        private Transform RoleGroup()
        {
            var name = GroupNameFor(role);
            var existing = GameObject.Find(name);
            if (existing != null) return existing.transform;

            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Create map group");
            return root.transform;
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

        // ---- Swatch ---------------------------------------------------------

        [System.Serializable]
        private sealed class Swatch
        {
            [HideInInspector] public Sprite sprite;

            private readonly int index;
            private readonly MapPainterWindow owner;

            public Swatch(int index, Sprite sprite, MapPainterWindow owner)
            {
                this.index = index;
                this.sprite = sprite;
                this.owner = owner;
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
            public string Size => $"{Metres.x:0.0} x {Metres.y:0.0} m";

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
