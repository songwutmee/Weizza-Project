using Sirenix.OdinInspector;
using StoryKit;
using StoryKit.Defaults;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Registers the StoryKit services that are plain C# classes rather than
    /// scene components. IScreenUI registers itself from ScreenFade.OnEnable,
    /// and IActor registers itself per instance from WeizzaActor.OnEnable, but
    /// IActorService, IInputService, IItemService and IInventoryService have
    /// nothing in the scene to own them, so this is that.
    ///
    /// The item pair reuses StoryKit's own data-side defaults rather than
    /// Weizza writing new ones: DatabaseItemService just reads an
    /// ItemDatabaseAsset, and BlackboardInventory just reads and writes
    /// Blackboard keys. Neither is presentation, which is the half of
    /// StoryKit's Defaults folder this project avoids. The card that actually
    /// draws a pickup is ItemRevealScreen, written here.
    /// </summary>
    [AddComponentMenu("Weizza/StoryKit Bootstrap")]
    [DefaultExecutionOrder(-500)]
    public sealed class StoryKitBootstrap : MonoBehaviour
    {
        [BoxGroup("Items")]
        [Tooltip("One entry per item id, name, description and icon. Create > " +
                 "StoryKit > Item Database.")]
        [SerializeField] private ItemDatabaseAsset itemDatabase;

        private static readonly WeizzaActorService ActorService = new();
        private static readonly WeizzaInputLock InputLock = new();
        private static readonly GameKit.BlackboardInventory Inventory = new();

        private void OnEnable()
        {
            StoryServices.Register<IActorService>(ActorService);
            StoryServices.Register<IInputService>(InputLock);
            StoryServices.Register<IInventoryService>(Inventory);

            if (itemDatabase != null)
                StoryServices.Register<IItemService>(new DatabaseItemService(itemDatabase));
        }

        private void OnDisable()
        {
            StoryServices.Register<IActorService>(null);
            StoryServices.Register<IInputService>(null);
            StoryServices.Register<IInventoryService>(null);
            StoryServices.Register<IItemService>(null);
        }
    }
}
