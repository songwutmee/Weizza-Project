using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Souls-like world state.
    ///
    /// FromSoftware games run on one idea: a giant table of permanent world
    /// flags. Bonfires lit, bosses killed, doors unlocked, NPCs alive/dead/moved,
    /// items picked up: everything is a flag that survives death and reload.
    /// NPC questlines then read those flags to decide where a character is
    /// standing today and what they say.
    ///
    /// These nodes formalise that pattern on top of the blackboard, so a
    /// souls-like is authored entirely in graphs.
    /// </summary>

    // ---------------- World flags ----------------

    /// <summary>
    /// Sets a permanent world flag. Unlike normal variables these are meant to
    /// be one-way ("boss_gargoyle_dead") and are the backbone of world state.
    /// </summary>
    [Serializable]
    [NodeMenu("World",
        "Set a permanent world flag (boss killed, door opened, item taken). Survives death and reload.")]
    public sealed class SetWorldFlagNode : ActionNode
    {
        [SerializeField] private string _flagId;
        [SerializeField] private bool _value = true;

        public string FlagId { get => _flagId; set => _flagId = value; }
        public bool Value { get => _value; set => _value = value; }

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_flagId} = {_value}";

        public static string KeyFor(string flagId) => "world." + flagId;
        public static bool Get(string flagId) =>
            StoryState.Global.Get<float>(KeyFor(flagId)) > 0f;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryState.Global.Set(KeyFor(_flagId), _value ? 1f : 0f);
            StoryEvents.Raise("World.FlagChanged", _flagId);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Branches on a permanent world flag.</summary>
    [Serializable]
    [NodeMenu("World",
        "Go True/False by a permanent world flag.")]
    public sealed class WorldFlagBranchNode : ConditionNode
    {
        [SerializeField] private string _flagId;
        [SerializeField] private bool _expected = true;

        public string FlagId { get => _flagId; set => _flagId = value; }
        public override string GetSummary() => $"{_flagId} is {_expected}";

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
            => UniTask.FromResult(SetWorldFlagNode.Get(_flagId) == _expected);
    }

    // ---------------- Bonfire / Site of Grace ----------------

    /// <summary>
    /// Rest point. Registers the spawn point, heals, restocks flasks, respawns
    /// enemies, and saves: the full bonfire ritual as one node.
    /// The game reacts via events; nothing here assumes your combat design.
    /// </summary>
    [Serializable]
    [NodeMenu("World",
        "Rest at a bonfire / site of grace: set respawn point, restore, respawn enemies, save.")]
    public sealed class RestAtBonfireNode : ActionNode
    {
        [SerializeField] private string _bonfireId;

        [Tooltip("Raise World.RespawnEnemies so the game can reset its spawners.")]
        [SerializeField] private bool _respawnEnemies = true;

        [Tooltip("Raise Player.Restore so the game can refill health and flasks.")]
        [SerializeField] private bool _restorePlayer = true;

        public string BonfireId { get => _bonfireId; set => _bonfireId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _bonfireId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            // Discovering a bonfire is itself a permanent flag (fast travel list).
            StoryState.Global.Set(SetWorldFlagNode.KeyFor("bonfire." + _bonfireId), 1f);
            StoryState.Global.Set("respawn.bonfire", _bonfireId);

            if (_restorePlayer) StoryEvents.Raise("Player.Restore", _bonfireId);
            if (_respawnEnemies) StoryEvents.Raise("World.RespawnEnemies", _bonfireId);
            StoryEvents.Raise("World.Rested", _bonfireId);

            StoryServices.Get<ISaveService>()?.RequestAutoSave();
            StorySave.SaveToDisk();
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Boss encounter ----------------

    /// <summary>
    /// The full boss ritual as one task: lock the fog gate behind the player,
    /// show the boss name banner, play the boss music, and hold until the boss
    /// dies. On completion it sets the "boss dead" world flag and clears the
    /// gates: and because cleanup is mandatory, quitting mid-fight can never
    /// leave a sealed arena or stuck music.
    /// </summary>
    [Serializable]
    [NodeMenu("World",
        "Boss fight: seal the fog gate, show the name banner, play boss music, wait for the kill, then unseal.")]
    public sealed class BossEncounterNode : TaskNode
    {
        [SerializeField] private string _bossId = "boss_01";

        [Tooltip("Localization key for the name banner (falls back to the text below).")]
        [SerializeField] private string _nameKey;
        [SerializeField] private string _nameFallback = "NAMELESS ONE";

        [Tooltip("Story Stage Ids of fog gates to seal during the fight.")]
        [SerializeField] private List<string> _fogGateIds = new();

        [SerializeField] private string _bossBgmId;

        [Tooltip("Event raised by the game when the boss dies.")]
        [SerializeField] private string _defeatedEvent = "Combat.BossDefeated";

        [Tooltip("Music played after victory (empty = silence).")]
        [SerializeField] private string _victoryBgmId;

        public string BossId { get => _bossId; set => _bossId = value; }
        public List<string> FogGateIds => _fogGateIds;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _bossId;

        [NonSerialized] private IDisposable _subscription;
        [NonSerialized] private UniTaskCompletionSource _defeated;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            SetFogGates(true);

            _defeated = new UniTaskCompletionSource();
            _subscription = StoryEvents.Subscribe(_defeatedEvent, payload =>
            {
                if (payload == null || string.IsNullOrEmpty(_bossId) ||
                    string.Equals(payload.ToString(), _bossId, StringComparison.Ordinal))
                    _defeated.TrySetResult();
            });

            if (!string.IsNullOrEmpty(_bossBgmId))
                StoryServices.Get<IAudioService>()?.PlayBgm(_bossBgmId, 0.5f);

            StoryEvents.Raise("World.BossFightStarted", _bossId);

            var screen = StoryServices.Get<IScreenUI>();
            if (screen != null && !context.FastForward)
            {
                string name = StoryServices.Get<ILocalizationService>()?.Resolve(_nameKey) ?? _nameKey;
                if (string.IsNullOrEmpty(name) || name == _nameKey) name = _nameFallback;
                await screen.ShowBannerAsync(name, 2f, ct);
            }
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => await _defeated.Task.AttachExternalCancellation(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            _subscription?.Dispose();
            _subscription = null;

            bool won = _defeated != null && _defeated.Task.Status == UniTaskStatus.Succeeded;
            _defeated = null;

            SetFogGates(false);   // never leave the player sealed in

            if (won)
            {
                StoryState.Global.Set(SetWorldFlagNode.KeyFor("boss." + _bossId + ".dead"), 1f);
                StoryEvents.Raise("World.BossDefeated", _bossId);
                var audio = StoryServices.Get<IAudioService>();
                if (!string.IsNullOrEmpty(_victoryBgmId)) audio?.PlayBgm(_victoryBgmId, 1.5f);
                else audio?.StopBgm(1.5f);
                StorySave.SaveToDisk();
            }
            else
            {
                StoryServices.Get<IAudioService>()?.StopBgm(1f);
            }
            return UniTask.CompletedTask;
        }

        private void SetFogGates(bool sealed_)
        {
            foreach (string id in _fogGateIds)
            {
                var gate = StoryStageId.Find(id);
                if (gate != null) gate.gameObject.SetActive(sealed_);
            }
        }
    }

    // ---------------- Player death loop ----------------

    /// <summary>
    /// Waits for the player to die, then runs the souls-like death ritual:
    /// drop the currency where they fell, respawn at the last bonfire, and
    /// respawn enemies. Put it on a parallel branch that lives for the level.
    /// </summary>
    [Serializable]
    [NodeMenu("World",
        "Wait for player death, drop their currency at the spot, respawn at the last bonfire.")]
    public sealed class OnPlayerDeathNode : TaskNode
    {
        [SerializeField] private string _deathEvent = "Player.Died";
        [SerializeField] private string _currencyKey = "souls";

        [Tooltip("Lose the dropped pile if the player dies again before reclaiming it.")]
        [SerializeField] private bool _loseOnSecondDeath = true;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _deathEvent;

        [NonSerialized] private IDisposable _subscription;
        [NonSerialized] private UniTaskCompletionSource _died;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _died = new UniTaskCompletionSource();
            _subscription = StoryEvents.Subscribe(_deathEvent, _ => _died.TrySetResult());
            return UniTask.CompletedTask;
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
        {
            await _died.Task.AttachExternalCancellation(ct);

            var board = StoryState.Global;
            float carried = board.Get<float>(_currencyKey);

            if (_loseOnSecondDeath || board.Get<float>("bloodstain.amount") <= 0f)
                board.Set("bloodstain.amount", carried);

            board.Set(_currencyKey, 0f);
            StoryEvents.Raise("World.BloodstainDropped", carried.ToString("0"));
            StoryEvents.Raise("World.RespawnEnemies", "death");
            StoryEvents.Raise("Player.Respawn", board.Get<string>("respawn.bonfire"));
            StorySave.SaveToDisk();
        }

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            _subscription?.Dispose();
            _subscription = null;
            _died = null;
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Gives back the dropped currency pile (touching the bloodstain).</summary>
    [Serializable]
    [NodeMenu("World",
        "Reclaim the currency dropped on death (bloodstain pickup).")]
    public sealed class ReclaimBloodstainNode : ActionNode
    {
        [SerializeField] private string _currencyKey = "souls";

        public override NodeEffect Effect => NodeEffect.Consequence;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var board = StoryState.Global;
            float amount = board.Get<float>("bloodstain.amount");
            if (amount <= 0f) return UniTask.CompletedTask;

            board.Set(_currencyKey, board.Get<float>(_currencyKey) + amount);
            board.Set("bloodstain.amount", 0f);
            StoryEvents.Raise("World.BloodstainReclaimed", amount.ToString("0"));
            return UniTask.CompletedTask;
        }
    }

    // ---------------- NPC questline phases ----------------

    /// <summary>
    /// Moves an NPC to the next stage of their questline. Souls NPCs live in
    /// numbered phases that decide where they stand and what they say;
    /// this writes "npc.&lt;id&gt;.phase" which spawners and dialogue read.
    /// </summary>
    [Serializable]
    [NodeMenu("World",
        "Advance an NPC's questline phase (where they stand today, what they say).")]
    public sealed class SetNpcPhaseNode : ActionNode
    {
        [SerializeField] private string _npcId;
        [SerializeField] private int _phase = 1;

        [Tooltip("Also mark them dead/gone so spawners stop placing them.")]
        [SerializeField] private bool _markGone;

        public string NpcId { get => _npcId; set => _npcId = value; }
        public int Phase { get => _phase; set => _phase = value; }

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_npcId} → phase {_phase}";

        public static string PhaseKey(string npcId) => "npc." + npcId + ".phase";
        public static int GetPhase(string npcId) =>
            Mathf.RoundToInt(StoryState.Global.Get<float>(PhaseKey(npcId)));

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryState.Global.Set(PhaseKey(_npcId), (float)_phase);
            if (_markGone)
                StoryState.Global.Set(SetWorldFlagNode.KeyFor("npc." + _npcId + ".gone"), 1f);
            StoryEvents.Raise("Npc.PhaseChanged", _npcId);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Routes dialogue by NPC questline phase: the node that makes a souls NPC
    /// say something different every time the world moves forward.
    /// Outputs map to phases in order: output 0 = phase 0, output 1 = phase 1...
    /// </summary>
    [Serializable]
    [NodeMenu("World",
        "Route by an NPC's questline phase. One labelled output per phase.")]
    public sealed class NpcPhaseRouterNode : StoryNode, IFixedOutputs
    {
        [SerializeField] private string _npcId;

        [Tooltip("How many phases this NPC has. One output port per phase.")]
        [SerializeField, Range(2, 8)] private int _phaseCount = 3;

        public string[] OutputNames
        {
            get
            {
                var names = new string[Mathf.Clamp(_phaseCount, 2, 8)];
                for (int i = 0; i < names.Length; i++) names[i] = $"Phase {i}";
                return names;
            }
        }

        public string NpcId { get => _npcId; set => _npcId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_npcId} phase → output";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            int phase = Mathf.Clamp(SetNpcPhaseNode.GetPhase(_npcId), 0, _phaseCount - 1);
            FollowFixedOutput(phase);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Graph chaining ----------------

    /// <summary>
    /// Play the next graph.
    ///
    /// Ends this graph and immediately starts another one on the same runner
    /// how a game is built from many small graphs instead of one huge one
    /// (chapter 1 → chapter 2, or intro → main loop).
    /// Unlike Subgraph (which returns here when done) this one hands over
    /// control completely, so the current graph's memory is released.
    /// </summary>
    [Serializable]
    [NodeMenu("Flow",
        "End this graph and start another one. Chain chapters together instead of one giant graph.")]
    public sealed class PlayNextGraphNode : ActionNode
    {
        [SerializeField] private FlowGraphAsset _nextGraph;

        [Tooltip("Start from this node id instead of the entry (optional).")]
        [SerializeField] private string _startNodeId;

        [Tooltip("Copy the current graph's variables into the next graph.")]
        [SerializeField] private bool _carryVariables = true;

        public FlowGraphAsset NextGraph { get => _nextGraph; set => _nextGraph = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _nextGraph != null ? _nextGraph.name : "(none)";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (_nextGraph == null)
            {
                Debug.LogWarning("[StoryKit] PlayNextGraph has no graph assigned.", context.Runner);
                return UniTask.CompletedTask;
            }

            var runner = context.Runner;
            var carried = _carryVariables ? context.Board.Snapshot() : null;
            string startNode = _startNodeId;
            var nextGraph = _nextGraph;

            // Hand over after this graph unwinds, so cleanup runs first.
            runner.PlayAfterCurrent(nextGraph, startNode, carried);
            runner.RequestEnd();
            return UniTask.CompletedTask;
        }
    }
}
