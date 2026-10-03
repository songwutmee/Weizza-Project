using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using StoryKit.Flow;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The one component a designer needs to make something happen in the level:
    /// a box in the world that plays a Flow Graph, a Cutscene Director, or just
    /// raises a StoryKit event, when the player walks in or presses E.
    ///
    /// Remembers that it played in the save, so a cutscene never replays after a
    /// reload, and can be gated on a story flag or the player's stance. Make the
    /// graph with Weizza > Story > Cutscene Wizard, drop it in here, done.
    ///
    /// StoryKit's own Story Graph Trigger remembers only for the session and needs
    /// a Player tag; this one works with Weizza's player and the save.
    /// </summary>
    [AddComponentMenu("Weizza/Story/Story Zone")]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class StoryZone : MonoBehaviour
    {
        public enum Trigger { WalkIn, PressInteract, ChunkLoaded }
        public enum Repeat { OncePerSave, OncePerSession, Always }

        [BoxGroup("What plays")]
        [Tooltip("A Flow Graph. Build one with Weizza > Story > Cutscene Wizard.")]
        [SerializeField] private FlowGraphAsset graph;

        [BoxGroup("What plays")]
        [Tooltip("Or a Cutscene Director placed in the scene (the Inspector beat list).")]
        [SerializeField] private CutsceneDirector cutscene;

        [BoxGroup("What plays")]
        [Tooltip("Or only raise this StoryKit event, for something listening elsewhere (a door, music, a boss).")]
        [SerializeField] private string raiseEvent;

        [BoxGroup("When")]
        [SerializeField] private Trigger fireOn = Trigger.WalkIn;

        [BoxGroup("When")]
        [SerializeField] private Repeat repeat = Repeat.OncePerSave;

        [BoxGroup("When")]
        [PropertyRange(0f, 10f)]
        [ShowIf(nameof(repeat), Repeat.Always)]
        [SerializeField] private float cooldown = 1f;

        [BoxGroup("Only if")]
        [Tooltip("A blackboard key that must be true. Empty means no requirement. Example: met.soka")]
        [SerializeField] private string requireFlag;

        [BoxGroup("Only if")]
        [Tooltip("A blackboard key that must NOT be true. Example: boss.forest.defeated")]
        [SerializeField] private string blockedByFlag;

        [BoxGroup("Only if")]
        [ToggleLeft]
        [SerializeField] private bool requireStance;

        [BoxGroup("Only if")]
        [ShowIf(nameof(requireStance))]
        [SerializeField] private StanceId stance = StanceId.Normal;

        [BoxGroup("Afterwards")]
        [Tooltip("Set this blackboard key true when it finishes. Lets later zones and dialogue know it happened.")]
        [SerializeField] private string setFlagWhenDone;

        [BoxGroup("Afterwards")]
        [ToggleLeft]
        [Tooltip("Save the game when it finishes.")]
        [SerializeField] private bool autosave;

        [ShowInInspector, ReadOnly] private bool _playing;
        private bool _playedThisSession;
        private bool _inside;
        private float _readyAt;
        private Narrative.InteractPrompt _prompt;

        private string DoneKey => "story.zone." + this.StableKey();

        public bool HasPlayed =>
            repeat == Repeat.OncePerSave ? StoryState.Global.Get(DoneKey, false) :
            repeat == Repeat.OncePerSession && _playedThisSession;

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2f, 4f, 6f);
            box.center = new Vector3(0f, 2f, 0f);
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private BoxCollider _box;
        private float _nextOverlapCheck;

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
            if (fireOn == Trigger.PressInteract)
            {
                _prompt = GetComponentInChildren<Narrative.InteractPrompt>(true);
                if (_prompt == null)
                {
                    var go = new GameObject("Prompt");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = Vector3.up * 2.6f;
                    _prompt = go.AddComponent<Narrative.InteractPrompt>();
                }
                _prompt.SetVisible(false);
            }
        }

        private void Start()
        {
            if (fireOn == Trigger.ChunkLoaded) TryFire();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!PlayerLocator.IsPlayer(other)) return;
            _inside = true;
            if (fireOn == Trigger.WalkIn) TryFire();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!PlayerLocator.IsPlayer(other)) return;
            _inside = false;
            if (_prompt != null) _prompt.SetVisible(false);
        }

        private void Update()
        {
            // A CharacterController only reports entering a trigger when it moves
            // into one. Teleported, respawned or loaded straight into the box, it
            // never would, so check the overlap by hand a few times a second.
            if (Time.time >= _nextOverlapCheck)
            {
                _nextOverlapCheck = Time.time + 0.1f;
                var player = PlayerLocator.Player;
                var body = player != null ? player.Motor.Controller : null;
                var overlapping = body != null && _box.bounds.Intersects(body.bounds);
                if (overlapping && !_inside)
                {
                    _inside = true;
                    if (fireOn == Trigger.WalkIn) TryFire();
                }
                else if (!overlapping && _inside)
                {
                    _inside = false;
                    if (_prompt != null) _prompt.SetVisible(false);
                }
            }

            if (fireOn != Trigger.PressInteract || !_inside) return;
            var show = CanFire();
            if (_prompt != null) _prompt.SetVisible(show);
            if (show && InputReader.Instance != null && InputReader.Instance.InteractPressed) TryFire();
        }

        private bool CanFire()
        {
            if (_playing || HasPlayed || Time.time < _readyAt) return false;
            if (StoryControl.MovementLocked) return false;
            var board = StoryState.Global;
            if (!string.IsNullOrEmpty(requireFlag) && !board.Get(requireFlag, false)) return false;
            if (!string.IsNullOrEmpty(blockedByFlag) && board.Get(blockedByFlag, false)) return false;
            if (requireStance && (PlayerLocator.Player == null || PlayerLocator.Player.Stance != stance)) return false;
            return true;
        }

        /// <summary>Fires now if every condition holds. Public so a lever or a boss death can call it.</summary>
        [Button, DisableInEditorMode]
        public void TryFire()
        {
            if (!CanFire()) return;
            RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            _playing = true;
            if (_prompt != null) _prompt.SetVisible(false);
            FlowRunner runner = null;
            try
            {
                if (!string.IsNullOrEmpty(raiseEvent)) StoryEvents.Raise(raiseEvent, this.StableKey());

                if (graph != null)
                {
                    runner = FlowRunner.Rent(gameObject);
                    await runner.PlayAsync(graph, ct);
                }
                else if (cutscene != null)
                {
                    cutscene.Play();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    while (cutscene != null && cutscene.Playing) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                _playedThisSession = true;
                if (repeat == Repeat.OncePerSave) StoryState.Global.Set(DoneKey, true);
                if (!string.IsNullOrEmpty(setFlagWhenDone)) StoryState.Global.Set(setFlagWhenDone, true);
                if (autosave) SaveSystem.Instance?.Save();
            }
            catch (System.OperationCanceledException) { }
            finally
            {
                if (runner != null) FlowRunner.ReturnRented(runner);
                _playing = false;
                _readyAt = Time.time + cooldown;
            }
        }

        [ShowInInspector, ReadOnly, PropertyOrder(-1)]
        [InfoBox("Nothing to play. Give it a Flow Graph, a Cutscene Director or an event name.", InfoMessageType.Warning,
            nameof(NothingToPlay))]
        private string Summary =>
            (graph != null ? "graph " + graph.name : cutscene != null ? "cutscene " + cutscene.name : raiseEvent) +
            $", {fireOn}, {repeat}";

        private bool NothingToPlay => graph == null && cutscene == null && string.IsNullOrEmpty(raiseEvent);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.75f, 0.6f, 1f, 0.18f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.75f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(transform.position + Vector3.up * (box.size.y + 0.3f),
                graph != null ? graph.name : cutscene != null ? cutscene.name : raiseEvent);
        }
#endif
    }
}
