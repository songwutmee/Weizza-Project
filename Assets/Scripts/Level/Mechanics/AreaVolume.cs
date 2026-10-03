using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A named region of the world. Entering one shows its name as a banner
    /// the first time, sets the current area on the blackboard for the save
    /// and for flow graphs, and raises an event music and ambience can follow.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Area Volume")]
    public sealed class AreaVolume : BodyZone
    {
        public const string EnteredEvent = "World.AreaEntered";
        public const string CurrentKey = "world.area";

        [BoxGroup("Area")]
        [Tooltip("Stable id the save and graphs use. Never renamed.")]
        [SerializeField] private string areaId = "forest_edge";

        [BoxGroup("Area")]
        [Tooltip("Shown in the banner. A localization key once the sheet has one; plain text until then.")]
        [SerializeField] private string displayName = "Forest Edge";

        [BoxGroup("Area")]
        [ToggleLeft]
        [Tooltip("Show the banner every visit. Off shows it on the first visit only.")]
        [SerializeField] private bool bannerEveryTime;

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
            var box = GetComponent<BoxCollider>();
            box.size = new Vector3(30f, 30f, 4f);
            box.center = new Vector3(0f, 10f, 0f);
        }

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (!PlayerLocator.IsPlayer(body)) return;

            var visitedKey = "world.visited." + areaId;
            var firstTime = !StoryState.Global.Get(visitedKey, false);
            StoryState.Global.Set(visitedKey, true);
            StoryState.Global.Set(CurrentKey, areaId);
            StoryEvents.Raise(EnteredEvent, areaId);

            if (!firstTime && !bannerEveryTime) return;

            var localization = StoryServices.Get<ILocalizationService>();
            var text = localization != null ? localization.Resolve(displayName) : displayName;
            HudScreen.Instance?.Banner(text);
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(1f, 1f, 1f, 0.05f);
#endif
    }
}
