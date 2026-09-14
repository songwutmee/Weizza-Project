using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>What one sprite sheet turned out to be.</summary>
    public struct SheetLayout
    {
        public int columns;
        public int rows;
        public int cellWidth;
        public int cellHeight;

        /// <summary>Normalised height of the ground line inside a cell.</summary>
        public float pivotY;

        /// <summary>Row of pixels the character stands on, for the readout.</summary>
        public int groundPixel;

        public bool valid;
        public string note;

        public int FrameCount => columns * rows;
    }

    /// <summary>
    /// Works out how a sprite sheet is laid out by looking at the pixels.
    ///
    /// Two things have to be read off a sheet before it can be sliced, and both
    /// are usually typed in by hand from a guess: how big a cell is, and where
    /// the character's feet are inside it.
    ///
    /// The cell size is not found by looking for empty gutters. Gutters work
    /// until two drawings touch, and on a twelve frame attack at least one pair
    /// always does, at which point counting gaps reports eight frames and every
    /// slice after that is wrong. What is reliable is the spacing between the
    /// middles of the drawings: an animator lays frames on an even grid even
    /// when the drawings inside them overlap, so the distance from one drawing's
    /// centre to the next is the cell width, and the median of those distances
    /// survives a couple of frames where the character lunges out of its box.
    ///
    /// The ground line is the lowest opaque pixel in each cell, taken as the
    /// most common value rather than the largest. Most common, because a sword
    /// swinging below the feet on two frames of an attack is not the floor.
    /// </summary>
    public static class SheetAnalysis
    {
        /// <summary>
        /// Loads a PNG straight off disk rather than through the asset pipeline.
        ///
        /// A texture imported normally is not readable, and making it readable
        /// means changing import settings, reimporting, reading, and changing
        /// them back. Reading the file is faster and leaves the project alone.
        /// </summary>
        public static Texture2D LoadReadable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(assetPath))) return texture;

            Object.DestroyImmediate(texture);

            // LoadImage decodes PNG and JPG and nothing else, so a PSD or a TIF
            // straight from the artist fails here. Those are already imported and
            // sitting in memory as a GPU texture, so take the pixels back off the
            // card instead of decoding the file a second way.
            return FromImported(assetPath);
        }

        /// <summary>
        /// Copies an imported texture into a readable one through the GPU.
        ///
        /// Goes through a RenderTexture rather than GetPixels because the importer
        /// setting that would allow GetPixels is off for almost every asset in a
        /// real project, and turning it on doubles the memory for every one of
        /// them. A blit does not care.
        /// </summary>
        private static Texture2D FromImported(string assetPath)
        {
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (source == null) return null;

            var buffer = RenderTexture.GetTemporary(
                source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

            var previous = RenderTexture.active;
            Graphics.Blit(source, buffer);
            RenderTexture.active = buffer;

            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);

            return copy;
        }

        public static SheetLayout Analyse(Texture2D texture)
        {
            var result = new SheetLayout();
            if (texture == null)
            {
                result.note = "could not read the file";
                return result;
            }

            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels32();

            // Texture space: index 0 is the bottom left pixel.
            var columnHasInk = new bool[width];
            var rowHasInk = new bool[height];

            for (var y = 0; y < height; y++)
            {
                var offset = y * width;
                for (var x = 0; x < width; x++)
                {
                    if (pixels[offset + x].a == 0) continue;
                    columnHasInk[x] = true;
                    rowHasInk[y] = true;
                }
            }

            result.columns = CountCells(columnHasInk, width);
            result.rows = CountCells(rowHasInk, height);
            result.cellWidth = width / result.columns;
            result.cellHeight = height / result.rows;

            result.pivotY = GroundPivot(pixels, width, height,
                result.columns, result.rows, result.cellHeight, out result.groundPixel);

            result.valid = true;
            result.note = result.rows > 1
                ? $"{result.rows} rows, so each row may want its own pivot"
                : "ok";

            return result;
        }

        /// <summary>
        /// How many cells fit along one axis, from the spacing between drawings.
        /// </summary>
        private static int CountCells(bool[] hasInk, int size)
        {
            var centres = new List<float>();
            var start = -1;

            for (var i = 0; i < size; i++)
            {
                if (hasInk[i] && start < 0) start = i;
                if (hasInk[i] || start < 0) continue;
                centres.Add((start + i - 1) * 0.5f);
                start = -1;
            }
            if (start >= 0) centres.Add((start + size - 1) * 0.5f);

            if (centres.Count < 2) return 1;

            var gaps = new List<float>(centres.Count - 1);
            for (var i = 1; i < centres.Count; i++) gaps.Add(centres[i] - centres[i - 1]);
            gaps.Sort();

            var spacing = gaps[gaps.Count / 2];
            if (spacing <= 0.5f) return 1;

            var count = Mathf.RoundToInt(size / spacing);
            count = Mathf.Clamp(count, 1, size);

            // The grid has to divide the image exactly. Stepping down finds the
            // nearest count that does, which corrects a median thrown off by one
            // frame where the character drifted inside its cell.
            while (count > 1 && size % count != 0) count--;
            return Mathf.Max(1, count);
        }

        private static float GroundPivot(Color32[] pixels, int width, int height,
            int columns, int rows, int cellHeight, out int groundPixel)
        {
            var grounds = new List<int>(columns * rows);
            var cellWidth = width / columns;

            for (var row = 0; row < rows; row++)
            {
                // Row zero is the top row of cells, which is how a sheet reads,
                // but texture space counts up from the bottom.
                var bottom = height - (row + 1) * cellHeight;

                for (var column = 0; column < columns; column++)
                {
                    var left = column * cellWidth;
                    var found = -1;

                    for (var y = bottom; y < bottom + cellHeight && found < 0; y++)
                    {
                        for (var x = left; x < left + cellWidth; x++)
                        {
                            if (pixels[y * width + x].a == 0) continue;
                            found = y - bottom;
                            break;
                        }
                    }

                    if (found >= 0) grounds.Add(found);
                }
            }

            if (grounds.Count == 0)
            {
                groundPixel = 0;
                return 0f;
            }

            groundPixel = MostCommon(grounds);
            return cellHeight <= 0 ? 0f : Mathf.Clamp01((float)groundPixel / cellHeight);
        }

        private static int MostCommon(List<int> values)
        {
            var counts = new Dictionary<int, int>();
            foreach (var value in values)
            {
                counts.TryGetValue(value, out var seen);
                counts[value] = seen + 1;
            }

            var best = values[0];
            var bestCount = 0;
            foreach (var pair in counts)
            {
                if (pair.Value < bestCount) continue;
                if (pair.Value == bestCount && pair.Key >= best) continue;
                best = pair.Key;
                bestCount = pair.Value;
            }
            return best;
        }
    }
}
