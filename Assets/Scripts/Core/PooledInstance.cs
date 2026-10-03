using UnityEngine;

namespace Weizza
{
    /// <summary>Marks an object as pooled and returns it when its lifetime ends. Added automatically by PrefabPool.</summary>
    [AddComponentMenu("")]
    public sealed class PooledInstance : MonoBehaviour
    {
        internal ComponentPool<PooledInstance> Owner;
        internal float ReturnAt = float.PositiveInfinity;

        private void Update()
        {
            if (Time.time < ReturnAt || Owner == null) return;
            ReturnAt = float.PositiveInfinity;
            Owner.Return(this);
        }
    }
}
