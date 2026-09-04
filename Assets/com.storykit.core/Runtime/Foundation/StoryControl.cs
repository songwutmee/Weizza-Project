using System;
using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// Whether the player is allowed to move.
    ///
    /// Every game with dialogue needs this and every game gets it wrong the
    /// same way. Talking to someone while still able to walk means the player
    /// wanders off mid sentence and the box follows them around. Locking with a
    /// plain on/off flag then breaks the moment two things overlap: a cutscene
    /// locks, dialogue inside it locks, the line ends and unlocks, and now the
    /// player can walk during the rest of the cutscene.
    ///
    /// So it counts. The lock goes on at the first request and comes off at the
    /// last release, and holders never have to know about each other.
    ///
    /// There used to be two separate copies of this with two separate counts,
    /// one in GameKit and one in the dialogue UI. Whichever registered last won
    /// and the other one's count leaked, which is how input gets stuck on for
    /// the rest of a play session.
    ///
    /// Your controller reads one line:
    ///     if (StoryControl.MovementLocked) return;
    /// </summary>
    public static class StoryControl
    {
        /// <summary>Blackboard key, so graphs and conditions can read it too.</summary>
        public const string LockedKey = "input.locked";

        private static int _count;

        /// <summary>True while anything is holding movement.</summary>
        public static bool MovementLocked => _count > 0;

        /// <summary>How many things are holding it. For the debug dashboard.</summary>
        public static int LockCount => _count;

        /// <summary>Raised when the lock goes on, with the reason given.</summary>
        public static event Action<string> Locked;

        /// <summary>Raised when the last holder lets go.</summary>
        public static event Action Unlocked;

        /// <summary>
        /// Takes the lock. Dispose the handle to let go, or call it in a using
        /// block so an exception cannot strand the player.
        /// </summary>
        public static IDisposable Lock(string reason)
        {
            _count++;

            if (_count == 1)
            {
                StoryState.Global.Set(LockedKey, 1f);
                StoryEvents.Raise("Player.InputLocked", reason);
                Locked?.Invoke(reason);
            }
            return new Handle(reason);
        }

        private static void Release(Handle handle)
        {
            if (handle.Released) return;      // disposing twice must not double count
            handle.Released = true;

            _count = Mathf.Max(0, _count - 1);
            if (_count > 0) return;

            StoryState.Global.Set(LockedKey, 0f);
            StoryEvents.Raise("Player.InputUnlocked", null);
            Unlocked?.Invoke();
        }

        /// <summary>
        /// Drops every lock. Called between play sessions, and available as the
        /// escape hatch when a bug in game code has stranded the player.
        /// </summary>
        public static void ForceRelease()
        {
            if (_count == 0) return;
            _count = 0;
            StoryState.Global.Set(LockedKey, 0f);
            StoryEvents.Raise("Player.InputUnlocked", null);
            Unlocked?.Invoke();
        }

        internal static void Reset()
        {
            _count = 0;
            Locked = null;
            Unlocked = null;
        }

        private sealed class Handle : IDisposable
        {
            public bool Released;
            public readonly string Reason;

            public Handle(string reason) => Reason = reason;
            public void Dispose() => Release(this);
        }
    }

    /// <summary>
    /// The service form, for code that goes through StoryServices. It is the
    /// same counter underneath, so a graph node and a UI screen locking at the
    /// same time behave correctly.
    /// </summary>
    public sealed class StoryControlService : IInputService
    {
        public IDisposable LockPlayerControl(string reason) => StoryControl.Lock(reason);
    }
}
