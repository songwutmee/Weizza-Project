using System.IO;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Weizza.Level;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Cuts tiling terrain strips out of art that was never drawn as strips.
    ///
    /// Hand drawn packs are full of finished platforms: a slab of ground with
    /// moss over the top and the moss wrapped neatly round both ends. Repeating
    /// one of those shows the rounded end in the middle of a run, which is why
    /// they cannot be used as they are.
    ///
    /// The middle of such a piece, though, is exactly the band a spline wants.
    /// This takes a window out of the middle, makes it meet itself, and keeps the
    /// two ends aside as caps. One good platform becomes a full set.
    /// </summary>
    public sealed class TerrainStripCutterWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Terrain Strip Cutter", priority = 9)]
        private static void Open()
        {
            var window = GetWindow<TerrainStripCutterWindow>();
            window.titleContent = new GUIContent("Strip Cutter");
            window.minSize = new Vector2(500f, 620f);
            window.Show();
        }

        // ---- Source ---------------------------------------------------------

        [TitleGroup("Source", "A finished platform to cut up", TitleAlignments.Centered)]
        [InfoBox("Point this at one of the long pieces, the sort with moss over " +
                 "the top and a rounded end at each side. The wider it is the more " +
                 "different the cut strips can be from each other.")]
        [Required]
        [PreviewField(72f, ObjectFieldAlignment.Left)]
        [OnValueChanged(nameof(Measure))]
        [SerializeField] private Sprite source;

        [TitleGroup("Source")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Which is")]
        private string Measured => source == null
            ? "nothing chosen"
            : $"{source.textureRect.width:0} x {source.textureRect.height:0} px in {source.texture.name}";

        // ---- Cut ------------------------------------------------------------

        [TitleGroup("Cut")]
        [PropertyRange(64, 512)]
        [Tooltip("Width of one strip in pixels. Smaller repeats sooner, wider " +
                 "needs more source to cut from.")]
        [SerializeField] private int stripWidth = 256;

        [TitleGroup("Cut")]
        [PropertyRange(0.15f, 1f)]
        [Tooltip("How much of the piece, measured down from its top, counts as " +
                 "the band. The rest is the dark middle that the fill covers " +
                 "anyway.")]
        [SerializeField] private float bandHeight = 0.45f;

        [TitleGroup("Cut")]
        [PropertyRange(0.15f, 1.5f)]
        [LabelText("Band height (m)")]
        [InfoBox("How tall the band of moss stands in the game world. The player " +
                 "is 1.8 m, so anything past about half a metre starts reading as " +
                 "a hedge rather than as ground. This is the number that decides " +
                 "whether it looks right, and pixels cannot answer it.")]
        [SerializeField] private float bandMetres = 0.4f;

        [TitleGroup("Cut")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Which makes")]
        private string SizeReadout
        {
            get
            {
                if (source == null) return "choose a sprite first";

                var band = Mathf.Max(1, Mathf.RoundToInt(source.textureRect.height * bandHeight));
                var wide = stripWidth / (float)band * bandMetres;
                return $"a band {bandMetres:0.00} m tall, repeating every {wide:0.00} m, " +
                       $"which is {bandMetres / 1.8f:P0} of the player";
            }
        }

        [TitleGroup("Cut")]
        [PropertyRange(2, 6)]
        [Tooltip("How many strips to cut, each from a different place along the " +
                 "piece, so a long run does not repeat one pattern.")]
        [SerializeField] private int variants = 4;

        [TitleGroup("Cut")]
        [LabelText("Match the body colour")]
        [Tooltip("Reads the colour of the slab the moss was drawn on and fills " +
                 "the ground with it. Off uses a plain dark green instead.")]
        [SerializeField] private bool cutFill = true;

        [TitleGroup("Cut")]
        [Tooltip("Also keeps the two rounded ends as caps, for wherever the ground " +
                 "stops in mid air.")]
        [SerializeField] private bool keepEnds = true;

        [TitleGroup("Output")]
        [FolderPath]
        [SerializeField] private string folder = "Assets/GameData/Terrain";

        [TitleGroup("Output")]
        [LabelText("Name")]
        [SerializeField] private string styleName = "MossyGround";

        // ---- Go -------------------------------------------------------------

        [PropertyOrder(10)]
        [Button("Cut strips and build a style", ButtonSizes.Gigantic)]
        [GUIColor(0.45f, 0.78f, 0.55f)]
        private void Cut()
        {
            if (source == null)
            {
                Debug.LogWarning("[Weizza] Choose a sprite to cut from first.");
                return;
            }

            var path = AssetDatabase.GetAssetPath(source);
            var readable = SheetAnalysis.LoadReadable(path);
            if (readable == null)
            {
                Debug.LogWarning($"[Weizza] Could not read {path}.");
                return;
            }

            var region = source.textureRect;
            var band = Mathf.Max(8, Mathf.RoundToInt(region.height * bandHeight));

            // Two strip widths of source are needed per strip, because making one
            // meet itself blends it against the same picture one width further
            // along. Less source than that and there is nothing to blend with.
            if (region.width < stripWidth * 2)
            {
                Debug.LogWarning($"[Weizza] {source.name} is only {region.width:0} px wide. " +
                                 $"Cutting {stripWidth} px strips needs at least {stripWidth * 2}. " +
                                 "Pick a longer piece or a smaller strip width.");
                Object.DestroyImmediate(readable);
                return;
            }

            EnsureFolder(folder);

            var caps = keepEnds ? 2 : 0;
            var sheetWidth = stripWidth * (variants + caps);
            var sheet = new Texture2D(sheetWidth, band, TextureFormat.RGBA32, false);
            var pixels = new Color32[sheetWidth * band];

            var slot = 0;

            if (keepEnds) CopyEnd(readable, region, band, pixels, sheetWidth, slot++, true);

            for (var v = 0; v < variants; v++)
            {
                // Spread the windows across whatever middle there is, keeping a
                // strip width of margin at each side so the blend always has
                // source to reach for and never touches a rounded end.
                var span = region.width - stripWidth * 2f;
                var at = region.x + stripWidth + span * (variants == 1 ? 0.5f : v / (float)(variants - 1));

                Seamless(readable, Mathf.RoundToInt(at), Mathf.RoundToInt(region.yMax) - band, band,
                         pixels, sheetWidth, slot++);
            }

            if (keepEnds) CopyEnd(readable, region, band, pixels, sheetWidth, slot, false);

            sheet.SetPixels32(pixels);
            sheet.Apply();

            var sheetPath = $"{folder}/{styleName}_Edges.png";
            File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(readable);
            Object.DestroyImmediate(sheet);
            AssetDatabase.Refresh();


            var body = cutFill ? SampleBody(path, region, band) : new Color(0.12f, 0.14f, 0.12f);

            SliceSheet(sheetPath, variants + caps, band);
            BuildStyle(sheetPath, body);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Weizza] Cut {variants} strip(s){(keepEnds ? " and two caps" : "")} " +
                      $"from {source.name} into {sheetPath}.");
        }

        // ---- Cutting --------------------------------------------------------

        /// <summary>
        /// Copies a window of the source, blended so its two ends match.
        ///
        /// Each pixel is mixed with the pixel one strip width behind it, by how
        /// far along the strip it sits. At the left end the mix is all of the
        /// original and at the right end all of the pixel that started the strip,
        /// so the two edges land on the same colour and the join disappears.
        ///
        /// The hand off happens inside a narrow window in the middle rather than
        /// across the whole strip. Spread over the full width, every column is
        /// half of one piece of moss and half of another, and the silhouette
        /// turns to mush. Confined to a window, most of the strip is verbatim
        /// art and there is one soft seam per tile that reads as overlap.
        /// </summary>
        private void Seamless(Texture2D src, int x0, int y0, int band,
                              Color32[] into, int sheetWidth, int slot)
        {
            // How much of the strip the cross fade is allowed to occupy.
            const float WindowFraction = 0.3f;
            var lo = 0.5f - WindowFraction * 0.5f;
            var hi = 0.5f + WindowFraction * 0.5f;

            for (var x = 0; x < stripWidth; x++)
            {
                var along = x / (float)stripWidth;
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, hi, along));

                for (var y = 0; y < band; y++)
                {
                    var a = src.GetPixel(x0 + x, y0 + y);
                    var b = src.GetPixel(x0 + x - stripWidth, y0 + y);

                    // Mixed with alpha already multiplied in. A transparent pixel
                    // still carries a colour in the file, usually white, and
                    // mixing that straight paints white haze over the moss tips
                    // wherever one side of the blend was empty.
                    var pa = new Vector3(a.r, a.g, a.b) * a.a;
                    var pb = new Vector3(b.r, b.g, b.b) * b.a;
                    var premultiplied = Vector3.Lerp(pa, pb, blend);

                    // Alpha is taken as the stronger of the two rather than
                    // blended. Averaging it eats the tips of the moss, which are
                    // the part that made the edge read as grown rather than cut.
                    var alpha = Mathf.Max(a.a, b.a);

                    var rgb = alpha > 0.001f ? premultiplied / alpha : Vector3.zero;

                    into[y * sheetWidth + slot * stripWidth + x] =
                        new Color(rgb.x, rgb.y, rgb.z, alpha);
                }
            }
        }

        /// <summary>Copies one rounded end verbatim, to be used as a cap.</summary>
        private void CopyEnd(Texture2D src, Rect region, int band,
                             Color32[] into, int sheetWidth, int slot, bool leftEnd)
        {
            var x0 = leftEnd
                ? Mathf.RoundToInt(region.x)
                : Mathf.RoundToInt(region.xMax) - stripWidth;
            var y0 = Mathf.RoundToInt(region.yMax) - band;

            for (var x = 0; x < stripWidth; x++)
            {
                for (var y = 0; y < band; y++)
                {
                    into[y * sheetWidth + slot * stripWidth + x] = src.GetPixel(x0 + x, y0 + y);
                }
            }
        }

        /// <summary>
        /// Reads the colour of the slab the moss sits on.
        ///
        /// A colour, not a texture. Ferr2D's rock terrain looks the way it does
        /// because somebody drew a seamless rock texture for the body, separately
        /// from the edges. This pack did not: the middle of a Mossy platform is
        /// very nearly flat black, and it is drawn that way on purpose so the
        /// moss reads against it.
        ///
        /// Cutting a body texture out of art that has no body texture in it only
        /// finds the moss on the other side and tiles that across the ground.
        /// Taking the colour it actually is gives back exactly what the artist
        /// drew, which is a dark slab.
        /// </summary>
        private Color SampleBody(string sourcePath, Rect region, int band)
        {
            var src = SheetAnalysis.LoadReadable(sourcePath);
            if (src == null) return new Color(0.10f, 0.12f, 0.10f);

            // The middle third, vertically. Above it is the moss that became the
            // band, below it is the moss on the underside, and between them is
            // the slab.
            var y0 = Mathf.RoundToInt(region.y + region.height * 0.34f);
            var y1 = Mathf.RoundToInt(region.yMax) - band;

            var total = Vector3.zero;
            var counted = 0;

            for (var y = y0; y < y1; y += 2)
            {
                for (var x = Mathf.RoundToInt(region.x) + 8; x < region.xMax - 8; x += 8)
                {
                    var pixel = src.GetPixel(x, y);
                    if (pixel.a < 0.9f) continue;

                    total += new Vector3(pixel.r, pixel.g, pixel.b);
                    counted++;
                }
            }

            Object.DestroyImmediate(src);

            if (counted == 0) return new Color(0.10f, 0.12f, 0.10f);

            var average = total / counted;

            // Lifted a little off pure black. A body at zero swallows the shadow
            // under the moss and the edge stops reading as an edge.
            return Color.Lerp(new Color(average.x, average.y, average.z),
                              new Color(0.16f, 0.18f, 0.16f), 0.35f);
        }

        // ---- Wiring ---------------------------------------------------------

        private void SliceSheet(string path, int count, int band)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = band / Mathf.Max(0.01f, bandMetres);
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var factories = new SpriteDataProviderFactories();
            factories.Init();

            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) return;

            provider.InitSpriteEditorDataProvider();

            var rects = new SpriteRect[count];
            for (var i = 0; i < count; i++)
            {
                var name = Name(i, count);
                rects[i] = new SpriteRect
                {
                    name = $"{styleName}_{name}",
                    spriteID = GUID.Generate(),
                    rect = new Rect(i * stripWidth, 0f, stripWidth, band),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                };
            }

            provider.SetSpriteRects(rects);
            provider.Apply();

            if (provider.targetObject is AssetImporter target) target.SaveAndReimport();
        }

        private string Name(int index, int count)
        {
            if (!keepEnds) return $"Body{index}";
            if (index == 0) return "CapLeft";
            if (index == count - 1) return "CapRight";
            return $"Body{index - 1}";
        }

        private void BuildStyle(string sheetPath, Color body)
        {
            var stylePath = $"{folder}/{styleName}.asset";
            var style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(stylePath);

            if (style == null)
            {
                style = CreateInstance<TerrainStyle>();
                AssetDatabase.CreateAsset(style, stylePath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{styleName}_Edges.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, $"{folder}/{styleName}_Edges.mat");
            }

            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            EditorUtility.SetDirty(material);

            var so = new SerializedObject(style);
            so.FindProperty("edgeMaterial").objectReferenceValue = material;

            // Width and depth are derived from the one number a person can
            // actually judge, which is how tall the band should be in metres.
            // Setting either of them alone is what squashes the art: a band drawn
            // at one aspect and shown at another is stretched by the difference,
            // and no amount of tuning afterwards puts it back.
            var band = Mathf.Max(1, Mathf.RoundToInt(source.textureRect.height * bandHeight));
            var tileWidth = stripWidth / (float)band * bandMetres;

            so.FindProperty("edgeTileWidth").floatValue = tileWidth;

            foreach (var face in new[] { "top", "left", "right", "bottom" })
            {
                var entry = so.FindProperty(face);
                entry.FindPropertyRelative("depth").floatValue = bandMetres;
                entry.FindPropertyRelative("capWidth").floatValue = tileWidth;

                // The band has to straddle the ground line rather than hang below
                // it. Hung below, every gap between two blades shows the body
                // behind, so the grass reads as a flat strip cut off square along
                // the top instead of as blades standing against the sky. The part
                // left under the line is what hides the body's own edge.
                entry.FindPropertyRelative("lift").floatValue =
                    face == "top" ? bandMetres * 0.62f : bandMetres * 0.3f;
            }

            var fillMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{styleName}_Fill.mat");
            if (fillMaterial == null)
            {
                fillMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                            ?? Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(fillMaterial, $"{folder}/{styleName}_Fill.mat");
            }

            fillMaterial.mainTexture = null;
            fillMaterial.color = body;
            if (fillMaterial.HasProperty("_BaseColor")) fillMaterial.SetColor("_BaseColor", body);
            EditorUtility.SetDirty(fillMaterial);

            so.FindProperty("fillMaterial").objectReferenceValue = fillMaterial;

            var top = so.FindProperty("top");
            var variantList = top.FindPropertyRelative("variants");
            variantList.arraySize = 0;

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
            {
                if (asset is not Sprite sprite) continue;

                if (sprite.name.EndsWith("CapLeft", System.StringComparison.Ordinal))
                {
                    top.FindPropertyRelative("leftCap").objectReferenceValue = sprite;
                }
                else if (sprite.name.EndsWith("CapRight", System.StringComparison.Ordinal))
                {
                    top.FindPropertyRelative("rightCap").objectReferenceValue = sprite;
                }
                else
                {
                    variantList.arraySize++;
                    variantList.GetArrayElementAtIndex(variantList.arraySize - 1).objectReferenceValue = sprite;
                }
            }

            // Side and underside get the same strips to begin with. Wrong looking
            // rock is more use than nothing at all, and it makes obvious which
            // faces still need their own art.
            foreach (var side in new[] { "left", "right", "bottom" })
            {
                var face = so.FindProperty(side);
                var list = face.FindPropertyRelative("variants");
                list.arraySize = variantList.arraySize;

                for (var i = 0; i < variantList.arraySize; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue =
                        variantList.GetArrayElementAtIndex(i).objectReferenceValue;
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(style);
            Selection.activeObject = style;
        }

        private void Measure() { }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var built = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
