using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// Something in the world the player can walk up to and read, and that is
    /// all: nothing is given, nothing is consumed, and it can be read again.
    ///
    /// It borrows ItemRevealScreen rather than drawing its own panel, because
    /// the card that screen already draws is exactly the shape a note wants,
    /// and two screens fighting over the same blur volume is a bug this project
    /// has already had once.
    ///
    /// The text lives here rather than in the Item Database on purpose. A note
    /// is not an item, it has no id, no stack and no inventory row, and giving
    /// it a database entry only to never grant it would leave a phantom item
    /// that shows up in every validator.
    /// </summary>
    [AddComponentMenu("Weizza/Readable Note")]
    public sealed class ReadableNote : MonoBehaviour
    {
        [TitleGroup("What it says")]
        [Tooltip("The heading on the card.")]
        [SerializeField] private string title = "Inscription";

        [TitleGroup("What it says")]
        [TextArea(2, 6)]
        [Tooltip("The body. Keep it short: this is read standing up, mid walk.")]
        [SerializeField] private string body = "";

        [TitleGroup("What it says")]
        [Tooltip("Optional picture on the card. Leave empty and the note's own " +
                 "sprite is used, so the player sees the thing they just walked up to.")]
        [SerializeField] private Sprite icon;

        [TitleGroup("Reach")]
        [PropertyRange(0.5f, 6f)]
        [Tooltip("How close the player has to stand. Measured flat, so reading " +
                 "from a ledge above still counts.")]
        [SerializeField] private float range = 2f;

        [TitleGroup("Reach")]
        [Tooltip("Optional. Leave empty and a floating 'E' builds itself, the " +
                 "same way a pickup does.")]
        [SerializeField] private GameObject prompt;

        private Transform _player;
        private InteractPrompt _autoPrompt;

        private void Awake()
        {
            if (prompt != null) prompt.SetActive(false);
            else _autoPrompt = gameObject.AddComponent<InteractPrompt>();

            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) _player = pc.transform;
        }

        private void Update()
        {
            var ready = CanRead();
            if (prompt != null && prompt.activeSelf != ready) prompt.SetActive(ready);
            if (_autoPrompt != null) _autoPrompt.SetVisible(ready);

            if (!ready) return;

            var reader = InputReader.Instance;
            if (reader != null && reader.InteractPressed) Read();
        }

        private bool CanRead()
        {
            // The reveal card and a conversation both want the screen and both
            // want the player suspended, so only one runs at a time.
            if (DialogueDirector.Talking) return false;
            if (ItemRevealScreen.Showing) return false;
            if (_player == null) return false;

            var flat = _player.position - transform.position;
            flat.y = 0f;
            return flat.magnitude <= range;
        }

        private void Read()
        {
            if (prompt != null) prompt.SetActive(false);
            if (_autoPrompt != null) _autoPrompt.SetVisible(false);

            var shown = icon;
            if (shown == null)
            {
                var art = GetComponentInChildren<SpriteRenderer>();
                if (art != null) shown = art.sprite;
            }

            ItemRevealScreen.Instance?.Show(new ItemView
            {
                Id = string.Empty,
                DisplayName = title,
                Description = body,
                Icon = shown,
            });
        }
    }
}
