using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using Weizza.Narrative;

namespace Weizza
{
    /// <summary>
    /// The live mix on top of the Audio Director: what the game's state does to how
    /// everything sounds, worked out every frame and eased, never switched.
    ///
    /// - Ducking: music dips under spoken lines and under a heavy blow, then comes back.
    /// - Muffle: a low pass closes over music and ambience while talking, paused or
    ///   close to death, the way Hollow Knight's world goes thick at one mask.
    /// - Space: a reverb that opens in caves and lairs, chosen by where the player is
    ///   along the world, so a swing in the beetle's den rings and one in the open does not.
    /// - Heartbeat: a loop that fades in below a quarter health.
    ///
    /// Works on filters added to the director's own sources, so it needs no mixer asset
    /// and nothing to wire: the director adds this to itself.
    /// </summary>
    [AddComponentMenu("Weizza/Audio/Audio Mix")]
    [RequireComponent(typeof(AudioDirector))]
    public sealed class AudioMix : MonoBehaviour
    {
        [Serializable]
        public sealed class Space
        {
            public string name = "cave";
            [Tooltip("World x where this space starts and ends. Blends over a few metres at each edge.")]
            public float fromX, toX;
            [PropertyRange(0f, 1f), Tooltip("How much reverb. 0 dry open forest, 1 a stone room.")]
            public float reverb = 0.7f;
            [PropertyRange(0f, 1f), Tooltip("How much the music and ambience close in here.")]
            public float muffle;
        }

        [BoxGroup("Ducking", centerLabel: true)]
        [PropertyRange(-24f, 0f), Tooltip("Decibels the music drops while a line is spoken or a dialogue is open.")]
        [SerializeField] private float duckForTalkDb = -7f;

        [BoxGroup("Ducking")]
        [PropertyRange(-18f, 0f), Tooltip("Decibels the music dips for a heavy blow or a kill, for a quarter second.")]
        [SerializeField] private float duckForImpactDb = -5f;

        [BoxGroup("Muffle", centerLabel: true)]
        [Tooltip("Low pass cutoff, in Hz, when fully muffled. 22000 is open.")]
        [SerializeField] private float muffledCutoff = 900f;

        [BoxGroup("Muffle")]
        [PropertyRange(0f, 1f), Tooltip("Muffle while a dialogue is open.")]
        [SerializeField] private float talkMuffle = 0.35f;

        [BoxGroup("Muffle")]
        [PropertyRange(0f, 1f), Tooltip("Muffle while the game is paused.")]
        [SerializeField] private float pauseMuffle = 0.85f;

        [BoxGroup("Muffle")]
        [PropertyRange(0f, 1f), Tooltip("Health fraction below which the world starts to go thick.")]
        [SerializeField] private float lowHealthFrom = 0.25f;

        [BoxGroup("Muffle")]
        [SerializeField] private string heartbeatId = "player.heartbeat";

        [BoxGroup("Space", centerLabel: true)]
        [ListDrawerSettings(ShowFoldout = true)]
        [SerializeField] private List<Space> spaces = new()
        {
            new Space { name = "beetle lair", fromX = 176f, toX = 206f, reverb = 0.75f, muffle = 0.1f },
        };

        [BoxGroup("Space")]
        [Tooltip("Metres over which a space fades in at its edges.")]
        [SerializeField] private float spaceBlend = 4f;

        [FoldoutGroup("Live"), ShowInInspector, ReadOnly] public float MusicGain { get; private set; } = 1f;
        [FoldoutGroup("Live"), ShowInInspector, ReadOnly] private float _muffle;
        [FoldoutGroup("Live"), ShowInInspector, ReadOnly] private float _reverb;

        private AudioDirector _director;
        private readonly List<AudioLowPassFilter> _worldLowPass = new();
        private readonly List<AudioReverbFilter> _sfxReverb = new();
        private AudioSource _heartbeat;
        private float _impactUntil;
        private float _duckDb;

        private void Awake()
        {
            _director = GetComponent<AudioDirector>();
            foreach (var source in _director.MusicAndAmbienceSources) _worldLowPass.Add(Filter<AudioLowPassFilter>(source));
            foreach (var voice in _director.EffectVoices)
            {
                // Effects muffle with the world too, but keep their reverb send for spaces.
                _worldLowPass.Add(Filter<AudioLowPassFilter>(voice));
                var reverb = Filter<AudioReverbFilter>(voice);
                reverb.reverbPreset = AudioReverbPreset.User;
                _sfxReverb.Add(reverb);
            }

            _heartbeat = gameObject.AddComponent<AudioSource>();
            _heartbeat.loop = true;
            _heartbeat.playOnAwake = false;
            _heartbeat.spatialBlend = 0f;
            SetReverb(0f);
        }

        private static T Filter<T>(AudioSource source) where T : Behaviour
        {
            var f = source.GetComponent<T>();
            if (f == null) f = source.gameObject.AddComponent<T>();
            return f;
        }

        private void OnEnable() => CombatSignals.HitLanded += OnHit;
        private void OnDisable() => CombatSignals.HitLanded -= OnHit;

        private void OnHit(HitReport report)
        {
            // Only the blows meant to land hard: a combo of jabs must not make the music pump.
            if (report.killed || report.weight >= 8) _impactUntil = Time.unscaledTime + 0.25f;
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            var talking = DialogueDirector.Talking;
            var paused = Time.timeScale <= 0.001f;
            var health = PlayerHealth();
            var lowHealth = health < lowHealthFrom ? Mathf.InverseLerp(lowHealthFrom, 0.05f, health) : 0f;
            var space = SpaceHere(out var spaceMuffle);

            // Ducking in decibels, then to a gain, so two reasons to duck add up the way ears do.
            var duck = 0f;
            if (talking || _director.VoicePlaying) duck += duckForTalkDb;
            if (Time.unscaledTime < _impactUntil) duck += duckForImpactDb;
            _duckDb = Mathf.Lerp(_duckDb, duck, 1f - Mathf.Exp(-(duck < _duckDb ? 30f : 4f) * dt));
            MusicGain = Mathf.Pow(10f, _duckDb / 20f);

            var muffle = Mathf.Max(talking ? talkMuffle : 0f, paused ? pauseMuffle : 0f, lowHealth * 0.8f, spaceMuffle);
            _muffle = Mathf.Lerp(_muffle, muffle, 1f - Mathf.Exp(-6f * dt));
            // Exponential across the range, so each step of muffle sounds like the same amount.
            var cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(muffledCutoff), _muffle));
            foreach (var f in _worldLowPass) if (f != null) f.cutoffFrequency = cutoff;

            _reverb = Mathf.Lerp(_reverb, space, 1f - Mathf.Exp(-3f * dt));
            SetReverb(_reverb);

            Heartbeat(lowHealth, dt);
            _director.RefreshMusicVolume();
        }

        private void SetReverb(float amount)
        {
            // A small dark room, faded in by its wet level; dry stays full so hits keep their punch.
            foreach (var r in _sfxReverb)
            {
                if (r == null) continue;
                r.enabled = amount > 0.01f;
                r.dryLevel = 0f;
                r.room = Mathf.Lerp(-10000f, -600f, amount);
                r.roomHF = -800f;
                r.decayTime = Mathf.Lerp(0.6f, 2.2f, amount);
                r.reflectionsLevel = Mathf.Lerp(-10000f, -400f, amount);
                r.reverbLevel = Mathf.Lerp(-10000f, 200f, amount);
            }
        }

        private float SpaceHere(out float muffle)
        {
            muffle = 0f;
            var player = PlayerLocator.Player;
            if (player == null || spaces.Count == 0) return 0f;
            var x = player.transform.position.x;
            var best = 0f;
            foreach (var s in spaces)
            {
                var inside = Mathf.Min(Mathf.InverseLerp(s.fromX - spaceBlend, s.fromX, x),
                                       Mathf.InverseLerp(s.toX + spaceBlend, s.toX, x));
                if (inside <= 0f) continue;
                best = Mathf.Max(best, s.reverb * inside);
                muffle = Mathf.Max(muffle, s.muffle * inside);
            }
            return best;
        }

        private static float PlayerHealth()
        {
            var vitals = PlayerLocator.Player != null ? PlayerLocator.Player.Vitals : null;
            return vitals != null && vitals.Max > 0f ? vitals.Current / vitals.Max : 1f;
        }

        private void Heartbeat(float amount, float dt)
        {
            if (amount > 0.01f && !_heartbeat.isPlaying)
            {
                var entry = _director.Find(heartbeatId);
                var clip = entry?.Pick();
                if (clip == null) return;
                _heartbeat.clip = clip;
                _heartbeat.volume = 0f;
                _heartbeat.Play();
            }
            if (!_heartbeat.isPlaying) return;
            var target = amount * GameSettings.MasterVolume * GameSettings.SfxVolume;
            _heartbeat.volume = Mathf.MoveTowards(_heartbeat.volume, target, dt * 1.5f);
            if (amount <= 0.01f && _heartbeat.volume <= 0.001f) _heartbeat.Stop();
        }

        [FoldoutGroup("Live"), Button, DisableInEditorMode]
        private void TestImpactDuck() => _impactUntil = Time.unscaledTime + 0.25f;
    }
}
