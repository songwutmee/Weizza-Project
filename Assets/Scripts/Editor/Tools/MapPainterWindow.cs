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
    /// Places finished pieces of scenery by clicking in the scene.
    ///
    /// Replaces the spline terrain approach for one reason: a pack of hand
    /// drawn art is a set of finished platforms, not a set of bands that tile.
    /// Every free pack on hand is drawn that way, and forcing one into strips
    /// costs a repaint of the whole set. Placing the pieces as drawn costs
    /// nothing and cannot come apart at a seam, because there is no seam.
    ///
    /// What it does that dragging a prefab by hand does not: snaps to a grid so
    /// neighbours line up, fits a box collider to the drawn pixels rather than
    /// to the sprite rectangle, keeps every piece on one sorting layer so the
    /// player is never buried, and lets a whole sheet be cycled through with
    /// the arrow keys so choosing a piece does not mean going back to Project.
    /// </summary>
    public sealed class MapPainterWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Map Painter %#m", priority = 3)]
        private static void Open()
        {
            var window = GetWindow<MapPainterWindow>();
            window.titleContent = new GUIContent("Map Painter");
            window.minSize = new Vector2(420f, 620f);
            window.Show();
        }

        // ---- Palette --------------------------------------------------------

        [TitleGroup("Palette", "The sheet you are painting from", TitleAlignments.Centered)]
        [InfoBox("Drop a sliced sheet here. Every piece on it becomes a swatch " +
                 "below. Nothing is tiled or stretched, so a piece lands in the " +
                 "scene exactly as it was drawn.")]
        [OnValueChanged(nameof(LoadSheet))]
        [SerializeField] private Texture2D sheet;

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

            LoadSheet();
        }

        [TitleGroup("Palette")]
        [ShowInInspector, HideLabel]
        [PropertyOrder(10)]
        [TableList(IsReadOnly = true, AlwaysExpanded = true, ShowPaging = true, NumberOfItemsPerPage = 10)]
        private List<Swatch> swatches = new();

        // ---- Placing --------------------------------------------------------

        [TitleGroup("Placing", "How a piece lands", TitleAlignments.Centered)]
        [InfoBox("$PaintVerdict", InfoMessageType.None)]
        [ToggleLeft]
        [SerializeField] private bool painting;

        [TitleGroup("Placing")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Rounds where a piece lands. Zero drops it exactly under the " +
                 "cursor, which is right for scattered props and wrong for a " +
                 "run of platforms meant to read as one ledge.")]
        [SerializeField] private float grid = 0.5f;

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
                 "on scenery hanging below the ledge.")]
        [SerializeField] private float colliderDepth = 0.35f;

        [TitleGroup("Placing")]
        [PropertyRange(-50, 50)]
        [Tooltip("Characters sit at zero. Below zero is behind them, above is " +
                 "in front.")]
        [SerializeField] private int sortingOrder = -10;

        [TitleGroup("Placing")]
        [ToggleLeft]
        [Tooltip("Mirrors every other piece as it is placed, so a long run does " +
                 "not read as the same shape stamped over and over.")]
        [SerializeField] private bool alternateFlip = true;

        [TitleGroup("Placing")]
        [Tooltip("Everything painted goes under this object, so a level can be " +
                 "hidden or moved in one go.")]
        [SerializeField] private string groupName = "Map";

        // ---- State ----------------------------------------------------------

        private int chosen;
        private int placed;
        private bool flipNext;

        private const float GamePixelsPerUnit = 150f;

        // ---- Readouts -------------------------------------------------------

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
            ? $"Painting. Click in the scene to place, arrow keys change piece, " +
              $"escape stops. {placed} placed so far."
            : "Tick this, then click in the scene view to place pieces.";

        // ---- Palette loading ------------------------------------------------

        private void LoadSheet()
        {
            swatches.Clear();
            chosen = 0;

            if (sheet == null) return;

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

            for (var i = 0; i < found.Count; i++)
            {
                swatches.Add(new Swatch(i, found[i], this));
            }
        }

        // ---- Scene interaction ----------------------------------------------

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnScene;
            if (sheet != null && swatches.Count == 0) LoadSheet();
        }

        private void OnDisable() => SceneView.duringSceneGui -= OnScene;

        private void OnScene(SceneView view)
        {
            if (!painting || swatches.Count == 0) return;

            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);

            // Takes the default handle away while painting, otherwise a click
            // meant to place a piece selects whatever is already under it.
            HandleUtility.AddDefaultControl(id);

            var where = PointerInScene(e.mousePosition);

            DrawGhost(where);

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    painting = false;
                    Repaint();
                    e.Use();
                }
                else if (e.keyCode is KeyCode.RightArrow or KeyCode.UpArrow)
                {
                    Step(1);
                    e.Use();
                }
                else if (e.keyCode is KeyCode.LeftArrow or KeyCode.DownArrow)
                {
                    Step(-1);
                    e.Use();
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Place(where);
                e.Use();
            }

            if (e.type == EventType.MouseMove) view.Repaint();
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
        /// and the sorting order stops meaning anything.
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
            var size = swatch.Metres;

            Handles.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Handles.DrawWireCube(at + new Vector3(0f, size.y * 0.5f, 0f), size);

            Handles.BeginGUI();
            var screen = HandleUtility.WorldToGUIPoint(at);
            var label = $"{chosen + 1} of {swatches.Count}   {size.x:0.0} x {size.y:0.0} m";
            GUI.Label(new Rect(screen.x + 12f, screen.y - 28f, 260f, 20f), label, Badge());
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
            var swatch = swatches[chosen];
            var sprite = swatch.sprite;

            var go = new GameObject(PieceName(sprite));
            Undo.RegisterCreatedObjectUndo(go, "Paint piece");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;

            if (alternateFlip && flipNext) renderer.flipX = true;
            flipNext = !flipNext;

            // Sat on the click rather than centred on it. A piece of ground is
            // placed by where its surface goes, and centring means every click
            // has to be aimed half a platform too high.
            var size = swatch.Metres;
            go.transform.position = at + new Vector3(0f, size.y * 0.5f, 0f);
            go.transform.SetParent(Group(), true);

            if (solid) AddCollider(go, renderer, size);

            Selection.activeGameObject = go;
            placed++;

            EditorSceneManager.MarkSceneDirty(go.scene);
            Repaint();
        }

        /// <summary>
        /// Boxes the top slice of the piece.
        ///
        /// Three dimensional on purpose. The player moves on a CharacterController,
        /// which is PhysX, and PhysX cannot see a 2D collider at all. A level built
        /// with 2D colliders looks finished in the scene view and drops the player
        /// through the floor on the first frame of play.
        /// </summary>
        private void AddCollider(GameObject go, SpriteRenderer renderer, Vector2 size)
        {
            var box = go.AddComponent<BoxCollider>();
            var depth = Mathf.Clamp01(colliderDepth);

            var height = Mathf.Max(0.05f, size.y * depth);
            var top = renderer.sprite.bounds.max.y;

            box.size = new Vector3(size.x, height, 1f);
            box.center = new Vector3(0f, top - height * 0.5f, 0f);
        }

        private Transform Group()
        {
            var existing = GameObject.Find(groupName);
            if (existing != null) return existing.transform;

            var root = new GameObject(groupName);
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
