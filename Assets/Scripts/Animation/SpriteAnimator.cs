using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One animation: a list of drawings and how fast to flip through them.
    ///
    /// No Animator Controller, no .anim files, no state graph. A hand drawn
    /// brawler does not need any of that, and all three cost real time every
    /// time a frame is added or reordered. This is the whole format: drop the
    /// sprites in, name it, done.
    /// </summary>
    [Serializable]
    public sealed class SpriteClip
    {
        [HorizontalGroup("Head", 0.35f)]
        [HideLabel]
        [Tooltip("What the code asks for by name. Keep it short and stable.")]
        public string name = "Idle";

        [HorizontalGroup("Head", 0.25f)]
        [LabelWidth(30)]
        [Tooltip("Drawings per second. Hand drawn action usually reads best " +
                 "between 10 and 15, not 60.")]
        [MinValue(1f)]
        public float fps = 12f;

        [HorizontalGroup("Head", 0.2f)]
        [ToggleLeft]
        public bool loop = true;

        [HorizontalGroup("Head", 0.2f)]
        [ToggleLeft]
        [Tooltip("For a one shot: stay on the last drawing instead of blanking.")]
        public bool hold = true;

        [ListDrawerSettings(ShowFoldout = false, DefaultExpandedState = false)]
        [Tooltip("In order. Frame zero is the first drawing shown.")]
        public Sprite[] frames;

        [Tooltip("Corrects a sheet whose ground line does not sit where the " +
                 "others do. Positive Y lifts the drawing. Fix it here rather " +
                 "than by re-slicing, so the correction is visible next to the " +
                 "animation it belongs to.")]
        public Vector2 artOffset;

        [Tooltip("Frames that raise FrameEvent. Footsteps, dust, a shout. " +
                 "Gameplay timing does not come from here, the attack data owns that.")]
        public int[] eventFrames;

        public int Length => frames?.Length ?? 0;
        public float Duration => fps <= 0f ? 0f : Length / fps;
    }

    /// <summary>
    /// Flips a SpriteRenderer through a list of drawings.
    ///
    /// It runs on scaled time on purpose. Hit stop freezes the whole game by
    /// setting the time scale to zero, and an animation that kept playing
    /// through a freeze would undo the single most important thing hit stop
    /// does, which is make a connect look like it landed on something solid.
    ///
    /// Combat asks for animations two ways. Play is for anything whose length
    /// belongs to the art, like idle and run. PlayStretched is for an attack:
    /// the frame data in AttackDefinition decides how long the swing takes, and
    /// the drawings are fitted to that, so retuning startup by two frames never
    /// leaves the art out of step with the hitbox.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Weizza/Sprite Animator")]
    public sealed class SpriteAnimator : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [SerializeField] private SpriteRenderer target;

        [BoxGroup("Setup")]
        [Tooltip("Optional. Lets a clip's art offset be applied without two " +
                 "components fighting over the same transform.")]
        [SerializeField] private SpriteFacing facing;

        [BoxGroup("Setup")]
        [Tooltip("Played on Start, if it exists.")]
        [SerializeField] private string defaultClip = "Idle";

        [ListDrawerSettings(ShowFoldout = false, DefaultExpandedState = true,
            ShowIndexLabels = false)]
        [SerializeField] private List<SpriteClip> clips = new();

        // ---- Live -----------------------------------------------------------

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly] public string CurrentClipName => _clip?.name ?? "-";

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Which drawing is on screen. Read this to line an effect up " +
                 "with the art rather than guessing at a delay.")]
        public int CurrentFrame { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Finished { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly, ProgressBar(0, 1)]
        private float Progress =>
            _clip == null || _clip.Length == 0 ? 0f : (float)CurrentFrame / _clip.Length;

        /// <summary>Raised on a frame listed in the clip's eventFrames.</summary>
        public event Action<int> FrameEvent;

        /// <summary>Raised once when a non looping clip reaches its last frame.</summary>
        public event Action<string> ClipFinished;

        private SpriteClip _clip;
        private float _timer;
        private float _fps;
        private int _lastEventFrame = -1;
        private readonly HashSet<string> _warnedMissingClips = new();
        private bool _warnedMissingTarget;

        private void Awake() => Wire();

        private void Reset() => Wire();

        private void OnValidate() => Wire();

        /// <summary>
        /// Finds the renderer and the facing on this object.
        ///
        /// Done in the editor as well as at runtime so the two Setup slots are
        /// filled in visibly. A slot that says None but works anyway at play
        /// time is worse than one that is wrong, because there is no way to tell
        /// the two apart by looking.
        /// </summary>
        private void Wire()
        {
            if (target == null) target = GetComponent<SpriteRenderer>();
            if (facing == null) facing = GetComponent<SpriteFacing>();
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(defaultClip)) Play(defaultClip);
        }

        private void Update()
        {
            if (_clip == null || _clip.Length == 0 || _fps <= 0f) return;
            if (Finished && !_clip.loop) return;

            _timer += Time.deltaTime;

            var step = 1f / _fps;
            while (_timer >= step)
            {
                _timer -= step;
                Advance();
            }
        }

        private void Advance()
        {
            var next = CurrentFrame + 1;

            if (next >= _clip.Length)
            {
                if (_clip.loop)
                {
                    next = 0;
                }
                else
                {
                    Finished = true;
                    ClipFinished?.Invoke(_clip.name);
                    if (_clip.hold) return;
                    next = _clip.Length - 1;
                }
            }

            SetFrame(next);
        }

        // ---- Playing --------------------------------------------------------

        /// <summary>
        /// Starts a clip by name. Asking for the clip that is already running
        /// does nothing, so this is safe to call every frame from a state
        /// machine without restarting the animation on every tick.
        /// </summary>
        public bool Play(string clipName, bool restart = false)
        {
            var clip = Find(clipName);
            if (clip == null)
            {
                // Warned once per name rather than returned silently. This gets
                // called every frame from a state machine, so a typo here used
                // to look exactly like a character that simply never animates,
                // with nothing in the console to say why.
                if (_warnedMissingClips.Add(clipName))
                {
                    Debug.LogWarning(
                        $"[Weizza] '{name}' has no clip called '{clipName}'. " +
                        "Check the exact spelling against the Sprite Animator's clip list.", this);
                }
                return false;
            }

            if (target == null && _warnedMissingTarget == false)
            {
                _warnedMissingTarget = true;
                Debug.LogWarning(
                    $"[Weizza] '{name}' Sprite Animator has no Target Sprite Renderer. " +
                    "It must be on the same object as the SpriteRenderer that shows the character.",
                    this);
            }

            if (ReferenceEquals(clip, _clip) && !restart) return true;

            Begin(clip, clip.fps);
            return true;
        }

        /// <summary>
        /// Starts a clip and fits it to a length in seconds, whatever its
        /// authored frame rate. This is how an attack keeps its drawings and its
        /// hitbox in step: the frame data is the truth and the art follows it.
        /// </summary>
        public bool PlayStretched(string clipName, float seconds, bool restart = true)
        {
            var clip = Find(clipName);
            if (clip == null || clip.Length == 0)
            {
                // Same silent-failure trap as Play: an attack's animationState
                // that does not match a real clip name used to swing the
                // hitbox with no visible change to the sprite at all, and
                // nothing in the console to explain why.
                if (_warnedMissingClips.Add(clipName))
                {
                    Debug.LogWarning(
                        $"[Weizza] '{name}' has no clip called '{clipName}'. Check the " +
                        "attack's Animation State field against the Sprite Animator's clip list.",
                        this);
                }
                return false;
            }

            if (ReferenceEquals(clip, _clip) && !restart) return true;

            var fps = seconds > 0f ? clip.Length / seconds : clip.fps;
            Begin(clip, fps);
            return true;
        }

        private void Begin(SpriteClip clip, float fps)
        {
            _clip = clip;
            _fps = Mathf.Max(0.01f, fps);
            _timer = 0f;
            Finished = false;
            _lastEventFrame = -1;

            if (facing != null) facing.ArtOffset = clip.artOffset;
            SetFrame(0);
        }

        public void SetFrame(int index)
        {
            if (_clip == null || _clip.Length == 0) return;

            CurrentFrame = Mathf.Clamp(index, 0, _clip.Length - 1);
            if (target != null) target.sprite = _clip.frames[CurrentFrame];

            if (_lastEventFrame == CurrentFrame) return;
            _lastEventFrame = CurrentFrame;

            if (_clip.eventFrames == null) return;
            foreach (var frame in _clip.eventFrames)
            {
                if (frame != CurrentFrame) continue;
                FrameEvent?.Invoke(CurrentFrame);
                break;
            }
        }

        public bool Has(string clipName) => Find(clipName) != null;

        private SpriteClip Find(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            foreach (var clip in clips)
            {
                if (clip != null && clip.name == clipName) return clip;
            }
            return null;
        }

        // ---- Authoring ------------------------------------------------------

#if UNITY_EDITOR

        // Editor only scratch fields. Marked NonSerialized so they are never
        // written into the scene: they are the three boxes above a button, not
        // data the component owns.

        [PropertyOrder(-10)]
        [BoxGroup("Add a clip", centerLabel: true)]
        [InfoBox("Drop a sheet in, check the suggested name, press the button. " +
                 "A clip is looked up by this exact name later, so this box is " +
                 "not cosmetic: a name that does not match what the player " +
                 "controller asks for is a character that never animates, with " +
                 "no error to explain why.")]
        [HorizontalGroup("Add a clip/Fields", 0.45f)]
        [LabelWidth(44)]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Sheet")]
        [OnValueChanged(nameof(SuggestClipName))]
        public Texture2D sheetToAdd;

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Fields", 0.25f)]
        [LabelWidth(36)]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Name")]
        public string clipNameToAdd = "";

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Fields", 0.15f)]
        [LabelWidth(28)]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Fps")]
        public float fpsToAdd = 12f;

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Fields", 0.15f)]
        [LabelWidth(36)]
        [ToggleLeft]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Loop")]
        public bool loopToAdd = true;

        /// <summary>
        /// Pulls every sliced sprite out of one sheet, in order, and adds it as
        /// a clip. Saves dragging fifteen sub sprites in one at a time and, more
        /// importantly, saves getting frame nine and frame ten the wrong way
        /// round, which is the sort of mistake that reads as a bad animation
        /// rather than as a bug.
        /// </summary>
        [PropertyOrder(-10)]
        [BoxGroup("Add a clip")]
        [Button("Add this sheet as a clip", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.82f, 0.55f)]
        /// <summary>
        /// Guesses a clip name from the sheet's file name the moment a sheet is
        /// dropped in, so the common case (Idle-Sheet.png import called
        /// Idle-Sheet) needs no typing at all, and the exact name being added
        /// is visible before the button is pressed rather than after.
        /// </summary>
        private void SuggestClipName()
        {
            if (sheetToAdd == null || !string.IsNullOrEmpty(clipNameToAdd)) return;
            clipNameToAdd = StripSheetSuffix(sheetToAdd.name);
        }

        private static string StripSheetSuffix(string sheetName)
        {
            const string suffix = "-Sheet";
            return sheetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? sheetName[..^suffix.Length]
                : sheetName;
        }

        private void AddClipFromSheet()
        {
            var sheet = sheetToAdd;
            var clipName = clipNameToAdd;
            var fps = fpsToAdd;
            var loop = loopToAdd;

            if (sheet == null)
            {
                Debug.LogWarning("[Weizza] Drop a sliced sheet into the Sheet slot first.", this);
                return;
            }

            var path = UnityEditor.AssetDatabase.GetAssetPath(sheet);
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);

            var found = new List<Sprite>();
            foreach (var asset in assets)
            {
                if (asset is Sprite sprite) found.Add(sprite);
            }

            if (found.Count == 0)
            {
                Debug.LogWarning(
                    $"[Weizza] '{sheet.name}' has no sprites in it. Set Texture Type " +
                    "to Sprite, Sprite Mode to Multiple, and slice it first.", this);
                return;
            }

            // Sub sprites come back in whatever order the importer wrote them,
            // and a plain string sort puts _10 before _2.
            found.Sort((a, b) => TrailingNumber(a.name).CompareTo(TrailingNumber(b.name)));

            var resolved = string.IsNullOrEmpty(clipName) ? StripSheetSuffix(sheet.name) : clipName;

            clips.Add(new SpriteClip
            {
                name = resolved,
                frames = found.ToArray(),
                fps = fps,
                loop = loop,
            });

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Weizza] Added '{resolved}' with {found.Count} frames.", this);

            // Empty the boxes so the next sheet starts from a clean slot rather
            // than from the last one, which is how a clip ends up added twice.
            sheetToAdd = null;
            clipNameToAdd = string.Empty;
        }

        private static int TrailingNumber(string value)
        {
            var end = value.Length;
            var start = end;
            while (start > 0 && char.IsDigit(value[start - 1])) start--;
            return start == end ? 0 : int.Parse(value[start..end]);
        }
#endif
    }
}
