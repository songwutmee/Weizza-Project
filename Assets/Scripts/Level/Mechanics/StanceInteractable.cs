using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using Weizza.Narrative;

namespace Weizza
{
    /// <summary>
    /// Something only one school can interact with. "Talk to animals" on the
    /// Se + list is the first use: in Se the creature talks, in any other
    /// stance it shies away.
    ///
    /// Written generally because the same shape keeps coming up: a lock only
    /// In can read, a forge only Than can work. The stance answer and the
    /// refusal are both just a conversation, so writers decide what each
    /// school hears without a programmer.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Stance Interactable")]
    public sealed class StanceInteractable : MonoBehaviour
    {
        [BoxGroup("Who")]
        [EnumToggleButtons]
        [SerializeField] private StanceId requiredStance = StanceId.Se;

        [BoxGroup("Who")]
        [PropertyRange(0.5f, 5f)]
        [SerializeField] private float range = 1.8f;

        [BoxGroup("What happens")]
        [Tooltip("Played in the right stance. The animal talking.")]
        [SerializeField] private Conversation conversation;

        [BoxGroup("What happens")]
        [Tooltip("Played in any other stance. The animal backing away, or a " +
                 "thought from the protagonist. Optional.")]
        [SerializeField] private Conversation refusal;

        [BoxGroup("What happens")]
        [Tooltip("Optional extra, raised after the right-stance conversation " +
                 "is started: open a path, give an item.")]
        [SerializeField] private UnityEvent onSuccess = new();

        [BoxGroup("What happens")]
        [ToggleLeft]
        [Tooltip("Only works the first time in the right stance.")]
        [SerializeField] private bool once;

        private InteractPrompt _prompt;
        private bool _done;

        private void Awake() => _prompt = gameObject.AddComponent<InteractPrompt>();

        private void Update()
        {
            var player = PlayerLocator.Player;
            var ready = !_done && player != null && player.CanAct &&
                        !DialogueDirector.Talking && !ItemRevealScreen.Showing &&
                        FlatDistance(player.transform.position) <= range;

            _prompt.SetVisible(ready);
            if (!ready) return;

            var input = InputReader.Instance;
            if (input == null || !input.InteractPressed) return;

            if (player.Stance == requiredStance)
            {
                if (conversation != null) DialogueDirector.Instance?.Play(conversation);
                onSuccess.Invoke();
                if (once) _done = true;
            }
            else if (refusal != null)
            {
                DialogueDirector.Instance?.Play(refusal);
            }
        }

        private float FlatDistance(Vector3 other)
        {
            var flat = other - transform.position;
            flat.y = 0f;
            flat.z = 0f;
            return flat.magnitude;
        }
    }
}
