using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Sets a story flag when this creature dies. Lets the level react to a fight
    /// (an area title, a door, Soka walking in) without the enemy knowing about it.
    /// </summary>
    [AddComponentMenu("Weizza/Story/Flag On Death")]
    [RequireComponent(typeof(Vitals))]
    public sealed class FlagOnDeath : MonoBehaviour
    {
        [Tooltip("Blackboard key set to true on death. Example: act0.ant1.down")]
        [SerializeField] private string flag = "act0.ant1.down";

        private Vitals _vitals;

        private void OnEnable()
        {
            _vitals = GetComponent<Vitals>();
            _vitals.Died += OnDied;
        }

        private void OnDisable()
        {
            if (_vitals != null) _vitals.Died -= OnDied;
        }

        private void OnDied()
        {
            if (!string.IsNullOrEmpty(flag)) StoryState.Global.Set(flag, true);
        }
    }
}
