using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

namespace Weizza
{
    /// <summary>
    /// Numbers that pop off a hit and float up. Off by default: the design
    /// bible says hit stop carries a hit better than a number, so this exists
    /// for accessibility and for tuning, switched on in Settings or with the
    /// toggle below.
    ///
    /// Drawn on the HUD's UI Toolkit panel and moved by hand each frame, so
    /// the numbers stay sharp at any resolution and cost no world objects.
    /// </summary>
    [AddComponentMenu("Weizza/Damage Numbers")]
    public sealed class DamageNumbers : MonoBehaviour
    {
        private sealed class Popup
        {
            public Label label;
            public Vector3 world;
            public float age;
            public float life;
            public Vector3 drift;
        }

        [BoxGroup("Look")]
        [ToggleLeft]
        [Tooltip("Ticked shows numbers whatever the setting says. For tuning sessions.")]
        [SerializeField] private bool forceOn;

        [BoxGroup("Look")]
        [PropertyRange(0.3f, 2f)]
        [SerializeField] private float lifetime = 0.8f;

        [BoxGroup("Look")]
        [SerializeField] private Color dealtColour = new(0.94f, 0.9f, 0.82f);

        [BoxGroup("Look")]
        [SerializeField] private Color armouredColour = new(1f, 0.85f, 0.4f);

        [BoxGroup("Look")]
        [SerializeField] private Color takenColour = new(0.9f, 0.35f, 0.35f);

        private readonly List<Popup> _live = new(16);
        private readonly Stack<Label> _free = new();

        private void OnEnable() => CombatSignals.HitLanded += OnHit;
        private void OnDisable() => CombatSignals.HitLanded -= OnHit;

        private void OnHit(HitReport report)
        {
            if (!forceOn && !GameSettings.DamageNumbers) return;
            if (report.damage <= 0.01f) return;

            // A hit on a player whose health is wings costs a whole wing, which the
            // HUD butterfly already shows. A raw "30" over their head would be a
            // second, contradicting readout (BUG-3).
            if (!report.byPlayer)
            {
                var player = PlayerLocator.Player;
                if (player != null && player.Vitals != null && player.Vitals.UsesWings) return;
            }

            var hud = HudScreen.Instance;
            if (hud == null || hud.Root == null) return;

            var label = _free.Count > 0 ? _free.Pop() : MakeLabel(hud.Root);
            if (label.parent != hud.Root) hud.Root.Add(label);

            var heavy = report.weight >= 8;
            label.text = Mathf.CeilToInt(report.damage).ToString();
            label.style.color = !report.byPlayer ? takenColour : report.result == HitResult.Armored ? armouredColour : dealtColour;
            label.style.fontSize = heavy ? 34 : 24;
            label.style.display = DisplayStyle.Flex;

            _live.Add(new Popup
            {
                label = label,
                world = report.point + Vector3.up * 0.4f,
                life = lifetime * (heavy ? 1.3f : 1f),
                drift = new Vector3(Random.Range(-0.4f, 0.4f), 1.4f, 0f),
            });
        }

        private static Label MakeLabel(VisualElement root)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(label);
            return label;
        }

        private void Update()
        {
            var hud = HudScreen.Instance;
            var dt = Time.unscaledDeltaTime;

            for (var i = _live.Count - 1; i >= 0; i--)
            {
                var popup = _live[i];
                popup.age += dt;

                if (popup.age >= popup.life || hud == null)
                {
                    popup.label.style.display = DisplayStyle.None;
                    _free.Push(popup.label);
                    _live.RemoveAt(i);
                    continue;
                }

                var t = popup.age / popup.life;
                var world = popup.world + popup.drift * (1f - (1f - t) * (1f - t));
                if (!hud.WorldToPanel(world, out var position)) continue;

                // Pop bigger for the first moment, then settle and fade.
                var pop = t < 0.15f ? 1f + (0.15f - t) * 3f : 1f;
                popup.label.style.left = position.x - 30f;
                popup.label.style.top = position.y - 20f;
                popup.label.style.width = 60f;
                popup.label.style.scale = new Scale(Vector3.one * pop);
                popup.label.style.opacity = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);
            }
        }
    }
}
