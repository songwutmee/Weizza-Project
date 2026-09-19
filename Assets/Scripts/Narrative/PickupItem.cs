using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// Something in the world the player can walk up to and take. On interact:
    /// give the item through IInventoryService, look its name and description
    /// up through IItemService, and hand both to ItemRevealScreen. The item
    /// itself never draws its own card, the same separation DialogueDirector
    /// keeps between running a conversation and drawing one.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PickupItem : MonoBehaviour
    {
        [TitleGroup("Item")]
        [Required("Nothing to give without an id in the Item Database.")]
        [Tooltip("Must match an entry's id in the ItemDatabaseAsset the scene " +
                 "registers with IItemService.")]
        [SerializeField] private string itemId;

        [TitleGroup("Item")]
        [MinValue(1)]
        [SerializeField] private int amount = 1;

        [TitleGroup("Item")]
        [PropertyRange(0.5f, 6f)]
        [Tooltip("How close the player has to stand. Measured flat, so standing " +
                 "on a ledge above still counts.")]
        [SerializeField] private float range = 1.8f;

        [TitleGroup("Item")]
        [Tooltip("Taken out of the world once collected. Off leaves it there " +
                 "to be picked up again, for a refillable pickup.")]
        [SerializeField] private bool consumeOnPickup = true;

        [TitleGroup("Item")]
        [Tooltip("Optional. Leave empty and a floating 'E' builds itself, the " +
                 "same self-building approach InteractableOutline uses. Assign " +
                 "a real GameObject only to replace that with hand made art.")]
        [SerializeField] private GameObject prompt;

        private Transform _player;
        private InteractPrompt _autoPrompt;
        private bool _taken;

        private void Awake()
        {
            if (prompt != null)
            {
                prompt.SetActive(false);
            }
            else
            {
                // A pickup with no prompt assigned used to be silent about
                // being interactable at all beyond the outline, and the
                // outline alone was found not to be enough either. This is
                // the fallback that makes every pickup readable out of the
                // box with zero authoring.
                _autoPrompt = gameObject.AddComponent<InteractPrompt>();
            }

            var pc = FindFirstObjectByType<Weizza.PlayerController>();
            if (pc != null) _player = pc.transform;
        }

        private void Update()
        {
            if (_taken) return;

            var ready = CanTake();
            if (prompt != null && prompt.activeSelf != ready) prompt.SetActive(ready);
            _autoPrompt?.SetVisible(ready);

            if (!ready) return;

            var reader = InputReader.Instance;
            if (reader != null && reader.InteractPressed) Take();
        }

        private bool CanTake()
        {
            // Item reveal and dialogue both want the screen and both want the
            // player suspended, so one owning the other at a time is the whole
            // rule, not a special case either system needs to know about.
            if (DialogueDirector.Talking) return false;
            if (ItemRevealScreen.Showing) return false;
            if (_player == null) return false;

            var flat = _player.position - transform.position;
            flat.y = 0f;
            return flat.magnitude <= range;
        }

        private void Take()
        {
            _taken = true;
            if (prompt != null) prompt.SetActive(false);
            _autoPrompt?.SetVisible(false);

            var inventory = StoryServices.Get<IInventoryService>();
            inventory?.GiveItem(itemId, amount);

            ItemView view = default;
            var haveView = StoryServices.Get<IItemService>()?.TryGet(itemId, out view) ?? false;

            if (!haveView)
            {
                view.Id = itemId;
                view.DisplayName = itemId;
                view.Description = string.Empty;
            }

            ItemRevealScreen.Instance?.Show(view);

            if (consumeOnPickup) gameObject.SetActive(false);
        }
    }
}
