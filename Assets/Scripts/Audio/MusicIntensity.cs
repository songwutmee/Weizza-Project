using System;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Switches music between exploring, fighting and a boss, on the events
    /// those moments already raise. Neva's music swells for fights and falls
    /// back after; this is that, with nothing in combat code knowing about it.
    /// </summary>
    [AddComponentMenu("Weizza/Music Intensity")]
    public sealed class MusicIntensity : MonoBehaviour
    {
        [BoxGroup("Tracks")] [SerializeField] private string exploreId = "bgm.forest";
        [BoxGroup("Tracks")] [SerializeField] private string combatId = "bgm.combat";
        [BoxGroup("Tracks")] [SerializeField] private string bossId = "bgm.boss";

        [BoxGroup("Tracks")]
        [PropertyRange(0.1f, 5f)]
        [SerializeField] private float crossfade = 1.2f;

        [BoxGroup("Tracks")]
        [ToggleLeft]
        [Tooltip("Start the explore track on scene start.")]
        [SerializeField] private bool playOnStart = true;

        private IDisposable[] _subscriptions;
        private int _fights;
        private bool _boss;

        private void Start()
        {
            if (playOnStart) AudioDirector.Instance?.PlayBgm(exploreId, crossfade);
        }

        private void OnEnable()
        {
            _subscriptions = new[]
            {
                StoryEvents.Subscribe(GameEvents.EncounterStarted, _ => { _fights++; Refresh(); }),
                StoryEvents.Subscribe(GameEvents.EncounterEnded, _ => { _fights = Mathf.Max(0, _fights - 1); Refresh(); }),
                StoryEvents.Subscribe(GameEvents.BossStarted, _ => { _boss = true; Refresh(); }),
                StoryEvents.Subscribe(GameEvents.BossDefeated, _ => { _boss = false; Refresh(); }),
                StoryEvents.Subscribe(GameEvents.BossForcedDeath, _ => { _boss = false; Refresh(); }),
                StoryEvents.Subscribe(GameEvents.BossReset, _ => { _boss = false; Refresh(); }),
            };
        }

        private void OnDisable()
        {
            if (_subscriptions == null) return;
            foreach (var s in _subscriptions) s?.Dispose();
            _subscriptions = null;
        }

        private void Refresh()
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;
            var id = _boss ? bossId : _fights > 0 ? combatId : exploreId;
            audio.PlayBgm(id, crossfade);
        }
    }
}
