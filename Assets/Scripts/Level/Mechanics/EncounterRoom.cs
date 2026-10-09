using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>One wave of an encounter: who appears, where, and how long after the previous wave.</summary>
    [Serializable]
    public sealed class EncounterWave
    {
        [Serializable]
        public sealed class Spawn
        {
            [Tooltip("An enemy prefab made with Mob Forge.")]
            public GameObject prefab;

            [Tooltip("Where it appears. Empty uses the room's own position.")]
            public Transform point;
        }

        [PropertyRange(0f, 10f)]
        [Tooltip("Seconds after the previous wave is cleared (or the room starts) before this one appears.")]
        public float delay = 0.6f;

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<Spawn> spawns = new();
    }

    /// <summary>
    /// A room that shuts when the player walks in, sends waves, and opens
    /// again once the last enemy falls. Raises EncounterStarted and
    /// EncounterEnded so music, the HUD and flow graphs can follow.
    ///
    /// Also answers StoryKit's ICombatService, so a flow graph can start a
    /// named encounter or end one, and a cleared room stays cleared in the save.
    ///
    /// The gates are any IActivatable, switched on for the length of the fight:
    /// a Door with its entry marked inverted closes on start and opens on clear.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Encounter Room")]
    public sealed class EncounterRoom : BodyZone, IActivatable, ISaveable, ICombatService
    {
        private static readonly List<EncounterRoom> Rooms = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Rooms.Clear();

        [BoxGroup("Room")]
        [Tooltip("Stable name a flow graph starts this with. Never renamed.")]
        [SerializeField] private string encounterId = "encounter";

        [BoxGroup("Room")]
        [ToggleLeft]
        [Tooltip("Starts when the player walks in. Off waits for a graph or a lever.")]
        [SerializeField] private bool startOnEnter = true;

        [BoxGroup("Room")]
        [ToggleLeft]
        [Tooltip("Stays cleared across saves.")]
        [SerializeField] private bool persistent = true;

        [BoxGroup("Room")]
        [InfoBox("Switched on when the fight starts and off when it ends. Mark a " +
                 "door's entry Inverted to have it close for the fight.")]
        [SerializeField] private ActivationTargets duringFight = new();

        [BoxGroup("Room")]
        [Tooltip("Switched on once, when the room is cleared. A reward chest, a bridge.")]
        [SerializeField] private ActivationTargets onCleared = new();

        [BoxGroup("Waves")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [SerializeField] private List<EncounterWave> waves = new();

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly] public bool Running { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Cleared { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public int WaveIndex { get; private set; } = -1;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public int Alive => CountAlive();

        public bool IsActivated => Running;
        public bool IsEncounterActive => Running;

        private readonly List<EnemyController> _spawned = new();

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
            var box = GetComponent<BoxCollider>();
            box.size = new Vector3(16f, 8f, 4f);
            box.center = new Vector3(0f, 4f, 0f);
        }

        private void Awake()
        {
            if (!Rooms.Contains(this)) Rooms.Add(this);
        }

        private void OnEnable()
        {
            if (Rooms.Count > 0 && Rooms[0] == this) StoryServices.Register<ICombatService>(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            // Pooled enemies live under the pool root, not in this chunk, so they would
            // outlive the room when its chunk streams out. Send them home with it.
            foreach (var enemy in _spawned)
                if (enemy != null) PrefabPool.Despawn(enemy.gameObject);
            _spawned.Clear();

            if (StoryServices.TryGet<ICombatService>(out var current) && ReferenceEquals(current, this))
                StoryServices.Register<ICombatService>(null);
        }

        private void OnDestroy() => Rooms.Remove(this);

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (startOnEnter && PlayerLocator.IsPlayer(body)) Begin();
        }

        public void SetActivated(bool activated, GameObject source)
        {
            if (activated) Begin();
        }

        [Button(ButtonSizes.Medium), DisableInEditorMode]
        public void Begin()
        {
            if (Running || Cleared) return;
            Running = true;
            WaveIndex = -1;
            duringFight.Send(true, gameObject);
            StoryEvents.Raise(GameEvents.EncounterStarted, encounterId);
            RunAsync().Forget();
        }

        private async UniTaskVoid RunAsync()
        {
            var ct = destroyCancellationToken;
            try
            {
                for (var i = 0; i < waves.Count; i++)
                {
                    WaveIndex = i;
                    var wave = waves[i];
                    if (wave.delay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(wave.delay), cancellationToken: ct);
                    SpawnWave(wave);

                    // A wave with nothing that spawned still counts as cleared,
                    // so a missing prefab never locks the player in.
                    await UniTask.WaitUntil(() => CountAlive() == 0 || !Running, cancellationToken: ct);
                    if (!Running) return;
                }
                Finish(true);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void SpawnWave(EncounterWave wave)
        {
            foreach (var spawn in wave.spawns)
            {
                if (spawn?.prefab == null) continue;
                var at = spawn.point != null ? spawn.point.position : transform.position;
                at.z = ViewRig.LaneZ(0);
                // Pooled (M2.8): a wave room that respawns three times makes no new
                // objects after the first wave.
                var go = PrefabPool.Spawn(spawn.prefab, at, Quaternion.identity);
                var enemy = go.GetComponent<EnemyController>();
                if (enemy != null) _spawned.Add(enemy);
                HitSpark.Play(at + Vector3.up, Vector2.up, 1f, new Color(0.75f, 0.9f, 1f));
            }
        }

        private int CountAlive()
        {
            var alive = 0;
            for (var i = _spawned.Count - 1; i >= 0; i--)
            {
                var enemy = _spawned[i];
                if (enemy == null || !enemy.IsAlive) _spawned.RemoveAt(i);
                else alive++;
            }
            return alive;
        }

        private void Finish(bool won)
        {
            if (!Running) return;
            Running = false;
            duringFight.Send(false, gameObject);
            StoryEvents.Raise(GameEvents.EncounterEnded, encounterId);

            if (!won) return;
            Cleared = true;
            onCleared.Send(true, gameObject);
            HudScreen.Instance?.Toast("Cleared");
        }

        // ---- ICombatService ----------------------------------------------------------------

        public void StartEncounter(string id)
        {
            foreach (var room in Rooms)
            {
                if (room != null && room.encounterId == id)
                {
                    room.Begin();
                    return;
                }
            }
            Debug.LogWarning($"[Weizza] No encounter room called '{id}'.");
        }

        public void EndEncounter(bool playerWon)
        {
            foreach (var room in Rooms)
            {
                if (room == null || !room.Running) continue;
                foreach (var enemy in room._spawned)
                {
                    if (enemy != null && enemy.IsAlive) enemy.GetComponent<Vitals>()?.Kill();
                }
                room.Finish(playerWon);
            }
        }

        public void SetInvincible(string actorId, bool invincible)
        {
            var vitals = ActorVitals(actorId);
            if (vitals != null) vitals.Invincible = invincible;
        }

        public void DealDamage(string actorId, float amount)
        {
            var vitals = ActorVitals(actorId);
            if (vitals != null) vitals.SetHealth(vitals.Current - amount);
        }

        private static Vitals ActorVitals(string actorId)
        {
            var actor = StoryServices.Get<IActorService>()?.Find(actorId);
            return actor?.Transform != null ? actor.Transform.GetComponentInParent<Vitals>() : null;
        }

        // ---- ISaveable ----------------------------------------------------------------------

        public string SaveKey => persistent ? "encounter." + this.StableKey() : null;
        public string CaptureState() => Cleared ? "1" : "0";

        public void RestoreState(string state)
        {
            Cleared = state == "1";
            if (Cleared) onCleared.Send(true, gameObject);
        }

#if UNITY_EDITOR
        /// <summary>For the Scene view preview, which draws who will appear where.</summary>
        public IReadOnlyList<EncounterWave> Waves => waves;

        protected override Color GizmoColour => Cleared ? new Color(0.5f, 0.85f, 0.55f, 0.08f) : new Color(0.9f, 0.5f, 0.35f, 0.08f);

        private void OnDrawGizmosSelected()
        {
            duringFight.DrawGizmos(transform.position, new Color(0.9f, 0.5f, 0.35f));
            onCleared.DrawGizmos(transform.position, new Color(0.5f, 0.85f, 0.55f));
            Gizmos.color = new Color(1f, 0.6f, 0.4f, 0.9f);
            foreach (var wave in waves)
            {
                foreach (var spawn in wave.spawns)
                {
                    if (spawn?.point != null) Gizmos.DrawWireSphere(spawn.point.position, 0.4f);
                }
            }
        }
#endif
    }
}
