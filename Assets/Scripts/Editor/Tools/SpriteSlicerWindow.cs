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

        // ---- Source ---------------------------------------------------------

        [BoxGroup("Source", centerLabel: true)]
        [FolderPath(RequireExistingPath = true)]
        [OnValueChanged(nameof(Scan))]
        [Tooltip("Every PNG under here is read. Nothing is changed until you " +
                 "press the slice button.")]
        public string folder = "Assets/Mock_Asset";

        [BoxGroup("Source")]
        [HorizontalGroup("Source/Row")]
        [Button("Scan and measure", ButtonSizes.Large), GUIColor(0.55f, 0.78f, 1f)]
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

            var files = Directory.GetFiles(root, "*.png", SearchOption.AllDirectories);
            System.Array.Sort(files);

            foreach (var file in files)
            {
                var assetPath = file.Replace('\\', '/');
                var index = assetPath.IndexOf("Assets/", System.StringComparison.Ordinal);
                if (index >= 0) assetPath = assetPath[index..];

                var readable = SheetAnalysis.LoadReadable(assetPath);
                if (readable == null) continue;

                var layout = SheetAnalysis.Analyse(readable);
                var frames = layout.FrameCount;

                sheets.Add(new SheetRow
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
                });

                DestroyImmediate(readable);
            }

            Debug.Log($"[Weizza] Measured {sheets.Count} sheets under {root}.");
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

        [PropertySpace(10)]
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

            var cellWidth = row.width / Mathf.Max(1, row.columns);
            var cellHeight = row.height / Mathf.Max(1, row.rows);
            var pivot = new Vector2(pivotX, row.pivotY);

            var rects = new List<SpriteRect>(row.columns * row.rows);
            var index = 0;

            for (var r = 0; r < row.rows; r++)
            {
                // Row zero is the top row, the way a sheet reads. Texture space
                // counts up from the bottom, hence the flip.
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
