using System.Collections.Generic;
using System.Linq;
using StoryKit.Flow;
using StoryKit.Quest;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Runs a health check on every StoryKit asset right before a build and
    /// stops the build if something is broken. This is the difference between
    /// finding a dead-end quest in the editor and finding it in a player review.
    ///
    /// Turn it off in Project Settings if you need to ship a known-broken build:
    /// Edit > Project Settings > Player > Scripting Define Symbols,
    /// add STORYKIT_SKIP_BUILD_CHECK.
    /// </summary>
    public sealed class StoryKitBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
#if STORYKIT_SKIP_BUILD_CHECK
            return;
#else
            var problems = StoryKitValidator.ValidateProject(errorsOnly: true);
            if (problems.Count == 0) return;

            foreach (var problem in problems.Take(20))
                Debug.LogError($"[StoryKit] {problem.Asset}: {problem.Message}", problem.Object);

            throw new BuildFailedException(
                $"StoryKit found {problems.Count} problem(s) that would break the game. " +
                "Open Tools > StoryKit > Validate to see them all.");
#endif
        }
    }

    public struct StoryProblem
    {
        public string Asset;
        public string Message;
        public bool IsError;
        public Object Object;
    }

    /// <summary>
    /// The shared validation pass. Used by the validation window, the build
    /// guard, and the graph editor status bar so they never disagree.
    /// </summary>
    public static class StoryKitValidator
    {
        public static List<StoryProblem> ValidateProject(bool errorsOnly = false)
        {
            var problems = new List<StoryProblem>();

            foreach (var graph in LoadAll<FlowGraphAsset>())
                ValidateGraph(graph, problems);

            foreach (var quest in LoadAll<QuestDefinition>())
                ValidateQuest(quest, problems);

            foreach (var machine in LoadAll<StoryKit.Fsm.StateMachineAsset>())
                ValidateStateMachine(machine, problems);

            foreach (var tree in LoadAll<StoryKit.Bt.BehaviorTreeAsset>())
                if (tree != null && tree.Root == null)
                    problems.Add(new StoryProblem
                    {
                        Asset = tree.name,
                        Message = "Tree has no root node, so this AI will never think.",
                        IsError = true,
                        Object = tree,
                    });

            return errorsOnly
                ? problems.Where(problem => problem.IsError).ToList()
                : problems;
        }

        // ---------------- Flow graphs ----------------

        public static void ValidateGraph(FlowGraphAsset graph, List<StoryProblem> problems)
        {
            if (graph == null) return;

            void Add(string message, bool isError) => problems.Add(new StoryProblem
            {
                Asset = graph.name,
                Message = message,
                IsError = isError,
                Object = graph,
            });

            if (graph.Nodes.Count == 0)
            {
                Add("Graph is empty.", false);
                return;
            }

            // 1. The graph works out its own entry, so this only matters when the
            //    guess would be a coin flip between two disconnected starts.
            if (graph.EntryIsAutomatic && CountRoots(graph) > 1)
                Add("Several nodes could be the start and none is pinned. " +
                    "Right-click the real one and choose Set as Entry.", false);

            var ids = new HashSet<string>(graph.Nodes
                .Where(node => node != null)
                .Select(node => node.Id));

            foreach (var node in graph.Nodes)
            {
                if (node == null) continue;
                string label = Describe(node);

                // 2. Connections that point at deleted nodes.
                foreach (string targetId in node.NextNodeIds)
                    if (!string.IsNullOrEmpty(targetId) && !ids.Contains(targetId))
                        Add($"{label} points at a node that no longer exists.", true);

                if (node is TaskNode task)
                {
                    CheckLink(task.RunningNextId, "Running", label);
                    CheckLink(task.CompletedNextId, "Completed", label);

                    // 3. A task with nothing on Completed can stall the graph.
                    if (string.IsNullOrEmpty(task.CompletedNextId))
                        Add($"{label} has nothing connected to Completed. " +
                            "The graph will stop here once the task ends.", false);
                }

                if (node is ConditionNode condition)
                {
                    CheckLink(condition.TrueNextId, "True", label);
                    CheckLink(condition.FalseNextId, "False", label);

                    if (string.IsNullOrEmpty(condition.TrueNextId) ||
                        string.IsNullOrEmpty(condition.FalseNextId))
                        Add($"{label} has an unconnected branch. " +
                            "One outcome leads nowhere.", false);
                }

                // 4. Cancel nodes that target something that is gone or not a task.
                if (node is CancelNode cancel)
                {
                    if (string.IsNullOrEmpty(cancel.TargetNodeId))
                        Add($"{label} has no target selected.", true);
                    else
                    {
                        var target = graph.FindNode(cancel.TargetNodeId);
                        if (target == null)
                            Add($"{label} targets a node that no longer exists.", true);
                        else if (target is not TaskNode)
                            Add($"{label} targets '{Describe(target)}', which is not a task. " +
                                "Cancel only works on task nodes.", true);
                    }
                }

                // 4b. Every option needs text and somewhere to go.
                if (node is IOptionOutputs optionNode)
                {
                    var branches = optionNode.OptionBranches;
                    if (branches.Count == 0)
                        Add($"{label} has no options, so the player has nothing to pick.", true);

                    for (int i = 0; i < branches.Count; i++)
                    {
                        if (string.IsNullOrEmpty(branches[i].TargetId))
                            Add($"{label} option '{branches[i].Label}' is not connected.", true);
                        else if (!ids.Contains(branches[i].TargetId))
                            Add($"{label} option '{branches[i].Label}' points at a deleted node.", true);
                    }

                    if (branches.Count > 0 && branches.All(branch => branch.HasGuard))
                        Add($"{label} hides every option behind a condition. " +
                            "If none pass, the graph stops there.", false);
                }

                // 4c. Nodes with named outputs: an empty slot is a dead end
                //     that only shows up when the game reaches that branch.
                if (node is IFixedOutputs fixedOutputs)
                {
                    var names = fixedOutputs.OutputNames;
                    for (int i = 0; i < names.Length; i++)
                    {
                        string target = node.GetFixedOutput(i);
                        if (string.IsNullOrEmpty(target))
                            Add($"{label} output '{names[i]}' is not connected.", false);
                        else if (!ids.Contains(target))
                            Add($"{label} output '{names[i]}' points at a deleted node.", true);
                    }
                }

                // 5. Say nodes with neither text nor key show an empty box.
                if (node is SayNode say &&
                    string.IsNullOrWhiteSpace(say.LineKey) &&
                    string.IsNullOrWhiteSpace(say.FallbackText))
                    Add($"{label} has no text and no line key.", true);

                // 6. Subgraph loops would recurse until the depth guard trips.
                if (node is SubgraphNode subgraph)
                {
                    if (subgraph.Subgraph == null)
                        Add($"{label} has no graph assigned.", true);
                    else if (subgraph.Subgraph == graph)
                        Add($"{label} calls its own graph, which loops forever.", true);
                }

                void CheckLink(string targetId, string portName, string from)
                {
                    if (!string.IsNullOrEmpty(targetId) && !ids.Contains(targetId))
                        Add($"{from} port '{portName}' points at a deleted node.", true);
                }
            }

            // 7. Nodes nothing can ever reach.
            var reachable = new HashSet<string>();
            var queue = new Queue<string>();
            if (!string.IsNullOrEmpty(graph.ResolvedEntryNodeId))
                queue.Enqueue(graph.ResolvedEntryNodeId);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (string.IsNullOrEmpty(id) || !reachable.Add(id)) continue;

                var node = graph.FindNode(id);
                if (node == null) continue;

                foreach (string next in node.NextNodeIds) queue.Enqueue(next);
                if (node is TaskNode task)
                {
                    queue.Enqueue(task.RunningNextId);
                    queue.Enqueue(task.CompletedNextId);
                }
                if (node is ConditionNode condition)
                {
                    queue.Enqueue(condition.TrueNextId);
                    queue.Enqueue(condition.FalseNextId);
                }
                if (node is IOptionOutputs optionNode)
                    foreach (var branch in optionNode.OptionBranches)
                        queue.Enqueue(branch.TargetId);
                if (node is IFixedOutputs fixedOutputs)
                    for (int i = 0; i < fixedOutputs.OutputNames.Length; i++)
                        queue.Enqueue(node.GetFixedOutput(i));
            }

            int orphans = graph.Nodes.Count(node =>
                node != null && node is not CommentNode && !reachable.Contains(node.Id));
            if (orphans > 0)
                Add($"{orphans} node(s) can never be reached from the entry node.", false);

            // 8. Complexity guards. These are the checks that stop a graph
            //    turning into the thing every visual scripting project ends up
            //    regretting: one enormous chart nobody can read or debug.
            int nodeCount = graph.Nodes.Count;
            if (nodeCount > 120)
                Add($"{nodeCount} nodes in one graph. Split it with Subgraph nodes " +
                    "or Play Next Graph before it becomes unreadable.", true);
            else if (nodeCount > 60)
                Add($"{nodeCount} nodes in one graph. Consider splitting it.", false);

            // Long chains of variable maths are a sign that game logic is
            // leaking into the graph. That belongs in C# behind a service or a
            // custom command, where it can be read, tested and profiled.
            int mathNodes = graph.Nodes.Count(node =>
                node is SetVarNode or ModifyVarNode or VarBranchNode);
            if (mathNodes > 20 && mathNodes > nodeCount / 3)
                Add($"{mathNodes} of {nodeCount} nodes are variable maths. " +
                    "Move the calculation into C# and expose one Custom Command node.", false);

            // 9. No End node means the runner never reports a clean finish.
            if (!graph.Nodes.Any(node => node is EndNode))
                Add("No End node. The graph will just stop instead of finishing.", false);
        }

        /// <summary>Nodes nothing connects to. One of these is the start.</summary>
        public static int CountRoots(FlowGraphAsset graph)
        {
            var targeted = new HashSet<string>(System.StringComparer.Ordinal);
            var scratch = new List<string>(4);

            foreach (var node in graph.Nodes)
            {
                if (node == null) continue;
                scratch.Clear();
                FlowGraphAsset.CollectOutgoing(node, scratch);
                foreach (string id in scratch)
                    if (!string.IsNullOrEmpty(id)) targeted.Add(id);
            }

            return graph.Nodes.Count(node =>
                node != null && node is not CommentNode && !targeted.Contains(node.Id));
        }

        // ---------------- State machines ----------------

        public static void ValidateStateMachine(
            StoryKit.Fsm.StateMachineAsset machine, List<StoryProblem> problems)
        {
            if (machine == null) return;

            void Add(string message, bool isError) => problems.Add(new StoryProblem
            {
                Asset = machine.name,
                Message = message,
                IsError = isError,
                Object = machine,
            });

            if (machine.States.Count == 0)
            {
                Add("No states, so this machine does nothing.", false);
                return;
            }

            var ids = new HashSet<string>(machine.States
                .Where(state => state != null)
                .Select(state => state.Id), System.StringComparer.Ordinal);

            void CheckList(IEnumerable<StoryKit.Fsm.FsmTransition> transitions, string from)
            {
                foreach (var transition in transitions)
                {
                    if (transition == null) continue;
                    if (string.IsNullOrEmpty(transition.targetStateId))
                        Add($"{from} has a transition with no target state.", true);
                    else if (!ids.Contains(transition.targetStateId))
                        Add($"{from} has a transition pointing at a deleted state.", true);
                }
            }

            foreach (var state in machine.States)
            {
                if (state == null) continue;
                CheckList(state.transitions, $"State '{state.title}'");

                bool leads = state.transitions.Any(t =>
                    t != null && !string.IsNullOrEmpty(t.targetStateId));
                bool doesSomething = state.onEnter.Count > 0 || state.flowGraph != null
                                     || state.subMachine != null;
                if (!leads && !doesSomething && machine.GlobalTransitions.Count == 0)
                    Add($"State '{state.title}' does nothing and leads nowhere.", false);
            }

            CheckList(machine.GlobalTransitions, "Global transitions");

            // Unreachable states are usually a transition wired to the wrong place.
            var reached = new HashSet<string>(System.StringComparer.Ordinal);
            var queue = new Queue<string>();
            if (!string.IsNullOrEmpty(machine.ResolvedInitialStateId))
                queue.Enqueue(machine.ResolvedInitialStateId);
            foreach (var global in machine.GlobalTransitions)
                if (global != null && !string.IsNullOrEmpty(global.targetStateId))
                    queue.Enqueue(global.targetStateId);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (string.IsNullOrEmpty(id) || !reached.Add(id)) continue;
                var state = machine.FindState(id);
                if (state == null) continue;
                foreach (var transition in state.transitions)
                    if (transition != null) queue.Enqueue(transition.targetStateId);
            }

            int unreachable = machine.States.Count(state =>
                state != null && !reached.Contains(state.Id));
            if (unreachable > 0)
                Add($"{unreachable} state(s) can never be entered.", false);
        }

        // ---------------- Quests ----------------

        public static void ValidateQuest(QuestDefinition quest, List<StoryProblem> problems)
        {
            if (quest == null) return;

            void Add(string message, bool isError) => problems.Add(new StoryProblem
            {
                Asset = quest.name,
                Message = message,
                IsError = isError,
                Object = quest,
            });

            if (string.IsNullOrWhiteSpace(quest.questId))
                Add("Quest has no id, so nothing can start it.", true);

            if (quest.objectives.Count == 0)
                Add("Quest has no objectives, so it completes instantly.", false);

            var seen = new HashSet<string>();
            foreach (var objective in quest.objectives)
            {
                if (objective == null) continue;

                if (string.IsNullOrWhiteSpace(objective.id))
                    Add("An objective has no id.", true);
                else if (!seen.Add(objective.id))
                    Add($"Duplicate objective id '{objective.id}'.", true);

                // An objective with no completion rule can never finish.
                bool hasRule = objective.completion != null &&
                               (objective.completion.requirements.Count > 0 ||
                                objective.completion.groups.Count > 0);
                if (!hasRule && !objective.optional)
                    Add($"Objective '{objective.id}' has no completion rule " +
                        "and is not optional, so the quest can never finish.", true);
            }

            foreach (var next in quest.nextQuests)
                if (next == null)
                    Add("Next Quests contains an empty slot.", false);
        }

        // ---------------- Helpers ----------------

        private static string Describe(StoryNode node)
        {
            string typeName = ObjectNames.NicifyVariableName(node.GetType().Name)
                .Replace(" Node", "");
            return string.IsNullOrEmpty(node.Title) ? typeName : $"{node.Title} [{typeName}]";
        }

        /// <summary>
        /// Brings every asset up to the current format in one pass. Opening a
        /// graph upgrades it anyway, but a project can hold graphs nobody opens
        /// for months, and a half-upgraded project is confusing to reason about.
        /// </summary>
        [MenuItem("Tools/StoryKit/Upgrade Assets", priority = 40)]
        public static void UpgradeAssets()
        {
            int upgraded = 0;
            int repaired = 0;

            foreach (var graph in LoadAll<FlowGraphAsset>())
            {
                bool changed = graph.Migrate();
                if (graph.EnsureIds()) { changed = true; repaired++; }
                if (!changed) continue;

                upgraded++;
                EditorUtility.SetDirty(graph);
            }

            foreach (var machine in LoadAll<StoryKit.Fsm.StateMachineAsset>())
                if (machine.EnsureIds())
                {
                    repaired++;
                    EditorUtility.SetDirty(machine);
                }

            AssetDatabase.SaveAssets();
            Debug.Log($"[StoryKit] Upgrade finished. {upgraded} graph(s) moved to " +
                      $"format {FlowGraphAsset.CurrentSchemaVersion}, {repaired} asset(s) had ids repaired.");
        }

        private static IEnumerable<T> LoadAll<T>() where T : Object
            => AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null);
    }

    /// <summary>
    /// Project-wide health report. Tools > StoryKit > Validate.
    /// Errors are things that will break the game; warnings are things worth
    /// a look. Clicking a row selects the asset.
    /// </summary>
    public sealed class StoryKitValidationWindow : EditorWindow
    {
        private List<StoryProblem> _problems = new();
        private Vector2 _scroll;
        private bool _errorsOnly;
        private bool _hasRun;

        [MenuItem("Tools/StoryKit/Validate %#v")]
        public static void Open()
        {
            var window = GetWindow<StoryKitValidationWindow>();
            window.titleContent = new GUIContent("StoryKit Validate");
            window.minSize = new Vector2(420, 260);
            window.Run();
            window.Show();
        }

        private void Run()
        {
            _problems = StoryKitValidator.ValidateProject();
            _hasRun = true;
        }


        /// <summary>
        /// Writes every Say and Choice line to a CSV the translation team can
        /// fill in. Columns match what the Localization Table importer expects,
        /// so the round trip is: export, translate, import.
        /// </summary>
        [MenuItem("Tools/StoryKit/Export Story Lines CSV")]
        public static void ExportLinesCsv()
        {
            string path = EditorUtility.SaveFilePanel(
                "Export story lines", "", "story_lines", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("key,source_text,speaker");
            int lines = 0;

            foreach (var graph in AssetDatabase.FindAssets("t:FlowGraphAsset")
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<FlowGraphAsset>)
                         .Where(asset => asset != null))
            {
                foreach (var node in graph.Nodes)
                {
                    if (node is SayNode say && !string.IsNullOrEmpty(say.LineKey))
                    {
                        builder.AppendLine(
                            $"{say.LineKey},\"{Escape(say.FallbackText)}\",{say.SpeakerId}");
                        lines++;
                    }
                    if (node is ChoiceNode choice)
                    {
                        foreach (var option in choice.Options)
                        {
                            if (string.IsNullOrEmpty(option.lineKey)) continue;
                            builder.AppendLine(
                                $"{option.lineKey},\"{Escape(option.fallbackText)}\",choice");
                            lines++;
                        }
                    }
                }
            }

            System.IO.File.WriteAllText(path, builder.ToString());
            Debug.Log($"[StoryKit] Exported {lines} line(s) to {path}");
        }

        private static string Escape(string text) => (text ?? "").Replace("\"", "\"\"");

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Re-check", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    Run();
                _errorsOnly = GUILayout.Toggle(_errorsOnly, "Errors only",
                    EditorStyles.toolbarButton, GUILayout.Width(80));
                GUILayout.FlexibleSpace();
            }

            if (!_hasRun)
            {
                EditorGUILayout.LabelField("Press Re-check to scan the project.",
                    StoryKitStyles.Summary);
                return;
            }

            int errors = _problems.Count(problem => problem.IsError);
            int warnings = _problems.Count - errors;

            StoryKitStyles.StatusLine(errors == 0,
                warnings == 0
                    ? "Everything checks out."
                    : $"No errors. {warnings} thing(s) worth a look.",
                $"{errors} error(s) that would break the game, {warnings} warning(s).");

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            string lastAsset = null;
            foreach (var problem in _problems.OrderByDescending(p => p.IsError)
                                             .ThenBy(p => p.Asset))
            {
                if (_errorsOnly && !problem.IsError) continue;

                if (problem.Asset != lastAsset)
                {
                    lastAsset = problem.Asset;
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(problem.Asset, EditorStyles.boldLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var badge = GUILayoutUtility.GetRect(46, 16, GUILayout.Width(46));
                    StoryKitStyles.DrawPill(badge, problem.IsError ? "ERROR" : "WARN",
                        problem.IsError ? StoryKitStyles.Danger : StoryKitStyles.Warning);

                    if (GUILayout.Button(problem.Message, EditorStyles.label))
                    {
                        Selection.activeObject = problem.Object;
                        EditorGUIUtility.PingObject(problem.Object);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
