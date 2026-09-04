using System;
using System.Collections.Generic;

namespace StoryKit
{
    /// <summary>
    /// Typed key-value store with parent fallback and change events.
    /// Scope is expressed by chaining: Instance -> Graph -> Shared -> Global.
    /// Reads fall back to the parent chain. Writes always go to this board
    /// unless the key already exists in a parent and writeThrough is true.
    /// </summary>
    public sealed class Blackboard
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        private readonly Blackboard _parent;

        /// <summary>Fired with the key name whenever a value is set or removed on this board.</summary>
        public event Action<string> KeyChanged;

        public Blackboard(Blackboard parent = null)
        {
            _parent = parent;
        }

        public Blackboard Parent => _parent;

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return _values.ContainsKey(key) || (_parent != null && _parent.Has(key));
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            return TryGet(key, out T value) ? value : defaultValue;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (!string.IsNullOrEmpty(key))
            {
                if (_values.TryGetValue(key, out object raw))
                {
                    if (raw is T typed)
                    {
                        value = typed;
                        return true;
                    }
                    // Numeric convenience: allow int stored, float requested and vice versa.
                    if (raw is IConvertible && typeof(T) == typeof(float) && raw is int i)
                    {
                        value = (T)(object)(float)i;
                        return true;
                    }
                    if (raw is IConvertible && typeof(T) == typeof(int) && raw is float f)
                    {
                        value = (T)(object)(int)f;
                        return true;
                    }
                }
                if (_parent != null)
                    return _parent.TryGet(key, out value);
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Sets a value. When writeThrough is true and the key exists in an ancestor,
        /// the write goes to that ancestor instead (PlayMaker-style shared variables).
        /// </summary>
        public void Set<T>(string key, T value, bool writeThrough = true)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (writeThrough && !_values.ContainsKey(key))
            {
                Blackboard owner = FindOwner(key);
                if (owner != null && owner != this)
                {
                    owner.Set(key, value, writeThrough: false);
                    return;
                }
            }

            bool existed = _values.TryGetValue(key, out object existing);

            // Compare before boxing. A float written every frame that never
            // actually changes would otherwise allocate on every single write,
            // which is the most common source of quiet GC pressure here.
            if (existed && ValuesMatch(existing, value)) return;

            _values[key] = value;
            KeyChanged?.Invoke(key);
        }

        /// <summary>
        /// Equality that avoids boxing for the types the game writes constantly.
        /// </summary>
        private static bool ValuesMatch<T>(object existing, T value)
        {
            if (existing is float existingFloat && value is float newFloat)
                return existingFloat == newFloat;
            if (existing is int existingInt && value is int newInt)
                return existingInt == newInt;
            if (existing is bool existingBool && value is bool newBool)
                return existingBool == newBool;
            if (existing is string existingString && value is string newString)
                return string.Equals(existingString, newString, StringComparison.Ordinal);

            return Equals(existing, value);
        }

        public void Remove(string key)
        {
            if (_values.Remove(key))
                KeyChanged?.Invoke(key);
        }

        private Blackboard FindOwner(string key)
        {
            Blackboard current = this;
            while (current != null)
            {
                if (current._values.ContainsKey(key))
                    return current;
                current = current._parent;
            }
            return null;
        }

        /// <summary>
        /// Every key on this board starting with the prefix, without copying the
        /// whole board. Used by the item screen, which would otherwise allocate
        /// a full dictionary every time it redrew.
        /// </summary>
        public void CollectWithPrefix(string prefix, List<(string key, object value)> into)
        {
            if (into == null) return;
            into.Clear();
            if (string.IsNullOrEmpty(prefix)) return;

            foreach (var pair in _values)
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    into.Add((pair.Key, pair.Value));
        }

        /// <summary>Shallow copy of this board's own values (parents excluded).</summary>
        public Dictionary<string, object> Snapshot()
        {
            return new Dictionary<string, object>(_values, StringComparer.Ordinal);
        }

        public void Restore(Dictionary<string, object> snapshot)
        {
            _values.Clear();
            if (snapshot != null)
                foreach (var pair in snapshot)
                    _values[pair.Key] = pair.Value;
            KeyChanged?.Invoke(string.Empty);
        }

        public void Clear()
        {
            _values.Clear();
            KeyChanged?.Invoke(string.Empty);
        }
    }

    /// <summary>Process-wide root blackboard. Games should persist this via their save system.</summary>
    public static class StoryState
    {
        public static Blackboard Global { get; private set; } = new Blackboard();

        /// <summary>Replace the global board (e.g. after loading a save file).</summary>
        public static void SetGlobal(Blackboard board)
        {
            Global = board ?? new Blackboard();
        }
    }
}
