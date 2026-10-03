using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Stops this object's animation, particles and (optionally) brain while it is
    /// off screen, and starts them again a margin before it comes back in. Put it on
    /// animated plants, waterfalls, particle props and background creatures.
    ///
    /// A linear level keeps 45 m ahead and 25 m behind loaded, about five screens,
    /// so most of what is loaded is never on screen at once. Without this every
    /// plant in those five screens swaps sprites every frame for nobody.
    /// </summary>
    [AddComponentMenu("Weizza/Performance/Offscreen Sleeper")]
    [DisallowMultipleComponent]
    public sealed class OffscreenSleeper : MonoBehaviour
    {
        [BoxGroup("What sleeps")]
        [ToggleLeft]
        [Tooltip("Sprite animators, Animators and particle systems on this object and its children.")]
        [SerializeField] private bool sleepAnimation = true;

        [BoxGroup("What sleeps")]
        [ToggleLeft]
        [Tooltip("Enemy brains too. Off by default: an enemy that freezes the moment it leaves the screen can be " +
                 "walked back to in the same pose, which reads as a bug in a fight.")]
        [SerializeField] private bool brains;

        [BoxGroup("What sleeps")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = false)]
        [Tooltip("Any other components to switch off while off screen.")]
        [SerializeField] private Behaviour[] extra = new Behaviour[0];

        [BoxGroup("Size")]
        [PropertyRange(0f, 10f)]
        [Tooltip("Metres past the screen edge where it wakes, so it is already moving when it comes into view.")]
        [SerializeField] private float wakeMargin = 2f;

        [BoxGroup("Size")]
        [ToggleLeft]
        [Tooltip("Follow the object as it moves. Only for things that move (creatures). Static props leave this off, it costs a little each frame.")]
        [SerializeField] private bool moves;

        [ShowInInspector, ReadOnly] public bool Asleep { get; private set; }

        internal int Index { get; set; } = -1;
        internal bool Moves => moves;

        private readonly List<Behaviour> _behaviours = new();
        private readonly List<ParticleSystem> _particles = new();
        private float _radius;

        private void Awake()
        {
            // Radius from the drawn size, measured once. Renderer bounds are already
            // in world space and include every child sprite.
            var bounds = new Bounds(transform.position, Vector3.zero);
            var any = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            _radius = (any ? bounds.extents.magnitude : 1f) + wakeMargin;

            if (sleepAnimation)
            {
                foreach (var a in GetComponentsInChildren<SpriteAnimator>(true)) _behaviours.Add(a);
                foreach (var a in GetComponentsInChildren<Animator>(true)) _behaviours.Add(a);
                _particles.AddRange(GetComponentsInChildren<ParticleSystem>(true));
            }
            if (brains)
            {
                foreach (var b in GetComponentsInChildren<EnemyController>(true)) _behaviours.Add(b);
                foreach (var b in GetComponentsInChildren<PatrolBrain>(true)) _behaviours.Add(b);
            }
            foreach (var b in extra) if (b != null) _behaviours.Add(b);
        }

        private void OnEnable() => SleepService.Register(this);
        private void OnDisable()
        {
            SleepService.Unregister(this);
            Wake();
        }

        internal BoundingSphere Sphere() => new(transform.position, _radius);

        internal void Sleep()
        {
            if (Asleep) return;
            Asleep = true;
            foreach (var b in _behaviours) if (b != null) b.enabled = false;
            _wasPlaying.Clear();
            foreach (var p in _particles)
            {
                // Only resume what was actually running, or a one shot burst that
                // already finished would fire again every time it scrolls back in.
                var playing = p != null && p.isPlaying;
                _wasPlaying.Add(playing);
                if (playing) p.Pause(true);
            }
        }

        private readonly List<bool> _wasPlaying = new();

        internal void Wake()
        {
            if (!Asleep) return;
            Asleep = false;
            foreach (var b in _behaviours) if (b != null) b.enabled = true;
            for (var i = 0; i < _particles.Count && i < _wasPlaying.Count; i++)
                if (_wasPlaying[i] && _particles[i] != null) _particles[i].Play(true);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Asleep ? new Color(0.5f, 0.5f, 0.9f, 0.6f) : new Color(0.5f, 0.9f, 0.6f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? _radius : wakeMargin + 1f);
        }
#endif
    }
}
