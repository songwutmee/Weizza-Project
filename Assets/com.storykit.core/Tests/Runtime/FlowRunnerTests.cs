using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using StoryKit;
using StoryKit.Flow;
using UnityEngine;
using UnityEngine.TestTools;

namespace StoryKit.Tests
{
    /// <summary>
    /// Tests for the flow runner. These cover the rules that would silently
    /// break a shipped game: cleanup always running, cancel firing the
    /// completed branch, and skip keeping game state correct.
    /// </summary>
    public sealed class FlowRunnerTests
    {
        private GameObject _host;

        [SetUp]
        public void SetUp()
        {
            StoryEvents.Reset();
            StoryServices.Reset();
            StoryState.SetGlobal(new Blackboard());
            _host = new GameObject("TestRunner");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ---------- helpers ----------

        /// <summary>Action node that records that it ran, for assertions.</summary>
        private sealed class SpyNode : ActionNode
        {
            public static readonly List<string> Ran = new();
            public string Tag = "spy";
            public NodeEffect EffectValue = NodeEffect.Consequence;

            public override NodeEffect Effect => EffectValue;

            public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
            {
                Ran.Add(Tag);
                return UniTask.CompletedTask;
            }
        }

        /// <summary>Task node that stays open until cancelled and records cleanup.</summary>
        private sealed class HoldNode : TaskNode
        {
            public static bool CleanedUp;
            public override NodeEffect Effect => NodeEffect.Presentation;

            public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
                => UniTask.CompletedTask;

            public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
                => UniTask.WaitUntilCanceled(ct);

            public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
            {
                CleanedUp = true;
                return UniTask.CompletedTask;
            }
        }

        private static FlowGraphAsset NewGraph()
            => ScriptableObject.CreateInstance<FlowGraphAsset>();

        // ---------- tests ----------

        [UnityTest]
        public IEnumerator FindsTheStartNodeWithoutBeingTold() => UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();

            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var only = graph.AddNode(new SpyNode { Tag = "only" });
            graph.Connect(start, only);
            // No SetEntry call on purpose.

            var runner = _host.AddComponent<FlowRunner>();
            await runner.PlayAsync(graph);

            Assert.AreEqual(start.Id, graph.ResolvedEntryNodeId);
            Assert.AreEqual(new[] { "only" }, SpyNode.Ran.ToArray());
        });

        [UnityTest]
        public IEnumerator FindsTheOnlyRootWhenThereIsNoStartNode() => UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();

            var graph = NewGraph();
            var head = graph.AddNode(new SpyNode { Tag = "head" });
            var tail = graph.AddNode(new SpyNode { Tag = "tail" });
            graph.Connect(head, tail);

            var runner = _host.AddComponent<FlowRunner>();
            await runner.PlayAsync(graph);

            Assert.AreEqual(head.Id, graph.ResolvedEntryNodeId);
            Assert.AreEqual(new[] { "head", "tail" }, SpyNode.Ran.ToArray());
        });

        [Test]
        public void PinnedEntryBeatsTheAutomaticOne()
        {
            var graph = NewGraph();
            graph.AddNode(new StartNode());
            var other = graph.AddNode(new SpyNode { Tag = "other" });

            Assert.IsTrue(graph.EntryIsAutomatic);
            graph.SetEntry(other);
            Assert.IsFalse(graph.EntryIsAutomatic);
            Assert.AreEqual(other.Id, graph.ResolvedEntryNodeId);
        }

        [UnityTest]
        public IEnumerator ChoiceKeepsItsBranchWhenOptionsAreReordered() =>
            UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();
            StoryServices.Register<IDialogueUI>(new PickFirstDialogueUI());

            var graph = NewGraph();
            var choice = graph.AddNode(new ChoiceNode());
            var fight = graph.AddNode(new SpyNode { Tag = "fight" });
            var talk = graph.AddNode(new SpyNode { Tag = "talk" });

            choice.Options.Add(new ChoiceNode.Option { fallbackText = "Fight" });
            choice.Options.Add(new ChoiceNode.Option { fallbackText = "Talk" });
            choice.SetOptionTarget(0, fight.Id);
            choice.SetOptionTarget(1, talk.Id);
            graph.SetEntry(choice);

            // Swap the options. The branches must swap with them, which is the
            // whole point: the old code paired option 0 with connection 0.
            (choice.Options[0], choice.Options[1]) = (choice.Options[1], choice.Options[0]);

            var runner = _host.AddComponent<FlowRunner>();
            await runner.PlayAsync(graph);

            Assert.AreEqual(new[] { "talk" }, SpyNode.Ran.ToArray(),
                "Picking the first option should now run Talk, not Fight.");
        });

        [Test]
        public void OldChoiceGraphsMigrateToPerOptionBranches()
        {
            var graph = NewGraph();
            var choice = graph.AddNode(new ChoiceNode());
            var a = graph.AddNode(new SpyNode { Tag = "a" });
            var b = graph.AddNode(new SpyNode { Tag = "b" });

            choice.Options.Add(new ChoiceNode.Option { fallbackText = "A" });
            choice.Options.Add(new ChoiceNode.Option { fallbackText = "B" });

            // The old format: branches lived in the connection list by index.
            choice.NextNodeIds.Add(a.Id);
            choice.NextNodeIds.Add(b.Id);

            Assert.IsTrue(choice.MigrateConnections());
            Assert.AreEqual(a.Id, choice.Options[0].nextNodeId);
            Assert.AreEqual(b.Id, choice.Options[1].nextNodeId);
            Assert.IsFalse(choice.MigrateConnections(), "Migration must not run twice.");
        }

        /// <summary>Always picks the first visible option, so tests are deterministic.</summary>
        private sealed class PickFirstDialogueUI : IDialogueUI
        {
            public UniTask SayAsync(string speakerId, string emotion, string text,
                CancellationToken ct) => UniTask.CompletedTask;

            public UniTask<int> ChoiceAsync(IReadOnlyList<string> options,
                CancellationToken ct) => UniTask.FromResult(0);

            public UniTask OpenAsync(CancellationToken ct) => UniTask.CompletedTask;
            public UniTask CloseAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        [Test]
        public void BlankAndDuplicateNodeIdsAreRepaired()
        {
            var graph = NewGraph();
            var first = graph.AddNode(new SpyNode { Tag = "a" });
            var second = graph.AddNode(new SpyNode { Tag = "b" });
            second.Id = first.Id;

            Assert.IsTrue(graph.EnsureIds());
            Assert.AreNotEqual(first.Id, second.Id);
        }

        [Test]
        public void StateMachineStartsAtTheFirstStateWithoutBeingTold()
        {
            var machine = ScriptableObject.CreateInstance<StoryKit.Fsm.StateMachineAsset>();
            var idle = machine.AddState("Idle");
            machine.AddState("Chase");

            Assert.IsTrue(machine.InitialIsAutomatic);
            Assert.AreEqual(idle.Id, machine.ResolvedInitialStateId);

            Object.DestroyImmediate(machine);
        }

        [UnityTest]
        public IEnumerator RunsNodesInOrder() => UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();

            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var first = graph.AddNode(new SpyNode { Tag = "first" });
            var second = graph.AddNode(new SpyNode { Tag = "second" });
            var end = graph.AddNode(new EndNode());

            graph.SetEntry(start)
                 .Connect(start, first)
                 .Connect(first, second)
                 .Connect(second, end);

            var runner = _host.AddComponent<FlowRunner>();
            await runner.PlayAsync(graph);

            Assert.AreEqual(new[] { "first", "second" }, SpyNode.Ran.ToArray());
        });

        [UnityTest]
        public IEnumerator CancelFiresCompletedBranch() => UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();
            HoldNode.CleanedUp = false;

            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var hold = graph.AddNode(new HoldNode());
            var duringHold = graph.AddNode(new SpyNode { Tag = "running" });
            var cancel = graph.AddNode(new CancelNode());
            var afterHold = graph.AddNode(new SpyNode { Tag = "completed" });
            var end = graph.AddNode(new EndNode());

            cancel.TargetNodeId = hold.Id;

            graph.SetEntry(start).Connect(start, hold);
            graph.ConnectRunning(hold, duringHold);
            graph.Connect(duringHold, cancel);
            graph.ConnectCompleted(hold, afterHold);
            graph.Connect(afterHold, end);

            var runner = _host.AddComponent<FlowRunner>();
            await runner.PlayAsync(graph);

            CollectionAssert.Contains(SpyNode.Ran, "running");
            CollectionAssert.Contains(SpyNode.Ran, "completed");
            Assert.IsTrue(HoldNode.CleanedUp, "Task cleanup must always run.");
        });

        [UnityTest]
        public IEnumerator StoppingMidRunStillCleansUp() => UniTask.ToCoroutine(async () =>
        {
            HoldNode.CleanedUp = false;

            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var hold = graph.AddNode(new HoldNode());
            graph.SetEntry(start).Connect(start, hold);

            var runner = _host.AddComponent<FlowRunner>();
            var play = runner.PlayAsync(graph);

            await UniTask.DelayFrame(2);
            runner.Stop();
            await play;

            Assert.IsTrue(HoldNode.CleanedUp,
                "A stopped graph must still run cleanup, or the game softlocks.");
        });

        [UnityTest]
        public IEnumerator SkipRunsConsequencesButNotPresentation()
            => UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();

            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var wait = graph.AddNode(new WaitNode { Seconds = 5f });
            var visual = graph.AddNode(new SpyNode
            {
                Tag = "visual",
                EffectValue = NodeEffect.Presentation,
            });
            var reward = graph.AddNode(new SpyNode
            {
                Tag = "reward",
                EffectValue = NodeEffect.Consequence,
            });
            var end = graph.AddNode(new EndNode());

            graph.SetEntry(start)
                 .Connect(start, wait)
                 .Connect(wait, visual)
                 .Connect(visual, reward)
                 .Connect(reward, end);

            var runner = _host.AddComponent<FlowRunner>();
            var play = runner.PlayAsync(graph);

            await UniTask.DelayFrame(2);
            runner.SkipToEnd();
            await play;

            CollectionAssert.DoesNotContain(SpyNode.Ran, "visual");
            CollectionAssert.Contains(SpyNode.Ran, "reward");
        });

        [UnityTest]
        public IEnumerator WaitForEventContinuesWhenRaised() => UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();

            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var gate = graph.AddNode(new WaitForEventNode { EventName = "Test.Go" });
            var after = graph.AddNode(new SpyNode { Tag = "after" });
            var end = graph.AddNode(new EndNode());

            graph.SetEntry(start).Connect(start, gate);
            graph.ConnectCompleted(gate, after);
            graph.Connect(after, end);

            var runner = _host.AddComponent<FlowRunner>();
            var play = runner.PlayAsync(graph);

            await UniTask.DelayFrame(2);
            CollectionAssert.DoesNotContain(SpyNode.Ran, "after");

            StoryEvents.Raise("Test.Go");
            await play;

            CollectionAssert.Contains(SpyNode.Ran, "after");
        });

        [UnityTest]
        public IEnumerator ConditionPicksCorrectBranch() => UniTask.ToCoroutine(async () =>
        {
            SpyNode.Ran.Clear();
            StoryState.Global.Set("gold", 150f);

            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var branch = graph.AddNode(new VarBranchNode
            {
                Key = "gold",
                Comparison = VarComparison.GreaterOrEqual,
                Target = 100f,
            });
            var rich = graph.AddNode(new SpyNode { Tag = "rich" });
            var poor = graph.AddNode(new SpyNode { Tag = "poor" });
            var end = graph.AddNode(new EndNode());

            graph.SetEntry(start).Connect(start, branch);
            graph.ConnectTrue(branch, rich);
            graph.ConnectFalse(branch, poor);
            graph.Connect(rich, end).Connect(poor, end);

            var runner = _host.AddComponent<FlowRunner>();
            await runner.PlayAsync(graph);

            CollectionAssert.Contains(SpyNode.Ran, "rich");
            CollectionAssert.DoesNotContain(SpyNode.Ran, "poor");
        });

        [UnityTest]
        public IEnumerator LoopingConnectionDoesNotHang() => UniTask.ToCoroutine(async () =>
        {
            var graph = NewGraph();
            var start = graph.AddNode(new StartNode());
            var a = graph.AddNode(new SpyNode { Tag = "a" });
            var b = graph.AddNode(new SpyNode { Tag = "b" });

            // a -> b -> a would loop forever without the visited guard.
            graph.SetEntry(start).Connect(start, a).Connect(a, b).Connect(b, a);

            var runner = _host.AddComponent<FlowRunner>();
            await runner.PlayAsync(graph).Timeout(System.TimeSpan.FromSeconds(3));

            Assert.Pass("Loop guard prevented an infinite walk.");
        });
    }

    /// <summary>Blackboard rules that other systems depend on.</summary>
    public sealed class BlackboardTests
    {
        [Test]
        public void ChildReadsFromParent()
        {
            var parent = new Blackboard();
            var child = new Blackboard(parent);

            parent.Set("gold", 50f);
            Assert.AreEqual(50f, child.Get<float>("gold"));
        }

        [Test]
        public void WriteThroughUpdatesTheOwner()
        {
            var parent = new Blackboard();
            var child = new Blackboard(parent);

            parent.Set("hp", 10f);
            child.Set("hp", 3f);

            Assert.AreEqual(3f, parent.Get<float>("hp"),
                "Write-through must update the board that owns the key.");
        }

        [Test]
        public void LocalWriteDoesNotLeakToParent()
        {
            var parent = new Blackboard();
            var child = new Blackboard(parent);

            child.Set("local", 1f, writeThrough: false);
            Assert.AreEqual(0f, parent.Get<float>("local"));
        }

        [Test]
        public void ChangeEventFiresOnlyOnRealChange()
        {
            var board = new Blackboard();
            int changes = 0;
            board.KeyChanged += _ => changes++;

            board.Set("x", 1f);
            board.Set("x", 1f);   // same value, should not fire again
            board.Set("x", 2f);

            Assert.AreEqual(2, changes);
        }

        [Test]
        public void SnapshotRestoreRoundTrips()
        {
            var board = new Blackboard();
            board.Set("gold", 42f);
            board.Set("name", "Ash");

            var snapshot = board.Snapshot();
            board.Clear();
            board.Restore(snapshot);

            Assert.AreEqual(42f, board.Get<float>("gold"));
            Assert.AreEqual("Ash", board.Get<string>("name"));
        }
    }

    /// <summary>Event bus behaviour that nodes rely on.</summary>
    public sealed class StoryEventsTests
    {
        [SetUp]
        public void SetUp() => StoryEvents.Reset();

        [Test]
        public void SubscriberReceivesPayload()
        {
            string received = null;
            using var subscription = StoryEvents.Subscribe("Test.Ping",
                payload => received = payload as string);

            StoryEvents.Raise("Test.Ping", "hello");
            Assert.AreEqual("hello", received);
        }

        [Test]
        public void DisposingStopsDelivery()
        {
            int count = 0;
            var subscription = StoryEvents.Subscribe("Test.Ping", _ => count++);

            StoryEvents.Raise("Test.Ping");
            subscription.Dispose();
            StoryEvents.Raise("Test.Ping");

            Assert.AreEqual(1, count);
        }

        [Test]
        public void UnsubscribingDuringDispatchIsSafe()
        {
            System.IDisposable inner = null;
            int count = 0;

            var outer = StoryEvents.Subscribe("Test.Ping", _ =>
            {
                count++;
                inner?.Dispose();     // mutates the list mid-dispatch
            });
            inner = StoryEvents.Subscribe("Test.Ping", _ => count++);

            Assert.DoesNotThrow(() => StoryEvents.Raise("Test.Ping"));
            outer.Dispose();
        }
    }
}
