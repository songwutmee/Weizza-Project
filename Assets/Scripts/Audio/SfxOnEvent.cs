using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Plays a sound whenever a StoryKit event is raised. Comes pre-filled with
    /// every game event worth a sound (dash, wall jump, shot, stance switch,
    /// death, checkpoint, save), so the moment a clip is added to the bank
    /// under that id, it plays.
    /// </summary>
    [AddComponentMenu("Weizza/Sfx On Event")]
    public sealed class SfxOnEvent : MonoBehaviour
    {
        [Serializable]
        public sealed class Pair
        {
            public string eventName;
            public string soundId;
        }

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [SerializeField] private List<Pair> pairs = new()
        {
            new Pair { eventName = GameEvents.Dashed, soundId = "move.dash" },
            new Pair { eventName = GameEvents.WallJumped, soundId = "move.walljump" },
            new Pair { eventName = GameEvents.ShotFired, soundId = "shot.fire" },
            new Pair { eventName = GameEvents.AimSlowStarted, soundId = "aim.slow.in" },
            new Pair { eventName = GameEvents.AimSlowEnded, soundId = "aim.slow.out" },
            new Pair { eventName = GameEvents.StanceSwitched, soundId = "stance.switch" },
            new Pair { eventName = GameEvents.StanceLocked, soundId = "ui.denied" },
            new Pair { eventName = GameEvents.StanceLearned, soundId = "stance.learned" },
            new Pair { eventName = GameEvents.PlayerDied, soundId = "player.death" },
            new Pair { eventName = GameEvents.PlayerRevived, soundId = "player.revive" },
            new Pair { eventName = GameEvents.HealedOnKill, soundId = "player.heal" },
            new Pair { eventName = GameEvents.CheckpointClaimed, soundId = "world.checkpoint" },
            new Pair { eventName = GameEvents.GameSaved, soundId = "ui.save" },
            new Pair { eventName = GameEvents.BossPhaseChanged, soundId = "boss.phase" },
            new Pair { eventName = Shrine.RestedEvent, soundId = "world.shrine" },
        };

        private readonly List<IDisposable> _subscriptions = new();

        private void OnEnable()
        {
            foreach (var pair in pairs)
            {
                if (pair == null || string.IsNullOrEmpty(pair.eventName)) continue;
                var id = pair.soundId;
                _subscriptions.Add(StoryEvents.Subscribe(pair.eventName, _ => AudioDirector.Instance?.PlaySfx(id)));
            }
        }

        private void OnDisable()
        {
            foreach (var s in _subscriptions) s?.Dispose();
            _subscriptions.Clear();
        }
    }
}
