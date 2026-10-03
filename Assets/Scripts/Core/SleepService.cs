using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One Unity CullingGroup for every OffscreenSleeper in the game. The engine
    /// tests all the spheres against the camera in one native pass during culling
    /// and only calls back when something crosses the edge, so a thousand animated
    /// plants cost nothing while they sit still on or off screen.
    ///
    /// Much cheaper than each object checking its own renderer.isVisible every
    /// frame, and it works for things that have no renderer of their own (a brain,
    /// a particle system parent). Created on first use, survives scene loads.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class SleepService : MonoBehaviour
    {
        private static SleepService _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private CullingGroup _group;
        private BoundingSphere[] _spheres = new BoundingSphere[256];
        private readonly List<OffscreenSleeper> _members = new(256);
        private readonly List<OffscreenSleeper> _moving = new(32);
        private Camera _camera;

        public static int Count => _instance != null ? _instance._members.Count : 0;

        private static SleepService Get()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[Sleep Service]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SleepService>();
            return _instance;
        }

        private void Awake()
        {
            _group = new CullingGroup { onStateChanged = OnStateChanged };
            _group.SetBoundingSpheres(_spheres);
            _group.SetBoundingSphereCount(0);
        }

        private void OnDestroy()
        {
            _group?.Dispose();
            _group = null;
            if (_instance == this) _instance = null;
        }

        public static void Register(OffscreenSleeper sleeper)
        {
            var service = Get();
            if (sleeper.Index >= 0) return;

            if (service._members.Count == service._spheres.Length)
            {
                // Grows by doubling, so a big chunk arriving costs one copy, not many.
                System.Array.Resize(ref service._spheres, service._spheres.Length * 2);
                service._group.SetBoundingSpheres(service._spheres);
            }

            sleeper.Index = service._members.Count;
            service._members.Add(sleeper);
            service._spheres[sleeper.Index] = sleeper.Sphere();
            service._group.SetBoundingSphereCount(service._members.Count);
            if (sleeper.Moves) service._moving.Add(sleeper);

            // The group only reports changes. Something that registers already off
            // screen would never hear "became invisible", so it is checked once
            // after the next cull instead.
            service._fresh.Add(sleeper);
        }

        private readonly List<OffscreenSleeper> _fresh = new(64);
        private int _freshFrame = -1;

        public static void Unregister(OffscreenSleeper sleeper)
        {
            if (_instance == null || sleeper.Index < 0) return;
            var service = _instance;
            var index = sleeper.Index;
            var last = service._members.Count - 1;

            // Swap the last one into the gap. Order never matters to a culling
            // group, and this keeps removal constant time.
            if (index != last)
            {
                var moved = service._members[last];
                service._members[index] = moved;
                service._spheres[index] = service._spheres[last];
                moved.Index = index;
            }
            service._members.RemoveAt(last);
            service._group.SetBoundingSphereCount(service._members.Count);
            if (sleeper.Moves) service._moving.Remove(sleeper);
            sleeper.Index = -1;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != _camera)
            {
                _camera = cam;
                _group.targetCamera = cam;
            }

            for (var i = 0; i < _moving.Count; i++)
            {
                var s = _moving[i];
                if (s.Index >= 0) _spheres[s.Index].position = s.transform.position;
            }

            // Fresh members are judged one frame late, after a cull has run with them in it.
            if (_fresh.Count > 0 && _camera != null)
            {
                if (_freshFrame < 0) { _freshFrame = Time.frameCount; return; }
                if (Time.frameCount == _freshFrame) return;
                foreach (var s in _fresh)
                    if (s != null && s.Index >= 0 && !_group.IsVisible(s.Index)) s.Sleep();
                _fresh.Clear();
                _freshFrame = -1;
            }
        }

        private void OnStateChanged(CullingGroupEvent e)
        {
            if (e.index < 0 || e.index >= _members.Count) return;
            if (e.hasBecomeVisible) _members[e.index].Wake();
            else if (e.hasBecomeInvisible) _members[e.index].Sleep();
        }
    }
}
