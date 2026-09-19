using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// StoryKit's IActorService, backed by whatever WeizzaActor components are
    /// enabled in the scene right now. There is no prefab spawning yet: every
    /// actor a cutscene wants to direct has to already exist and be found by
    /// id, which covers the player and any NPC placed by hand. Spawning a
    /// fresh one from a prefab is a real gap, noted rather than faked.
    /// </summary>
    public sealed class WeizzaActorService : IActorService
    {
        private static readonly Dictionary<string, WeizzaActor> _actors = new();

        internal static void Register(WeizzaActor actor)
        {
            if (actor == null || string.IsNullOrEmpty(actor.Id)) return;
            _actors[actor.Id] = actor;
        }

        internal static void Unregister(WeizzaActor actor)
        {
            if (actor == null) return;
            if (_actors.TryGetValue(actor.Id, out var current) && current == actor)
                _actors.Remove(actor.Id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _actors.Clear();

        public UniTask<IActor> SpawnAsync(string actorId, Vector3 position, CancellationToken ct)
        {
            if (_actors.TryGetValue(actorId, out var existing))
            {
                existing.Transform.position = position;
                return UniTask.FromResult((IActor)existing);
            }

            Debug.LogWarning(
                $"[Weizza] SpawnAsync asked for actor '{actorId}', which is not placed in the " +
                "scene. There is no prefab spawning yet, only finding an existing WeizzaActor.");
            return UniTask.FromResult<IActor>(null);
        }

        public IActor Find(string actorId) =>
            _actors.TryGetValue(actorId, out var actor) ? actor : null;

        public void Despawn(string actorId)
        {
            // Nothing is ever spawned from a prefab yet, so there is nothing
            // this could destroy that was not already placed by hand.
        }
    }
}
