using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Optional built-in toast UI. Add this component anywhere and Toast/Hint/
    /// SoundCaption nodes render through it; without it they only raise events
    /// for your own UI to handle.
    /// </summary>
    [AddComponentMenu("StoryKit/Simple Toast UI")]
    public sealed class SimpleToastUI : MonoBehaviour
    {
        private static SimpleToastUI _instance;

        private readonly List<(GameObject go, float dieAt)> _active = new();
        private Transform _root;

        private void Awake()
        {
            _instance = this;
            BuildCanvas();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void ShowIfPresent(string text, Flow.ToastNode.ToastKind kind, float duration)
            => _instance?.Show(text, kind, duration);

        private void Show(string text, Flow.ToastNode.ToastKind kind, float duration)
        {
            var go = new GameObject("Toast",
                typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(_root, false);
            go.GetComponent<UnityEngine.UI.LayoutElement>().minHeight = 40;

            go.GetComponent<UnityEngine.UI.Image>().color = kind switch
            {
                Flow.ToastNode.ToastKind.Success => new Color(0.15f, 0.45f, 0.25f, 0.92f),
                Flow.ToastNode.ToastKind.Warning => new Color(0.55f, 0.40f, 0.10f, 0.92f),
                Flow.ToastNode.ToastKind.Reward => new Color(0.35f, 0.25f, 0.55f, 0.92f),
                _ => new Color(0.12f, 0.14f, 0.20f, 0.92f),
            };

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            textGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)textGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12, 4);
            rect.offsetMax = new Vector2(-12, -4);

            var label = textGo.GetComponent<UnityEngine.UI.Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.text = text;

            _active.Add((go, Time.unscaledTime + duration));
        }

        private void Update()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _active[i].dieAt) continue;
                if (_active[i].go != null) Destroy(_active[i].go);
                _active.RemoveAt(i);
            }
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas",
                typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 700;
            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var stack = new GameObject("ToastStack",
                typeof(RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup),
                typeof(UnityEngine.UI.ContentSizeFitter));
            stack.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)stack.transform;
            rect.anchorMin = new Vector2(1f, 1f);      // top-right corner
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24, -24);
            rect.sizeDelta = new Vector2(420, 0);

            var layout = stack.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;

            var fitter = stack.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            _root = stack.transform;
        }
    }
}
