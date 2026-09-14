using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Slices every sheet in a folder, with the cell size and the pivot read off
    /// the pixels instead of typed in.
    ///
    /// Slicing by hand is not slow because there are a lot of clicks. It is slow
    /// because two numbers per sheet have to be guessed, and a wrong guess does
    /// not look like a wrong guess. A cell width that is off produces an
    /// animation that slides; a pivot that is off produces a character that
    /// hops half a metre when it changes animation. Both of those get diagnosed
    /// as movement bugs, and the movement code gets rewritten before anyone
    /// opens the sheet again.
    ///
    /// So the tool measures both, shows what it found before touching anything,
    /// and lets any row be corrected by hand.
    /// </summary>
    public sealed class SpriteSlicerWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Sprite Slicer", priority = 2)]
        private static void Open()
        {
            var window = GetWindow<SpriteSlicerWindow>();
            window.titleContent = new GUIContent("Sprite Slicer");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        // ---- Header ---------------------------------------------------------

        [PropertyOrder(-100)]
        [Title("Sprite Slicer", "Cuts every sheet in a folder, measuring the cells instead of guessing",
            TitleAlignments.Left)]
        [TitleGroup("What kind of sheet")]
        [EnumToggleButtons, HideLabel]
        [InfoBox("Frames is for a character sheet, where the poses sit on an even " +
                 "grid and the gaps between them give the cell size away. Grid is " +
                 "for a tileset that touches edge to edge, where there are no gaps " +
                 "to find and the size has to be typed. Automatic is Unity's own " +
                 "detection, the same one behind Slice in the Sprite Editor: it " +
                 "finds each separate piece whatever shape and size it is, which " +
                 "is what a hand drawn pack actually looks like.")]
        [OnValueChanged(nameof(Scan))]
        public SliceMode mode = SliceMode.Automatic;

        [TitleGroup("What kind of sheet")]
        [ShowIf("@mode == SliceMode.Automatic")]
        [LabelText("Ignore pieces under (px)")]
        [MinValue(1)]
        [Tooltip("Anything smaller than this is treated as a stray speck rather " +
                 "than a piece. Raise it when the slice finds hundreds of specks " +
                 "scattered around the real art.")]
        [OnValueChanged(nameof(Scan))]
        public int autoMinSize = 8;

        [TitleGroup("What kind of sheet")]
        [ShowIf("@mode == SliceMode.Automatic")]
        [LabelText("Grow pieces by (px)")]
        [PropertyRange(0, 8)]
        [Tooltip("Pads each piece outwards. A pixel or two stops a soft edge being " +
                 "clipped where the detection decided the art ended.")]
        [OnValueChanged(nameof(Scan))]
        public int autoExtrude = 0;

        [TitleGroup("What kind of sheet")]
        [ShowIf("@mode == SliceMode.Grid")]
        [LabelText("Tile size (px)")]
        [MinValue(2)]
        [InfoBox("One square tile, in pixels. Sixteen and thirty two cover most " +
                 "packs. If the slice comes out half a tile off, this number is " +
                 "wrong and nothing else is.")]
        [OnValueChanged(nameof(Scan))]
        public int tileSize = 16;

        [TitleGroup("What kind of sheet")]
        [ShowIf("@mode == SliceMode.Grid")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Which means")]
        private string GridReadout
        {
            get
            {
                if (sheets == null || sheets.Count == 0) return "scan a folder to see";

                var first = sheets[0];
                return $"{first.name} cuts into {first.columns} x {first.rows} " +
                       $"= {first.columns * first.rows} tiles";
            }
        }

        [InfoBox("Point a folder at it, press Scan, look at what it measured, then " +
                 "slice.\n\n" +
                 "Slicing by hand is not slow because of the clicks. It is slow " +
                 "because two numbers per sheet have to be guessed, and a wrong " +
                 "guess does not look like a wrong guess: a cell width that is " +
                 "off by a pixel makes an animation that slides, and a pivot " +
                 "that is off makes a character that hops half a metre whenever " +
                 "it changes animation. Both get diagnosed as movement bugs.")]
        [ShowInInspector, DisplayAsString(false), HideLabel]
        [PropertySpace(0f, 8f)]
        private string Blurb => "";

        [PropertyOrder(-99)]
        [ShowInInspector, DisplayAsString, HideLabel]
        [GUIColor(1f, 0.85f, 0.55f)]
        [ShowIf("@this.filterMode == UnityEngine.FilterMode.Point")]
        [PropertySpace(0f, 6f)]
        private string PixelArtWarning =>
            "Filter Mode is Point, which is a pixel art setting. Correct for these " +
            "placeholder sheets, wrong for the hand drawn art this game ships with. " +
            "Switch to Bilinear when the real drawings arrive.";

        // ---- Source ---------------------------------------------------------

        [BoxGroup("Source", centerLabel: true)]
        [FolderPath(RequireExistingPath = true)]
        [OnValueChanged(nameof(Scan))]
        [Tooltip("Every PNG under here is read. Nothing is changed until you " +
                 "press the slice button.")]
        public string folder = "Assets/Mock_Asset";

        [BoxGroup("Source")]
        [HorizontalGroup("Source/Row")]
        [Button("Scan and measure", ButtonSizes.Gigantic), GUIColor(0.55f, 0.78f, 1f)]
        private void ScanButton() => Scan();

        [HorizontalGroup("Source/Row", 0.25f)]
        [Button("Clear", ButtonSizes.Large)]
        private void Clear() => sheets.Clear();

        // ---- Import settings -------------------------------------------------

        [BoxGroup("Import settings", centerLabel: true)]
        [InfoBox("$PpuHint")]
        [HorizontalGroup("Import settings/A")]
        [LabelWidth(120)]
        [MinValue(1f)]
        [Tooltip("Pixels per world unit. This is what decides how big the " +
                 "character is next to the jump height and run speed in the " +
                 "stance assets, so it is a gameplay number, not an art one.")]
        public float pixelsPerUnit = 27f;

        [HorizontalGroup("Import settings/A")]
        [LabelWidth(120)]
        [Tooltip("Point for pixel art. Bilinear for hand drawn art at a higher " +
                 "resolution than the screen.")]
        public FilterMode filterMode = FilterMode.Point;

        [BoxGroup("Import settings")]
        [HorizontalGroup("Import settings/B")]
        [LabelWidth(120)]
        [ToggleLeft]
        [Tooltip("Off for 2D. Mipmaps blur sprites and cost memory for nothing " +
                 "when the camera never changes distance.")]
        public bool generateMipmaps;

        [HorizontalGroup("Import settings/B")]
        [LabelWidth(120)]
        [ToggleLeft]
        [Tooltip("Off while prototyping. Compression artefacts on a soft alpha " +
                 "edge look exactly like a bad cutout.")]
        public bool compress;

        [BoxGroup("Import settings")]
        [LabelWidth(120)]
        [PropertyRange(0f, 1f)]
        [Tooltip("Horizontal pivot. Half is centred, which is what a character " +
                 "standing on its own feet wants.")]
        public float pivotX = 0.5f;

        private string PpuHint
        {
            get
            {
                if (sheets == null || sheets.Count == 0)
                {
                    return "Scan a folder to see what the numbers do to the art.";
                }

                var tallest = sheets.Max(s => s.cellHeight);
                return $"At {pixelsPerUnit:0.##} pixels per unit, the tallest cell " +
                       $"({tallest} px) is {tallest / pixelsPerUnit:0.00} world units. " +
                       "The stance assets assume a person is about 1.8.";
            }
        }

        // ---- Results ---------------------------------------------------------

        [TableList(AlwaysExpanded = true, IsReadOnly = false, ShowIndexLabels = false,
            DrawScrollView = true, MaxScrollViewHeight = 380)]
        [ShowInInspector, LabelText("Sheets")]
        public List<SheetRow> sheets = new();

        /// <summary>How the cells in a sheet are found.</summary>
        public enum SliceMode
        {
            /// <summary>Measured from the gaps between poses. Character sheets.</summary>
            Frames = 0,

            /// <summary>Cut on a fixed square grid. Tilesets.</summary>
            Grid = 1,

            /// <summary>Unity's own detection. Loose pieces of any shape or size.</summary>
            Automatic = 2,
        }

        [Serializable]
        public sealed class SheetRow
        {
            [TableColumnWidth(58, Resizable = false)]
            [PreviewField(50, ObjectFieldAlignment.Center)]
            [HideLabel, ReadOnly]
            public Texture2D preview;

            [TableColumnWidth(150)]
            [DisplayAsString, HideLabel]
            public string name;

            [TableColumnWidth(78, Resizable = false)]
            [DisplayAsString, HideLabel]
            public string size;

            [TableColumnWidth(58, Resizable = false)]
            [LabelText("Cols")]
            [MinValue(1)]
            public int columns = 1;

            [TableColumnWidth(58, Resizable = false)]
            [LabelText("Rows")]
            [MinValue(1)]
            public int rows = 1;

            [TableColumnWidth(78, Resizable = false)]
            [DisplayAsString, HideLabel]
            public string cell;

            [TableColumnWidth(76, Resizable = false)]
            [LabelText("Pivot Y")]
            [PropertyRange(0f, 1f)]
            public float pivotY = 0f;

            [TableColumnWidth(150)]
            [DisplayAsString(EnableRichText = true), HideLabel]
            public string status;

            [TableColumnWidth(50, Resizable = false)]
            [LabelText("Do it")]
            public bool include = true;

            [HideInInspector] public string path;
            [HideInInspector] public int width;
            [HideInInspector] public int height;
            [HideInInspector] public int cellHeight = 1;
            [HideInInspector] public Rect[] found;
        }

        // ---- Scanning ---------------------------------------------------------

        private void Scan()
        {
            sheets.Clear();
            var root = ResolveFolder();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                Debug.LogWarning($"[Weizza] '{folder}' is not a folder in this project.");
                return;
            }

            // PSD included because a working file straight from the artist
            // should not have to be flattened to PNG before it can be sliced.
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(root, "*.png", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(root, "*.psd", SearchOption.AllDirectories));
            files.Sort();

            foreach (var file in files)
            {
                var assetPath = file.Replace('\\', '/');
                var index = assetPath.IndexOf("Assets/", System.StringComparison.Ordinal);
                if (index >= 0) assetPath = assetPath[index..];

                var readable = SheetAnalysis.LoadReadable(assetPath);
                if (readable == null) continue;

                sheets.Add(mode switch
                {
                    SliceMode.Grid => GridRow(assetPath, readable),
                    SliceMode.Automatic => AutoRow(assetPath, readable),
                    _ => FrameRow(assetPath, readable),
                });

                DestroyImmediate(readable);
            }

            Debug.Log($"[Weizza] Measured {sheets.Count} sheets under {root}.");
        }

        private SheetRow FrameRow(string assetPath, Texture2D readable)
        {
            var layout = SheetAnalysis.Analyse(readable);
            var frames = layout.FrameCount;

            return new SheetRow
            {
                preview = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath),
                path = assetPath,
                name = Path.GetFileNameWithoutExtension(assetPath),
                width = readable.width,
                height = readable.height,
                size = $"{readable.width} x {readable.height}",
                columns = layout.columns,
                rows = layout.rows,
                cell = $"{layout.cellWidth} x {layout.cellHeight}",
                pivotY = layout.pivotY,
                cellHeight = layout.cellHeight,
                status = Describe(layout, frames),
                include = frames > 1,
            };
        }

        /// <summary>
        /// Cuts a sheet on a fixed grid instead of measuring it.
        ///
        /// A tileset has no gaps between its tiles, so there is nothing for the
        /// measuring to find and it reports the whole sheet as one enormous frame.
        /// That is not a failure of the measuring: a dense grid genuinely does
        /// not carry the information, and the only place the tile size exists is
        /// in whatever the artist decided. So it gets typed in.
        /// </summary>
        private SheetRow GridRow(string assetPath, Texture2D readable)
        {
            var size = Mathf.Max(2, tileSize);
            var columns = Mathf.Max(1, readable.width / size);
            var rows = Mathf.Max(1, readable.height / size);

            // A sheet that is not a whole number of tiles across is the loudest
            // sign the size is wrong, and saying so is far more use than slicing
            // it anyway and leaving a sliver on the right hand edge.
            var ragged = readable.width % size != 0 || readable.height % size != 0;

            return new SheetRow
            {
                preview = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath),
                path = assetPath,
                name = Path.GetFileNameWithoutExtension(assetPath),
                width = readable.width,
                height = readable.height,
                size = $"{readable.width} x {readable.height}",
                columns = columns,
                rows = rows,
                cell = $"{size} x {size}",
                pivotY = 0.5f,
                cellHeight = size,
                status = ragged
                    ? $"<color=#E8A33D>{readable.width} x {readable.height} does not divide " +
                      $"by {size}, so the size is probably wrong</color>"
                    : $"<color=#7BE38B>{columns} x {rows} = {columns * rows} tiles</color>",
                include = !ragged,
            };
        }

        /// <summary>
        /// Hands the sheet to Unity and asks where the pieces are.
        ///
        /// This is the same detection that Slice runs in the Sprite Editor, so a
        /// sheet cut here and a sheet cut by hand come out identical. Writing a
        /// second detector would mean two answers to one question, and the one
        /// that disagreed with the Sprite Editor would always be judged the wrong
        /// one no matter which was actually better.
        /// </summary>
        private SheetRow AutoRow(string assetPath, Texture2D readable)
        {
            var found = Automatic(readable, Mathf.Max(1, autoMinSize), autoExtrude);

            // Top to bottom, then left to right, the way the sheet reads. Unity
            // hands them back in whatever order the scan happened to find them,
            // and unordered names turn a re-slice into a reshuffle of every
            // reference pointing at this sheet.
            System.Array.Sort(found, (a, b) =>
            {
                var byRow = b.yMax.CompareTo(a.yMax);
                return byRow != 0 ? byRow : a.xMin.CompareTo(b.xMin);
            });

            return new SheetRow
            {
                preview = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath),
                path = assetPath,
                name = Path.GetFileNameWithoutExtension(assetPath),
                width = readable.width,
                height = readable.height,
                size = $"{readable.width} x {readable.height}",
                columns = found.Length,
                rows = 1,
                cell = found.Length > 0 ? $"{found[0].width:0} x {found[0].height:0}" : "none",
                pivotY = 0.5f,
                cellHeight = found.Length > 0 ? Mathf.RoundToInt(found[0].height) : 1,
                found = found,
                status = found.Length == 0
                    ? "<color=#8A8F98>nothing found, the sheet may be empty</color>"
                    : $"<color=#7BE38B>{found.Length} piece(s) found</color>",
                include = found.Length > 0,
            };
        }

        /// <summary>
        /// Calls Unity's automatic sprite detection.
        ///
        /// Reached by reflection because the class is internal to the editor. It
        /// has sat in the same place for years and most slicing tools lean on it,
        /// so the risk is small, and the alternative is reimplementing connected
        /// component detection and then explaining why it disagrees with the
        /// editor sitting next to it.
        /// </summary>
        private static Rect[] Automatic(Texture2D texture, int minSize, int extrude)
        {
            var type = System.Type.GetType("UnityEditorInternal.InternalSpriteUtility, UnityEditor");
            var method = type?.GetMethod("GenerateAutomaticSpriteRectangles",
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static);

            if (method == null)
            {
                Debug.LogWarning("[Weizza] Unity's automatic slicing is not reachable in this " +
                                 "version, so use Grid or Frames instead.");
                return System.Array.Empty<Rect>();
            }

            return method.Invoke(null, new object[] { texture, minSize, extrude }) as Rect[]
                   ?? System.Array.Empty<Rect>();
        }

        private static string Describe(SheetLayout layout, int frames)
        {
            if (frames <= 1)
            {
                return "<color=#8A8F98>one frame, skipped</color>";
            }

            var ground = $"feet {layout.groundPixel} px up";
            return layout.rows > 1
                ? $"<color=#E0B54B>{frames} frames, {layout.rows} rows</color>"
                : $"<color=#7FC98A>{frames} frames, {ground}</color>";
        }

        private string ResolveFolder()
        {
            if (string.IsNullOrEmpty(folder)) return null;
            var path = folder.Replace('\\', '/');
            if (path.StartsWith("Assets", System.StringComparison.Ordinal)) return path;
            var index = path.IndexOf("Assets/", System.StringComparison.Ordinal);
            return index >= 0 ? path[index..] : path;
        }

        // ---- Slicing -----------------------------------------------------------

        [PropertySpace(10f, 2f)]
        [ShowInInspector, DisplayAsString, HideLabel]
        [GUIColor(nameof(TallyColour))]
        private string Tally
        {
            get
            {
                if (sheets == null || sheets.Count == 0) return "Nothing scanned yet";

                var ticked = 0;
                var unsure = 0;
                foreach (var sheet in sheets)
                {
                    if (sheet == null || !sheet.include) continue;
                    ticked++;

                    // One cell means the measurement found no repeating frame,
                    // which is either a genuinely single image or a sheet it
                    // failed to read. Worth a look either way before slicing.
                    if (sheet.columns * sheet.rows <= 1) unsure++;
                }

                return unsure > 0
                    ? $"{ticked} sheets ticked, {unsure} measured as a single frame. Check those first."
                    : $"{ticked} of {sheets.Count} sheets ticked and measured";
            }
        }

        private Color TallyColour()
        {
            if (sheets == null || sheets.Count == 0) return new Color(0.72f, 0.75f, 0.8f);

            foreach (var sheet in sheets)
            {
                if (sheet == null || !sheet.include) continue;
                if (sheet.columns * sheet.rows <= 1) return new Color(1f, 0.8f, 0.5f);
            }

            return new Color(0.72f, 0.93f, 0.78f);
        }

        [Button("Slice everything ticked", ButtonSizes.Gigantic), GUIColor(0.45f, 0.82f, 0.55f)]
        private void SliceAll()
        {
            var todo = sheets.Where(s => s.include && s.columns * s.rows > 0).ToList();
            if (todo.Count == 0)
            {
                Debug.LogWarning("[Weizza] Nothing ticked.");
                return;
            }

            // Deliberately not wrapped in StartAssetEditing. Slicing needs the
            // importer to be reimported into Multiple mode before the data
            // provider will hand out per frame rects, and batching defers that
            // reimport to the end, which leaves every sheet with one frame.
            try
            {
                for (var i = 0; i < todo.Count; i++)
                {
                    var row = todo[i];
                    EditorUtility.DisplayProgressBar(
                        "Slicing", row.name, (float)i / todo.Count);
                    Slice(row);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Weizza] Sliced {todo.Count} sheets.");
        }

        private void Slice(SheetRow row)
        {
            if (AssetImporter.GetAtPath(row.path) is not TextureImporter importer) return;

            // One cell is not an animation, it is a picture: a backdrop, a
            // parallax layer, an icon. Leaving those in Multiple mode with a
            // single frame inside works but reads as a mistake to anyone who
            // opens the importer later, so they go in as a plain sprite.
            var single = row.columns * row.rows <= 1;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = single ? SpriteImportMode.Single : SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = filterMode;
            importer.mipmapEnabled = generateMipmaps;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = compress
                ? TextureImporterCompression.Compressed
                : TextureImporterCompression.Uncompressed;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            // Full Rect rather than Tight. A tight mesh trims the transparent
            // border, which moves the drawing relative to the pivot that was
            // just measured off that border.
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = single
                ? (int)SpriteAlignment.Center
                : (int)SpriteAlignment.Custom;
            if (!single) settings.spritePivot = new Vector2(pivotX, row.pivotY);
            importer.SetTextureSettings(settings);

            // The data provider only offers per frame rects once the importer
            // is already in Multiple mode, so this reimport has to happen first.
            importer.SaveAndReimport();

            if (single) return;

            var factories = new SpriteDataProviderFactories();
            factories.Init();

            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) return;
            provider.InitSpriteEditorDataProvider();

            // Reusing the id of a frame with the same name keeps anything that
            // already points at that sprite pointing at it after a re-slice.
            var known = new Dictionary<string, GUID>();
            foreach (var existing in provider.GetSpriteRects())
            {
                known[existing.name] = existing.spriteID;
            }

            var pivot = new Vector2(pivotX, row.pivotY);
            var rects = new List<SpriteRect>(row.columns * row.rows);
            var index = 0;

            if (row.found is { Length: > 0 })
            {
                foreach (var piece in row.found)
                {
                    var found = $"{row.name}_{index}";
                    rects.Add(new SpriteRect
                    {
                        name = found,
                        rect = piece,

                        // Centre, not the measured pivot. A pivot taken off the
                        // bottom of a cell means nothing when every piece is a
                        // different height, which is the whole point of this mode.
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                        spriteID = known.TryGetValue(found, out var foundId) ? foundId : GUID.Generate(),
                    });
                    index++;
                }
            }
            else
            {
                var cellWidth = row.width / Mathf.Max(1, row.columns);
                var cellHeight = row.height / Mathf.Max(1, row.rows);

                for (var r = 0; r < row.rows; r++)
                {
                    // Row zero is the top row, the way a sheet reads. Texture
                    // space counts up from the bottom, hence the flip.
                    var y = row.height - (r + 1) * cellHeight;

                    for (var c = 0; c < row.columns; c++)
                    {
                        var name = $"{row.name}_{index}";
                        rects.Add(new SpriteRect
                        {
                            name = name,
                            rect = new Rect(c * cellWidth, y, cellWidth, cellHeight),
                            alignment = SpriteAlignment.Custom,
                            pivot = pivot,
                            border = Vector4.zero,
                            spriteID = known.TryGetValue(name, out var id) ? id : GUID.Generate(),
                        });
                        index++;
                    }
                }
            }

            provider.SetSpriteRects(rects.ToArray());

            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (names != null)
            {
                var pairs = new List<SpriteNameFileIdPair>(rects.Count);
                foreach (var rect in rects)
                {
                    var pair = new SpriteNameFileIdPair { name = rect.name };
                    pair.SetFileGUID(rect.spriteID);
                    pairs.Add(pair);
                }
                names.SetNameFileIdPairs(pairs);
            }

            provider.Apply();
            importer.SaveAndReimport();
        }
    }
}
