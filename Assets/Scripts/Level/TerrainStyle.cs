using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Level
{
    /// <summary>Which way a piece of terrain surface faces.</summary>
    public enum TerrainSide
    {
        Top = 0,
        Left = 1,
        Right = 2,
        Bottom = 3,
    }

    /// <summary>
    /// How a kind of ground is dressed: grass on top, rock down the sides, dirt
    /// underneath, and how much of each.
    ///
    /// Held apart from any one piece of terrain because a style outlives the
    /// shapes that wear it. A cliff face, a ledge and an island in the same area
    /// all want the same rock, and authoring that four times is how three of
    /// them end up subtly wrong.
    /// </summary>
    [CreateAssetMenu(fileName = "TerrainStyle", menuName = "Weizza/Terrain Style")]
    public sealed class TerrainStyle : ScriptableObject
    {
        [System.Serializable]
        public sealed class Side
        {
            [HorizontalGroup("row", 120f)]
            [VerticalGroup("row/left")]
            [LabelWidth(60f)]
            public bool draw = true;

            [VerticalGroup("row/left")]
            [LabelWidth(60f)]
            [Tooltip("Whether the player can stand on this face. A cave ceiling " +
                     "is drawn and not walked on.")]
            public bool collide = true;

            [VerticalGroup("row/left")]
            [LabelWidth(60f)]
            [PropertyRange(0.05f, 4f)]
            [Tooltip("How far the band reaches in from the edge, in metres.")]
            public float depth = 0.6f;

            [VerticalGroup("row/left")]
            [LabelWidth(60f)]
            [PropertyRange(-1f, 1f)]
            [Tooltip("Slides the band out past the edge or back inside it. A little " +
                     "overhang stops the fill showing through along the seam.")]
            public float lift;

            [VerticalGroup("row/left")]
            [LabelWidth(60f)]
            [PropertyRange(-0.5f, 0.5f)]
            [Tooltip("Nudge towards or away from the camera. Two bands meeting at " +
                     "a corner fight over the same pixels without this.")]
            public float depthSort;

            [VerticalGroup("row/right")]
            [LabelText("Left cap")]
            [PreviewField(44f, ObjectFieldAlignment.Left)]
            [Tooltip("Optional. The piece that finishes the left hand end of a " +
                     "run, where the ground stops in mid air. Without one the " +
                     "band is cut off square and reads as unfinished.")]
            public Sprite leftCap;

            [VerticalGroup("row/right")]
            [LabelText("Right cap")]
            [PreviewField(44f, ObjectFieldAlignment.Left)]
            public Sprite rightCap;

            [VerticalGroup("row/right")]
            [LabelText("Cap width (m)")]
            [PropertyRange(0.1f, 4f)]
            public float capWidth = 0.6f;

            [VerticalGroup("row/right")]
            [LabelText("Art, picked at random")]
            [InfoBox("Every sprite here has to come from the same texture, because " +
                     "the whole surface is drawn in one pass and a pass has one " +
                     "texture. Slice a sheet and drop the pieces in.",
                     InfoMessageType.None)]
            [ListDrawerSettings(ShowFoldout = false, DraggableItems = false)]
            [PreviewField(48f, ObjectFieldAlignment.Left)]
            public List<Sprite> variants = new();
        }

        [TitleGroup("Body", "What fills the inside", TitleAlignments.Centered)]
        [Required("Without this the body draws as flat magenta.")]
        [SerializeField] private Material fillMaterial;

        [TitleGroup("Body")]
        [PropertyRange(0.25f, 20f)]
        [Tooltip("How many metres one copy of the fill texture covers.")]
        [SerializeField] private float fillTileSize = 4f;

        [TitleGroup("Edges", "One texture, four faces", TitleAlignments.Centered)]
        [Required("Build this from the sprite sheet with the button below.")]
        [SerializeField] private Material edgeMaterial;

        [TitleGroup("Edges")]
        [PropertyRange(0.25f, 8f)]
        [Tooltip("How many metres one piece of edge art covers before the next " +
                 "one is picked.")]
        [SerializeField] private float edgeTileWidth = 2f;

        [TitleGroup("Edges")]
        [Tooltip("Changes which pieces get picked where, without changing anything " +
                 "else. Turn it when a stretch of ground reads as patterned.")]
        [SerializeField] private int shuffle = 1;

        [FoldoutGroup("Top")] [HideLabel] [SerializeField] private Side top = new();
        [FoldoutGroup("Left")] [HideLabel] [SerializeField] private Side left = new() { depth = 0.9f };
        [FoldoutGroup("Right")] [HideLabel] [SerializeField] private Side right = new() { depth = 0.9f };
        [FoldoutGroup("Bottom")] [HideLabel] [SerializeField] private Side bottom = new() { collide = false };

        public Material FillMaterial => fillMaterial;
        public Material EdgeMaterial => edgeMaterial;
        public float FillTileSize => Mathf.Max(0.01f, fillTileSize);
        public float EdgeTileWidth => Mathf.Max(0.05f, edgeTileWidth);
        public int Shuffle => shuffle;

        public Side Of(TerrainSide side) => side switch
        {
            TerrainSide.Left => left,
            TerrainSide.Right => right,
            TerrainSide.Bottom => bottom,
            _ => top,
        };

        /// <summary>
        /// Which face a stretch of ground is, from the way it leans.
        ///
        /// The outward normal is the segment turned a quarter turn, and whichever
        /// of its two components is larger decides the face. It costs nothing,
        /// needs no angles configured, and agrees with what a person would call
        /// that piece of ground: anything leaning less than forty five degrees
        /// from flat is the top, and past that it has become a wall.
        /// </summary>
        public static TerrainSide SideOf(Vector2 from, Vector2 to)
        {
            var along = to - from;
            var outward = new Vector2(-along.y, along.x);

            if (Mathf.Abs(outward.x) > Mathf.Abs(outward.y))
            {
                return outward.x < 0f ? TerrainSide.Left : TerrainSide.Right;
            }

            return outward.y < 0f ? TerrainSide.Bottom : TerrainSide.Top;
        }

        /// <summary>
        /// Picks a piece of art for a tile, the same way every time.
        ///
        /// Seeded from where the tile is rather than from a running random, so a
        /// stretch of ground looks identical after a reload, after a rebuild, and
        /// in a build. A live random would reshuffle the whole level every time
        /// anyone nudged one point, which makes it impossible to say a level is
        /// finished.
        /// </summary>
        public Sprite Piece(TerrainSide side, int tile)
        {
            var variants = Of(side).variants;
            if (variants == null || variants.Count == 0) return null;

            // The bits are mixed before the remainder is taken. Multiplying the
            // tile number and going straight to a remainder leaves the low bits
            // marching in step with the tile, so four variants came out as the
            // same four in the same order all the way along a run, which is the
            // one thing the variants exist to avoid.
            unchecked
            {
                var hash = (uint)(tile * 73856093) ^ (uint)((int)side * 19349663) ^ (uint)(shuffle * 83492791);
                hash ^= hash >> 16;
                hash *= 0x7feb352dU;
                hash ^= hash >> 15;
                hash *= 0x846ca68bU;
                hash ^= hash >> 16;

                return variants[(int)(hash % (uint)variants.Count)];
            }
        }

        /// <summary>The texture every edge sprite must share, or null if they disagree.</summary>
        public Texture SharedTexture(out string complaint)
        {
            Texture found = null;
            complaint = null;

            foreach (TerrainSide side in System.Enum.GetValues(typeof(TerrainSide)))
            {
                var face = Of(side);
                var all = new List<Sprite>(face.variants) { face.leftCap, face.rightCap };

                foreach (var sprite in all)
                {
                    if (sprite == null) continue;

                    if (found == null) found = sprite.texture;
                    else if (sprite.texture != found)
                    {
                        complaint = $"{sprite.name} comes from a different texture than the rest, " +
                                    "so it cannot be drawn in the same pass.";
                        return null;
                    }
                }
            }

            if (found == null) complaint = "No edge sprites have been assigned yet.";
            return found;
        }
    }
}
