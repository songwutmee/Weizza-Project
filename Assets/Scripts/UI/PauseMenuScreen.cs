using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Weizza.Narrative;

namespace Weizza
{
    /// <summary>
    /// The pause screen. An intro flipbook plays once full screen, then a
    /// short loop repeats until the player presses pause again.
    ///
    /// Built as a real, pre-placed scene hierarchy the component only wires
    /// up, the same shape as Dialogue Canvas and DialogueScreen, not a
    /// canvas welded together in code the way ScreenFade and ItemRevealScreen
    /// do it. Those two never need adjusting by eye: a fade is just a colour
    /// and a fully generic card. A flipbook does need adjusting by eye, sizing
    /// and cropping the art against the real screen, so it has to exist in
    /// the Hierarchy at edit time rather than only appearing once Play is
    /// pressed. Hidden by CanvasGroup alpha zero, not by SetActive(false), so
    /// selecting it and pushing alpha to one previews it without entering
    /// Play mode at all.
    ///
    /// Pausing means StoryTimeScale.Stop(), not a local freeze flag or
    /// Time.timeScale written directly, because HitStop already relies on
    /// that counter surviving a freeze that lands mid pause or a pause that
    /// lands mid freeze. This menu is just one more holder of the same
    /// counter, never a second clock. StoryControl.Lock stops the player on
    /// top of that, the same one line WeizzaInputLock already answers
    /// StoryKit's IInputService with.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [AddComponentMenu("Weizza/Pause Menu Screen")]
    public sealed class PauseMenuScreen : MonoBehaviour
    {
        public static PauseMenuScreen Instance { get; private set; }

        /// <summary>True while the menu owns the screen.</summary>
        public static bool Paused { get; private set; }

        [BoxGroup("Scene")]
        [Tooltip("The CanvasGroup on this same GameObject's Canvas. Alpha zero is how the " +
                 "menu stays invisible between pauses, not SetActive(false), so it can be " +
                 "previewed in the Scene/Game view by hand without pressing Play.")]
        [SerializeField] private CanvasGroup rootGroup;

        [BoxGroup("Scene")]
        [Tooltip("The full screen Image the frames are drawn onto.")]
        [SerializeField] private Image art;

        [BoxGroup("Frames")]
        [Tooltip("Plays once, in order, every time the menu opens.")]
        [SerializeField] private Sprite[] introFrames = Array.Empty<Sprite>();

        [BoxGroup("Frames")]
        [Tooltip("Loops from the end of the intro until the menu closes.")]
        [SerializeField] private Sprite[] loopFrames = Array.Empty<Sprite>();

        [BoxGroup("Frames")]
        [PropertyRange(1f, 60f)]
        [SerializeField] private float introFps = 24f;

        [BoxGroup("Frames")]
        [PropertyRange(1f, 60f)]
        [SerializeField] private float loopFps = 18f;

        [BoxGroup("Scene")]
        [Tooltip("Shared with DialogueDirector and ItemRevealScreen. Safe because a pause " +
                 "never opens over a conversation or a pickup card, both are checked before " +
                 "opening and neither of the other two owns the volume at the same time.")]
        [SerializeField] private Volume blurVolume;

        [BoxGroup("Scene")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float blurWeight = 1f;

        [BoxGroup("Scene")]
        [PropertyRange(0f, 1.5f)]
        [SerializeField] private float transitionSeconds = 0.35f;

        private Sprite[] _introReversed = Array.Empty<Sprite>();
        private Tween _fadeTween;
        private Tween _blurTween;
        private CancellationTokenSource _life;
        private CancellationTokenSource _open;
        private IDisposable _inputLock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Paused = false;
        }

        private void Awake()
        {
            Instance = this;

            _introReversed = (Sprite[])introFrames.Clone();
            Array.Reverse(_introReversed);

            if (rootGroup != null)
            {
                rootGroup.alpha = 0f;
                rootGroup.blocksRaycasts = false;
            }

            _life = new CancellationTokenSource();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            _open?.Cancel();
            _open?.Dispose();
            _life?.Cancel();
            _life?.Dispose();
            _inputLock?.Dispose();
            _inputLock = null;
            Instance = null;
            Paused = false;
        }

        private void Update()
        {
            var reader = InputReader.Instance;
            if (reader == null || !reader.PausePressed) return;

            // A pause that could interrupt a conversation or a pickup card
            // would fight both for the same blur volume and the same input
            // lock. Neither of those two is built to expect a second owner,
            // so pause simply declines to open while either is up.
            if (SettingsScreen.IsOpen) return;
            if (!Paused && ItemRevealScreen.Showing) return;
            if (!Paused && DialogueDirector.Talking) return;

            Toggle();
        }

        public void Toggle()
        {
            if (Paused) Close();
            else Open();
        }

        /// <summary>
        /// Alt tab, a notification, the controller going to sleep: whatever pulled the
        /// player away, they come back to a paused game instead of a dead character.
        /// Build only, because the editor loses focus all the time while working.
        /// Respects the same screens the pause key does, so it never fights a talk.
        /// </summary>
        private void OnApplicationFocus(bool focused)
        {
            if (focused || Paused || Application.isEditor) return;
            if (SettingsScreen.IsOpen || ItemRevealScreen.Showing || DialogueDirector.Talking) return;
            Open();
        }

        private void Open()
        {
            if (Paused) return;
            Paused = true;

            // Reset to the first frame before anything becomes visible, not
            // after. The sprite field holds whatever frame the last session
            // ended the loop on, and starting the fade in before overwriting
            // it flashed that leftover frame for a beat, which read as the
            // sequence starting at the wrong place.
            if (art != null && introFrames.Length > 0) art.sprite = introFrames[0];

            _open?.Dispose();
            _open = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);

            _inputLock = StoryControl.Lock("Pause");
            StoryTimeScale.Stop();

            RunAsync(_open.Token, _life.Token).Forget();
        }

        private void Close()
        {
            if (!Paused) return;
            Paused = false;
            _open?.Cancel();
        }

        private async UniTaskVoid RunAsync(CancellationToken openCt, CancellationToken lifeCt)
        {
            if (rootGroup != null) rootGroup.blocksRaycasts = true;

            try
            {
                // Fade and playback run at the same time on purpose. Waiting
                // for the blur and alpha to finish creeping in before
                // advancing off frame 1 read as the menu freezing on the
                // first frame for a third of a second. The animation has to
                // start moving the instant Open() runs, the fade is just
                // riding along underneath it.
                FadeAsync(1f, openCt).Forget();
                FadeBlurAsync(blurWeight, openCt).Forget();

                await PlaySequenceOnceAsync(introFrames, introFps, openCt);
                await LoopSequenceAsync(loopFrames, loopFps, openCt);
            }
            catch (OperationCanceledException)
            {
                // The normal way this ends: the player pressed pause again,
                // or the component is being torn down (lifeCt cancelled too).
            }

            try
            {
                // The second ESC reverses the flipbook fully back down to
                // frame 1 first, no fade running alongside it. Running the
                // fade concurrently made it finish before the reverse
                // animation did, so the screen vanished mid reverse instead
                // of after landing on frame 1. Closing with a snap right
                // after the reverse actually finishes, instead of another
                // tweened fade, is what removes the "held on frame 1 a beat
                // too long" feeling from before without cutting the
                // animation short either.
                await PlaySequenceOnceAsync(_introReversed, introFps, lifeCt);

                // Without reverse: the plain fade out this had before the
                // reverse existed. Kept here, commented, in case the art
                // team wants to flip back for another comparison.
                // await UniTask.WhenAll(FadeAsync(0f, lifeCt), FadeBlurAsync(0f, lifeCt));
            }
            catch (OperationCanceledException)
            {
                // Torn down mid close. The snap below still has to run.
            }

            if (rootGroup != null)
            {
                rootGroup.alpha = 0f;
                rootGroup.blocksRaycasts = false;
            }
            if (blurVolume != null) blurVolume.weight = 0f;

            StoryTimeScale.Resume();
            _inputLock?.Dispose();
            _inputLock = null;
            Paused = false;
        }

        private async UniTask PlaySequenceOnceAsync(Sprite[] frames, float fps, CancellationToken ct)
        {
            if (frames == null || frames.Length == 0) return;

            var frameSeconds = 1f / Mathf.Max(1f, fps);
            foreach (var frame in frames)
            {
                ct.ThrowIfCancellationRequested();
                if (art != null) art.sprite = frame;
                await UniTask.Delay(
                    TimeSpan.FromSeconds(frameSeconds),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
            }
        }

        private async UniTask LoopSequenceAsync(Sprite[] frames, float fps, CancellationToken ct)
        {
            if (frames == null || frames.Length == 0) return;

            while (true)
            {
                await PlaySequenceOnceAsync(frames, fps, ct);
            }
        }

        private async UniTask FadeAsync(float target, CancellationToken ct)
        {
            if (rootGroup == null) return;

            _fadeTween.Stop();
            _fadeTween = Tween.Custom(rootGroup.alpha, target, transitionSeconds,
                v => { if (rootGroup != null) rootGroup.alpha = v; }, useUnscaledTime: true);

            await UniTask.WaitUntil(() => !_fadeTween.isAlive, PlayerLoopTiming.Update, ct);
        }

        private async UniTask FadeBlurAsync(float target, CancellationToken ct)
        {
            if (blurVolume == null) return;

            _blurTween.Stop();
            _blurTween = Tween.Custom(blurVolume.weight, target, transitionSeconds,
                v => { if (blurVolume != null) blurVolume.weight = v; }, useUnscaledTime: true);

            await UniTask.WaitUntil(() => !_blurTween.isAlive, PlayerLoopTiming.Update, ct);
        }
    }
}
