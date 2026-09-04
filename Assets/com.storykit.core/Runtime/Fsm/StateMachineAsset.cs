using System.Collections.Generic;
using StoryKit.Flow;
using UnityEngine;

namespace StoryKit.Fsm
{
    /// <summary>
    /// A state machine stored as an asset. Unity requires a ScriptableObject to
    /// live in a file matching its class name, so this type has its own file.
    /// </summary>
    [CreateAssetMenu(fileName = "StateMachine", menuName = "StoryKit/State Machine")]
    public class StateMachineAsset : ScriptableObject
    {
        [SerializeField] private string _initialStateId;
        [SerializeField] private List<FsmState> _states = new();

        /// <summary>Transitions that can fire from any state ("Died", "Stunned").</summary>
        [SerializeField] private List<FsmTransition> _globalTransitions = new();

        [SerializeField] private int _schemaVersion = 1;

        public string InitialStateId { get => _initialStateId; set => _initialStateId = value; }
        public List<FsmState> States => _states;
        public List<FsmTransition> GlobalTransitions => _globalTransitions;
        public int SchemaVersion => _schemaVersion;

        /// <summary>
        /// The state the machine starts in. You only have to pick one when the
        /// first state in the list is not the one you want, so a machine is
        /// playable the moment you add a state.
        /// </summary>
        public string ResolvedInitialStateId
        {
            get
            {
                if (!string.IsNullOrEmpty(_initialStateId) && FindState(_initialStateId) != null)
                    return _initialStateId;
                for (int i = 0; i < _states.Count; i++)
                    if (_states[i] != null && !string.IsNullOrEmpty(_states[i].Id))
                        return _states[i].Id;
                return null;
            }
        }

        /// <summary>True when the start state was worked out rather than picked.</summary>
        public bool InitialIsAutomatic =>
            string.IsNullOrEmpty(_initialStateId) || FindState(_initialStateId) == null;

        /// <summary>
        /// Repairs blank or duplicated state ids. Adding an element with the
        /// default inspector list copies the element above it, id included, and
        /// two states sharing an id makes every transition land on the wrong one.
        /// </summary>
        public bool EnsureIds()
        {
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            bool changed = false;

            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                if (state == null) continue;

                if (string.IsNullOrEmpty(state.Id) || !seen.Add(state.Id))
                {
                    state.AssignNewId();
                    seen.Add(state.Id);
                    changed = true;
                }

                if (string.IsNullOrEmpty(state.title))
                {
                    state.title = $"State {i + 1}";
                    changed = true;
                }
            }

            return changed;
        }

        private void OnValidate() => EnsureIds();

        public FsmState FindState(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _states.Count; i++)
                if (_states[i] != null && _states[i].Id == id)
                    return _states[i];
            return null;
        }

        // Code-first authoring
        public FsmState AddState(string title)
        {
            // No need to pin the first one as the start: ResolvedInitialStateId
            // already falls back to the first state in the list.
            var state = new FsmState { title = title };
            _states.Add(state);
            return state;
        }

        public void AddTransition(FsmState from, FsmState to, string eventName,
            string guardKey = null, VarComparison cmp = VarComparison.GreaterOrEqual, float guardValue = 1f)
        {
            from.transitions.Add(new FsmTransition
            {
                eventName = eventName,
                targetStateId = to.Id,
                guardKey = guardKey,
                guardComparison = cmp,
                guardValue = guardValue,
            });
        }
    }
}
