using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Defaults
{
    /// <summary>
    /// Interaction for a first person game: whatever is under the centre of the
    /// screen is the target, not whatever happens to be nearest.
    ///
    /// Registering the aim target with StoryGraphTrigger matters more than it
    /// looks. Triggers decide on their own whether to show a prompt, and their
    /// default answer is proximity. Standing between two crates and looking at
    /// the left one would light up the right one, which reads as the game
    /// choosing for you.
    /// </summary>
    [AddComponentMenu("StoryKit/First Person Interactor")]
    [DisallowMultipleComponent]
    public sealed class FirstPersonInteractor : MonoBehaviour
    {
        [Tooltip("How far the player can reach, in metres.")]
        [SerializeField, Min(0.1f)] private float _interactionDistance = 3f;

        [Tooltip("Layers the aim ray is allowed to hit. Exclude the player's own "
               + "collider, or the ray stops on the body it started in.")]
        [SerializeField] private LayerMask _interactionMask = Physics.DefaultRaycastLayers;

        [Tooltip("Seconds between aim checks. Zero tests every frame. A twentieth "
               + "of a second is imperceptible on a walking player and cheap on a "
               + "four player session where every client runs this.")]
        [SerializeField, Min(0f)] private float _aimInterval = 0.05f;

        private Camera _camera;
        private DynamicCrosshair _crosshair;
        private StoryGraphTrigger _currentTarget;
        private float _nextAimAt;
        private float _nextCrosshairSearchAt;

        private void Awake()
        {
            _camera = Camera.main;
            if (_camera == null) _camera = GetComponentInChildren<Camera>();

            if (_camera == null)
            {
                Debug.LogWarning(
                    "[StoryKit] First Person Interactor found no camera, so nothing " +
                    "can be aimed at. Put it on the player root with the view camera " +
                    "as a child, or tag that camera MainCamera.", this);
            }
        }

        private void Update()
        {
            // Interaction is off while a conversation or cutscene owns the player.
            // The crosshair has to be told, or a highlighted prop stays lit up
            // through the whole scene inviting a press that does nothing.
            if (StoryControl.MovementLocked || _camera == null)
            {
                if (_currentTarget != null)
                {
                    _currentTarget = null;
                    StoryGraphTrigger.SetAimTarget(null);
                }
                SetCrosshair(DynamicCrosshair.State.Hidden);
                return;
            }

            if (Time.unscaledTime >= _nextAimAt)
            {
                _nextAimAt = Time.unscaledTime + _aimInterval;
                _currentTarget = FindTarget();
            }

            // Re-stamped every frame, not only when the target changes, because
            // the triggers time this out deliberately so that removing the
            // interactor hands control back to proximity.
            StoryGraphTrigger.SetAimTarget(_currentTarget);

            bool ready = _currentTarget != null && !_currentTarget.IsPlaying;
            SetCrosshair(ready
                ? DynamicCrosshair.State.Interactable
                : DynamicCrosshair.State.Default);

            if (ready && StoryInput.Interact)
            {
                StoryInput.WarnIfMissing(this);

                // force skips the trigger's own proximity test: the ray already
                // proved the player can reach it, and a thin collider the player
                // is looking straight at may not overlap them at all.
                _currentTarget.Interact(force: true);
            }
        }

        private StoryGraphTrigger FindTarget()
        {
            var ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            return Physics.Raycast(ray, out RaycastHit hit, _interactionDistance,
                       _interactionMask, QueryTriggerInteraction.Collide)
                ? hit.collider.GetComponentInParent<StoryGraphTrigger>()
                : null;
        }

        /// <summary>
        /// Found on demand instead of cached in Awake, because the HUD is usually
        /// built at runtime by GameKit or the setup wizard: a crosshair looked up
        /// once in Awake is null for the whole session in exactly the setup this
        /// package recommends. The retry is rate limited, since a scene with no
        /// crosshair at all would otherwise search every frame forever.
        /// </summary>
        private void SetCrosshair(DynamicCrosshair.State state)
        {
            if (_crosshair == null && Time.unscaledTime >= _nextCrosshairSearchAt)
            {
                _nextCrosshairSearchAt = Time.unscaledTime + 1f;
                _crosshair = FindFirstObjectByType<DynamicCrosshair>(
                    FindObjectsInactive.Exclude);
            }

            // Explicit Unity null check rather than ?., which tests managed null
            // and would happily call into a destroyed component.
            if (_crosshair != null) _crosshair.SetState(state);
        }

        private void OnDisable()
        {
            // The frame stamp lapses by itself, so proximity resumes deciding
            // within a frame without anything being cleared here.
            _currentTarget = null;
        }
    }
}
