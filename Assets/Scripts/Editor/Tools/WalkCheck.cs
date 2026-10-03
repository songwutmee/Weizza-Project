using System.Collections.Generic;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Sweeps the whole line every 0.2 m and reports the three things that break a
    /// walk: a gap too wide to jump, a step too high to climb, and a ceiling too low
    /// to stand under. A floor sweep alone missed real traps twice in this project
    /// (the project notes, head room), so all three are checked together.
    ///
    /// The numbers are the measured player physics, not guesses: furthest jump
    /// 3.13 m, jump height 2.4 m, body 1.7 m that needs about 1.9 m of room.
    /// Everything it reports is a suggestion: a gap meant to be a pit is fine, which
    /// is why it is a list to read and not an error.
    /// </summary>
    public static class WalkCheck
    {
        public const float MaxGap = 3.0f;      // 3.13 m measured, minus a little margin
        public const float MaxRise = 2.2f;     // 2.4 m jump height, minus a margin
        public const float MinHeadroom = 1.9f;

        public struct Issue
        {
            public Vector3 at;
            public string text;
            public bool serious;
        }

        public static List<Issue> Run(float from, float to, float step = 0.2f)
        {
            var issues = new List<Issue>();
            var mask = ~LayerMask.GetMask("Player", "Enemy", "Ignore Raycast", "EnemyWall");
            Physics.SyncTransforms();

            float? gapStart = null;
            var lastTop = float.NaN;
            var ceilingReported = -999f;

            for (var x = from; x <= to; x += step)
            {
                var hit = Physics.Raycast(new Vector3(x, 60f, 0f), Vector3.down, out var h, 120f, mask, QueryTriggerInteraction.Ignore);
                if (!hit || h.point.y < -10f)
                {
                    gapStart ??= x;
                    continue;
                }

                if (gapStart.HasValue)
                {
                    var width = x - gapStart.Value;
                    var rise = float.IsNaN(lastTop) ? 0f : h.point.y - lastTop;
                    if (width > MaxGap)
                        issues.Add(new Issue { at = new Vector3((gapStart.Value + x) * 0.5f, h.point.y, 0f), serious = true,
                            text = $"Gap {width:0.0} m at x {gapStart:0.0}: wider than a jump ({MaxGap:0.0} m)" });
                    else if (rise > 0.8f && width > 2.3f)
                        issues.Add(new Issue { at = new Vector3((gapStart.Value + x) * 0.5f, h.point.y, 0f), serious = false,
                            text = $"Gap {width:0.0} m that also climbs {rise:0.0} m at x {gapStart:0.0}: tight, test it" });
                    gapStart = null;
                }
                else if (!float.IsNaN(lastTop) && h.point.y - lastTop > MaxRise)
                {
                    issues.Add(new Issue { at = new Vector3(x, h.point.y, 0f), serious = true,
                        text = $"Step up {h.point.y - lastTop:0.0} m at x {x:0.0}: higher than a jump ({MaxRise:0.0} m)" });
                }

                if (Physics.Raycast(h.point + Vector3.up * 0.05f, Vector3.up, out var up, MinHeadroom, mask, QueryTriggerInteraction.Ignore)
                    && x - ceilingReported > 1f)
                {
                    ceilingReported = x;
                    issues.Add(new Issue { at = h.point, serious = true,
                        text = $"Ceiling {up.distance:0.0} m above the floor at x {x:0.0} ({up.collider.name}): the player gets stuck" });
                }

                lastTop = h.point.y;
            }

            if (gapStart.HasValue)
                issues.Add(new Issue { at = new Vector3(gapStart.Value, lastTop, 0f), serious = false,
                    text = $"No floor from x {gapStart:0.0} to the end of the check" });
            return issues;
        }
    }
}
