using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Marks a piece placed by Map Painter, and remembers what it was painted as.
    /// The eraser, the eyedropper and the layer tools find pieces by this mark, so
    /// "anything Map Painter made can be erased" is a fact rather than a guess based
    /// on names or groups.
    ///
    /// No Update, no Awake: it is data only and costs nothing at runtime.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class PaintedPiece : MonoBehaviour
    {
        [Tooltip("The band it was painted into (SceneryRole as a number, so this runtime script does not depend on editor code).")]
        public int role;

        [Tooltip("The sheet or folder it came from, for the eyedropper.")]
        public string source;
    }
}
