using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Decides which enemies are allowed to attack right now, and where the
    /// rest of them stand while they wait.
    ///
    /// Five enemies that all attack the moment they are in range is not a hard
    /// fight, it is an unreadable one: the player dies in two seconds and
    /// correctly concludes the game cheated. Every brawler worth copying
    /// solves this the same way, by handing out a small number of permissions
    /// and making everyone else circle. The fight still looks like five
    /// enemies. It plays like one or two at a time, with four more visibly
    /// waiting their turn, which is threatening in a way that a pile of bodies
    /// all swinging at once never is.
    ///
    /// This is a director, not a brain. It never moves anyone and never picks
    /// a move. It answers two questions: may I attack, and where should I
    /// stand. Each enemy still decides everything else for itself, so removing
    /// this component leaves every enemy working, just less coordinated.
    /// </summary>
    [AddComponentMenu("Weizza/Encounter Director")]
    [DefaultExecutionOrder(-200)]
    public sealed class EncounterDirector : MonoBehaviour
    {
        private static EncounterDirector _instance;

        /// <summary>The director for this scene, or null when there is none.</summary>
        public static EncounterDirector Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        // ---- What this is ----------------------------------------------------

        [PropertyOrder(-100)]
        [Title("What this does", "It decides whose turn it is to attack. Nothing else.",
            TitleAlignments.Left)]
        [InfoBox("Put one in any room that has more than one enemy.\n\n" +
                 "Without it every enemy attacks the moment it is in range, the " +
                 "player dies in two seconds, and it reads as the game cheating " +
                 "rather than as a hard fight. With it the room still looks like " +
                 "five enemies and plays like one at a time, with four visibly " +
                 "circling and waiting.\n\n" +
                 "It never moves anyone and never picks a move. Enemies ask it " +
                 "two questions: may I attack, and where should I stand.")]
        [ShowInInspector, DisplayAsString(false), HideLabel]
        [PropertySpace(0f, 6f)]
        private string Blurb => "";

        [PropertyOrder(-90)]
        [HorizontalGroup("Presets", Title = "Start from a preset, then tune")]
        [Button("Tutorial fight", ButtonSizes.Large), GUIColor(0.55f, 0.82f, 0.6f)]
        private void PresetTutorial() => Apply("Tutorial fight", 1, 1f, 0.6f, 1f);

        [PropertyOrder(-90)]
        [HorizontalGroup("Presets")]
        [Button("Normal crowd", ButtonSizes.Large), GUIColor(0.6f, 0.72f, 0.95f)]
        private void PresetNormal() => Apply("Normal crowd", 2, 0.8f, 0.45f, 3f);

        [PropertyOrder(-90)]
        [HorizontalGroup("Presets")]
        [Button("Pack hunt", ButtonSizes.Large), GUIColor(0.95f, 0.7f, 0.45f)]
        private void PresetPack() => Apply("Pack hunt", 2, 0.5f, 0.3f, 5f);

        [PropertyOrder(-89)]
        [ShowInInspector, DisplayAsString, HideLabel]
        [GUIColor(0.65f, 0.85f, 0.7f)]
        [PropertySpace(0f, 8f)]
        private string PresetInUse => string.IsNullOrEmpty(_preset)
            ? "Hand tuned, no preset applied"
            : $"Using the {_preset} preset. The numbers below changed.";

        [SerializeField, HideInInspector] private string _preset;

        private void Apply(string label, int attackers, float grace, float gap, float behind)
        {
            maxAttackers = attackers;
            graceAfterPlayerHit = grace;
            minGapBetweenAttacks = gap;
            behindBonus = behind;
            _preset = label;

#if UNITY_EDITOR
            // Without this the change is visible in the inspector and then
            // thrown away on the next save, which looks exactly like the
            // button having done nothing at all.
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // ---- Pressure --------------------------------------------------------

        [BoxGroup("Pressure", centerLabel: true)]
        [InfoBox("One is the right answer for a tutorial fight and for most of " +
                 "a first act. Two starts to feel like a real crowd. Three is " +
                 "already more than most players can read, whatever the enemy " +
                 "count on screen is.")]
        [PropertyRange(1, 4)]
        [SerializeField] private int maxAttackers = 1;

        [BoxGroup("Pressure")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Seconds of quiet after the player is hit before anyone else " +
                 "may start. This one number is most of the difference between " +
                 "a hard fight and an unfair one, because without it a player " +
                 "who gets hit once gets hit until they die.")]
        [SerializeField] private float graceAfterPlayerHit = 0.8f;

        [BoxGroup("Pressure")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Minimum gap between two enemies being allowed to start. Stops " +
                 "two telegraphs overlapping, which is what makes a crowd " +
                 "unreadable even when only two of them are swinging.")]
        [SerializeField] private float minGapBetweenAttacks = 0.45f;

        [BoxGroup("Pressure")]
        [PropertyRange(1f, 10f)]
        [Tooltip("An enemy that holds permission this long without landing " +
                 "anything gives it back, so one stuck enemy cannot freeze the " +
                 "whole encounter.")]
        [SerializeField] private float claimTimeout = 4f;

        [BoxGroup("Tactics", centerLabel: true)]
        [InfoBox("Turns are not first come first served. Every enemy that wants " +
                 "to attack asks, and the director picks one. That single choice " +
                 "is where the fight stops looking like a scramble and starts " +
                 "looking like the pack is working you.")]
        [PropertyRange(0f, 6f)]
        [Tooltip("How much the director favours whoever is standing on the " +
                 "player's blind side. High values mean the pack keeps trying " +
                 "to come from behind, which is what teaches a player to stop " +
                 "tunnel visioning on the one in front.")]
        [SerializeField] private float behindBonus = 3f;

        [BoxGroup("Tactics")]
        [PropertyRange(0f, 1f)]
        [Tooltip("How much closer enemies are favoured. Zero picks purely on " +
                 "position, which lets a distant enemy win a turn it cannot " +
                 "act on and waste it.")]
        [SerializeField] private float closenessWeight = 0.25f;

        // ---- Standing room ---------------------------------------------------

        [BoxGroup("Standing room", centerLabel: true)]
        [InfoBox("Waiting enemies claim a spot around the player instead of " +
                 "piling into the same one. Spots alternate sides and step " +
                 "outwards, so a crowd reads as a gang closing in from both " +
                 "directions rather than as a single file line.")]
        [PropertyRange(2, 10)]
        [SerializeField] private int slotCount = 6;

        [BoxGroup("Standing room")]
        [PropertyRange(1f, 6f)]
        [Tooltip("How far from the player the first waiting spot sits.")]
        [SerializeField] private float slotRadius = 2.6f;

        [BoxGroup("Standing room")]
        [PropertyRange(0.4f, 3f)]
        [Tooltip("Gap between one waiting spot and the next one out on the " +
                 "same side. Too tight and the crowd reads as one mass, too " +
                 "wide and the ones at the back look uninterested.")]
        [SerializeField] private float slotSpacing = 1.1f;

        // ---- Live ------------------------------------------------------------

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ProgressBar(0, nameof(maxAttackers), ColorGetter = nameof(SlotColour))]
        [LabelText("Turns in use")]
        private int TurnsUsed => _attackers.Count;

        [FoldoutGroup("Live")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Attacking now")]
        private string Attacking => _attackers.Count == 0
            ? "nobody"
            : string.Join(", ", _attackers.ConvertAll(e => e == null ? "?" : e.name));

        [FoldoutGroup("Live")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Circling")]
        private string Waiting
        {
            get
            {
                var waiting = Mathf.Max(0, _slots.Count - _attackers.Count);
                return waiting == 0 ? "nobody" : $"{waiting} holding position";
            }
        }

        [FoldoutGroup("Live")]
        [ShowInInspector, ProgressBar(0, 1, ColorGetter = nameof(GraceColour))]
        [LabelText("Player grace")]
        [Tooltip("Full means nobody is allowed to start an attack yet, because " +
                 "the player was just hit.")]
        private float GraceLeft => graceAfterPlayerHit <= 0f
            ? 0f
            : Mathf.Clamp01((_quietUntil - Time.time) / graceAfterPlayerHit);

        [FoldoutGroup("Live")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Blind side")]
        [Tooltip("Which way the player is looking, and therefore which side the " +
                 "director is favouring for the next turn.")]
        private string BlindSide => _playerMotor == null
            ? "no player"
            : _playerMotor.Facing == Facing.Right ? "attacks favoured from the left"
                                                  : "attacks favoured from the right";

        private Color SlotColour() => _attackers.Count >= maxAttackers
            ? new Color(0.9f, 0.5f, 0.35f)
            : new Color(0.45f, 0.75f, 0.95f);

        private Color GraceColour() => new(0.9f, 0.45f, 0.45f);

        private readonly List<EnemyController> _attackers = new(4);
        private readonly Dictionary<EnemyController, int> _slots = new();
        private readonly Dictionary<EnemyController, float> _claimedAt = new();

        private readonly List<EnemyController> _asking = new(8);
        private readonly Dictionary<EnemyController, float> _askedFrom = new();

        private Transform _player;
        private HitReactor _playerReactor;
        private CharacterMotor3D _playerMotor;
        private float _quietUntil;
        private float _nextGrantAt;

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void OnEnable()
        {
            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            _player = player.transform;
            _playerMotor = player.GetComponent<CharacterMotor3D>();
            _playerReactor = player.GetComponent<HitReactor>();
            if (_playerReactor != null) _playerReactor.Staggered += OnPlayerHit;
        }

        private void OnDisable()
        {
            if (_playerReactor != null) _playerReactor.Staggered -= OnPlayerHit;
        }

        private void Update()
        {
            // An attacker that died, was disabled, or has been sitting on
            // permission too long gives it back on its own. Relying on every
            // path through the enemy code to call Release is how an encounter
            // ends up permanently frozen after one unlucky death.
            for (var i = _attackers.Count - 1; i >= 0; i--)
            {
                var attacker = _attackers[i];
                var expired = _claimedAt.TryGetValue(attacker, out var since) &&
                              Time.time - since > claimTimeout;

                if (attacker == null || !attacker.isActiveAndEnabled || !attacker.IsAlive || expired)
                {
                    _attackers.RemoveAt(i);
                    if (attacker != null) _claimedAt.Remove(attacker);
                }
            }

            GrantTurn();
            _asking.Clear();
            _askedFrom.Clear();
        }

        /// <summary>
        /// Picks one of the enemies asking for a turn, rather than handing it
        /// to whoever happened to ask first.
        ///
        /// First come first served is not a tactic, it is a race, and it gives
        /// the turn to whichever enemy's decision timer happened to land on
        /// this frame. Choosing on position is the whole difference: the pack
        /// keeps sending the one you are not looking at.
        /// </summary>
        private void GrantTurn()
        {
            if (_asking.Count == 0) return;
            if (Time.time < _quietUntil) return;
            if (Time.time < _nextGrantAt) return;
            if (_attackers.Count >= maxAttackers) return;

            EnemyController best = null;
            var bestScore = float.MinValue;

            foreach (var candidate in _asking)
            {
                if (candidate == null || !candidate.IsAlive) continue;

                var score = -_askedFrom[candidate] * closenessWeight;
                if (IsOnBlindSide(candidate)) score += behindBonus;

                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            if (best == null) return;

            _attackers.Add(best);
            _claimedAt[best] = Time.time;
            _nextGrantAt = Time.time + minGapBetweenAttacks;
        }

        /// <summary>True when this enemy is standing behind the player.</summary>
        private bool IsOnBlindSide(EnemyController enemy)
        {
            if (_player == null || _playerMotor == null) return false;

            var side = enemy.transform.position.x - _player.position.x;
            if (Mathf.Abs(side) < 0.2f) return false;

            return Mathf.Sign(side) != _playerMotor.Facing.Sign();
        }

        private void OnPlayerHit(DamageInfo info) => _quietUntil = Time.time + graceAfterPlayerHit;

        // ---- Permission ------------------------------------------------------

        /// <summary>
        /// Puts this enemy forward for a turn. The answer arrives later, as
        /// Holds going true, because the director compares everyone asking
        /// before it picks. Safe to call every decision tick.
        /// </summary>
        public void RequestTurn(EnemyController enemy, float distance)
        {
            if (enemy == null || _attackers.Contains(enemy)) return;
            if (_askedFrom.ContainsKey(enemy)) return;

            _asking.Add(enemy);
            _askedFrom[enemy] = distance;
        }

        public bool Holds(EnemyController enemy) => enemy != null && _attackers.Contains(enemy);

        public void Release(EnemyController enemy)
        {
            if (enemy == null) return;
            _attackers.Remove(enemy);
            _claimedAt.Remove(enemy);
        }

        // ---- Standing room ---------------------------------------------------

        /// <summary>
        /// Where this enemy should wait. Each one keeps its spot until it
        /// leaves the fight, so a crowd does not shuffle every time the player
        /// takes a step.
        /// </summary>
        public Vector3 StandingSpot(EnemyController enemy)
        {
            if (_player == null || enemy == null) return enemy != null ? enemy.transform.position : Vector3.zero;

            if (!_slots.TryGetValue(enemy, out var slot))
            {
                slot = ClaimNearestFreeSlot(enemy);
                _slots[enemy] = slot;
            }

            return _player.position + SlotOffset(slot);
        }

        public void LeaveSlot(EnemyController enemy)
        {
            if (enemy != null) _slots.Remove(enemy);
        }

        private int ClaimNearestFreeSlot(EnemyController enemy)
        {
            var best = 0;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < slotCount; i++)
            {
                if (_slots.ContainsValue(i)) continue;

                var world = _player.position + SlotOffset(i);
                var distance = (world - enemy.transform.position).sqrMagnitude;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Alternating sides, stepping further out each time round. Side first
        /// so that with only two enemies they stand on opposite sides of the
        /// player rather than both queued up on the same one.
        /// </summary>
        private Vector3 SlotOffset(int slot)
        {
            var side = slot % 2 == 0 ? 1f : -1f;
            var tier = slot / 2;

            return new Vector3(side * (slotRadius + tier * slotSpacing), 0f, 0f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var centre = _player != null ? _player.position : transform.position;

            for (var i = 0; i < slotCount; i++)
            {
                var taken = _slots.ContainsValue(i);
                Gizmos.color = taken ? new Color(1f, 0.5f, 0.3f, 0.9f) : new Color(0.4f, 0.8f, 1f, 0.4f);
                Gizmos.DrawWireSphere(centre + SlotOffset(i), 0.28f);
            }
        }
#endif
    }
}
