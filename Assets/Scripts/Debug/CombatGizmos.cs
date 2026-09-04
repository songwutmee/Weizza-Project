using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Draws hitboxes and hurtboxes over the game view, not just the scene view.
    ///
    /// Scene view gizmos are no help for the question this is for: whether the
    /// box the player is seeing matches the box the game is testing, at the
    /// speed the game actually runs. So this draws in screen space, works in a
    /// build, and can be left on during a playtest.
    /// </summary>
    public static class CombatGizmos
    {
        private static bool _enabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _enabled = false;
            Emitters.Clear();
            Bodies.Clear();
        }

        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        internal static readonly List<HitboxEmitter> Emitters = new();
        internal static readonly List<Collider> Bodies = new();

        public static void Register(HitboxEmitter emitter)
        {
            if (emitter != null && !Emitters.Contains(emitter)) Emitters.Add(emitter);
        }

        public static void Unregister(HitboxEmitter emitter) => Emitters.Remove(emitter);

        public static void RegisterBody(Collider body)
        {
            if (body != null && !Bodies.Contains(body)) Bodies.Add(body);
        }

        public static void UnregisterBody(Collider body) => Bodies.Remove(body);
    }
}
