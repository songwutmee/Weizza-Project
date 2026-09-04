using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Defaults
{
    [AddComponentMenu("StoryKit/Simple Actor Service")]
    public sealed class SimpleActorService : MonoBehaviour, IActorService
    {
        private static readonly Dictionary<string, IActor> _actors = new();

        public static void RegisterActor(IActor actor)
        {
            if (!string.IsNullOrEmpty(actor.Id))
            {
                _actors[actor.Id] = actor;
            }
        }

        public static void UnregisterActor(IActor actor)
        {
            if (!string.IsNullOrEmpty(actor.Id) && _actors.TryGetValue(actor.Id, out var existing) && existing == actor)
            {
                _actors.Remove(actor.Id);
            }
        }

        private void Awake()
        {
            StoryServices.Register<IActorService>(this);
        }

        private void OnDestroy()
        {
            if (StoryServices.Get<IActorService>() == (IActorService)this)
            {
                StoryServices.Register<IActorService>(null);
            }
        }

        public UniTask<IActor> SpawnAsync(string actorId, Vector3 position, CancellationToken ct)
        {
            // For a frictionless implementation, we assume actors are placed in the scene.
            // If they are missing, we warn the user.
            if (!_actors.ContainsKey(actorId))
            {
                Debug.LogWarning($"[SimpleActorService] Cannot spawn {actorId}. SimpleActorService expects actors to be pre-placed in the scene.");
                return UniTask.FromResult<IActor>(null);
            }
            
            var actor = _actors[actorId];
            actor.Transform.position = position;
            actor.Transform.gameObject.SetActive(true);
            return UniTask.FromResult(actor);
        }

        public IActor Find(string actorId)
        {
            _actors.TryGetValue(actorId, out var actor);
            return actor;
        }

        public void Despawn(string actorId)
        {
            if (_actors.TryGetValue(actorId, out var actor))
            {
                actor.Transform.gameObject.SetActive(false);
            }
        }
    }
}
