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
    /// Draws placeholder terrain art, because none of the free packs are the
    /// right shape.
    ///
    /// Spline terrain wants horizontal bands that tile end to end, and almost
    /// every asset pack on the internet is a tileset: square pieces on a grid,
    /// with corners that a curve never uses. Hunting for the rare pack that
    /// happens to fit costs more than drawing something plain, and something
    /// plain is all a blockout needs.
    ///
    /// What matters here is not that it looks good. It is that the tiling is
    /// provably seamless, so that when the real art arrives and a seam shows up,
    /// nobody has to wonder whether the tool was ever right.
    /// </summary>
    public sealed class TerrainArtForgeWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Terrain Art Forge", priority = 8)]
        private static void Open()
        {
            var window = GetWindow<TerrainArtForgeWindow>();
            window.titleContent = new GUIContent("Terrain Art Forge");
            window.minSize = new Vector2(460f, 520f);
            window.Show();
        }

        [TitleGroup("Colours", "Plain on purpose", TitleAlignments.Centered)]
        [InfoBox("Placeholder art earns its keep by being obviously unfinished. " +
                 "Anything prettier than this starts getting defended in reviews.")]
        [SerializeField] private Color grass = new(0.32f, 0.52f, 0.28f);

        [TitleGroup("Colours")]
        [SerializeField] private Color soil = new(0.30f, 0.24f, 0.19f);

        [TitleGroup("Colours")]
        [SerializeField] private Color rock = new(0.34f, 0.34f, 0.36f);

        [TitleGroup("Shape")]
        [PropertyRange(64, 512)]
        [Tooltip("Width of one piece in pixels. Wider means the pattern takes " +
                 "longer to come round again.")]
        [SerializeField] private int stripWidth = 256;

        [TitleGroup("Shape")]
        [PropertyRange(32, 256)]
        [SerializeField] private int stripHeight = 96;

        [TitleGroup("Shape")]
        [PropertyRange(0.05f, 0.6f)]
        [Tooltip("How far the tufts along the top edge rise and fall, as a share " +
                 "of the strip height.")]
        [SerializeField] private float roughness = 0.28f;

        [TitleGroup("Output")]
        [FolderPath]
        [SerializeField] private string folder = "Assets/GameData/Terrain";

        [TitleGroup("Output")]
        [Tooltip("Also makes the style asset, the two materials, and wires the " +
                 "sliced pieces into the right faces.")]
        [SerializeField] private bool alsoBuildStyle = true;

        [PropertyOrder(10)]
        [Button("Draw placeholder terrain art", ButtonSizes.Gigantic)]
        [GUIColor(0.45f, 0.78f, 0.55f)]
        private void Forge()
        {
            EnsureFolder(folder);

            var edgePath = $"{folder}/PlaceholderEdges.png";
            var fillPath = $"{folder}/PlaceholderFill.png";

            File.WriteAllBytes(edgePath, BuildEdgeSheet().EncodeToPNG());
            File.WriteAllBytes(fillPath, BuildFill().EncodeToPNG());
            AssetDatabase.Refresh();

            ImportEdges(edgePath);
            ImportFill(fillPath);

            if (alsoBuildStyle) BuildStyle(edgePath, fillPath);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Weizza] Placeholder terrain art written to {folder}.");
        }

        // ---- Drawing --------------------------------------------------------

        private const int Variants = 4;

        /// <summary>
        /// One sheet holding every face, laid out in rows.
        ///
        /// Rows rather than a frame around the outside, because every piece here
        /// is the same size and a regular grid is the one thing the slicer can
        /// cut without being told anything.
        /// </summary>
        private Texture2D BuildEdgeSheet()
        {
            var width = stripWidth * Variants;
            var height = stripHeight * 3;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];

            // Row 0 is the bottom row in texture space, and the sheet is read
            // bottom up, so the faces are written in reverse of how they read.
            for (var v = 0; v < Variants; v++)
            {
                Band(pixels, width, v * stripWidth, stripHeight * 2, grass, soil, v, true);
                Band(pixels, width, v * stripWidth, stripHeight, rock, rock * 0.72f, v + 7, false);
                Band(pixels, width, v * stripWidth, 0, soil * 0.8f, soil * 0.55f, v + 13, false);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Draws one band: a fringe of blades over a solid body.
        ///
        /// Blades rather than a wavy line, because a wavy line reads as a shape
        /// that was cut out, and a fringe of overlapping blades reads as
        /// something growing. That difference is most of what separates
        /// placeholder ground from ground, and it costs one loop.
        ///
        /// Everything wraps around the strip, so a blade hanging off the right
        /// edge is drawn again on the left. That is what lets any piece follow
        /// any other piece, which is the rule the random picking depends on.
        /// </summary>
        private void Band(Color32[] pixels, int sheetWidth, int x0, int y0,
                          Color top, Color bottom, int seed, bool fringed)
        {
            var random = new System.Random(seed * 7919 + 13);

            // The body sits under a flat line and the fringe is what makes the
            // edge irregular. Keeping the body flat is what guarantees the seam
            // matches whichever pieces end up next to each other.
            var bodyTop = Mathf.RoundToInt(stripHeight * (1f - roughness));

            for (var x = 0; x < stripWidth; x++)
            {
                for (var y = 0; y < bodyTop; y++)
                {
                    var depth = Mathf.Clamp01(y / (float)Mathf.Max(1, bodyTop));
                    var shade = Color.Lerp(bottom, top, depth * depth * 0.85f);

                    // A little grain, so a flat body does not read as a swatch of
                    // paint. Periodic in x, so it wraps with everything else.
                    var grain = Mathf.Sin(x / (float)stripWidth * Mathf.PI * 2f * 17f) * 0.03f
                                + Mathf.Sin(y * 0.7f) * 0.02f;

                    Plot(pixels, sheetWidth, x0, y0, x, y, shade * (1f + grain));
                }
            }

            if (!fringed)
            {
                // A rock face gets a chipped edge instead of blades: the same
                // idea done with short flat chips rather than tall round ones.
                Chips(pixels, sheetWidth, x0, y0, bodyTop, top, bottom, random);
                return;
            }

            var blades = Mathf.Max(8, stripWidth / 7);
            for (var i = 0; i < blades; i++)
            {
                var at = i / (float)blades * stripWidth + (float)random.NextDouble() * 3f;
                var height = stripHeight * roughness * (0.55f + (float)random.NextDouble() * 0.85f);
                var width = stripHeight * (0.07f + (float)random.NextDouble() * 0.05f);
                var lean = ((float)random.NextDouble() - 0.5f) * 0.7f;

                Blade(pixels, sheetWidth, x0, y0, at, bodyTop, height, width, lean, top, bottom);
            }
        }

        /// <summary>
        /// One blade of moss: a leaning capsule with a dark rim and a lit tip.
        ///
        /// The rim is the part that matters. Flat shapes of colour pile into mush
        /// at any distance, and a dark outline is what keeps each blade readable
        /// once there are forty of them overlapping.
        /// </summary>
        private void Blade(Color32[] pixels, int sheetWidth, int x0, int y0,
                           float at, int baseY, float height, float width, float lean,
                           Color light, Color dark)
        {
            var steps = Mathf.CeilToInt(height);
            var rim = dark * 0.45f;

            for (var s = steps; s >= 0; s--)
            {
                var t = s / (float)Mathf.Max(1, steps);
                var y = baseY + s - height * 0.25f;

                // Leaning more towards the tip, so a blade curves rather than
                // tilting like a matchstick.
                var cx = at + lean * height * t * t;

                // Fattest around a third of the way up, tapering to a point.
                var radius = width * Mathf.Sin(Mathf.Clamp01(t + 0.15f) * Mathf.PI * 0.92f);
                if (radius <= 0.4f) continue;

                var tone = Color.Lerp(dark, light * 1.25f, t * t);

                for (var dx = -radius - 1.2f; dx <= radius + 1.2f; dx += 0.5f)
                {
                    var px = Mathf.RoundToInt(cx + dx);
                    var py = Mathf.RoundToInt(y);

                    var edge = Mathf.Abs(dx) > radius;
                    Plot(pixels, sheetWidth, x0, y0, px, py, edge ? rim : tone);
                }
            }
        }

        /// <summary>Short flat chips, for a face of rock rather than a fringe of moss.</summary>
        private void Chips(Color32[] pixels, int sheetWidth, int x0, int y0,
                           int baseY, Color light, Color dark, System.Random random)
        {
            var count = Mathf.Max(4, stripWidth / 22);

            for (var i = 0; i < count; i++)
            {
                var at = i / (float)count * stripWidth;
                var width = stripWidth / (float)count * (0.5f + (float)random.NextDouble() * 0.6f);
                var height = stripHeight * roughness * (0.2f + (float)random.NextDouble() * 0.5f);

                for (var dx = 0; dx < width; dx++)
                {
                    for (var dy = 0; dy < height; dy++)
                    {
                        var lip = dy > height - 2f;
                        Plot(pixels, sheetWidth, x0, y0, Mathf.RoundToInt(at + dx), baseY + dy,
                             lip ? dark * 0.5f : Color.Lerp(dark, light, 0.6f));
                    }
                }
            }
        }

        /// <summary>
        /// Writes a pixel, wrapping across the strip rather than running off it.
        ///
        /// The wrap is the whole reason any of this tiles. A blade drawn near the
        /// right edge continues on the left, so the two ends of a strip are
        /// genuinely the same picture and meet without a seam.
        /// </summary>
        private void Plot(Color32[] pixels, int sheetWidth, int x0, int y0, int x, int y, Color colour)
        {
            if (y < 0 || y >= stripHeight) return;

            var wrapped = ((x % stripWidth) + stripWidth) % stripWidth;
            var index = (y0 + y) * sheetWidth + x0 + wrapped;
            if (index < 0 || index >= pixels.Length) return;

            pixels[index] = new Color(colour.r, colour.g, colour.b, 1f);
        }

        /// <summary>
        /// The body texture, seamless in both directions.
        ///
        /// Built from sines at whole numbers of cycles across the tile for the
        /// same reason as the edges: anything periodic over the tile meets itself
        /// exactly at the join, with no blending or mirroring needed.
        /// </summary>
        private Texture2D BuildFill()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)size * Mathf.PI * 2f;
                    var v = y / (float)size * Mathf.PI * 2f;

                    var n = Mathf.Sin(u * 3f) * Mathf.Cos(v * 2f) * 0.5f
                            + Mathf.Sin(u * 7f + 1.2f) * Mathf.Cos(v * 5f) * 0.3f
                            + Mathf.Sin(u * 11f) * Mathf.Cos(v * 13f + 0.4f) * 0.2f;

                    pixels[y * size + x] = Color.Lerp(soil * 0.72f, soil * 1.12f, n * 0.5f + 0.5f);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // ---- Importing ------------------------------------------------------

        private void ImportEdges(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = stripHeight;
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

            var rects = new SpriteRect[Variants * 3];
            var names = new[] { "Bottom", "Side", "Top" };

            for (var row = 0; row < 3; row++)
            {
                for (var v = 0; v < Variants; v++)
                {
                    rects[row * Variants + v] = new SpriteRect
                    {
                        name = $"{names[row]}_{v}",
                        spriteID = GUID.Generate(),
                        rect = new Rect(v * stripWidth, row * stripHeight, stripWidth, stripHeight),
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                    };
                }
            }

            provider.SetSpriteRects(rects);
            provider.Apply();

            if (provider.targetObject is AssetImporter target) target.SaveAndReimport();
        }

        private static void ImportFill(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Bilinear;

            // Repeat, because the body texture is genuinely tiled by the shader
            // across whatever shape it lands in, unlike the edge pieces which are
            // addressed by UV rect.
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        // ---- Wiring ---------------------------------------------------------

        private void BuildStyle(string edgePath, string fillPath)
        {
            var stylePath = $"{folder}/PlaceholderTerrain.asset";
            var style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(stylePath);

            if (style == null)
            {
                style = CreateInstance<TerrainStyle>();
                AssetDatabase.CreateAsset(style, stylePath);
            }

            var fillMaterial = Material($"{folder}/PlaceholderFill.mat", "Universal Render Pipeline/Unlit");
            fillMaterial.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(fillPath);
            if (fillMaterial.HasProperty("_BaseMap"))
            {
                fillMaterial.SetTexture("_BaseMap", fillMaterial.mainTexture);
            }

            var edgeMaterial = Material($"{folder}/PlaceholderEdges.mat", "Sprites/Default");
            edgeMaterial.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(edgePath);

            var so = new SerializedObject(style);
            so.FindProperty("fillMaterial").objectReferenceValue = fillMaterial;
            so.FindProperty("edgeMaterial").objectReferenceValue = edgeMaterial;
            so.FindProperty("edgeTileWidth").floatValue = stripWidth / (float)stripHeight;

            Assign(so, "top", edgePath, "Top");
            Assign(so, "left", edgePath, "Side");
            Assign(so, "right", edgePath, "Side");
            Assign(so, "bottom", edgePath, "Bottom");

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(style);
        }

        private static void Assign(SerializedObject style, string side, string sheetPath, string prefix)
        {
            var variants = style.FindProperty(side).FindPropertyRelative("variants");
            variants.arraySize = 0;

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
            {
                if (asset is not Sprite sprite || !sprite.name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                variants.arraySize++;
                variants.GetArrayElementAtIndex(variants.arraySize - 1).objectReferenceValue = sprite;
            }
        }

        private static Material Material(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find(shaderName) ?? Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

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
