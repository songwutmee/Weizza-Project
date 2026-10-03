using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Weizza
{
    /// <summary>
    /// Saves and restores rebinds for the actions InputReader builds in code.
    ///
    /// The actions are not in an asset, so there is no single collection to
    /// save. Each action's own overrides are stored by action name in
    /// GameSettings, which survives a new game. When the bindings move into an
    /// .inputactions asset later, only this file changes.
    /// </summary>
    public static class InputBindings
    {
        [Serializable]
        private sealed class Store
        {
            public List<string> names = new();
            public List<string> overrides = new();
        }

        /// <summary>Applies the saved rebinds to every action the reader owns.</summary>
        public static void Load(InputReader reader)
        {
            if (reader == null || string.IsNullOrEmpty(GameSettings.Bindings)) return;
            var store = JsonUtility.FromJson<Store>(GameSettings.Bindings);
            if (store == null) return;

            foreach (var action in reader.Actions)
            {
                if (action == null) continue;
                var index = store.names.IndexOf(action.name);
                if (index < 0 || index >= store.overrides.Count) continue;
                action.LoadBindingOverridesFromJson(store.overrides[index]);
            }
        }

        public static void Save(InputReader reader)
        {
            if (reader == null) return;
            var store = new Store();
            foreach (var action in reader.Actions)
            {
                if (action == null) continue;
                store.names.Add(action.name);
                store.overrides.Add(action.SaveBindingOverridesAsJson());
            }
            GameSettings.Bindings = JsonUtility.ToJson(store);
        }

        public static void ResetAll(InputReader reader)
        {
            if (reader == null) return;
            foreach (var action in reader.Actions) action?.RemoveAllBindingOverrides();
            GameSettings.Bindings = string.Empty;
        }

        /// <summary>
        /// Listens for the next key or button and puts it on the given binding.
        /// Escape cancels. The action is disabled while listening, which the
        /// Input System requires.
        /// </summary>
        public static void Rebind(InputReader reader, InputAction action, int bindingIndex, Action<bool> done)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                done?.Invoke(false);
                return;
            }

            action.Disable();
            action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(operation =>
                {
                    operation.Dispose();
                    action.Enable();
                    Save(reader);
                    done?.Invoke(true);
                })
                .OnCancel(operation =>
                {
                    operation.Dispose();
                    action.Enable();
                    done?.Invoke(false);
                })
                .Start();
        }

        /// <summary>The binding a player would read: "Space", "Left Button".</summary>
        public static string Display(InputAction action, int bindingIndex) =>
            action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count
                ? action.GetBindingDisplayString(bindingIndex)
                : "-";
    }
}
