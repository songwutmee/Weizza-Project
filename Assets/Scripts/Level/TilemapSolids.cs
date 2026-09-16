using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Weizza.Level
{
    /// <summary>
    /// Gives a Tilemap collision the player can actually stand on.
    ///
    /// Unity ships TilemapCollider2D and nothing else. The player moves on a
    /// CharacterController, which is PhysX, and PhysX cannot see a 2D collider
    /// at all. Without this a level looks finished in the scene view and drops
    /// the player through the floor on the first frame of play, with no error.
    ///
    /// This is the only piece the Tilemap workflow is missing. Everything else
    /// (the palette, the brush, Rule Tiles picking their own corners) is
    /// Unity's and is better than anything written here would be.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Tilemap))]
    [DisallowMultipleComponent]
    public sealed class TilemapSolids : MonoBehaviour
    {
        [TitleGroup("Collision")]
        [InfoBox("Boxes, not one collider per tile. A thousand tiles painted as " +
                 "a thousand colliders costs a thousand PhysX queries per step " +
                 "and shows up as stutter long before it shows up as a bug.")]
        [PropertyRange(0.25f, 8f)]
        [Tooltip("How deep into the screen the boxes reach. Only has to cover " +
                 "the character's own radius.")]
        [SerializeField] private float depth = 2f;

        [TitleGroup("Collision")]
        [Tooltip("Tiles on this map that should be walked through rather than " +
                 "stood on. Leave empty to make every painted tile solid.")]
        [SerializeField] private List<TileBase> passThrough = new();

        [TitleGroup("Collision")]
        [SerializeField] private PhysicsMaterial surface;

        [TitleGroup("Collision")]
        [ToggleLeft]
        [Tooltip("Rebuilds whenever the map changes in the editor. Turn off for " +
                 "a very large level where the pause becomes noticeable.")]
        [SerializeField] private bool liveRebuild = true;

        [TitleGroup("Collision")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Built")]
        private string Built => _tiles == 0
            ? "nothing yet"
            : $"{_tiles} tile(s) merged into {_boxes} box(es)";

        private const string HolderName = "Solids";

        private int _tiles;
        private int _boxes;

        private Tilemap Map => _map != null ? _map : _map = GetComponent<Tilemap>();
        private Tilemap _map;

        [TitleGroup("Collision")]
        [Button(ButtonSizes.Large, Icon = SdfIconType.Bricks), GUIColor(0.4f, 0.85f, 0.5f)]
        public void Rebuild()
        {
            var holder = Holder();
            for (var i = holder.childCount - 1; i >= 0; i--)
            {
                DestroySafely(holder.GetChild(i).gameObject);
            }

            var solid = SolidCells(out _tiles);
            _boxes = 0;

            foreach (var span in Merge(solid))
            {
                Box(holder, span);
                _boxes++;
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying) return;
            Tilemap.tilemapTileChanged += OnTilesChanged;
        }

        private void OnDisable() => Tilemap.tilemapTileChanged -= OnTilesChanged;

        private void OnTilesChanged(Tilemap changed, Tilemap.SyncTile[] _)
        {
            if (liveRebuild && changed == Map) Rebuild();
        }

        // ---- Reading the map ------------------------------------------------

        private HashSet<Vector2Int> SolidCells(out int count)
        {
            var cells = new HashSet<Vector2Int>();
            var bounds = Map.cellBounds;

            for (var y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (var x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var tile = Map.GetTile(new Vector3Int(x, y, 0));
                    if (tile == null || passThrough.Contains(tile)) continue;

                    cells.Add(new Vector2Int(x, y));
                }
            }

            count = cells.Count;
            return cells;
        }

        /// <summary>
        /// Turns painted cells into as few rectangles as will cover them.
        ///
        /// Runs greedily: take the lowest leftmost cell still uncovered, grow it
        /// right as far as the row allows, then grow that whole strip upwards
        /// while every row above matches. Not the smallest possible set of
        /// rectangles, which is a much harder problem, but it collapses the
        /// long flat floors and tall walls that a level is mostly made of, and
        /// those are where the count actually comes from.
        /// </summary>
        private static IEnumerable<RectInt> Merge(HashSet<Vector2Int> cells)
        {
            var left = new HashSet<Vector2Int>(cells);
            var order = new List<Vector2Int>(cells);

            order.Sort((a, b) =>
            {
                var byRow = a.y.CompareTo(b.y);
                return byRow != 0 ? byRow : a.x.CompareTo(b.x);
            });

            foreach (var start in order)
            {
                if (!left.Contains(start)) continue;

                var width = 1;
                while (left.Contains(new Vector2Int(start.x + width, start.y))) width++;

                var height = 1;
                while (RowClear(left, start.x, start.y + height, width)) height++;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        left.Remove(new Vector2Int(start.x + x, start.y + y));
                    }
                }

                yield return new RectInt(start.x, start.y, width, height);
            }
        }

        private static bool RowClear(HashSet<Vector2Int> left, int x, int y, int width)
        {
            for (var i = 0; i < width; i++)
            {
                if (!left.Contains(new Vector2Int(x + i, y))) return false;
            }

            return true;
        }

        // ---- Writing the colliders ------------------------------------------

        private void Box(Transform holder, RectInt span)
        {
            var go = new GameObject($"Box {span.x},{span.y}");
            go.transform.SetParent(holder, false);
            go.layer = gameObject.layer;

            var box = go.AddComponent<BoxCollider>();
            if (surface != null) box.material = surface;

            // Corner to corner through the grid rather than cell size times
            // count, so an isometric or offset grid lands in the right place
            // without this knowing anything about which kind it is.
            var min = Map.CellToLocal(new Vector3Int(span.xMin, span.yMin, 0));
            var max = Map.CellToLocal(new Vector3Int(span.xMax, span.yMax, 0));

            var size = max - min;
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), depth);
            box.center = (min + max) * 0.5f;

            go.transform.localPosition = Vector3.zero;
        }

        private Transform Holder()
        {
            var existing = transform.Find(HolderName);
            if (existing != null) return existing;

            var holder = new GameObject(HolderName);
            holder.transform.SetParent(transform, false);
            return holder.transform;
        }

        private static void DestroySafely(Object thing)
        {
            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }
    }
}
