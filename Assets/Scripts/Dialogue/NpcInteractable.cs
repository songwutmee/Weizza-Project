using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// Someone worth walking up to.
    ///
    /// Two ways in, because players reach for different ones. A key press is
    /// what a pad player expects and what a keyboard player finds without being
    /// told. A click is what everyone tries first when the character is standing
    /// right there on screen.
    ///
    /// The click has to be arbitrated rather than simply accepted: left mouse is
    /// also the light attack. The rule is that a click only talks when the
    /// pointer is genuinely over this character and the player is close enough
    /// to be talking anyway, so swinging at empty air never opens a panel and
    /// standing in a crowd never swallows an attack.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class NpcInteractable : MonoBehaviour
    {
        [TitleGroup("Talk")]
        [Required("Nothing to say without one.")]
        [SerializeField] private Conversation conversation;

        [TitleGroup("Talk")]
        [PropertyRange(0.5f, 6f)]
        [Tooltip("How close the player has to stand. Measured flat, so standing " +
                 "on a ledge above still counts.")]
        [SerializeField] private float range = 2f;

        [TitleGroup("Talk")]
        [Tooltip("Optional. Turned on when the player is in range. A floating key " +
                 "cap, an arrow, whatever the art is. The glow below works on its " +
                 "own while there is no art for this.")]
        [SerializeField] private GameObject prompt;

        [TitleGroup("Glow", "How they show they can be talked to", TitleAlignments.Centered)]
        [InfoBox("Something has to say that a key press will do anything here. " +
                 "Without it the player walks past, because nothing on screen " +
                 "separates a character who talks from painted scenery.")]
        [SerializeField] private bool glowInRange = true;

        [TitleGroup("Glow")]
        [ShowIf(nameof(glowInRange))]
        [PropertyRange(0f, 0.15f)]
        [Tooltip("How much bigger they get at the top of the pulse. This is the " +
                 "part that actually reads, so turning it to zero leaves the " +
                 "highlight almost invisible.")]
        [SerializeField] private float glowSwell = 0.06f;

        [TitleGroup("Glow")]
        [ShowIf(nameof(glowInRange))]
        [Tooltip("Colour at the bottom of the pulse. A sprite renderer multiplies " +
                 "this against the art, so it can only darken: the dip is what is " +
                 "visible, not a brightening.")]
        [SerializeField] private Color glowDip = new(0.72f, 0.78f, 0.86f);

        [TitleGroup("Glow")]
        [ShowIf(nameof(glowInRange))]
        [PropertyRange(0f, 4f)]
        [Tooltip("Pulses per second. A still highlight gets read as part of the " +
                 "art within a few seconds of standing there.")]
        [SerializeField] private float glowSpeed = 1.6f;

        [TitleGroup("Talk")]
        [Tooltip("Plays once the first time and then goes quiet. Off lets the " +
                 "player hear the same lines as often as they like.")]
        [SerializeField] private bool onlyOnce;

        private Transform _player;
        private Camera _camera;
        private bool _spent;
        private SpriteRenderer _art;
        private Color _restingTint;
        private Vector3 _restingScale;
        private float _swell;

        private void Awake()
        {
            if (prompt != null) prompt.SetActive(false);

            _art = GetComponentInChildren<SpriteRenderer>();
            if (_art != null) _restingTint = _art.color;
            _restingScale = transform.localScale;
        }

        private void Update()
        {
            var ready = CanTalk();
            if (prompt != null && prompt.activeSelf != ready) prompt.SetActive(ready);
            Glow(ready);

            if (!ready) return;

            var reader = InputReader.Instance;
            if (reader == null) return;

            if (reader.InteractPressed || (reader.PointerPressed && PointerIsOnMe(reader.PointerPosition)))
            {
                Talk();
            }
        }

        /// <summary>
        /// Marks the character out while they can be spoken to.
        ///
        /// Mostly by size, not by colour. A sprite renderer's colour multiplies
        /// against the art, and art drawn on white is already at the top of that
        /// range, so a tint can only ever darken it: the obvious idea of
        /// brightening the sprite does nothing at all. Swelling it does, and a
        /// character who breathes is read as alive and approachable in a way a
        /// recoloured one is not.
        ///
        /// Unscaled time, so the pulse carries on through a hitstop, and eased
        /// back rather than reset, so walking out of range settles instead of
        /// snapping.
        /// </summary>
        private void Glow(bool ready)
        {
            if (!glowInRange || _art == null) return;

            var pulse = ready
                ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * glowSpeed * Mathf.PI * 2f)
                : 0f;

            var ease = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            _swell = Mathf.Lerp(_swell, pulse, ease);

            transform.localScale = _restingScale * (1f + glowSwell * _swell);
            _art.color = Color.Lerp(_restingTint, glowDip, _swell * 0.55f);
        }

        private bool CanTalk()
        {
            if (conversation == null || DialogueDirector.Talking) return false;
            if (onlyOnce && _spent) return false;

            var player = Player();
            if (player == null) return false;

            // Flat distance. A player on a ledge a metre above the NPC is still
            // standing in front of them as far as the screen is concerned, and
            // refusing to talk there reads as the prompt being broken.
            var here = transform.position;
            var there = player.position;
            return Mathf.Abs(there.x - here.x) <= range && Mathf.Abs(there.y - here.y) <= range * 2f;
        }

        private bool PointerIsOnMe(Vector2 screenPoint)
        {
            var camera = Camera();
            if (camera == null) return false;

            var ray = camera.ScreenPointToRay(screenPoint);
            return Physics.Raycast(ray, out var hit, 500f) && hit.transform.IsChildOf(transform);
        }

        private void Talk()
        {
            var director = DialogueDirector.Instance;
            if (director == null)
            {
                Debug.LogWarning("[Weizza] No DialogueDirector in the scene, so nobody can talk.");
                return;
            }

            _spent = true;
            if (prompt != null) prompt.SetActive(false);
            director.Play(conversation);
        }

        private Transform Player()
        {
            if (_player != null) return _player;

            var controller = FindAnyObjectByType<PlayerController>();
            _player = controller != null ? controller.transform : null;
            return _player;
        }

        private Camera Camera()
        {
            if (_camera != null) return _camera;

            _camera = UnityEngine.Camera.main;
            return _camera;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.7f);
            Gizmos.DrawWireCube(transform.position, new Vector3(range * 2f, range * 4f, 0.2f));
        }
    }
}
