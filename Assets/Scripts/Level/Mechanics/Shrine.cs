using PrimeTween;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using Weizza.Narrative;

namespace Weizza
{
    /// <summary>
    /// A wayside shrine. Interact to rest: full health, poison cleared, this
    /// becomes the respawn point, and the game saves.
    ///
    /// Act 0 needs one: the second meeting with Soka happens at a shrine. The
    /// rest itself is mechanical and lives here; whatever is said there is a
    /// conversation hung on the event this raises, not code.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Shrine")]
    [RequireComponent(typeof(Checkpoint))]
    public sealed class Shrine : MonoBehaviour
    {
        public const string RestedEvent = "World.ShrineRested";

        [BoxGroup("Shrine")]
        [PropertyRange(0.5f, 4f)]
        [SerializeField] private float range = 1.6f;

        [BoxGroup("Shrine")]
        [Tooltip("Pulses brighter while the player is in range. Usually the glow sprite.")]
        [SerializeField] private SpriteRenderer glow;

        [BoxGroup("Shrine")]
        [Tooltip("Optional. Played on the first rest only, such as Soka at the shrine.")]
        [SerializeField] private Conversation firstRestConversation;

        [BoxGroup("Shrine")]
        [ToggleLeft]
        [SerializeField] private bool autosave = true;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public int TimesRested { get; private set; }

        private Checkpoint _checkpoint;
        private InteractPrompt _prompt;
        private Color _glowBase = Color.white;

        private void Awake()
        {
            _checkpoint = GetComponent<Checkpoint>();
            _prompt = gameObject.AddComponent<InteractPrompt>();
            if (glow != null) _glowBase = glow.color;
        }

        private void Update()
        {
            var player = PlayerLocator.Player;
            var ready = player != null && player.CanAct && !DialogueDirector.Talking && !ItemRevealScreen.Showing &&
                        Mathf.Abs(player.transform.position.x - transform.position.x) <= range &&
                        Mathf.Abs(player.transform.position.y - transform.position.y) <= 2f;

            _prompt.SetVisible(ready);

            if (glow != null)
            {
                var target = ready ? 1f : 0.55f;
                var c = _glowBase;
                c.a = _glowBase.a * Mathf.Lerp(target, target * 0.75f, Mathf.PingPong(Time.time, 1f));
                glow.color = c;
            }

            var input = InputReader.Instance;
            if (ready && input != null && input.InteractPressed) Rest(player);
        }

        public void Rest(PlayerController player)
        {
            if (player == null) return;

            player.Vitals?.SetHealth(player.Vitals.Max);
            player.GetComponent<EffectReceiver>()?.Cleanse();
            RespawnDirector.Instance?.Claim(_checkpoint);

            HitSpark.Play(transform.position + Vector3.up * 1.2f, Vector2.up, 1.2f, new Color(0.5f, 0.88f, 0.69f));
            if (glow != null) Tween.PunchScale(glow.transform, Vector3.one * 0.3f, 0.4f);

            TimesRested++;
            StoryEvents.Raise(RestedEvent, this.StableKey());

            if (TimesRested == 1 && firstRestConversation != null)
                DialogueDirector.Instance?.Play(firstRestConversation);

            if (autosave) SaveSystem.Instance?.Save();
        }
    }
}
