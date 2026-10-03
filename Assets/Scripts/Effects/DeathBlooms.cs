using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A few small flowers grow where an enemy is defeated, and stay.
    ///
    /// "Permanence" from Vlambeer's Art of Screenshake (researched 3 October 2026):
    /// shells and corpses that stay on the floor make a fight feel like it happened.
    /// Weizza is wholesome, so what stays behind is a bloom, not a body. It also says
    /// the game's theme quietly: something ends, something grows.
    ///
    /// Pooled and capped. The oldest bloom is reused once the cap is reached, so a
    /// long session never adds objects. Created by itself at boot, no wiring.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class DeathBlooms : MonoBehaviour
    {
        private const int Cap = 48;
        private const int PerKill = 4;

        private static readonly Color[] Palette =
        {
            new(0.95f, 0.92f, 0.82f), new(0.82f, 0.9f, 0.98f), new(0.98f, 0.85f, 0.88f), new(0.9f, 0.96f, 0.84f),
        };

        private readonly List<SpriteRenderer> _blooms = new(Cap);
        private int _next;
        private int _groundMask = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<DeathBlooms>() != null) return;
            var go = new GameObject("[Death Blooms]");
            DontDestroyOnLoad(go);
            go.AddComponent<DeathBlooms>();
        }

        private void OnEnable() => CombatSignals.HitLanded += OnHit;
        private void OnDisable() => CombatSignals.HitLanded -= OnHit;

        private void OnHit(HitReport report)
        {
            if (!report.killed || !report.byPlayer) return;
            if (_groundMask == -1) _groundMask = ~LayerMask.GetMask(Layers.Player, Layers.Enemy, Layers.EnemyHitbox, "Ignore Raycast", "EnemyWall");

            for (var i = 0; i < PerKill; i++)
            {
                var x = report.point.x + Random.Range(-0.8f, 0.8f);
                var from = new Vector3(x, report.point.y + 1.5f, 0f);
                if (!Physics.Raycast(from, Vector3.down, out var hit, 5f, _groundMask, QueryTriggerInteraction.Ignore)) continue;
                Grow(new Vector3(x, hit.point.y + 0.22f, 0.08f), i * 0.08f);
            }
        }

        private void Grow(Vector3 at, float delay)
        {
            SpriteRenderer bloom;
            if (_blooms.Count < Cap)
            {
                var go = new GameObject("Bloom");
                go.transform.SetParent(transform, false);
                bloom = go.AddComponent<SpriteRenderer>();
                bloom.sprite = ProceduralSprites.Flower();
                bloom.sortingLayerName = "Default";
                bloom.sortingOrder = -1;
                _blooms.Add(bloom);
            }
            else
            {
                bloom = _blooms[_next];
                _next = (_next + 1) % Cap;
            }

            var size = Random.Range(0.32f, 0.48f);
            bloom.color = Palette[Random.Range(0, Palette.Length)];
            bloom.transform.position = at;
            bloom.transform.localScale = Vector3.zero;
            bloom.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 72f));
            Tween.StopAll(bloom.transform);
            Tween.Scale(bloom.transform, Vector3.one * size, 0.45f, Ease.OutBack, startDelay: delay + 0.25f);
        }
    }
}
