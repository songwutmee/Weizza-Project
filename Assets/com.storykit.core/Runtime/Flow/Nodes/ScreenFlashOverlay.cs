using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>Full-screen colour flash overlay, created on demand.</summary>
    public sealed class ScreenFlashOverlay : MonoBehaviour
    {
        private static ScreenFlashOverlay _instance;
        private UnityEngine.UI.Image _image;
        private float _elapsed = -1f;
        private float _duration;
        private Color _color;

        public static void Flash(Color color, float duration)
        {
            Ensure();
            _instance._color = color;
            _instance._duration = Mathf.Max(0.02f, duration);
            _instance._elapsed = 0f;
        }

        private static void Ensure()
        {
            if (_instance != null) return;

            var host = new GameObject("StoryKit.ScreenFlash");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<ScreenFlashOverlay>();

            var canvasGo = new GameObject("Canvas",
                typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasGo.transform.SetParent(host.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1200;   // above fade and dialogue

            var imageGo = new GameObject("Flash",
                typeof(RectTransform), typeof(UnityEngine.UI.Image));
            imageGo.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _instance._image = imageGo.GetComponent<UnityEngine.UI.Image>();
            _instance._image.raycastTarget = false;
            _instance._image.color = Color.clear;
        }

        private void Update()
        {
            if (_elapsed < 0f) return;

            _elapsed += Time.unscaledDeltaTime;
            float t = _elapsed / _duration;

            if (t >= 1f)
            {
                _image.color = Color.clear;
                _elapsed = -1f;
                return;
            }

            var color = _color;
            color.a = _color.a * (1f - t);   // fade out
            _image.color = color;
        }
    }
}
