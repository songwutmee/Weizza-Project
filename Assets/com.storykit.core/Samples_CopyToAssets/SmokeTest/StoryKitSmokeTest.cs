using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using StoryKit;
using StoryKit.Bt;
using StoryKit.Flow;
using StoryKit.Fsm;
using UnityEngine;

/// <summary>
/// Drop on an empty GameObject and press Play.
/// Proves the whole loop with zero setup: a quest flow waits for a boss FSM
/// (driven by a BT dealing damage) to die, then gives a reward.
/// Expected console tail: "SMOKE TEST PASSED".
/// </summary>
public sealed class StoryKitSmokeTest : MonoBehaviour
{
    private sealed class FakeInventory : IInventoryService
    {
        public int Gold { get; private set; }
        public void GiveItem(string itemId, int amount) => Debug.Log($"[Test] +{amount} {itemId}");
        public bool TakeItem(string itemId, int amount) => true;
        public int CountItem(string itemId) => 0;
        public void GiveCurrency(int amount) { Gold += amount; Debug.Log($"[Test] +{amount} gold"); }

        public void CollectOwnedItems(List<(string itemId, int count)> into) => into?.Clear();
    }

    private sealed class AttackLeaves : IBtLeafProvider
    {
        public StateMachineRunner Boss;

        public BtStatus Execute(string leafId, BtContext context)
        {
            if (leafId != "AttackBoss" || Boss == null || !Boss.IsRunning)
                return BtStatus.Failure;

            float hp = Boss.Board.Get<float>("hp");
            Boss.Board.Set("hp", hp - 0.25f);
            Debug.Log($"[Test] boss hp -> {Boss.Board.Get<float>("hp"):0.00}");
            return BtStatus.Success;
        }
    }

    private async UniTaskVoid Start()
    {
        StoryServices.Reset();
        StoryEvents.Reset();

        var inventory = new FakeInventory();
        StoryServices.Register<IInventoryService>(inventory);

        // ---- Boss FSM: Fight -> Death (auto transition on hp <= 0) ----
        var fsm = ScriptableObject.CreateInstance<StateMachineAsset>();
        var fight = fsm.AddState("Fight");
        var death = fsm.AddState("Death");
        fsm.InitialStateId = fight.Id;
        fsm.AddTransition(fight, death, eventName: null,
            guardKey: "hp", cmp: VarComparison.LessOrEqual, guardValue: 0f);
        death.onEnter.Add(new RaiseEventNode { EventName = "Boss.Defeated" });

        var bossObject = new GameObject("Boss");
        var boss = bossObject.AddComponent<StateMachineRunner>();
        boss.Play(fsm);
        boss.Board.Set("hp", 1.0f, writeThrough: false);

        // ---- Attacker BT: hit the boss once per second while it lives ----
        var leaves = new AttackLeaves { Boss = boss };
        StoryServices.Register<IBtLeafProvider>(leaves);

        var tree = ScriptableObject.CreateInstance<BehaviorTreeAsset>();
        tree.Root = new BtSequence
        {
            Children =
            {
                new BtCooldown { Seconds = 1f, Child = new BtCustomLeaf { LeafId = "AttackBoss" } },
            }
        };
        var attackerObject = new GameObject("Attacker");
        var attacker = attackerObject.AddComponent<BehaviorTreeRunner>();
        attacker.SetTree(tree);

        // ---- Quest flow: wait for Boss.Defeated -> reward -> end ----
        var graph = ScriptableObject.CreateInstance<FlowGraphAsset>();
        var start = graph.AddNode(new StartNode());
        var gate = graph.AddNode(new WaitForEventNode { EventName = "Boss.Defeated" });
        var reward = graph.AddNode(new GiveItemNode { ItemId = "sword_flame", Amount = 1 });
        var log = graph.AddNode(new LogNode { Message = "Quest complete!" });
        var end = graph.AddNode(new EndNode());

        graph.SetEntry(start).Connect(start, gate);
        graph.ConnectCompleted(gate, reward);
        graph.Connect(reward, log).Connect(log, end);

        var runner = gameObject.AddComponent<FlowRunner>();
        await runner.PlayAsync(graph);

        bool bossDead = boss.CurrentState != null && boss.CurrentState.title == "Death";
        Debug.Log(bossDead
            ? "<color=green>SMOKE TEST PASSED</color>: flow, FSM, and BT all cooperated."
            : "<color=red>SMOKE TEST FAILED</color>: boss did not reach Death state.");

        Object.Destroy(attackerObject);
        Object.Destroy(bossObject);
    }
}
