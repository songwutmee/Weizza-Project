using System.Collections.Generic;

namespace Weizza.EditorTools
{
    /// <summary>
    /// The seven bands a sprite in this level can belong to. One source of
    /// truth shared by Map Painter and the Scenery snap menu, because two
    /// separately kept copies of the same numbers is exactly the bug class
    /// the project notes warns about: they drift apart and nobody notices which one
    /// is stale.
    /// </summary>
    public enum SceneryRole
    {
        FarFog,
        DarkThings,
        EarthFill,
        GroundMoss,
        Vines,
        PlayerPlane,
        ForegroundSilhouette,
    }

    /// <summary>
    /// The measured numbers behind each role, read off the real scene on
    /// 18 September 2026. See the project notes, "The map is not a Tilemap", for the
    /// table and the reasoning behind the Z values.
    ///
    /// Three of the roles cover a range of sorting orders rather than one
    /// value, because several pieces stack among themselves inside that band
    /// for their own sense of depth. A new piece is painted at the
    /// representative `Order`, and `OrderMin` to `OrderMax` is what counts as
    /// belonging to the band when the scene is being filtered.
    /// </summary>
    public static class SceneryRoleData
    {
        public readonly struct Numbers
        {
            public readonly string Layer;
            public readonly int Order;
            public readonly int OrderMin;
            public readonly int OrderMax;
            public readonly float Z;
            public readonly bool DefaultSolid;
            public readonly float DefaultColliderDepth;

            public Numbers(string layer, int order, int orderMin, int orderMax, float z,
                bool defaultSolid, float defaultColliderDepth)
            {
                Layer = layer;
                Order = order;
                OrderMin = orderMin;
                OrderMax = orderMax;
                Z = z;
                DefaultSolid = defaultSolid;
                DefaultColliderDepth = defaultColliderDepth;
            }
        }

        public static Numbers Get(SceneryRole role) => role switch
        {
            SceneryRole.FarFog => new Numbers("Background", 6, 0, 13, 9f, false, 0f),
            SceneryRole.DarkThings => new Numbers("Background", 30, 14, 60, 5f, false, 0f),
            SceneryRole.EarthFill => new Numbers("Default", -7, -9, -6, 0.45f, false, 0f),
            SceneryRole.GroundMoss => new Numbers("Default", -5, -5, -4, 0.25f, true, 0.35f),
            SceneryRole.Vines => new Numbers("Default", -3, -3, -1, 0.12f, false, 0f),
            SceneryRole.PlayerPlane => new Numbers("Default", 0, 0, 9, 0f, false, 0f),
            SceneryRole.ForegroundSilhouette => new Numbers("Foreground", 2, -50, 50, -2.5f, false, 0f),
            _ => new Numbers("Default", 0, 0, 0, 0f, false, 0f),
        };

        /// <summary>
        /// What the dropdown says. The enum names alone read as jargon to
        /// anyone who did not place these bands, so every entry says where it
        /// sits relative to the player in plain words.
        /// </summary>
        public static string Label(SceneryRole role) => role switch
        {
            SceneryRole.FarFog => "1 Far Fog  (furthest back, sky and mist)",
            SceneryRole.DarkThings => "2 Dark Things  (behind, mid distance trees)",
            SceneryRole.EarthFill => "3 Earth Fill  (the solid dark mass under the ground)",
            SceneryRole.GroundMoss => "4 Ground Moss  (what the player walks on)",
            SceneryRole.Vines => "5 Vines  (hangs just in front of the ground)",
            SceneryRole.PlayerPlane => "6 Player Plane  (player, enemies, NPCs)",
            SceneryRole.ForegroundSilhouette => "7 Foreground  (black shapes in front of everything)",
            _ => role.ToString(),
        };

        /// <summary>Back to front, the order they draw in, for a layers list.</summary>
        public static IReadOnlyList<SceneryRole> BackToFront { get; } = new[]
        {
            SceneryRole.FarFog,
            SceneryRole.DarkThings,
            SceneryRole.EarthFill,
            SceneryRole.GroundMoss,
            SceneryRole.Vines,
            SceneryRole.PlayerPlane,
            SceneryRole.ForegroundSilhouette,
        };

        /// <summary>
        /// Whether a renderer already in the scene counts as part of a band.
        /// Matched on Sorting Layer and order rather than on which group object
        /// it happens to sit under, because the level was hand placed long
        /// before Map Painter grouped anything by role, and the bands are real
        /// regardless of hierarchy.
        /// </summary>
        public static bool Matches(UnityEngine.SpriteRenderer renderer, SceneryRole role)
        {
            if (renderer == null) return false;
            var n = Get(role);
            return renderer.sortingLayerName == n.Layer
                   && renderer.sortingOrder >= n.OrderMin
                   && renderer.sortingOrder <= n.OrderMax;
        }
    }
}
