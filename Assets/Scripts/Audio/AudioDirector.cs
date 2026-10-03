using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Every sound in the game goes through here: effects from a small pool of
    /// sources, music on two crossfading sources, one ambience bed, voice.
    ///
    /// Answers StoryKit's IAudioService, so a flow graph's music and sound
    /// nodes work with no further wiring. Volumes come from GameSettings and
    /// update live while the settings sliders move.
    ///
    /// Builds itself on scene load when none is placed, and picks up any
    /// SoundBank in a Resources/Audio folder, so a sound added to a bank plays
    /// everywhere without touching a scene.
    /// </summary>
    [AddComponentMenu("Weizza/Audio Director")]
    [DefaultExecutionOrder(-450)]
    public sealed class AudioDirector : MonoBehaviour, IAudioService
    {
        public static AudioDirector Instance { get; private set; }

        [BoxGroup("Banks")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [SerializeField] private List<SoundBank> banks = new();

        [BoxGroup("Mix")]
        [PropertyRange(4, 32)]
        [SerializeField] private int effectVoices = 14;

        [BoxGroup("Mix")]
        [PropertyRange(0f, 1f)]
        [Tooltip("How far a sound pans left or right with its position on " +
                 "screen. Small: full panning on headphones is tiring.")]
        [SerializeField] private float panAmount = 0.35f;

        [BoxGroup("Mix")]
        [PropertyRange(0f, 0.3f)]
        [Tooltip("Fraction of the music volume kept while a voice line plays.")]
        [SerializeField] private float voiceDuck = 0.5f;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public string Music { get; private set; } = "-";

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public string Ambience { get; private set; } = "-";

        private readonly List<AudioSource> _voices = new();
        private AudioSource _musicA;
        private AudioSource _musicB;
        private AudioSource _ambience;
        private AudioSource _voice;
        private bool _musicOnA = true;
        private float _musicVolume;
        private float _ambienceVolume;
        private SoundEntry _musicEntry;
        private SoundEntry _ambienceEntry;
        private CancellationTokenSource _musicFade;
        private CancellationTokenSource _ambienceFade;
        private readonly HashSet<string> _warned = new();
        private bool _voicePlaying;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null || FindFirstObjectByType<AudioDirector>() != null) return;
            new GameObject("Audio Director").AddComponent<AudioDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            foreach (var bank in Resources.LoadAll<SoundBank>("Audio"))
            {
                if (bank != null && !banks.Contains(bank)) banks.Add(bank);
            }

            for (var i = 0; i < effectVoices; i++) _voices.Add(MakeSource("Sfx " + i));
            _musicA = MakeSource("Music A");
            _musicB = MakeSource("Music B");
            _ambience = MakeSource("Ambience");
            _voice = MakeSource("Voice");
            _musicA.loop = _musicB.loop = _ambience.loop = true;

            if (GetComponent<CombatAudio>() == null) gameObject.AddComponent<CombatAudio>();
            if (GetComponent<SfxOnEvent>() == null) gameObject.AddComponent<SfxOnEvent>();
        }

        private void OnEnable()
        {
            StoryServices.Register<IAudioService>(this);
            GameSettings.Changed += ApplyVolumes;
        }

        private void OnDisable()
        {
            StoryServices.Register<IAudioService>(null);
            GameSettings.Changed -= ApplyVolumes;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _musicFade?.Cancel();
            _ambienceFade?.Cancel();
        }

        private AudioSource MakeSource(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        // ---- Lookup ---------------------------------------------------------------

        public SoundEntry Find(string id)
        {
            foreach (var bank in banks)
            {
                var entry = bank != null ? bank.Find(id) : null;
                if (entry != null) return entry;
            }

            // A missing id is reported once, then quietly ignored. Sound being
            // absent should never stop a build being playable, but it should
            // never be silently absent either.
            if (_warned.Add(id ?? "")) Debug.Log($"[Weizza] No sound called '{id}' in any bank yet.");
            return null;
        }

        private static float CategoryVolume(SoundCategory category) => GameSettings.MasterVolume * category switch
        {
            SoundCategory.Music => GameSettings.MusicVolume,
            SoundCategory.Ambience => GameSettings.AmbienceVolume,
            SoundCategory.Voice => GameSettings.VoiceVolume,
            _ => GameSettings.SfxVolume,
        };

        // ---- Effects --------------------------------------------------------------------

        public void PlaySfx(string sfxId) => Play(sfxId, null, 1f);

        /// <summary>Plays an effect panned to where it happened on screen.</summary>
        public void PlayAt(string sfxId, Vector3 worldPoint, float volumeScale = 1f) => Play(sfxId, worldPoint, volumeScale);

        private void Play(string id, Vector3? at, float volumeScale)
        {
            var entry = Find(id);
            if (entry == null) return;
            if (Time.unscaledTime - entry.lastPlayed < entry.cooldown) return;

            var clip = entry.Pick();
            if (clip == null) return;
            entry.lastPlayed = Time.unscaledTime;

            var source = FreeVoice();
            source.clip = clip;
            source.loop = false;
            source.volume = entry.volume * volumeScale * CategoryVolume(entry.category);
            source.pitch = UnityEngine.Random.Range(entry.pitch.x, entry.pitch.y);
            source.panStereo = at.HasValue ? Pan(at.Value) : 0f;
            source.Play();
        }

        private AudioSource FreeVoice()
        {
            AudioSource oldest = _voices[0];
            foreach (var voice in _voices)
            {
                if (!voice.isPlaying) return voice;
                if (voice.time > oldest.time) oldest = voice;
            }
            return oldest;
        }

        private float Pan(Vector3 world)
        {
            var camera = Camera.main;
            if (camera == null) return 0f;
            var viewport = camera.WorldToViewportPoint(world);
            return Mathf.Clamp((viewport.x - 0.5f) * 2f * panAmount, -1f, 1f);
        }

        // ---- Music ----------------------------------------------------------------------

        public void PlayBgm(string bgmId, float crossfade)
        {
            var entry = Find(bgmId);
            if (entry == null || entry == _musicEntry) return;
            var clip = entry.Pick();
            if (clip == null) return;

            _musicEntry = entry;
            Music = bgmId;
            _musicVolume = entry.volume;

            var incoming = _musicOnA ? _musicB : _musicA;
            var outgoing = _musicOnA ? _musicA : _musicB;
            _musicOnA = !_musicOnA;

            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();

            _musicFade?.Cancel();
            _musicFade = new CancellationTokenSource();
            CrossfadeAsync(incoming, outgoing, crossfade, _musicFade.Token).Forget();
        }

        public void StopBgm(float fadeOut)
        {
            _musicEntry = null;
            Music = "-";
            _musicFade?.Cancel();
            _musicFade = new CancellationTokenSource();
            var current = _musicOnA ? _musicA : _musicB;
            CrossfadeAsync(null, current, fadeOut, _musicFade.Token).Forget();
        }

        private async UniTaskVoid CrossfadeAsync(AudioSource incoming, AudioSource outgoing, float seconds,
            CancellationToken ct)
        {
            var target = MusicTarget();
            var start = outgoing != null ? outgoing.volume : 0f;
            var elapsed = 0f;
            seconds = Mathf.Max(0.01f, seconds);

            try
            {
                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = elapsed / seconds;
                    if (incoming != null) incoming.volume = Mathf.Lerp(0f, target, t);
                    if (outgoing != null) outgoing.volume = Mathf.Lerp(start, 0f, t);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (incoming != null) incoming.volume = target;
            if (outgoing != null)
            {
                outgoing.volume = 0f;
                outgoing.Stop();
            }
        }

        private float MusicTarget() =>
            _musicVolume * CategoryVolume(SoundCategory.Music) * (_voicePlaying ? voiceDuck : 1f);

        // ---- Ambience -------------------------------------------------------------------

        public void SetAmbience(string ambienceId, float fade)
        {
            var entry = string.IsNullOrEmpty(ambienceId) ? null : Find(ambienceId);
            if (entry == _ambienceEntry) return;
            _ambienceEntry = entry;
            Ambience = entry != null ? ambienceId : "-";
            _ambienceVolume = entry != null ? entry.volume : 0f;

            _ambienceFade?.Cancel();
            _ambienceFade = new CancellationTokenSource();
            AmbienceAsync(entry != null ? entry.Pick() : null, fade, _ambienceFade.Token).Forget();
        }

        private async UniTaskVoid AmbienceAsync(AudioClip next, float fade, CancellationToken ct)
        {
            fade = Mathf.Max(0.01f, fade);
            try
            {
                var start = _ambience.volume;
                for (var t = 0f; t < fade; t += Time.unscaledDeltaTime)
                {
                    _ambience.volume = Mathf.Lerp(start, 0f, t / fade);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                _ambience.Stop();
                if (next == null) return;

                _ambience.clip = next;
                _ambience.Play();
                var target = _ambienceVolume * CategoryVolume(SoundCategory.Ambience);
                for (var t = 0f; t < fade; t += Time.unscaledDeltaTime)
                {
                    _ambience.volume = Mathf.Lerp(0f, target, t / fade);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                _ambience.volume = target;
            }
            catch (OperationCanceledException)
            {
            }
        }

        // ---- Voice -----------------------------------------------------------------------

        public async UniTask PlayVoiceAsync(string lineKey, CancellationToken ct)
        {
            var entry = Find(lineKey);
            var clip = entry?.Pick();
            if (clip == null) return;

            _voicePlaying = true;
            ApplyVolumes();
            _voice.clip = clip;
            _voice.volume = entry.volume * CategoryVolume(SoundCategory.Voice);
            _voice.Play();

            try
            {
                await UniTask.WaitWhile(() => _voice != null && _voice.isPlaying, PlayerLoopTiming.Update, ct);
            }
            catch (OperationCanceledException)
            {
                if (_voice != null) _voice.Stop();
                throw;
            }
            finally
            {
                _voicePlaying = false;
                ApplyVolumes();
            }
        }

        private void ApplyVolumes()
        {
            var music = _musicOnA ? _musicA : _musicB;
            if (music != null && music.isPlaying) music.volume = MusicTarget();
            if (_ambience != null && _ambience.isPlaying && _ambienceEntry != null)
                _ambience.volume = _ambienceVolume * CategoryVolume(SoundCategory.Ambience);
        }

        [FoldoutGroup("Live")]
        [Button, DisableInEditorMode]
        private void TestSound(string id = "hit.light") => PlaySfx(id);
    }
}
