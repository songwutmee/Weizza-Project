using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using StoryKit.Bridge;
using UnityEngine;

namespace Weizza
{
    /// <summary>What one line of a cutscene does.</summary>
    public enum CutsceneBeatKind
    {
        Letterbox,
        Fade,
        LockInput,
        UnlockInput,
        MoveActor,
        PlayAnimation,
        LookAt,
        Wait,
        HudVisible,
        Dialogue,
    }

    /// <summary>
    /// One row of a cutscene, authored in the Inspector. Every field routes
    /// through the same StoryKit services a FadeNode or an ActorMoveNode
    /// would use, so a beat list built here and a graph built later behave
    /// identically, and either can be swapped for the other without redoing
    /// the scene wiring.
    /// </summary>
    [Serializable]
    public sealed class CutsceneBeat
    {
        [HideLabel, GUIColor(nameof(KindColour))]
        public CutsceneBeatKind kind = CutsceneBeatKind.Wait;

        [ShowIf("@kind == CutsceneBeatKind.Letterbox")]
        [ToggleLeft, LabelText("Show")]
        public bool letterboxShow = true;

        [ShowIf("@kind == CutsceneBeatKind.Fade")]
        [PropertyRange(0f, 1f)]
        public float fadeToAlpha = 1f;

        [ShowIf("@kind == CutsceneBeatKind.Letterbox || kind == CutsceneBeatKind.Fade || " +
                "kind == CutsceneBeatKind.Wait")]
        [PropertyRange(0f, 5f)]
        public float seconds = 0.5f;

        [ShowIf("@kind == CutsceneBeatKind.LockInput")]
        public string lockReason = "cutscene";

        [ShowIf("@kind == CutsceneBeatKind.MoveActor || kind == CutsceneBeatKind.PlayAnimation || " +
                "kind == CutsceneBeatKind.LookAt")]
        [Tooltip("Matches a WeizzaActor's Actor Id in the scene.")]
        public string actorId = "Player";

        [ShowIf("@kind == CutsceneBeatKind.MoveActor")]
        [Tooltip("A StoryStageId placed in the scene to walk toward.")]
        public string targetStageId;

        [ShowIf("@kind == CutsceneBeatKind.MoveActor")]
        [PropertyRange(0.2f, 8f)]
        public float moveSpeed = 2f;

        [ShowIf("@kind == CutsceneBeatKind.PlayAnimation")]
        public string animationName = "Idle";

        [ShowIf("@kind == CutsceneBeatKind.PlayAnimation")]
        [ToggleLeft]
        public bool waitForEnd = true;

        [ShowIf("@kind == CutsceneBeatKind.LookAt")]
        [Tooltip("A StoryStageId placed in the scene to face toward.")]
        public string lookAtStageId;

        [ShowIf("@kind == CutsceneBeatKind.HudVisible")]
        [ToggleLeft, LabelText("Show HUD")]
        public bool hudShow;

        [ShowIf("@kind == CutsceneBeatKind.Dialogue")]
        [Tooltip("Plays this conversation and waits until it ends.")]
        public Narrative.Conversation conversation;

        private Color KindColour() => kind switch
        {
            CutsceneBeatKind.Letterbox => new Color(0.55f, 0.55f, 0.6f),
            CutsceneBeatKind.Fade => new Color(0.3f, 0.3f, 0.35f),
            CutsceneBeatKind.LockInput or CutsceneBeatKind.UnlockInput => new Color(0.8f, 0.6f, 0.3f),
            CutsceneBeatKind.MoveActor => new Color(0.4f, 0.75f, 0.5f),
            CutsceneBeatKind.PlayAnimation => new Color(0.4f, 0.6f, 0.85f),
            CutsceneBeatKind.LookAt => new Color(0.7f, 0.5f, 0.8f),
            _ => Color.white,
        };
    }

    /// <summary>
    /// Plays an ordered list of beats: letterbox, fade, input lock, an actor
    /// walking somewhere, a clip playing on them, all through the same
    /// IScreenUI, IInputService and IActorService a StoryKit graph would call.
    ///
    /// This exists because a full FlowGraphAsset is a visual authoring tool
    /// with its own editor, and Act 0 needs one working cutscene today rather
    /// than a graph nobody has drawn yet. When the graph editor is used later,
    /// FadeNode, LetterboxNode, LockControlNode, ActorMoveNode and
    /// ActorAnimationNode all call the exact same services this does, so
    /// nothing here is thrown away, it becomes one more way to author the
    /// same beats.
    /// </summary>
    [AddComponentMenu("Weizza/Cutscene Director")]
    public sealed class CutsceneDirector : MonoBehaviour
    {
        [BoxGroup("Beats", centerLabel: true)]
        [InfoBox("Runs top to bottom. LockInput and UnlockInput are separate " +
                 "rows on purpose: forgetting the unlock row is a bug you can " +
                 "see in this list, not one hidden inside a task node's wiring.")]
        [ListDrawerSettings(ShowFoldout = false, DefaultExpandedState = true)]
        [SerializeField] private CutsceneBeat[] beats = Array.Empty<CutsceneBeat>();

        [BoxGroup("Playback", centerLabel: true)]
        [Tooltip("Plays once automatically when the scene starts.")]
        [SerializeField] private bool playOnStart;

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly]
        public bool Playing { get; private set; }

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly]
        private int CurrentBeat { get; set; } = -1;

        private CancellationTokenSource _life;
        private IDisposable _inputLock;

        private void Awake() => _life = new CancellationTokenSource();

        private void OnDestroy()
        {
            _inputLock?.Dispose();
            _life?.Cancel();
            _life?.Dispose();
        }

        private void Start()
        {
            if (playOnStart) PlayAsync().Forget();
        }

        [BoxGroup("Live")]
        [Button(ButtonSizes.Large), GUIColor(0.4f, 0.75f, 0.5f)]
        [DisableInEditorMode]
        public void Play() => PlayAsync().Forget();

        public async UniTaskVoid PlayAsync()
        {
            if (Playing) return;
            Playing = true;
            var token = _life?.Token ?? CancellationToken.None;

            try
            {
                for (var i = 0; i < beats.Length; i++)
                {
                    CurrentBeat = i;
                    await RunBeatAsync(beats[i], token);
                }
            }
            catch (OperationCanceledException)
            {
                // Scene torn down mid cutscene. The finally block below still
                // lifts whatever lock was held, so input is never left stuck.
            }
            finally
            {
                _inputLock?.Dispose();
                _inputLock = null;
                CurrentBeat = -1;
                Playing = false;
            }
        }

        private async UniTask RunBeatAsync(CutsceneBeat beat, CancellationToken ct)
        {
            switch (beat.kind)
            {
                case CutsceneBeatKind.Letterbox:
                    if (Letterbox.Instance != null)
                        await Letterbox.Instance.SetAsync(beat.letterboxShow, beat.seconds, ct);
                    break;

                case CutsceneBeatKind.Fade:
                    var screen = StoryServices.Get<IScreenUI>();
                    if (screen != null) await screen.FadeAsync(beat.fadeToAlpha, beat.seconds, ct);
                    break;

                case CutsceneBeatKind.LockInput:
                    _inputLock?.Dispose();
                    _inputLock = StoryServices.Get<IInputService>()?.LockPlayerControl(beat.lockReason);
                    break;

                case CutsceneBeatKind.UnlockInput:
                    _inputLock?.Dispose();
                    _inputLock = null;
                    EndDirectingAll();
                    break;

                case CutsceneBeatKind.MoveActor:
                    var mover = StoryServices.Get<IActorService>()?.Find(beat.actorId);
                    var target = StoryStageId.FindTransform(beat.targetStageId);
                    if (mover != null && target != null)
                    {
                        BeginDirecting(beat.actorId);
                        await mover.MoveToAsync(target.position, beat.moveSpeed, ct);
                    }
                    break;

                case CutsceneBeatKind.PlayAnimation:
                    var performer = StoryServices.Get<IActorService>()?.Find(beat.actorId);
                    if (performer != null)
                    {
                        BeginDirecting(beat.actorId);
                        await performer.PlayAnimationAsync(beat.animationName, beat.waitForEnd, ct);
                    }
                    break;

                case CutsceneBeatKind.LookAt:
                    var looker = StoryServices.Get<IActorService>()?.Find(beat.actorId);
                    var lookTarget = StoryStageId.FindTransform(beat.lookAtStageId);
                    if (looker != null && lookTarget != null) looker.LookAt(lookTarget.position);
                    break;

                case CutsceneBeatKind.HudVisible:
                    HudScreen.Instance?.SetVisible(beat.hudShow);
                    break;

                case CutsceneBeatKind.Dialogue:
                    if (beat.conversation != null && Narrative.DialogueDirector.Instance != null)
                    {
                        Narrative.DialogueDirector.Instance.Play(beat.conversation);
                        // Talking turns true on the next frame, so wait one before watching it.
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        while (Narrative.DialogueDirector.Talking) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    break;

                case CutsceneBeatKind.Wait:
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(beat.seconds),
                        DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
                    break;
            }
        }

        // Every actor a MoveActor or PlayAnimation beat touches is put under
        // direction for the whole cutscene, not just its own beat, so its own
        // controller cannot wake up and fight it between two beats.
        private void BeginDirecting(string actorId)
        {
            if (StoryServices.Get<IActorService>()?.Find(actorId) is WeizzaActor actor)
                actor.BeginDirected();
        }

        private void EndDirectingAll()
        {
            foreach (var actor in FindObjectsByType<WeizzaActor>(FindObjectsSortMode.None))
                if (actor.Directed) actor.EndDirected();
        }
    }
}
