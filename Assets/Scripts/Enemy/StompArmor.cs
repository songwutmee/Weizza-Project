using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A shell on an enemy that shrugs off every hit until Than lands on it
    /// from a jump. "Jump to break a monster's shell or armour" on the Than +
    /// list.
    ///
    /// While the shell is on, the enemy holds the HyperArmor tag (hits land as
    /// chip, nothing flinches) and takes a fraction of damage. One heavy stomp
    /// on its back cracks it, and from then on it is an ordinary enemy. That
    /// makes a Bark Beetle a puzzle with one answer rather than a damage
    /// sponge.
    /// </summary>
    [AddComponentMenu("Weizza/Stomp Armor")]
    [RequireComponent(typeof(Vitals))]
    public sealed class StompArmor : MonoBehaviour
    {
        [BoxGroup("Shell")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Fraction of damage that still gets through the shell.")]
        [SerializeField] private float damageThroughShell = 0.15f;

        [BoxGroup("Shell")]
        [PropertyRange(0f, 20f)]
        [Tooltip("Landing speed a Heavy body needs to crack it.")]
        [SerializeField] private float minImpact = 6f;

        [BoxGroup("Shell")]
        [PropertyRange(0f, 20f)]
        [Tooltip("Damage the cracking stomp deals on top of breaking the shell.")]
        [SerializeField] private float stompDamage = 12f;

        [BoxGroup("Shell")]
        [PropertyRange(0f, 20f)]
        [Tooltip("Upward bounce given to the stomper, so they do not land in the enemy's face.")]
        [SerializeField] private float stomperBounce = 8f;

        [BoxGroup("Shell")]
        [Tooltip("Hidden when the shell breaks. The shell art, if it is a separate sprite.")]
        [SerializeField] private GameObject shellVisual;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Shelled { get; private set; } = true;

        private Vitals _vitals;
        private System.IDisposable _armor;
        private float _baseMultiplier = 1f;

        private void Awake() => _vitals = GetComponent<Vitals>();

        private void OnEnable()
        {
            CharacterMotor3D.LandedOn += OnLanded;
            if (Shelled) Equip();
        }

        private void OnDisable()
        {
            CharacterMotor3D.LandedOn -= OnLanded;
            _armor?.Dispose();
            _armor = null;
        }

        private void Equip()
        {
            _armor ??= this.Tags().Add(GameTag.HyperArmor);
            _vitals.RefreshTags();
            _baseMultiplier = _vitals.DamageMultiplier;
            _vitals.DamageMultiplier = _baseMultiplier * damageThroughShell;
        }

        private void OnLanded(CharacterMotor3D body, Collider ground, float impact)
        {
            if (!Shelled || ground == null || !ground.transform.IsChildOf(transform)) return;
            if (!LevelPiece.IsHeavy(body) || impact < minImpact) return;
            Crack(body);
        }

        /// <summary>
        /// The player's capsule and an enemy's pass through each other (the
        /// collision matrix keeps them apart so a pack never shoves the player
        /// around), so a stomp never arrives as a landing. It is checked here
        /// instead: a heavy body falling fast with its feet inside the top of
        /// this enemy.
        /// </summary>
        private void Update()
        {
            if (!Shelled) return;
            var player = PlayerLocator.Player;
            if (player == null || !LevelPiece.IsHeavy(player)) return;
            if (player.Motor.Velocity.y > -minImpact) return;

            var body = GetComponent<CharacterController>();
            if (body == null) return;

            var bounds = body.bounds;
            var feet = player.transform.position;
            var overTop = feet.y <= bounds.max.y + 0.15f && feet.y >= bounds.max.y - 0.5f;
            var aligned = Mathf.Abs(feet.x - bounds.center.x) <= bounds.extents.x + 0.2f &&
                          Mathf.Abs(feet.z - bounds.center.z) < 0.5f;
            if (overTop && aligned) Crack(player.Motor);
        }

        private void Crack(CharacterMotor3D body)
        {
            Shelled = false;
            _armor?.Dispose();
            _armor = null;
            _vitals.DamageMultiplier = _baseMultiplier;
            if (shellVisual != null) shellVisual.SetActive(false);

            _vitals.Receive(new DamageInfo
            {
                amount = stompDamage,
                poiseDamage = 999f,
                source = body.gameObject,
                point = transform.position + Vector3.up,
                knockback = Vector2.zero,
                hitStopFrames = 10,
                hitStunFrames = 40,
                screenShake = 0.4f,
                defendable = false,
            });

            body.Velocity.y = stomperBounce;
            HitStop.Freeze(10);
            CameraFeel.Instance?.Shake(0.45f, Vector2.down);
            HitSpark.Play(transform.position + Vector3.up * 1.2f, Vector2.up, 1.4f, new Color(1f, 0.85f, 0.4f));
        }
    }
}
