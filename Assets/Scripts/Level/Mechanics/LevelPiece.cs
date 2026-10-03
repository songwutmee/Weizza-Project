using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;

namespace Weizza
{
    /// <summary>The player, found once and cached. Level pieces ask this instead of each searching.</summary>
    public static class PlayerLocator
    {
        private static PlayerController _player;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _player = null;

        public static PlayerController Player
        {
            get
            {
                if (_player == null) _player = Object.FindFirstObjectByType<PlayerController>();
                return _player;
            }
        }

        /// <summary>True when this collider belongs to the player.</summary>
        public static bool IsPlayer(Component c) =>
            c != null && Player != null && c.transform.IsChildOf(Player.transform);
    }

    /// <summary>Small shared pieces of behaviour the level mechanics all need.</summary>
    public static class LevelPiece
    {
        /// <summary>The body a collider belongs to, if it is a character.</summary>
        public static CharacterMotor3D BodyOf(Component c) =>
            c != null ? c.GetComponentInParent<CharacterMotor3D>() : null;

        /// <summary>True when the body carries the Heavy tag (Than, or anything else made heavy).</summary>
        public static bool IsHeavy(Component body) => body != null && body.HasTag(GameTag.Heavy);

        /// <summary>True when this collider is one of the piece's own colliders.</summary>
        public static bool Owns(Component piece, Collider collider) =>
            piece != null && collider != null && collider.transform.IsChildOf(piece.transform);

        /// <summary>
        /// Shakes, disappears, and optionally comes back. Used by everything
        /// that breaks: weak wood, jump-break platforms, burst mushrooms.
        ///
        /// The shake comes first on purpose. A floor that vanishes the instant
        /// it is touched reads as a bug; one that trembles first reads as a
        /// rule the player can learn.
        /// </summary>
        public static async UniTaskVoid CrumbleAsync(Transform piece, float warnSeconds, float respawnSeconds,
            CancellationToken ct)
        {
            if (piece == null) return;

            var home = piece.localPosition;
            var colliders = piece.GetComponentsInChildren<Collider>();
            var renderers = piece.GetComponentsInChildren<Renderer>();

            try
            {
                if (warnSeconds > 0f)
                {
                    Tween.ShakeLocalPosition(piece, new Vector3(0.06f, 0.03f, 0f), warnSeconds, frequency: 30f);
                    await UniTask.Delay(System.TimeSpan.FromSeconds(warnSeconds), cancellationToken: ct);
                    piece.localPosition = home;
                }

                SetSolid(colliders, renderers, false);
                if (respawnSeconds <= 0f) return;

                await UniTask.Delay(System.TimeSpan.FromSeconds(respawnSeconds), cancellationToken: ct);

                // Never come back inside someone. Wait until nothing is standing
                // where the floor would reappear.
                var player = PlayerLocator.Player;
                while (player != null && colliders.Length > 0 && colliders[0] != null &&
                       colliders[0].bounds.Intersects(player.Motor.Controller.bounds))
                {
                    await UniTask.Yield(ct);
                }

                SetSolid(colliders, renderers, true);
            }
            catch (System.OperationCanceledException)
            {
                if (piece != null) piece.localPosition = home;
            }
        }

        public static void SetSolid(Collider[] colliders, Renderer[] renderers, bool solid)
        {
            foreach (var c in colliders) if (c != null && !c.isTrigger) c.enabled = solid;
            foreach (var r in renderers) if (r != null) r.enabled = solid;
        }
    }
}
