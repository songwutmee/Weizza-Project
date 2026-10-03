using System;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Closes the arena while a boss fight is on: the child colliders become solid
    /// when the boss wakes and open again when the fight ends, by defeat or by the
    /// scripted death. Listens to the boss events rather than holding a reference
    /// to the boss, so it works from any chunk and with any boss.
    ///
    /// Walls are invisible on purpose. Neva never shows a gate slamming shut; the
    /// fight simply has edges.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Boss Arena")]
    public sealed class BossArena : MonoBehaviour
    {
        [InfoBox("Children with a Box Collider are the walls. They are open (off) until the boss wakes.")]
        [ShowInInspector, ReadOnly]
        private int WallCount => GetComponentsInChildren<BoxCollider>(true).Length;

        private IDisposable _started;
        private IDisposable _defeated;
        private IDisposable _forced;
        private IDisposable _reset;

        private void Awake() => SetClosed(false);

        private void OnEnable()
        {
            _started = StoryEvents.Subscribe(GameEvents.BossStarted, _ => SetClosed(true));
            _defeated = StoryEvents.Subscribe(GameEvents.BossDefeated, _ => SetClosed(false));
            _forced = StoryEvents.Subscribe(GameEvents.BossForcedDeath, _ => SetClosed(false));
            _reset = StoryEvents.Subscribe(GameEvents.BossReset, _ => SetClosed(false));
        }

        private void OnDisable()
        {
            _started?.Dispose();
            _defeated?.Dispose();
            _forced?.Dispose();
            _reset?.Dispose();
        }

        private void SetClosed(bool closed)
        {
            foreach (var wall in GetComponentsInChildren<BoxCollider>(true)) wall.enabled = closed;
        }
    }
}
