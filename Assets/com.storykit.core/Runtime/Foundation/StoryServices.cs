using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit
{
    /// <summary>Marker interface for all StoryKit services.</summary>
    public interface IStoryService { }

    /// <summary>
    /// Service registry. Nodes never touch game managers directly; they resolve
    /// interfaces here. A game registers its implementations once at startup.
    /// Missing services log a warning once and return null instead of throwing,
    /// so a graph never hard-crashes because one subsystem is not wired yet.
    /// </summary>
    public static class StoryServices
    {
        private static readonly Dictionary<Type, IStoryService> _services = new();
        private static readonly HashSet<Type> _warned = new();

        public static void Register<T>(T implementation) where T : class, IStoryService
        {
            if (implementation == null)
            {
                _services.Remove(typeof(T));
                return;
            }
            _services[typeof(T)] = implementation;
            _warned.Remove(typeof(T));
        }

        public static T Get<T>() where T : class, IStoryService
        {
            if (_services.TryGetValue(typeof(T), out var service))
                return (T)service;

            if (_warned.Add(typeof(T)))
                Debug.LogWarning($"[StoryKit] Service {typeof(T).Name} is not registered. Related nodes will be skipped.");
            return null;
        }

        public static bool TryGet<T>(out T service) where T : class, IStoryService
        {
            service = Get<T>();
            return service != null;
        }

        public static void Reset()
        {
            _services.Clear();
            _warned.Clear();
        }
    }

    /// <summary>
    /// Time abstraction so Wait/Cooldown nodes are testable with a mock clock
    /// and respect game pause if the game supplies its own provider.
    /// </summary>
    public interface ITimeProvider
    {
        float Time { get; }
        float DeltaTime { get; }
    }

    public sealed class UnityTimeProvider : ITimeProvider
    {
        public float Time => UnityEngine.Time.time;
        public float DeltaTime => UnityEngine.Time.deltaTime;
    }

    /// <summary>Manually advanced clock for unit tests.</summary>
    public sealed class ManualTimeProvider : ITimeProvider
    {
        public float Time { get; private set; }
        public float DeltaTime { get; private set; }

        public void Advance(float seconds)
        {
            DeltaTime = seconds;
            Time += seconds;
        }
    }

    public static class StoryTime
    {
        public static ITimeProvider Provider { get; set; } = new UnityTimeProvider();
        public static float Time => Provider.Time;
        public static float DeltaTime => Provider.DeltaTime;
    }
}
