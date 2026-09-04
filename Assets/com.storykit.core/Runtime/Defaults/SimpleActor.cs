using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace StoryKit.Defaults
{
    [AddComponentMenu("StoryKit/Simple Actor")]
    public sealed class SimpleActor : MonoBehaviour, IActor
    {
        [SerializeField] private string _id;
        public string Id => _id;
        public Transform Transform => transform;

        private SimpleAnimationController _animController;
        private NavMeshAgent _agent;

        private void Awake()
        {
            _animController = GetComponentInChildren<SimpleAnimationController>();
            _agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable()
        {
            SimpleActorService.RegisterActor(this);
        }

        private void OnDisable()
        {
            SimpleActorService.UnregisterActor(this);
        }

        public async UniTask MoveToAsync(Vector3 position, float speed, CancellationToken ct)
        {
            if (_agent != null)
            {
                _agent.speed = speed;
                _agent.SetDestination(position);
                
                while (_agent.pathPending || _agent.remainingDistance > 0.1f)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                _agent.ResetPath();
            }
            else
            {
                // Fallback basic move
                float duration = Vector3.Distance(transform.position, position) / speed;
                float elapsed = 0f;
                Vector3 start = transform.position;

                while (elapsed < duration)
                {
                    ct.ThrowIfCancellationRequested();
                    elapsed += Time.deltaTime;
                    transform.position = Vector3.Lerp(start, position, elapsed / duration);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                transform.position = position;
            }
        }

        public UniTask PlayAnimationAsync(string animationName, bool waitForEnd, CancellationToken ct)
        {
            if (_animController != null)
            {
                _animController.PlayAnimation(animationName);
            }
            
            // In a simple implementation, we just fire it. 
            // We could wait for the Animator state to finish, but for frictionless setup, 
            // we'll just wait a fixed small amount or not at all to prevent softlocks.
            if (waitForEnd)
            {
                return UniTask.Delay(System.TimeSpan.FromSeconds(1f), cancellationToken: ct);
            }
            
            return UniTask.CompletedTask;
        }

        public void LookAt(Vector3 worldPosition)
        {
            Vector3 dir = (worldPosition - transform.position).normalized;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(dir);
            }
        }
    }
}
