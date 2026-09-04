using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Mirrors a gameplay value into the blackboard every time it changes.
    /// Your health script calls Push(); FSM guards and BT conditions react.
    /// Example: boss health → key "hp" → auto phase transitions with zero coupling.
    /// </summary>
    [AddComponentMenu("StoryKit/Blackboard Value Pusher")]
    public sealed class BlackboardValuePusher : MonoBehaviour
    {
        [Tooltip("Blackboard key, e.g. 'hp' or 'player.stamina'.")]
        [SerializeField] private string _key = "hp";

        [Tooltip("Leave empty to write to the Global board. When set, writes to the "
               + "Instance board of a StateMachineRunner/BehaviorTreeRunner on this object.")]
        [SerializeField] private bool _useLocalRunnerBoard = true;

        private Blackboard ResolveBoard()
        {
            if (_useLocalRunnerBoard)
            {
                var fsm = GetComponent<Fsm.StateMachineRunner>();
                if (fsm != null && fsm.Board != null) return fsm.Board;
                var bt = GetComponent<Bt.BehaviorTreeRunner>();
                if (bt != null && bt.Board != null) return bt.Board;
            }
            return StoryState.Global;
        }

        /// <summary>Call from gameplay code: pusher.Push(currentHp / maxHp);</summary>
        public void Push(float value) => ResolveBoard().Set(_key, value, writeThrough: false);
        public void PushBool(bool value) => ResolveBoard().Set(_key, value ? 1f : 0f, writeThrough: false);
    }
}
