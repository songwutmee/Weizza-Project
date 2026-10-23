using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Executes a FlowGraphAsset. Core patterns:
    /// - Recursive async walk from the entry node.
    /// - Task nodes: Running branch runs in parallel while the task is alive;
    ///   Completed branch fires after the task ends.
    /// - Cancel-as-signal: cancelling a task node fires its Completed output.
    /// - Per-node CancellationTokenSource so one branch can be cancelled
    ///   without stopping the whole graph.
    /// - Fast-forward mode: presentation nodes are skipped instantly,
    ///   consequence nodes always run (safe cutscene skip).
    ///
    /// The asset is cloned per run: node runtime state ([NonSerialized] fields,
    /// branch trimming) never leaks into the shared asset or other runners.
    /// </summary>
    public sealed class FlowRunner : MonoBehaviour
    {
        [SerializeField] private FlowGraphAsset _graphAsset;
        [SerializeField] private bool _playOnStart;

        private FlowGraphAsset _runtimeGraph;
        private CancellationTokenSource _cts;
        private FlowContext _context;
        private bool _endRequested;

        private readonly Dictionary<string, CancellationTokenSource> _activeNodes =
            new(StringComparer.Ordinal);

        /// <summary>Graph-scope blackboard, parented to the global board.</summary>
        public Blackboard Board { get; private set; }

        public bool IsPlaying => _cts != null && !_cts.IsCancellationRequested;
        public IReadOnlyCollection<string> ActiveNodeIds => _activeNodes.Keys;

        public event Action Started;
        public event Action Completed;
        public event Action Stopped;
        /// <summary>Editor debugger hooks.</summary>
        public event Action<string> NodeEntered;
        public event Action<string> NodeExited;

        /// <summary>All runners currently playing (editor debugger uses this).</summary>
        public static readonly List<FlowRunner> ActiveRunners = new();

        /// <summary>The asset this runner was asked to play (not the runtime clone).</summary>
        public FlowGraphAsset SourceAsset => _graphAsset;

        /// <summary>
        /// Borrows a spare runner on a host object, reusing a disabled one when
        /// possible. Destroy on a Component is deferred to the end of the frame,
        /// so code that starts and finishes subgraphs quickly would otherwise
        /// pile up dead components on the same GameObject.
        /// </summary>
        public static FlowRunner Rent(GameObject host)
        {
            var existing = host.GetComponents<FlowRunner>();
            for (int i = 0; i < existing.Length; i++)
                if (!existing[i].enabled && !existing[i].IsPlaying)
                {
                    existing[i].enabled = true;
                    return existing[i];
                }

            var runner = host.AddComponent<FlowRunner>();
            runner._playOnStart = false;   // rented runners are driven manually
            return runner;
        }

        /// <summary>Returns a rented runner instead of destroying it.</summary>
        public static void ReturnRented(FlowRunner runner)
        {
            if (runner == null) return;
            runner.Stop();
            runner._graphAsset = null;
            runner.enabled = false;
        }

        private void Start()
        {
            if (_playOnStart && _graphAsset != null)
                PlayAsync().Forget();
        }

        private void OnDestroy() => Stop();

        public UniTask PlayAsync(CancellationToken externalToken = default)
            => PlayAsync(_graphAsset, externalToken);

        /// <param name="configureBoard">
        /// Invoked after the board exists but before any node runs.
        /// Used by SubgraphNode to inject inputs race-free.
        /// </param>
        /// <param name="startNodeId">
        /// Resume support: start walking from this node instead of the entry
        /// (use with StorySave.TryGetCheckpoint after loading a save).
        /// </param>
        public async UniTask PlayAsync(
            FlowGraphAsset graph,
            CancellationToken externalToken = default,
            Action<Blackboard> configureBoard = null,
            string startNodeId = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (string.IsNullOrEmpty(graph.ResolvedEntryNodeId))
                throw new InvalidOperationException(
                    $"[StoryKit] Graph '{graph.name}' is empty, so there is nothing to play.");
            if (IsPlaying)
            {
                Debug.LogWarning($"[StoryKit] Runner already playing '{_graphAsset?.name}'. Stop it first.", this);
                return;
            }

            _graphAsset = graph;
            // Per-run clone isolates node runtime state from the shared asset.
            // Clones are pooled, so a graph played hundreds of times (a bark,
            // a repeatable NPC line) does not allocate a new ScriptableObject
            // and a full node list every single time.
            _runtimeGraph = GraphClonePool.Rent(graph);
            _endRequested = false;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(
                externalToken, this.GetCancellationTokenOnDestroy());
            Board = new Blackboard(StoryState.Global);
            _context = new FlowContext(this, _runtimeGraph, Board);
            configureBoard?.Invoke(Board);

            Started?.Invoke();
            ActiveRunners.Add(this);
            bool finishedNormally = false;

            try
            {
                string entry = !string.IsNullOrEmpty(startNodeId)
                    && _runtimeGraph.FindNode(startNodeId) != null
                    ? startNodeId
                    : _runtimeGraph.ResolvedEntryNodeId;
                await RunNodeAsync(entry,
                    new Dictionary<string, int>(StringComparer.Ordinal), _cts.Token);
                finishedNormally = true;
            }
            catch (OperationCanceledException)
            {
                // Reached an EndNode (_endRequested) or stopped externally.
            }
            finally
            {
                ActiveRunners.Remove(this);
                CancelAllActiveNodes();
                _cts?.Dispose();
                _cts = null;

                if (_runtimeGraph != null)
                {
                    GraphClonePool.Return(_graphAsset, _runtimeGraph);
                    _runtimeGraph = null;
                }

                if (finishedNormally || _endRequested) Completed?.Invoke();
                else Stopped?.Invoke();
                _endRequested = false;
            }

            // Graph chaining: PlayNextGraph queued a hand-over. Start it now
            // that this graph has fully unwound and released its resources.
            if (_queuedGraph != null)
            {
                var next = _queuedGraph;
                string nextStart = _queuedStartNodeId;
                var carried = _queuedVariables;
                _queuedGraph = null;
                _queuedStartNodeId = null;
                _queuedVariables = null;

                await PlayAsync(next, externalToken,
                    board =>
                    {
                        if (carried == null) return;
                        foreach (var pair in carried)
                            board.Set(pair.Key, pair.Value, writeThrough: false);
                    },
                    nextStart);
            }
        }

        private FlowGraphAsset _queuedGraph;
        private string _queuedStartNodeId;
        private Dictionary<string, object> _queuedVariables;

        /// <summary>
        /// Queues a graph to start as soon as the current one finishes unwinding.
        /// Used by PlayNextGraphNode so chapters can hand over cleanly.
        /// </summary>
        public void PlayAfterCurrent(FlowGraphAsset graph, string startNodeId = null,
            Dictionary<string, object> carriedVariables = null)
        {
            _queuedGraph = graph;
            _queuedStartNodeId = startNodeId;
            _queuedVariables = carriedVariables;
        }

        public void Stop()
        {
            if (_cts == null) return;
            CancelAllActiveNodes();
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        /// <summary>Reached an EndNode: cancel everything but report as completed.</summary>
        internal void RequestEnd()
        {
            _endRequested = true;
            Stop();
        }

        /// <summary>
        /// Skip mode: presentation nodes return instantly from now on while
        /// consequence nodes still run, so state after skipping is exact.
        /// </summary>
        public void SkipToEnd()
        {
            if (_context == null || _runtimeGraph == null) return;
            _context.FastForward = true;

            // Wake active presentation tasks; their cancel counts as completion.
            foreach (var nodeId in _activeNodes.Keys.ToArray())
            {
                var node = _runtimeGraph.FindNode(nodeId);
                if (node != null && node.Effect == NodeEffect.Presentation)
                    TryCancelNode(nodeId);
            }
        }

        /// <summary>Cancels one running node branch. Fires a task's Completed output.</summary>
        public bool TryCancelNode(string nodeId)
        {
            if (_activeNodes.TryGetValue(nodeId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                return true;
            }
            return false;
        }

        public async UniTask<bool> TryCancelNodeAsync(string nodeId, CancellationToken ct = default)
        {
            if (!TryCancelNode(nodeId)) return false;
            await UniTask.WaitUntil(() => !_activeNodes.ContainsKey(nodeId), cancellationToken: ct);
            return true;
        }

        private void CancelAllActiveNodes()
        {
            foreach (var cts in _activeNodes.Values.ToArray())
            {
                try { if (!cts.IsCancellationRequested) cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            _activeNodes.Clear();
        }

        /// <summary>
        /// How many times one path may pass through the same node. Loops are allowed
        /// ("ask again" dialogue, a patrol that repeats), this only stops a loop that
        /// was never meant to end from running forever unseen.
        /// </summary>
        private const int MaxVisitsPerNode = 10000;

        private async UniTask RunNodeAsync(string nodeId, Dictionary<string, int> visits, CancellationToken ct)
        {
            // A straight chain is walked in this loop rather than by calling itself
            // once per node, so a long or looping graph does not grow the call chain.
            while (!string.IsNullOrEmpty(nodeId))
            {
                visits.TryGetValue(nodeId, out var seen);
                visits[nodeId] = seen + 1;
                if (seen >= MaxVisitsPerNode)
                {
                    Debug.LogError($"[StoryKit] '{_runtimeGraph.name}' passed through node '{nodeId}' {MaxVisitsPerNode} times on one path. " +
                                   "Stopping that path; a loop needs a node that waits (a choice, a wait, an event) or a way out.", this);
                    return;
                }

                // Coming back to a node always waits one frame first, so a loop made
                // only of instant nodes runs once a frame instead of freezing the game.
                if (seen > 0) await UniTask.Yield(PlayerLoopTiming.Update, ct);

                var node = _runtimeGraph.FindNode(nodeId);
                if (node == null)
                {
                    Debug.LogWarning($"[StoryKit] Node id '{nodeId}' not found in '{_runtimeGraph.name}'.", this);
                    return;
                }

                ct.ThrowIfCancellationRequested();

                if (node is EndNode)
                {
                    RequestEnd();
                    return;
                }

                NodeEntered?.Invoke(nodeId);

                using var nodeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _activeNodes[nodeId] = nodeCts;

                List<string> nextIds;
                try
                {
                    nextIds = await ExecuteNodeAsync(node, visits, ct, nodeCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Node-local cancel of a non-task node: stop this branch only.
                    return;
                }
                finally
                {
                    _activeNodes.Remove(nodeId);
                    NodeExited?.Invoke(nodeId);
                }

                ct.ThrowIfCancellationRequested();

                switch (nextIds.Count)
                {
                    case 0:
                        return;
                    case 1:
                        nodeId = nextIds[0];
                        continue;
                    default:
                        // Each branch counts its own visits from here on.
                        await UniTask.WhenAll(nextIds.Select(id =>
                            RunNodeAsync(id, new Dictionary<string, int>(visits, StringComparer.Ordinal), ct)));
                        return;
                }
            }
        }

        private async UniTask<List<string>> ExecuteNodeAsync(
            StoryNode node, Dictionary<string, int> visited,
            CancellationToken graphToken, CancellationToken nodeToken)
        {
            bool skip = _context.FastForward && node.Effect == NodeEffect.Presentation;

            if (node is ConditionNode condition)
            {
                bool result = await condition.EvaluateAsync(_context, nodeToken);
                string chosen = result ? condition.TrueNextId : condition.FalseNextId;
                var list = new List<string>(1);
                if (!string.IsNullOrEmpty(chosen)) list.Add(chosen);
                return list;
            }

            if (node is TaskNode task)
                return await ExecuteTaskAsync(task, visited, graphToken, nodeToken, skip);

            if (!skip)
                await node.ExecuteAsync(_context, nodeToken);

            return node.NextNodeIds
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private async UniTask<List<string>> ExecuteTaskAsync(
            TaskNode task, Dictionary<string, int> visited,
            CancellationToken graphToken, CancellationToken nodeToken, bool skip)
        {
            using var branchCts = CancellationTokenSource.CreateLinkedTokenSource(nodeToken);
            bool started = false;

            async UniTask RunBranch()
            {
                try
                {
                    await RunNodeAsync(task.RunningNextId,
                        new Dictionary<string, int>(visited, StringComparer.Ordinal),
                        branchCts.Token);
                }
                catch (OperationCanceledException) when (!graphToken.IsCancellationRequested)
                {
                    // The task ended and took its Running branch with it: that is the
                    // design, not an error.
                }
            }

            try
            {
                if (!skip)
                {
                    await task.ExecuteAsync(_context, nodeToken);
                    started = true;
                }

                // The Running branch always executes: it may contain consequence
                // nodes (rewards, flags) that must survive a skip. In skip mode the
                // presentation nodes inside it fast-forward to nothing anyway.
                //
                // It runs alongside the task, not before it. Awaiting the branch first
                // meant a task only began holding after its branch had finished, so an
                // event a WaitForEvent was listening for could arrive and still leave the
                // graph stuck until an unrelated branch ended.
                var branch = string.IsNullOrEmpty(task.RunningNextId) ? UniTask.CompletedTask : RunBranch();

                if (skip)
                {
                    // Skipping: nothing holds the task open, so let the branch play out
                    // its consequences before moving on.
                    await branch;
                }
                else
                {
                    try
                    {
                        await task.OnRunningAsync(_context, nodeToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Node cancel means the task is finished, which is normal
                        // and not an error. A graph cancel is the whole run being
                        // stopped, so it is passed up; the finally below still
                        // does the cleanup either way.
                        if (graphToken.IsCancellationRequested) throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex, this);
                    }

                    // The task is over, so its Running branch ends with it. Waiting for
                    // the branch to unwind first means its own cleanups (a lock, a
                    // camera) are released before the Completed branch starts.
                    try { branchCts.Cancel(); } catch (ObjectDisposedException) { }
                    await branch;
                }

                var list = new List<string>(1);
                if (!string.IsNullOrEmpty(task.CompletedNextId) && !graphToken.IsCancellationRequested)
                    list.Add(task.CompletedNextId);
                return list;
            }
            finally
            {
                if (!branchCts.IsCancellationRequested)
                {
                    try { branchCts.Cancel(); } catch (ObjectDisposedException) { }
                }

                // Cleanup ALWAYS runs if the task started: the anti-softlock guarantee.
                if (started)
                {
                    try
                    {
                        await task.OnFinallyAsync(_context, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex, this);
                    }
                }
            }
        }
    }
}
