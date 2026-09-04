using System.Collections.Generic;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Recycles the per-run copies of a graph.
    ///
    /// A runner needs its own copy so two NPCs playing the same conversation do
    /// not share node state. Making that copy with Instantiate is fine once,
    /// but a bark line or a repeatable greeting can run hundreds of times, and
    /// each Instantiate allocates the asset plus every node in it.
    ///
    /// This keeps finished copies around and hands them back out. Node state is
    /// reset on return, so a reused copy behaves exactly like a fresh one.
    /// </summary>
    internal static class GraphClonePool
    {
        private static readonly Dictionary<int, Stack<FlowGraphAsset>> _idle = new();

        /// <summary>How many spare copies to keep per graph. Beyond this the
        /// extra copies are destroyed instead of held in memory.</summary>
        private const int MaxIdlePerGraph = 4;

        public static FlowGraphAsset Rent(FlowGraphAsset source)
        {
            int key = source.GetInstanceID();

            if (_idle.TryGetValue(key, out var stack))
            {
                while (stack.Count > 0)
                {
                    var reused = stack.Pop();
                    if (reused == null) continue;      // destroyed by a scene unload
                    ResetRuntimeState(source, reused);
                    return reused;
                }
            }

            return Object.Instantiate(source);
        }

        public static void Return(FlowGraphAsset source, FlowGraphAsset clone)
        {
            if (clone == null) return;

            if (source == null)
            {
                Object.Destroy(clone);
                return;
            }

            int key = source.GetInstanceID();
            if (!_idle.TryGetValue(key, out var stack))
            {
                stack = new Stack<FlowGraphAsset>();
                _idle[key] = stack;
            }

            if (stack.Count >= MaxIdlePerGraph)
            {
                Object.Destroy(clone);
                return;
            }

            stack.Push(clone);
        }

        /// <summary>
        /// Copies the connection lists back from the source. Choice and random
        /// nodes trim their own outputs while running, so a reused clone has to
        /// get the full list back or the second playthrough loses branches.
        /// </summary>
        private static void ResetRuntimeState(FlowGraphAsset source, FlowGraphAsset clone)
        {
            var sourceNodes = source.Nodes;
            var cloneNodes = clone.Nodes;
            if (sourceNodes.Count != cloneNodes.Count) return;

            for (int i = 0; i < sourceNodes.Count; i++)
            {
                var original = sourceNodes[i];
                var copy = cloneNodes[i];
                if (original == null || copy == null) continue;

                if (copy.NextNodeIds.Count == original.NextNodeIds.Count) continue;

                copy.NextNodeIds.Clear();
                copy.NextNodeIds.AddRange(original.NextNodeIds);
            }
        }

        public static void Clear()
        {
            foreach (var stack in _idle.Values)
                while (stack.Count > 0)
                {
                    var clone = stack.Pop();
                    if (clone != null) Object.Destroy(clone);
                }
            _idle.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _idle.Clear();
    }
}
