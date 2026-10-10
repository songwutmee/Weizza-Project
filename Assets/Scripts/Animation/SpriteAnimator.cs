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
        [HorizontalGroup("Head", 0.46f)]
        [HideLabel]
        [GUIColor(nameof(HeadColour))]
        [Tooltip("What the code asks for by name. Keep it short and stable.")]
        public string name = "Idle";

        [HorizontalGroup("Head", 0.18f)]
        [LabelText("Fps"), LabelWidth(26)]
        [Tooltip("Drawings per second. Hand drawn action usually reads best " +
                 "between 10 and 15, not 60.")]
        [MinValue(1f)]
        public float fps = 12f;

        [HorizontalGroup("Head", 0.18f)]
        [ToggleLeft, LabelWidth(36)]
        [LabelText("Loop")]
        public bool loop = true;

        [HorizontalGroup("Head", 0.18f)]
        [ToggleLeft, LabelWidth(36)]
        [LabelText("Hold")]
        [Tooltip("For a one shot: stay on the last drawing instead of blanking.")]
        public bool hold = true;

        // Everything below folds away. A clip with thirty drawings in it used
        // to push every other clip off the bottom of the panel, which made a
        // list of eight animations unusable for finding anything in.
        [FoldoutGroup("Frames and offsets")]
        [ShowInInspector, DisplayAsString, HideLabel]
        private string Readout =>
            Length == 0
                ? "no drawings yet, this clip will not play"
                : $"{Length} drawings, {Duration * 1000f:0} ms at {fps:0.#} fps";

        [FoldoutGroup("Frames and offsets")]
        [ListDrawerSettings(ShowFoldout = false, DefaultExpandedState = true,
            ShowIndexLabels = true, DraggableItems = true)]
        [Tooltip("In order. Frame zero is the first drawing shown.")]
        public Sprite[] frames;

        [FoldoutGroup("Frames and offsets")]
        [Tooltip("Corrects a sheet whose ground line does not sit where the " +
                 "others do. Positive Y lifts the drawing. Fix it here rather " +
                 "than by re-slicing, so the correction is visible next to the " +
                 "animation it belongs to.")]
        public Vector2 artOffset;

        [FoldoutGroup("Frames and offsets")]
        [ToggleLeft]
        [Tooltip("This clip is drawn facing the other way from the character's other clips, " +
                 "so it is mirrored. Sheets drawn by different people disagree about " +
                 "which way is forward, and the swing, the hitbox and the slash all follow " +
                 "the real facing, so a clip that looks the wrong way reads as a bug.")]
        public bool mirror;

        [FoldoutGroup("Frames and offsets")]
        [Tooltip("Frames that raise FrameEvent. Footsteps, dust, a shout. " +
                 "Gameplay timing does not come from here, the attack data owns that.")]
        public int[] eventFrames;

        /// <summary>
        /// Red for a clip with no drawings in it, because that is the one
        /// broken state that still looks perfectly fine in the list.
        /// </summary>
        private Color HeadColour => Length == 0
            ? new Color(1f, 0.55f, 0.55f)
            : new Color(0.72f, 0.93f, 0.78f);

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

        [BoxGroup("Setup")]
        [InfoBox("Only for prototyping, while most of the art does not exist yet. " +
                 "A missing clip borrows this one instead of leaving the " +
                 "character frozen, and still logs once so it never quietly " +
                 "ships that way. Empty this the moment the real clips exist.")]
        [Tooltip("Stand in for any clip that has not been drawn yet.")]
        [SerializeField] private string fallbackClip = "";

        [PropertyOrder(-5)]
        [ShowInInspector, DisplayAsString, HideLabel]
        [GUIColor(nameof(RosterColour))]
        [PropertySpace(6f, 2f)]
        private string Roster
        {
            get
            {
                if (clips == null || clips.Count == 0) return "No clips yet. Add one below.";

                var empty = 0;
                foreach (var clip in clips)
                {
                    if (clip == null || clip.Length == 0) empty++;
                }

                return empty == 0
                    ? $"{clips.Count} clips, all with drawings in them"
                    : $"{clips.Count} clips, {empty} of them empty and will not play";
            }
        }

        private Color RosterColour()
        {
            if (clips == null || clips.Count == 0) return new Color(1f, 0.85f, 0.55f);

            foreach (var clip in clips)
            {
                if (clip == null || clip.Length == 0) return new Color(1f, 0.6f, 0.55f);
            }

            return new Color(0.7f, 0.92f, 0.76f);
        }

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
            var clip = Resolve(clipName);
            if (clip == null) return false;

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
            var clip = Resolve(clipName);
            if (clip == null || clip.Length == 0) return false;

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

            if (facing != null)
            {
                facing.ArtOffset = clip.artOffset;
                facing.ClipMirror = clip.mirror;
            }
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

        /// <summary>
        /// The clip itself, for code that needs to know its length or loop
        /// flag before deciding how long to wait on it. Play and PlayStretched
        /// stay the only ways to actually start one.
        /// </summary>
        public SpriteClip GetClip(string clipName) => Find(clipName);

        /// <summary>The first drawing of the idle clip (or of the first clip), for the Edit mode preview.</summary>
        public Sprite FirstIdleFrame
        {
            get
            {
                SpriteClip pick = null;
                foreach (var clip in clips)
                {
                    if (clip == null || clip.Length == 0) continue;
                    if (pick == null) pick = clip;
                    if (clip.name.IndexOf("Idle", System.StringComparison.OrdinalIgnoreCase) >= 0) { pick = clip; break; }
                }
                return pick?.frames[0];
            }
        }

        public SpriteRenderer Target => target != null ? target : GetComponent<SpriteRenderer>();

        /// <summary>
        /// Finds a clip, or the stand in, and says so once either way.
        ///
        /// The warning is the important half. A missing clip used to look
        /// exactly like a character that simply never animates, and a silent
        /// stand in would be worse again: the game would look finished while
        /// half the moves were borrowing one drawing.
        /// </summary>
        private SpriteClip Resolve(string clipName)
        {
            var clip = Find(clipName);
            if (clip != null) return clip;

            var stand = string.IsNullOrEmpty(fallbackClip) ? null : Find(fallbackClip);

            if (_warnedMissingClips.Add(clipName))
            {
                if (stand != null)
                {
                    Debug.LogWarning(
                        $"[Weizza] '{name}' has no clip '{clipName}', using '{fallbackClip}' " +
                        "as a stand in. Fine while the art is missing, not fine to ship.", this);
                }
                else
                {
                    Debug.LogWarning(
                        $"[Weizza] '{name}' has no clip called '{clipName}'. Check the exact " +
                        "spelling against the Sprite Animator's clip list, or set a Fallback " +
                        "Clip while the art does not exist yet.", this);
                }
            }

            return stand;
        }

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

        // Everything in this section stacks vertically on purpose. A labelled
        // field inside a fixed width horizontal group is unreadable the moment
        // the inspector panel is narrow, and the inspector panel is narrow most
        // of the time. Only the button rows go sideways, because buttons have
        // no label to collide with their own value.

        [PropertyOrder(-12)]
        [BoxGroup("Add a clip", centerLabel: true)]
        [InfoBox("Drop a sheet in, name it, press the green button. The name is " +
                 "how code finds the clip, so a typo is a character that never " +
                 "animates and never says why.")]
        [ShowInInspector, DisplayAsString(false), HideLabel]
        private string AddHint => "";

        [PropertyOrder(-11)]
        [BoxGroup("Add a clip")]
        [PreviewField(64, ObjectFieldAlignment.Center)]
        [HideLabel]
        [ShowInInspector, System.NonSerialized]
        [OnValueChanged(nameof(SuggestClipName))]
        public Texture2D sheetToAdd;

        // Common names as one press each. Typing them by hand is where the
        // silent misspellings come from, and a clip called Attak animates
        // nothing while looking completely correct in the list.
        [PropertyOrder(-10)]
        [BoxGroup("Add a clip")]
        [HorizontalGroup("Add a clip/Quick")]
        [Button("Idle"), GUIColor(0.62f, 0.78f, 0.95f)]
        private void QuickIdle() => SetQuick("Idle", 8f, true);

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Quick")]
        [Button("Walk"), GUIColor(0.62f, 0.78f, 0.95f)]
        private void QuickWalk() => SetQuick("Walk", 10f, true);

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Quick")]
        [Button("Run"), GUIColor(0.62f, 0.78f, 0.95f)]
        private void QuickRun() => SetQuick("Run", 14f, true);

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Quick")]
        [Button("Attack"), GUIColor(0.95f, 0.82f, 0.5f)]
        private void QuickAttack() => SetQuick("Attack", 14f, false);

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Quick")]
        [Button("Hit"), GUIColor(0.95f, 0.72f, 0.45f)]
        private void QuickHit() => SetQuick("Hit", 12f, false);

        [PropertyOrder(-10)]
        [HorizontalGroup("Add a clip/Quick")]
        [Button("Death"), GUIColor(0.9f, 0.55f, 0.5f)]
        private void QuickDeath() => SetQuick("Death", 10f, false);

        [PropertyOrder(-9)]
        [BoxGroup("Add a clip")]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Clip name")]
        [GUIColor(nameof(NameColour))]
        public string clipNameToAdd = "";

        [PropertyOrder(-9)]
        [BoxGroup("Add a clip")]
        [PropertyRange(1f, 30f)]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Frames per second")]
        public float fpsToAdd = 12f;

        [PropertyOrder(-9)]
        [BoxGroup("Add a clip")]
        [ToggleLeft]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Loops forever")]
        public bool loopToAdd = true;

        // Hand drawn art arrives as one PNG per frame, not as a grid sheet,
        // because drawn poses have wildly different bounding boxes and a
        // uniform grid pays for the widest one in every single cell.
        [PropertyOrder(-8)]
        [BoxGroup("Add a clip")]
        [FolderPath]
        [ShowInInspector, System.NonSerialized]
        [LabelText("Folder of frames")]
        [Tooltip("For art delivered as one PNG per frame. Ordered by the number " +
                 "at the end of the file name, so Attack_2 comes before Attack_10 " +
                 "rather than after it.")]
        public string frameFolderToAdd = "";

        [PropertyOrder(-8)]
        [BoxGroup("Add a clip")]
        [Button("Add every frame in that folder", ButtonSizes.Large)]
        [GUIColor(0.5f, 0.75f, 0.9f)]
        [EnableIf("@!string.IsNullOrEmpty(this.frameFolderToAdd)")]
        private void AddClipFromFolderButton()
        {
            if (!AddClipFromFolder(frameFolderToAdd, clipNameToAdd, fpsToAdd, loopToAdd)) return;
            clipNameToAdd = string.Empty;
        }

        private void SetQuick(string clipName, float fps, bool loop)
        {
            clipNameToAdd = clipName;
            fpsToAdd = fps;
            loopToAdd = loop;
        }

        private string AddButtonLabel
        {
            get
            {
                if (sheetToAdd == null) return "Drop a sliced sheet in first";
                var resolved = string.IsNullOrEmpty(clipNameToAdd)
                    ? StripSheetSuffix(sheetToAdd.name)
                    : clipNameToAdd;

                return Has(resolved)
                    ? $"Replace '{resolved}' with this sheet"
                    : $"Add '{resolved}'";
            }
        }

        private Color NameColour() => string.IsNullOrWhiteSpace(clipNameToAdd)
            ? new Color(1f, 0.72f, 0.6f)
            : Has(clipNameToAdd)
                ? new Color(1f, 0.9f, 0.55f)
                : new Color(0.72f, 0.93f, 0.78f);

        /// <summary>
        /// Pulls every sliced sprite out of one sheet, in order, and adds it as
        /// a clip. Saves dragging fifteen sub sprites in one at a time and, more
        /// importantly, saves getting frame nine and frame ten the wrong way
        /// round, which is the sort of mistake that reads as a bad animation
        /// rather than as a bug.
        /// </summary>
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

        public static string StripSheetSuffix(string sheetName)
        {
            const string suffix = "-Sheet";
            return sheetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? sheetName[..^suffix.Length]
                : sheetName;
        }

        // The attributes below sit directly on the method they fire, with no
        // doc comment between them. A doc comment in that gap is not a comment
        // to the compiler, it is the next member's documentation, and Odin
        // bound this button to whatever declaration followed it: the button
        // used to call SuggestClipName and AddClipFromSheet was never reached.
        [PropertyOrder(-10)]
        [BoxGroup("Add a clip")]
        [Button("$AddButtonLabel", ButtonSizes.Gigantic)]
        [GUIColor(0.45f, 0.82f, 0.55f)]
        [EnableIf("@this.sheetToAdd != null")]
        private void AddClipFromSheetButton()
        {
            if (!AddClipFromSheet(sheetToAdd, clipNameToAdd, fpsToAdd, loopToAdd)) return;

            // Empty the boxes so the next sheet starts from a clean slot rather
            // than from the last one, which is how a clip ends up added twice.
            sheetToAdd = null;
            clipNameToAdd = string.Empty;
        }

        /// <summary>
        /// Pulls every sliced sprite out of one sheet, in order, and adds it as
        /// a clip. Saves dragging fifteen sub sprites in one at a time and, more
        /// importantly, saves getting frame nine and frame ten the wrong way
        /// round, which is the sort of mistake that reads as a bad animation
        /// rather than as a bug.
        ///
        /// Public so a builder tool can assemble a whole character's clip list
        /// without a human pressing this button once per animation.
        /// </summary>
        public bool AddClipFromFolder(string folder, string clipName, float fps, bool loop)
        {
            if (string.IsNullOrEmpty(folder) || UnityEditor.AssetDatabase.IsValidFolder(folder) == false)
            {
                Debug.LogWarning($"[Weizza] '{folder}' is not a folder in this project.", this);
                return false;
            }

            var found = new List<Sprite>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) found.Add(sprite);
            }

            if (found.Count == 0)
            {
                Debug.LogWarning(
                    $"[Weizza] No sprites in '{folder}'. The PNGs are probably still " +
                    "importing as plain textures: set Texture Type to Sprite on them first.",
                    this);
                return false;
            }

            found.Sort((a, b) => TrailingNumber(a.name).CompareTo(TrailingNumber(b.name)));

            var resolved = string.IsNullOrEmpty(clipName)
                ? System.IO.Path.GetFileName(folder)
                : clipName;

            clips.RemoveAll(existing => existing != null && existing.name == resolved);
            clips.Add(new SpriteClip
            {
                name = resolved,
                frames = found.ToArray(),
                fps = fps,
                loop = loop,
            });

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Weizza] Added '{resolved}' with {found.Count} frames from {folder}.", this);
            return true;
        }

        public bool AddClipFromSheet(Texture2D sheet, string clipName, float fps, bool loop)
        {
            if (sheet == null)
            {
                Debug.LogWarning("[Weizza] Drop a sliced sheet into the Sheet slot first.", this);
                return false;
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
                return false;
            }

            // Sub sprites come back in whatever order the importer wrote them,
            // and a plain string sort puts _10 before _2.
            found.Sort((a, b) => TrailingNumber(a.name).CompareTo(TrailingNumber(b.name)));

            var resolved = string.IsNullOrEmpty(clipName) ? StripSheetSuffix(sheet.name) : clipName;

            // Replacing rather than appending, so running a builder twice over
            // the same character does not leave two clips answering to one name
            // with only the first one ever findable.
            clips.RemoveAll(existing => existing != null && existing.name == resolved);

            clips.Add(new SpriteClip
            {
                name = resolved,
                frames = found.ToArray(),
                fps = fps,
                loop = loop,
            });

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Weizza] Added '{resolved}' with {found.Count} frames.", this);
            return true;
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
