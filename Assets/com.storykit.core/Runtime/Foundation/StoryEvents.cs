using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// Global event bus. The single bridge between game code and all graph types.
    /// Game code raises events; Flow graphs wait on them, FSMs transition on them,
    /// Behavior Trees observe them. Names are dot-namespaced by convention
    /// (e.g. "Combat.EnemyKilled", "Quest.Completed").
    /// </summary>
    public static class StoryEvents
    {
        private sealed class Subscription : IDisposable
        {
            public string Name;
            public Action<object> Handler;
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Unsubscribe(this);
            }
        }

        private static readonly Dictionary<string, List<Subscription>> _subscribers =
            new(StringComparer.Ordinal);

        // Reused scratch list so Raise does not allocate per call.
        private static readonly List<Subscription> _scratch = new();

        /// <summary>
        /// Editor-only tap so the Dashboard can log every event. Null in builds
        /// unless something assigns it, so it costs one null check.
        /// </summary>
        public static Action<string, object> Monitor;

        public static void Raise(string eventName, object payload = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            Monitor?.Invoke(eventName, payload);

            if (!_subscribers.TryGetValue(eventName, out var list) || list.Count == 0)
                return;

            // Copy to scratch so handlers can subscribe/unsubscribe during dispatch.
            _scratch.Clear();
            _scratch.AddRange(list);
            for (int i = 0; i < _scratch.Count; i++)
            {
                try
                {
                    _scratch[i].Handler?.Invoke(payload);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            _scratch.Clear();
        }

        public static IDisposable Subscribe(string eventName, Action<object> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return EmptyDisposable.Instance;

            var sub = new Subscription { Name = eventName, Handler = handler };
            if (!_subscribers.TryGetValue(eventName, out var list))
            {
                list = new List<Subscription>();
                _subscribers[eventName] = list;
            }
            list.Add(sub);
            return sub;
        }

        private static void Unsubscribe(Subscription sub)
        {
            if (_subscribers.TryGetValue(sub.Name, out var list))
            {
                list.Remove(sub);
                if (list.Count == 0)
                    _subscribers.Remove(sub.Name);
            }
        }

        /// <summary>Clears all subscriptions. Call between play sessions / scene resets in tests.</summary>
        public static void Reset()
        {
            _subscribers.Clear();
            _scratch.Clear();
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
