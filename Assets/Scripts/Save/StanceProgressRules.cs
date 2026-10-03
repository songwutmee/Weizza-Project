using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Opts a scene in to stance progression and says what is known at the start.
    /// </summary>
    [AddComponentMenu("Weizza/Stance Progress Rules")]
    public sealed class StanceProgressRules : MonoBehaviour
    {
        [InfoBox("With this in the scene, switching to a school the player has " +
                 "not learned is refused. Without it, every school is open, which " +
                 "is what test rooms want.")]
        [SerializeField] private List<StanceId> knownAtStart = new() { StanceId.Normal };

        private void Awake()
        {
            StanceProgress.Restricted = true;
            foreach (var id in knownAtStart) StanceProgress.Learn(id);
        }

        private void OnDestroy() => StanceProgress.Restricted = false;

        [Button("Learn all (debug)"), DisableInEditorMode]
        private void LearnAll()
        {
            StanceProgress.Learn(StanceId.Than);
            StanceProgress.Learn(StanceId.Se);
            StanceProgress.Learn(StanceId.In);
        }
    }
}
