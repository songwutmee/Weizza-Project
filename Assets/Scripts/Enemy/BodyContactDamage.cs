using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Touching this enemy hurts, the Hollow Knight rule. A crawler with no attack
    /// at all is still a threat you have to jump over, which is what makes simple
    /// enemies worth placing.
    ///
    /// CharacterController raises no collision event against another body, so this
    /// measures the touch itself each frame. The Player and Enemy layers do collide,
    /// which stops the two capsules at each other's surface, hence the reach.
    /// </summary>
    [AddComponentMenu("Weizza/Enemy/Body Contact Damage")]
    [RequireComponent(typeof(Vitals))]
    public sealed class BodyContactDamage : MonoBehaviour
    {
        [BoxGroup("Hurt")]
        [MinValue(0f)]
        [SerializeField] private float damage = 8f;

        [BoxGroup("Hurt")]
        [SerializeField] private Vector2 knockback = new(5f, 5f);

        [BoxGroup("Hurt")]
        [PropertyRange(0.2f, 2f)]
        [Tooltip("Seconds before contact can hurt again. The player's own hit stun usually covers this anyway.")]
        [SerializeField] private float repeatDelay = 0.6f;

        [BoxGroup("Hurt")]
        [ToggleLeft]
        [Tooltip("Off while the enemy is staggered, so hitting it does not cost the player health on the bounce back.")]
        [SerializeField] private bool harmlessWhenStaggered = true;

        [BoxGroup("Hurt")]
        [PropertyRange(-0.3f, 0.3f)]
        [Tooltip("Grows (positive) or shrinks (negative) the touch area. The Player and Enemy layers collide, so " +
                 "the two capsules stop at each other's surface and never overlap: a touch has to reach a little " +
                 "past the body to count at all.")]
        [SerializeField] private float touchReach = 0.1f;

        private Vitals _vitals;
        private EnemyController _enemy;
        private CharacterController _body;
        private float _readyAt;
        private PlayerController _playerOwner;
        private CharacterController _playerBody;

        private void Awake()
        {
            _vitals = GetComponent<Vitals>();
            _enemy = GetComponent<EnemyController>();
            _body = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (Time.time < _readyAt || _vitals == null || !_vitals.IsAlive || _body == null) return;
            if (harmlessWhenStaggered && _enemy != null && _enemy.Current == EnemyController.State.Stagger) return;

            var player = PlayerLocator.Player;
            if (player == null || player.Vitals == null || !player.Vitals.IsAlive) return;

            // Cached: every enemy runs this every frame, and GetComponent per
            // enemy per frame adds up in a crowded room.
            if (_playerOwner != player)
            {
                _playerOwner = player;
                _playerBody = player.GetComponent<CharacterController>();
            }
            var playerBody = _playerBody;
            if (playerBody == null) return;

            var mine = _body.bounds;
            mine.Expand(touchReach * 2f);
            if (!mine.Intersects(playerBody.bounds)) return;

            _readyAt = Time.time + repeatDelay;
            var away = Mathf.Sign(player.transform.position.x - transform.position.x);
            if (away == 0f) away = 1f;

            player.Vitals.Receive(new DamageInfo
            {
                amount = damage,
                poiseDamage = 999f,
                source = gameObject,
                point = playerBody.bounds.center,
                knockback = new Vector2(knockback.x * away, knockback.y),
                hitStopFrames = 5,
                hitStunFrames = 16,
                screenShake = 0.15f,
                // Dash i-frames and Than's armour should both answer touch damage.
                defendable = true,
            });
        }
    }
}
