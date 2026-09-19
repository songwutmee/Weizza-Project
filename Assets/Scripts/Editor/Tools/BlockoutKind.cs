using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// What a painted blockout box means. The direct equivalent of LDtk's
    /// IntGrid values, checked in its own platformer sample, where the
    /// Collisions layer is painted as dirt, stone, ladder and walls long
    /// before any tile art exists and the art is laid over it afterwards.
    /// That is the whole point here too: a level can be designed, walked and
    /// fixed while the art is still being drawn.
    /// </summary>
    public enum BlockoutKind
    {
        Ground,
        Ledge,
        Wall,
        Hazard,
        Water,
        Note,
    }

    public enum BlockoutSolidity
    {
        /// <summary>A real BoxCollider the player stands on and walks into.</summary>
        Solid,

        /// <summary>A collider marked as a trigger, for anything that reacts rather than blocks.</summary>
        Trigger,

        /// <summary>Drawn only. A label on the level for a person to read.</summary>
        None,
    }

    public static class BlockoutKindData
    {
        public readonly struct Look
        {
            public readonly string Label;
            public readonly Color Colour;
            public readonly BlockoutSolidity Solidity;
            public readonly string Note;

            public Look(string label, Color colour, BlockoutSolidity solidity, string note)
            {
                Label = label;
                Colour = colour;
                Solidity = solidity;
                Note = note;
            }
        }

        /// <summary>
        /// Flat, muted and clearly different from each other, the same colour
        /// language LDtk's own palette uses (#733E39 dirt, #EAD4AA stone,
        /// #8B9BB4 ladder). Blockout colour is read at a glance across a whole
        /// level at once, so saturation is the enemy and hue separation is the
        /// job.
        /// </summary>
        public static Look Get(BlockoutKind kind) => kind switch
        {
            BlockoutKind.Ground => new Look(
                "Ground", Hex("#8C6A4F"), BlockoutSolidity.Solid,
                "Walk on top of it and into its sides. The ordinary floor."),

            BlockoutKind.Ledge => new Look(
                "Ledge", Hex("#D8C08A"), BlockoutSolidity.Solid,
                "A thin platform. Same collider, drawn flat, for reading a jump route."),

            BlockoutKind.Wall => new Look(
                "Wall", Hex("#7E8CA8"), BlockoutSolidity.Solid,
                "Blocks sideways. Where Se's wall jump will go when it exists."),

            BlockoutKind.Hazard => new Look(
                "Hazard", Hex("#C4675E"), BlockoutSolidity.Trigger,
                "Marks something that should hurt. Nothing reads it yet, it is a note with a shape."),

            BlockoutKind.Water => new Look(
                "Water", Hex("#5E9AA8"), BlockoutSolidity.Trigger,
                "Marks water or miasma. A trigger so it never blocks a test walk."),

            BlockoutKind.Note => new Look(
                "Note", Hex("#9B7FB8"), BlockoutSolidity.None,
                "Pure annotation. Draw the shape you want the artist to fill and say so in the name."),

            _ => new Look("Ground", Color.grey, BlockoutSolidity.Solid, string.Empty),
        };

        public static readonly BlockoutKind[] All =
        {
            BlockoutKind.Ground,
            BlockoutKind.Ledge,
            BlockoutKind.Wall,
            BlockoutKind.Hazard,
            BlockoutKind.Water,
            BlockoutKind.Note,
        };

        private static Color Hex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }
}
