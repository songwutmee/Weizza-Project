using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Flow;
using UnityEngine;

namespace StoryKit.Fsm
{
    /// <summary>
    /// PlayMaker-style transition: event name + optional blackboard guard.
    /// Empty eventName = auto transition, checked whenever the blackboard changes
    /// (used for "hp &lt; 33% -> Phase3" style rules).
    /// </summary>
    [Serializable]
    public sealed class FsmTransition
    {
        public string eventName;
        public string guardKey;
        public VarComparison guardComparison = VarComparison.GreaterOrEqual;
        public float guardValue = 1f;
        public string targetStateId;

        public bool GuardPasses(Blackboard board)
        {
            if (string.IsNullOrEmpty(guardKey)) return true;
            return guardComparison.Evaluate(board.Get<float>(guardKey), guardValue);
        }
    }

    [Serializable]
    public sealed class FsmState
    {
        [SerializeField] private string _id = Guid.NewGuid().ToString("N");
        public string title = "State";
        public Vector2 position;

        /// <summary>Flow nodes executed sequentially when entering / exiting the state.</summary>
        [SerializeReference] public List<StoryNode> onEnter = new();
        [SerializeReference] public List<StoryNode> onExit = new();

        public List<FsmTransition> transitions = new();

        /// <summary>Optional: a flow graph played while in this state (e.g. boss intro cutscene).
        /// Finishing it raises event "Fsm.FlowDone" on this machine.</summary>
        public FlowGraphAsset flowGraph;

        /// <summary>Optional: nested state machine active while in this state.</summary>
        public StateMachineAsset subMachine;

        public string Id => _id;

        /// <summary>Used by the asset to repair blank or copied ids.</summary>
        public void AssignNewId() => _id = Guid.NewGuid().ToString("N");
    }


}
