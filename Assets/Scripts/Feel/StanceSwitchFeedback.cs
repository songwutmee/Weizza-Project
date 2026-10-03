using System;
using PrimeTween;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A ring in the new stance's own colour that bursts out from the player
    /// the moment a switch completes, plus a tint flash on the sprite.
    ///
    /// The switch costs 0.4 seconds of standing exposed; the instant it pays
    /// off should be unmistakable, or a player in a fight cannot tell whether
    /// the school changed without looking at the HUD. Code against the drawn
    /// frames, no new drawing, per the feel rules.
    /// </summary>
    [AddComponentMenu("Weizza/Stance Switch Feedback")]
    public sealed class StanceSwitchFeedback : MonoBehaviour
    {
        private SpriteRenderer _ring;
        private IDisposable _subscription;
        private Tween _scale;
        private Tween _fade;
        private bool _first = true;

        private void Awake()
        {
            var go = new GameObject("Stance Ring");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.9f;
            _ring = go.AddComponent<SpriteRenderer>();
            _ring.sprite = ProceduralSprites.Ring(128, 0.08f);
            _ring.sortingLayerName = "Foreground";
            _ring.sortingOrder = 60;
            _ring.color = Color.clear;
        }

        private void OnEnable() => _subscription = StoryEvents.Subscribe(GameEvents.StanceSwitched, OnSwitched);

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void OnSwitched(object payload)
        {
            // The first raise is the starting stance being applied on load,
            // which is not a switch the player made.
            if (_first)
            {
                _first = false;
                return;
            }

            var player = GetComponent<PlayerController>();
            var tint = player != null && player.CurrentStance != null ? player.CurrentStance.tint : Color.white;

            _ring.transform.rotation = Quaternion.Euler(ViewRig.Pitch, 0f, 0f);
            _ring.transform.localScale = Vector3.one * 0.4f;
            var start = tint;
            start.a = GameSettings.ReduceFlashes ? 0.5f : 0.95f;
            _ring.color = start;
            var end = tint;
            end.a = 0f;

            _scale.Stop();
            _fade.Stop();
            _scale = Tween.Scale(_ring.transform, Vector3.one * 2.6f, 0.35f, Ease.OutCubic, useUnscaledTime: true);
            _fade = Tween.Color(_ring, end, 0.35f, Ease.InQuad, useUnscaledTime: true);

            GetComponentInChildren<SpriteFeel>()?.Flash(Color.Lerp(Color.white, tint, 0.6f));
        }
    }
}
