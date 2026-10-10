using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Leans a sprite back so it sits square to a pitched camera.
    ///
    /// A drawing standing straight up in the world, viewed by a camera angled
    /// down, is foreshortened: the character loses height and gains nothing. Tilt
    /// the drawing by the same angle the camera is tilted and its plane becomes
    /// perpendicular to the view again, so it renders at exactly the proportions
    /// it was drawn at, anywhere in the frame. Orthographic makes this exact
    /// rather than approximate, because every view ray is parallel.
    ///
    /// The tilt happens around the transform origin, so put the sprite pivot at
    /// the character's feet (Sprite import, Pivot: Bottom) or the character will
    /// sink into the floor as the angle goes up.
    ///
    /// Facing left and right is done with flipX rather than a negative scale,
    /// because a negative scale on a tilted quad also mirrors the tilt.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("Weizza/Sprite Facing")]
    public sealed class SpriteFacing : MonoBehaviour
    {
        [BoxGroup("Facing")]
        [Tooltip("Reads the scene's camera rig. Turn this off to pin one sprite " +
                 "to a fixed angle, for a prop drawn at a different tilt.")]
        [SerializeField] private bool followRig = true;

        [Tooltip("Tick when the source drawings face left. Sprite packs are not " +
                 "consistent about which way a character is drawn, and getting " +
                 "it wrong mirrors everything: the character runs backwards, " +
                 "and attacks come out of the wrong side of the body.")]
        [ToggleLeft]
        [SerializeField] private bool artFacesLeft;

        [BoxGroup("Facing")]
        [HideIf(nameof(followRig))]
        [PropertyRange(0f, 45f)]
        [SerializeField] private float customPitch = ViewRig.DefaultPitch;

        [BoxGroup("Facing")]
        [Tooltip("Lifts the sprite slightly along the camera's own axis to stop " +
                 "it fighting with the floor mesh for the same pixels. A " +
                 "millimetre is usually enough.")]
        [PropertyRange(0f, 0.1f)]
        [SerializeField] private float depthBias = 0.005f;

        /// <summary>
        /// Nudge applied by whatever is driving the art, in local space.
        ///
        /// Hand drawn sheets almost never agree about where the ground line
        /// sits inside a cell, and re-slicing every sheet to fix that is a bad
        /// trade. SpriteAnimator writes the per clip correction here instead, so
        /// the fix lives next to the animation it belongs to and only one thing
        /// in the project ever writes this transform's position.
        /// </summary>
        public Vector2 ArtOffset { get; set; }

        private SpriteRenderer _renderer;

        public SpriteRenderer Renderer
        {
            get
            {
                if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
                return _renderer;
            }
        }

        /// <summary>True when the character is looking left.</summary>
        public bool FlippedX
        {
            get => Renderer != null && Renderer.flipX;
            set { if (Renderer != null) Renderer.flipX = value; }
        }

        private void OnEnable() => ApplyPitch();

        private void OnValidate() => ApplyPitch();

        private void LateUpdate()
        {
            // Cheap enough to keep correct every frame, and it has to be, because
            // anything that rotates a parent would otherwise quietly untilt the
            // sprite and nobody would spot it until the art looked slightly short.
            ApplyPitch();
        }

        /// <summary>Squares this sprite to the camera. Safe to call from edit mode.</summary>
        public void ApplyPitch()
        {
            var pitch = followRig ? ViewRig.Pitch : customPitch;
            var rotation = Quaternion.Euler(pitch, 0f, 0f);
            transform.rotation = rotation;

            // The art correction rides along the tilted plane, not along world
            // axes, so raising a sprite by a few centimetres does not also push
            // it backwards in the room.
            var offset = rotation * new Vector3(ArtOffset.x, ArtOffset.y, 0f);

            // The bias goes along the camera's own forward, so the nudge never
            // changes where the character reads as standing.
            var forward = rotation * Vector3.forward;
            transform.localPosition = offset - forward * depthBias;
        }

        private Facing _facing = Facing.Right;
        private bool _clipMirror;

        /// <summary>Set by the animator when a clip is drawn facing the opposite way.</summary>
        public bool ClipMirror
        {
            get => _clipMirror;
            set
            {
                if (_clipMirror == value) return;
                _clipMirror = value;
                SetFacing(_facing);
            }
        }

        public void SetFacing(Facing facing)
        {
            _facing = facing;
            var flip = artFacesLeft ? facing == Facing.Right : facing == Facing.Left;
            FlippedX = flip ^ _clipMirror;
        }
    }
}
