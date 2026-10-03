using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.Events;

namespace Weizza
{
    /// <summary>
    /// When the player walks in or out, raise a StoryKit event, switch some
    /// targets, and fire a UnityEvent. The generic glue for "something happens
    /// here" that does not deserve its own script.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Trigger Relay")]
    public sealed class TriggerRelay : BodyZone
    {
        [BoxGroup("Enter")]
        [Tooltip("StoryKit event raised on enter. Empty raises nothing.")]
        [SerializeField] private string enterEvent = "";

        [BoxGroup("Enter")]
        [SerializeField] private ActivationTargets onEnterTargets = new();

        [BoxGroup("Enter")]
        [SerializeField] private UnityEvent onEnter = new();

        [BoxGroup("Exit")]
        [SerializeField] private string exitEvent = "";

        [BoxGroup("Exit")]
        [SerializeField] private UnityEvent onExit = new();

        [BoxGroup("Rule")]
        [ToggleLeft]
        [Tooltip("Fires only the first time.")]
        [SerializeField] private bool once;

        private bool _fired;

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
        }

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (!PlayerLocator.IsPlayer(body) || (once && _fired)) return;
            _fired = true;
            if (!string.IsNullOrEmpty(enterEvent)) StoryEvents.Raise(enterEvent, name);
            onEnterTargets.Send(true, body.gameObject);
            onEnter.Invoke();
        }

        protected override void OnExited(CharacterMotor3D body)
        {
            if (!PlayerLocator.IsPlayer(body)) return;
            if (!string.IsNullOrEmpty(exitEvent)) StoryEvents.Raise(exitEvent, name);
            onExit.Invoke();
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.6f, 0.5f, 0.72f, 0.15f);
#endif
    }
}
