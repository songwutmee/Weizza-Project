using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Turns every hit into a sound, by what the hit was. Listens to
    /// CombatSignals, so nothing in the player or enemy code knows audio exists.
    ///
    /// Heavy and light are split on the hit's own hit stop frames, the number
    /// already tuned for how heavy a hit should read, so the sound and the
    /// freeze always agree.
    /// </summary>
    [AddComponentMenu("Weizza/Combat Audio")]
    public sealed class CombatAudio : MonoBehaviour
    {
        [BoxGroup("Ids")] [SerializeField] private string lightHit = "hit.light";
        [BoxGroup("Ids")] [SerializeField] private string heavyHit = "hit.heavy";
        [BoxGroup("Ids")] [SerializeField] private string armoredHit = "hit.armored";
        [BoxGroup("Ids")] [SerializeField] private string ignoredHit = "hit.ignored";
        [BoxGroup("Ids")] [SerializeField] private string playerHurt = "player.hurt";
        [BoxGroup("Ids")] [SerializeField] private string shotHit = "shot.hit";
        [BoxGroup("Ids")] [SerializeField] private string kill = "enemy.kill";

        [BoxGroup("Ids")]
        [PropertyRange(1, 20)]
        [Tooltip("Hit stop frames at or above which a hit counts as heavy.")]
        [SerializeField] private int heavyFrom = 8;

        private void OnEnable() => CombatSignals.HitLanded += OnHit;
        private void OnDisable() => CombatSignals.HitLanded -= OnHit;

        private void OnHit(HitReport report)
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;

            string id;
            if (!report.byPlayer && report.result == HitResult.Hit) id = playerHurt;
            else if (report.result == HitResult.Ignored) id = ignoredHit;
            else if (report.result == HitResult.Armored) id = armoredHit;
            else if (report.ranged) id = shotHit;
            else id = report.weight >= heavyFrom ? heavyHit : lightHit;

            audio.PlayAt(id, report.point);
            if (report.killed && report.byPlayer) audio.PlayAt(kill, report.point);
        }
    }
}
