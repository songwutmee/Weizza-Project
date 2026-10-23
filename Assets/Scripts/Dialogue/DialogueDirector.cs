using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.Rendering;

namespace Weizza.Narrative
{
    /// <summary>
    /// Runs a conversation and takes the world out of the way while it does.
    ///
    /// Three things happen together and none of them is optional. The HUD goes,
    /// because health bars over a close up read as a bug report. The scene
    /// blurs, because a portrait drawn at full detail against a background drawn
    /// at full detail gives the eye nowhere to land. And whatever was driving
    /// the player stops, because walking away mid sentence leaves the panel
    /// talking to an empty street.
    ///
    /// It is a director in the narrow sense: it sequences and it cleans up. What
    /// a line says, who says it, and which face they wear are all answered
    /// elsewhere.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public sealed class DialogueDirector : MonoBehaviour
    {
        [TitleGroup("Scene")]
        [Tooltip("Turned off for the length of the conversation. The player " +
                 "controller belongs here, and so does anything else that would " +
                 "keep acting on input while the panel is up.")]
        [SerializeField] private List<Behaviour> suspendWhileTalking = new();

        [TitleGroup("Scene")]
        [Tooltip("Hidden for the length of the conversation.")]
        [SerializeField] private List<GameObject> hideWhileTalking = new();

        [TitleGroup("Scene")]
        [Tooltip("A local volume holding the blur. Its weight is driven from zero " +
                 "to one as the panel opens, so the amount of blur is tuned on " +
                 "the volume itself rather than in code.")]
        [SerializeField] private Volume blurVolume;

        [TitleGroup("Scene")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float blurSeconds = 0.25f;

        [TitleGroup("Scene")]
        [ToggleLeft, Tooltip("Blur the world behind the dialogue panel. Off by default: the camera lifts the player and the speaker above the panel so both stay readable.")]
        [SerializeField] private bool blurBehindPanel;

        [TitleGroup("Scene")]
        [Tooltip("Optional. A StoryKit screen service, if this game has one, so " +
                 "that a HUD it owns goes away with the rest. The two lists above " +
                 "cover everything else.")]
        [SerializeField] private MonoBehaviour screenService;

        private static DialogueDirector _instance;
        private CancellationTokenSource _running;

        /// <summary>
        /// The optional screen service, if one was assigned.
        ///
        /// Taken from a field rather than asked of StoryServices, because asking
        /// for a service nobody registered logs a warning, and this one is meant
        /// to be absent in a game that hides its HUD through the lists above. A
        /// warning about something working as intended is how a console stops
        /// being worth reading.
        /// </summary>
        private IScreenUI screenUI => screenService as IScreenUI;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public static DialogueDirector Instance => _instance;

        /// <summary>True while a conversation owns the screen.</summary>
        public static bool Talking { get; private set; }

        /// <summary>
        /// Who started the conversation, so a speech bubble knows whose head to sit
        /// over when a line names a speaker with no Story Stage Id of their own.
        /// </summary>
        public static Transform Speaker { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTalking()
        {
            Talking = false;
            Speaker = null;
        }

        private void Awake()
        {
            _instance = this;
            if (blurVolume != null) blurVolume.weight = 0f;
        }

        private void OnDestroy()
        {
            if (_instance != this) return;

            _running?.Cancel();
            _running?.Dispose();
            _running = null;
            _instance = null;
            Talking = false;
        }

        /// <summary>
        /// Plays a conversation. Safe to call again while one is running: the
        /// second call is dropped rather than queued, because the usual cause is
        /// a second interact press landing on the same NPC.
        /// </summary>
        public void Play(Conversation conversation, Transform speaker = null)
        {
            if (conversation == null || Talking) return;
            Speaker = speaker;

            _running?.Dispose();
            _running = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            PlayAsync(conversation, _running.Token).Forget();
        }

        private async UniTaskVoid PlayAsync(Conversation conversation, CancellationToken ct)
        {
            var ui = StoryServices.Get<IDialogueUI>();
            if (ui == null)
            {
                Debug.LogWarning("[Weizza] No dialogue UI is registered, so nothing can be said.");
                return;
            }

            Talking = true;
            Suspend(true);

            // Locked through StoryControl as well as by switching the controller off:
            // the lock is what every other system asks (the actor goes idle and keeps
            // its gravity, enemies and tools see that a scene is running), and it is
            // counted, so a cutscene that also locks cannot be released by this one.
            var movementLock = StoryControl.Lock("dialogue");

            // A bubble keeps the world in view on purpose, so it gets no blur, and the
            // panel only blurs when asked: the camera now lifts the two speakers above
            // the panel, and a blur would hide the very people talking.
            var blurs = blurBehindPanel && ui is not BubbleDialogueScreen;

            try
            {
                // Dressed before the fade, so the face and the frame arrive as
                // one movement rather than as a panel that fills in afterwards.
                var opening = First(conversation);
                if (ui is DialogueScreen screen && opening != null)
                {
                    screen.Prime(opening.speakerId, opening.emotion);
                }

                var blur = blurs ? FadeBlur(1f, ct) : UniTask.CompletedTask;
                await ui.OpenAsync(ct);
                await blur;

                var loca = StoryServices.Get<ILocalizationService>();
                foreach (var line in conversation.Lines)
                {
                    if (line == null) continue;

                    var text = loca != null ? loca.Resolve(line.textKey) : line.textKey;
                    await ui.SayAsync(line.speakerId, line.emotion, text, ct);
                }

                await ui.CloseAsync(ct);
                if (blurs) await FadeBlur(0f, ct);
            }
            catch (System.OperationCanceledException)
            {
                // Leaving play mode or destroying the director lands here. The
                // finally block is what matters, so there is nothing to log.
            }
            finally
            {
                movementLock.Dispose();
                Suspend(false);
                Talking = false;
                Speaker = null;
                if (blurVolume != null) blurVolume.weight = 0f;
            }
        }

        private static Conversation.Line First(Conversation conversation)
        {
            foreach (var line in conversation.Lines)
            {
                if (line != null) return line;
            }

            return null;
        }

        private void Suspend(bool talking)
        {
            foreach (var behaviour in suspendWhileTalking)
            {
                if (behaviour != null) behaviour.enabled = !talking;
            }

            foreach (var shown in hideWhileTalking)
            {
                if (shown != null) shown.SetActive(!talking);
            }

            if (screenUI != null) screenUI.SetHudVisible(!talking);
        }

        private async UniTask FadeBlur(float target, CancellationToken ct)
        {
            if (blurVolume == null) return;

            if (blurSeconds <= 0f)
            {
                blurVolume.weight = target;
                return;
            }

            var from = blurVolume.weight;
            var elapsed = 0f;
            while (elapsed < blurSeconds)
            {
                // Unscaled, because a conversation that opens during a hitstop
                // would otherwise blur in slow motion or not at all.
                elapsed += Time.unscaledDeltaTime;
                blurVolume.weight = Mathf.Lerp(from, target, elapsed / blurSeconds);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            blurVolume.weight = target;
        }
    }
}
