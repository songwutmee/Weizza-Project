using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A boss move too big for an EnemyMove: one that leaves the ground, changes
    /// the screen, or strikes in several places over several seconds.
    ///
    /// MiniBossBrain still picks it by id from a phase, exactly like a move, so a
    /// fight's shape stays in the phase assets. The brain waits for Run to finish
    /// before resting. A skill is part of the body (the way EnemyController's own
    /// moves are), so it may hold the body and place it by hand while it runs; it
    /// must always give it back, even when cancelled.
    /// </summary>
    public abstract class BossSkill : MonoBehaviour
    {
        [Tooltip("The id a boss phase picks this skill by.")]
        [SerializeField] private string skillId = "skill";

        public string Id => skillId;

        /// <summary>Whether the skill can start right now. Distance, cooldown, ground.</summary>
        public virtual bool CanStart(MiniBossBrain brain) => true;

        public abstract UniTask Run(MiniBossBrain brain, CancellationToken ct);

        /// <summary>Called when the fight resets or the boss dies mid skill. Undo everything.</summary>
        public virtual void Abort() { }
    }
}
