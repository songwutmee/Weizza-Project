using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A stretch of level that offers a hint. Walk in and the hint shows (after a wait, if one
    /// is set: a hint that waits only appears for a player who is lingering, so someone who
    /// already knows what to do never sees it). It goes when the player leaves, or the moment
    /// they do the thing it asked for.
    ///
    /// Put it on a Box Collider set as a trigger.
    /// </summary>
    [AddComponentMenu("Weizza/UI/Hint Zone")]
    [RequireComponent(typeof(Collider))]
    public sealed class HintZone : MonoBehaviour
    {
        public enum Done { Never, Jump, Dash, Attack, Interact, SwitchStance }

        [TextArea(1, 3), Required]
        [SerializeField] private string text = "Press Space to jump.";

        [Tooltip("Seconds the player must stay in the zone first. 0 shows it at once; 6 or so offers it only to someone stuck.")]
        [SerializeField] private float waitSeconds;

        [Tooltip("The hint goes away the moment the player does this.")]
        [SerializeField] private Done doneWhen = Done.Never;

        [ToggleLeft, Tooltip("Once done, never again, remembered in the save.")]
        [SerializeField] private bool onlyOnce = true;

        [ShowIf(nameof(onlyOnce)), Tooltip("Save key. Leave empty to use this object's name and position.")]
        [SerializeField] private string id = "";

        private bool _inside;
        private float _enteredAt;
        private bool _showing;

        private string Key => "hint.done." + (string.IsNullOrEmpty(id) ? $"{name}@{Mathf.RoundToInt(transform.position.x)}" : id);

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            if (onlyOnce && StoryState.Global.Get(Key, false)) return;
            _inside = true;
            _enteredAt = Time.time;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            _inside = false;
            Hide();
        }

        private void OnDisable() => Hide();

        private void Update()
        {
            if (!_inside) return;

            // Time spent in a cutscene is not time spent stuck: the wait only counts while the player can move.
            if (StoryControl.MovementLocked)
            {
                if (!_showing) _enteredAt = Time.time;
                return;
            }

            if (!_showing && Time.time - _enteredAt >= waitSeconds)
            {
                _showing = true;
                HintSystem.Instance?.Show(text, this);
            }

            if (_showing && DidIt())
            {
                if (onlyOnce) StoryState.Global.Set(Key, true);
                _inside = false;
                Hide();
            }
        }

        private void Hide()
        {
            if (!_showing) return;
            _showing = false;
            HintSystem.Instance?.Hide(this);
        }

        private bool DidIt()
        {
            var input = InputReader.Instance;
            if (input == null) return false;
            return doneWhen switch
            {
                Done.Jump => input.JumpPressed,
                Done.Dash => input.DashPressed,
                Done.Attack => input.LightPressed || input.HeavyPressed,
                Done.Interact => input.InteractPressed,
                Done.SwitchStance => input.StanceRequest(StanceId.Normal) != null,
                _ => false,
            };
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(0.55f, 0.85f, 1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
