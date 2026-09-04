using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Flow;
using UnityEngine;

namespace StoryKit.Fsm
{
    /// <summary>
    /// Runs a StateMachineAsset on a GameObject (boss, door, trap, NPC).
    /// Each runner gets its own Instance blackboard parented to Global.
    /// Local events go through SendEvent(); StoryEvents subscriptions can be
    /// bridged in by listing forwarded event names.
    /// </summary>
    public sealed class StateMachineRunner : MonoBehaviour
    {
        [SerializeField] private StateMachineAsset _asset;
        [SerializeField] private bool _playOnStart = true;

        [Tooltip("Global StoryEvents forwarded into this machine as local events.")]
        [SerializeField] private List<string> _listenGlobalEvents = new();

        /// <summary>Instance blackboard (hp, target, alert...) parented to Global.</summary>
        public Blackboard Board { get; private set; }

        /// <summary>The asset being run, for inspectors and save code.</summary>
        public StateMachineAsset Asset => _asset;

        public string CurrentStateId { get; private set; }
        public FsmState CurrentState => _runtimeAsset != null ? _runtimeAsset.FindState(CurrentStateId) : null;
        public bool IsRunning { get; private set; }

        public event Action<string> StateEntered;
        public event Action<string> StateExited;

        private readonly List<IDisposable> _subscriptions = new();
        private readonly Queue<string> _pendingEvents = new();
        private CancellationTokenSource _lifeCts;
        private FlowRunner _stateFlowRunner;
        private StateMachineRunner _subMachineRunner;
        private bool _transitioning;

        private void Awake()
        {
            Board = new Blackboard(StoryState.Global);
        }

        private void Start()
        {
            if (_playOnStart && _asset != null)
                Play(_asset);
        }

        /// <summary>Per-instance clone of the asset; onEnter/onExit nodes carry
        /// runtime fields, so two bosses must not share one asset instance.</summary>
        private StateMachineAsset _runtimeAsset;

        public void Play(StateMachineAsset asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("[StoryKit] StateMachineRunner.Play called with null asset.", this);
                return;
            }
            if (IsRunning) StopMachine();

            _playOnStart = false;                 // Play() owns startup from here;
                                                  // prevents Start() double-playing
                                                  // runners created via AddComponent.
            _asset = asset;                       // keep the source reference
            _runtimeAsset = Instantiate(asset);   // run on an isolated clone
            _lifeCts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());
            IsRunning = true;

            foreach (string eventName in _listenGlobalEvents)
            {
                string captured = eventName;
                _subscriptions.Add(StoryEvents.Subscribe(captured, _ => SendEvent(captured)));
            }

            Board.KeyChanged += OnBoardChanged;

            string start = _runtimeAsset.ResolvedInitialStateId;
            if (string.IsNullOrEmpty(start))
            {
                Debug.LogWarning($"[StoryKit] State machine '{asset.name}' has no states.", this);
                return;
            }
            EnterState(start).Forget();
        }

        public void StopMachine()
        {
            if (!IsRunning) return;
            IsRunning = false;

            Board.KeyChanged -= OnBoardChanged;
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
            _pendingEvents.Clear();

            CleanupStateExtras();

            try { _lifeCts?.Cancel(); } catch (ObjectDisposedException) { }
            _lifeCts?.Dispose();
            _lifeCts = null;
            CurrentStateId = null;

            if (_runtimeAsset != null)
            {
                Destroy(_runtimeAsset);
                _runtimeAsset = null;
            }
        }

        private void OnDestroy() => StopMachine();

        /// <summary>Sends a local event. Boss takes damage -> SendEvent("Hit") etc.</summary>
        public void SendEvent(string eventName)
        {
            if (!IsRunning || string.IsNullOrEmpty(eventName)) return;

            if (_transitioning)
            {
                _pendingEvents.Enqueue(eventName);
                return;
            }
            TryTransition(eventName);
        }

        private void OnBoardChanged(string key)
        {
            if (!IsRunning || _transitioning) return;
            // Auto transitions (no event name) re-checked when data changes.
            TryTransition(null);
        }

        private void TryTransition(string eventName)
        {
            var state = CurrentState;
            if (state == null) return;

            FsmTransition match = FindMatch(state.transitions, eventName)
                                  ?? FindMatch(_runtimeAsset.GlobalTransitions, eventName);
            if (match != null)
                TransitionTo(match.targetStateId).Forget();
        }

        private FsmTransition FindMatch(List<FsmTransition> transitions, string eventName)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                var t = transitions[i];
                bool eventMatches = string.IsNullOrEmpty(t.eventName)
                    ? eventName == null          // auto transitions only on data change
                    : t.eventName == eventName;
                if (eventMatches && t.GuardPasses(Board) && !string.IsNullOrEmpty(t.targetStateId))
                    return t;
            }
            return null;
        }

        private async UniTaskVoid TransitionTo(string stateId)
        {
            if (_transitioning || !IsRunning) return;
            _transitioning = true;

            try
            {
                await ExitState();
                await EnterStateCore(stateId);
            }
            finally
            {
                _transitioning = false;
            }

            // Drain events raised mid-transition.
            while (_pendingEvents.Count > 0 && IsRunning && !_transitioning)
                TryTransition(_pendingEvents.Dequeue());
        }

        private async UniTaskVoid EnterState(string stateId)
        {
            _transitioning = true;
            try { await EnterStateCore(stateId); }
            finally { _transitioning = false; }
        }

        private async UniTask EnterStateCore(string stateId)
        {
            var state = _runtimeAsset.FindState(stateId);
            if (state == null)
            {
                Debug.LogWarning($"[StoryKit] FSM state '{stateId}' not found in '{_runtimeAsset.name}'.", this);
                return;
            }

            CurrentStateId = stateId;
            StateEntered?.Invoke(stateId);

            await RunActionList(state.onEnter);

            if (state.flowGraph != null)
            {
                _stateFlowRunner = FlowRunner.Rent(gameObject);
                RunStateFlow(state.flowGraph).Forget();
            }

            if (state.subMachine != null)
            {
                _subMachineRunner = gameObject.AddComponent<StateMachineRunner>();
                _subMachineRunner.Play(state.subMachine);
            }

            // Auto transitions may already be satisfied on entry.
            TryTransition(null);
        }

        private async UniTaskVoid RunStateFlow(FlowGraphAsset graph)
        {
            var runner = _stateFlowRunner;
            await runner.PlayAsync(graph, _lifeCts.Token);
            if (IsRunning && runner == _stateFlowRunner)
                SendEvent("Fsm.FlowDone");
        }

        private async UniTask ExitState()
        {
            var state = CurrentState;
            if (state == null) return;

            CleanupStateExtras();
            await RunActionList(state.onExit);
            StateExited?.Invoke(state.Id);
        }

        private void CleanupStateExtras()
        {
            if (_stateFlowRunner != null)
            {
                FlowRunner.ReturnRented(_stateFlowRunner);
                _stateFlowRunner = null;
            }
            if (_subMachineRunner != null)
            {
                _subMachineRunner.StopMachine();
                Destroy(_subMachineRunner);
                _subMachineRunner = null;
            }
        }

        private async UniTask RunActionList(List<StoryNode> actions)
        {
            if (actions == null || actions.Count == 0) return;

            var context = new FlowContext(null, null, Board);
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action == null) continue;

                // A list has no branches, so a task node runs start to finish
                // in place. Without this its work would silently never happen.
                if (action is TaskNode task)
                {
                    try
                    {
                        await task.ExecuteAsync(context, _lifeCts.Token);
                        await task.OnRunningAsync(context, _lifeCts.Token);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { Debug.LogException(ex, this); }
                    finally
                    {
                        try { await task.OnFinallyAsync(context, CancellationToken.None); }
                        catch (Exception ex) { Debug.LogException(ex, this); }
                    }
                    continue;
                }

                try
                {
                    await action.ExecuteAsync(context, _lifeCts.Token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Debug.LogException(ex, this); }
            }
        }
    }
}
