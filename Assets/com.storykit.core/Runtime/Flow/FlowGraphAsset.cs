using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// A flow graph stored as a ScriptableObject.
    /// SerializeReference allows polymorphic node types in one list.
    /// Connections are id strings, not object references, keeping assets diff-friendly.
    /// </summary>
    [CreateAssetMenu(fileName = "FlowGraph", menuName = "StoryKit/Flow Graph")]
    public class FlowGraphAsset : ScriptableObject
    {
        [SerializeField] private string _entryNodeId;

        [SerializeReference] private List<StoryNode> _nodes = new();

        /// <summary>
        /// Format version of this asset on disk. Compared against
        /// CurrentSchemaVersion so old graphs can be upgraded in place instead
        /// of quietly behaving differently.
        ///   1: choice branches lived in the connection list, paired by index
        ///   2: every choice option owns its own branch
        /// </summary>
        [SerializeField] private int _schemaVersion = CurrentSchemaVersion;

        /// <summary>Bump this whenever a migration is added below.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>
        /// The entry the designer pinned by hand. Usually left empty: read
        /// ResolvedEntryNodeId instead, which falls back to the obvious answer.
        /// </summary>
        public string EntryNodeId { get => _entryNodeId; set { _entryNodeId = value; _autoEntry = null; } }
        public List<StoryNode> Nodes => _nodes;
        public int SchemaVersion => _schemaVersion;

        /// <summary>
        /// Where playback actually starts.
        ///
        /// You only have to pin an entry node when the graph is ambiguous. If it
        /// is not pinned, the graph picks in this order:
        ///   1. the Start node, if there is exactly one
        ///   2. the only node nothing else connects to
        ///   3. the first node in the list
        /// </summary>
        public string ResolvedEntryNodeId
        {
            get
            {
                if (!string.IsNullOrEmpty(_entryNodeId) && FindNode(_entryNodeId) != null)
                    return _entryNodeId;
#if UNITY_EDITOR
                // Connections change while editing without the node count moving,
                // so the cache would go stale. Nothing is cloned yet, recompute.
                if (!Application.isPlaying) return FindAutoEntry();
#endif
                if (_autoEntry == null || _autoEntryFor != _nodes.Count)
                {
                    _autoEntry = FindAutoEntry() ?? string.Empty;
                    _autoEntryFor = _nodes.Count;
                }
                return string.IsNullOrEmpty(_autoEntry) ? null : _autoEntry;
            }
        }

        /// <summary>True when the entry was worked out rather than pinned.</summary>
        public bool EntryIsAutomatic =>
            string.IsNullOrEmpty(_entryNodeId) || FindNode(_entryNodeId) == null;

        private Dictionary<string, StoryNode> _lookup;
        private string _autoEntry;
        private int _autoEntryFor = -1;

        private string FindAutoEntry()
        {
            StoryNode firstAlive = null;
            StoryNode singleStart = null;
            int startCount = 0;

            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null) continue;
                firstAlive ??= node;
                if (node is StartNode)
                {
                    startCount++;
                    singleStart = node;
                }
            }

            if (startCount == 1) return singleStart.Id;
            if (firstAlive == null) return null;

            // Nothing points at a root node, so a graph with one root has an
            // obvious start even without a Start node on it.
            var targeted = new HashSet<string>(StringComparer.Ordinal);
            var scratch = new List<string>(4);
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] == null) continue;
                scratch.Clear();
                CollectOutgoing(_nodes[i], scratch);
                for (int j = 0; j < scratch.Count; j++)
                    if (!string.IsNullOrEmpty(scratch[j])) targeted.Add(scratch[j]);
            }

            StoryNode onlyRoot = null;
            int rootCount = 0;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null || targeted.Contains(node.Id)) continue;
                rootCount++;
                onlyRoot ??= node;
            }

            if (rootCount == 1) return onlyRoot.Id;
            return firstAlive.Id;   // last resort: never leave the graph unplayable
        }

        /// <summary>Every node id this node can hand control to, on any port.</summary>
        public static void CollectOutgoing(StoryNode node, List<string> into)
        {
            if (node == null || into == null) return;
            into.AddRange(node.NextNodeIds);

            if (node is TaskNode task)
            {
                into.Add(task.RunningNextId);
                into.Add(task.CompletedNextId);
            }
            else if (node is ConditionNode condition)
            {
                into.Add(condition.TrueNextId);
                into.Add(condition.FalseNextId);
            }
            else if (node is IOptionOutputs options)
            {
                var branches = options.OptionBranches;
                for (int i = 0; i < branches.Count; i++)
                    into.Add(branches[i].TargetId);
            }
        }

        /// <summary>
        /// Repairs blank or duplicated node ids. Adding list elements through the
        /// default inspector copies the previous element, ids included, which
        /// would make two nodes answer to the same id.
        /// </summary>
        public bool EnsureIds()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool changed = false;

            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null) continue;
                if (string.IsNullOrEmpty(node.Id) || !seen.Add(node.Id))
                {
                    node.Id = Guid.NewGuid().ToString("N");
                    seen.Add(node.Id);
                    changed = true;
                }
            }

            if (changed) _lookup = null;
            return changed;
        }

        private void OnValidate()
        {
            EnsureIds();
            Migrate();
            _autoEntry = null;
        }

        /// <summary>
        /// Upgrades an older asset to the current format. Safe to call any
        /// number of times: each step only runs when the recorded version is
        /// behind, and each step is written to be harmless if it runs twice.
        /// </summary>
        public bool Migrate()
        {
            if (_schemaVersion >= CurrentSchemaVersion) return false;

            // 1 -> 2: choice branches move from the connection list onto the
            // options, so reordering an option carries its branch with it.
            if (_schemaVersion < 2)
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i] is ChoiceNode choice) choice.MigrateConnections();
                    else if (_nodes[i] is TimedChoiceNode timed) timed.MigrateConnections();
                }
            }

            _schemaVersion = CurrentSchemaVersion;
            return true;
        }

        public StoryNode FindNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            // Lazy index; invalidated when node count changes (editor edits).
            if (_lookup == null || _lookup.Count != _nodes.Count)
            {
                _lookup = new Dictionary<string, StoryNode>(_nodes.Count);
                for (int i = 0; i < _nodes.Count; i++)
                {
                    var node = _nodes[i];
                    if (node != null && !string.IsNullOrEmpty(node.Id))
                        _lookup[node.Id] = node;
                }
            }

            return _lookup.TryGetValue(nodeId, out var found) ? found : null;
        }

        // ---- Code-first authoring API (works without the visual editor) ----

        public T AddNode<T>(T node) where T : StoryNode
        {
            _nodes.Add(node);
            _lookup = null;
            return node;
        }

        public FlowGraphAsset Connect(StoryNode from, StoryNode to)
        {
            if (from != null && to != null && !from.NextNodeIds.Contains(to.Id))
                from.NextNodeIds.Add(to.Id);
            return this;
        }

        public FlowGraphAsset ConnectRunning(TaskNode from, StoryNode to)
        {
            if (from != null && to != null) from.RunningNextId = to.Id;
            return this;
        }

        public FlowGraphAsset ConnectCompleted(TaskNode from, StoryNode to)
        {
            if (from != null && to != null) from.CompletedNextId = to.Id;
            return this;
        }

        public FlowGraphAsset ConnectTrue(ConditionNode from, StoryNode to)
        {
            if (from != null && to != null) from.TrueNextId = to.Id;
            return this;
        }

        public FlowGraphAsset ConnectFalse(ConditionNode from, StoryNode to)
        {
            if (from != null && to != null) from.FalseNextId = to.Id;
            return this;
        }

        public FlowGraphAsset SetEntry(StoryNode node)
        {
            _entryNodeId = node?.Id;
            return this;
        }
    }
}
