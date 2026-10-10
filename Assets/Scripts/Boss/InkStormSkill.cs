using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The beetle's big move, in three beats:
    ///
    /// 1. It leaps up out of the frame and clings to the top of the screen. All
    ///    the player sees is its huge dark underside and legs hanging down.
    /// 2. It shakes its head and sprays ink over the screen (Ultimate 1). The
    ///    blots stay for a while, so the player fights half blind.
    /// 3. It stabs its tail down in 3 or 4 volleys (Ultimate 2), each a pattern: a hunt,
    ///    a trap that closes both sides first, a row laid ahead of the run, a wave to
    ///    jump, a scatter with one safe gap. Every
    ///    strike is marked first by a warm yellow ring on the floor, drawn above
    ///    the ink so it can always be read. The first stabs aim at the player, the
    ///    later ones lead them, so standing still or running one way both lose.
    ///
    /// Then it drops back down away from the player and the fight goes on.
    ///
    /// No Shader Graph needed: the ink is a handful of blot sprites painted once in
    /// code and parented to the camera, so it is part of the picture (the colour
    /// grade applies to it and a screenshot shows it), not an overlay on top.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Ink Storm Skill")]
    public sealed class InkStormSkill : BossSkill
    {
        [BoxGroup("Use")]
        [PropertyRange(0f, 60f)]
        [Tooltip("Seconds before it can be used again.")]
        [SerializeField] private float cooldown = 11f;

        [BoxGroup("Leap")]
        [PropertyRange(0.1f, 1.5f)]
        [Tooltip("Crouch before the leap. The player's cue that something big is coming.")]
        [SerializeField] private float crouchSeconds = 0.55f;

        [BoxGroup("Leap")]
        [PropertyRange(0.1f, 1f)]
        [SerializeField] private float leapSeconds = 0.35f;

        [BoxGroup("Looming")]
        [PropertyRange(1f, 6f)]
        [Tooltip("How big the clinging body is drawn at the top of the screen.")]
        [SerializeField] private float loomScale = 3.2f;

        [BoxGroup("Looming")]
        [SerializeField] private Color loomTint = new(0.07f, 0.06f, 0.08f, 0.96f);

        [BoxGroup("Ink (Ultimate 1)")]
        [PropertyRange(0f, 30f)]
        [SerializeField] private int blotCount = 11;

        [BoxGroup("Ink (Ultimate 1)")]
        [PropertyRange(0.3f, 3f)]
        [Tooltip("Seconds of head shaking while the ink lands.")]
        [SerializeField] private float spraySeconds = 1.1f;

        [BoxGroup("Ink (Ultimate 1)")]
        [PropertyRange(1f, 20f)]
        [Tooltip("Seconds the ink stays at full strength once the stabs are over.")]
        [SerializeField] private float inkLingers = 3.5f;

        [BoxGroup("Ink (Ultimate 1)")]
        [PropertyRange(0.3f, 1f)]
        [Tooltip("How opaque one blot is. Below 1 so the world shows through, dimly.")]
        [SerializeField] private float inkOpacity = 0.86f;

        [BoxGroup("Stabs (Ultimate 2)")]
        [Tooltip("Volleys per use, random between the two. Each volley is one stab pattern.")]
        [SerializeField] private Vector2Int stabCount = new(3, 5);

        [BoxGroup("Stabs (Ultimate 2)")]
        [PropertyRange(0.3f, 1.5f)]
        [Tooltip("Seconds a ring shows before its stab lands. Later stabs come a little faster.")]
        [SerializeField] private float ringWarning = 1f;

        [BoxGroup("Stabs (Ultimate 2)")]
        [PropertyRange(0.1f, 1.5f)]
        [Tooltip("Seconds of calm between two volleys, to read the floor again.")]
        [SerializeField] private float volleyRest = 0.55f;

        [BoxGroup("Stabs (Ultimate 2)")]
        [PropertyRange(0.3f, 2f)]
        [SerializeField] private float stabRadius = 0.85f;

        [BoxGroup("Stabs (Ultimate 2)")]
        [SerializeField] private float stabDamage = 12f;

        [BoxGroup("Stabs (Ultimate 2)")]
        [SerializeField] private Color ringTint = new(1f, 0.93f, 0.5f, 1f);

        [BoxGroup("Senses")]
        [SerializeField] private LayerMask groundMask = 1;

        private float _readyAt;
        private EnemyController _body;
        private CharacterMotor3D _motor;
        private Vitals _vitals;
        private SpriteRenderer _sprite;
        private IDisposable _untouchable;
        private Vector3 _groundedAt;
        private readonly List<GameObject> _spawned = new();
        private readonly List<SpriteRenderer> _ink = new();

        private void Awake()
        {
            _body = GetComponent<EnemyController>();
            _motor = GetComponent<CharacterMotor3D>();
            _vitals = GetComponent<Vitals>();
            var drawing = GetComponentInChildren<SpriteFeel>();
            _sprite = drawing != null ? drawing.GetComponent<SpriteRenderer>() : GetComponentInChildren<SpriteRenderer>();
        }

        public override bool CanStart(MiniBossBrain brain) =>
            Time.time >= _readyAt && _motor != null && _motor.Grounded && Camera.main != null;

        public override async UniTask Run(MiniBossBrain brain, CancellationToken ct)
        {
            _readyAt = Time.time + cooldown;
            var cam = Camera.main;
            _groundedAt = transform.position;
            var feel = GetComponentInChildren<SpriteFeel>();

            try
            {
                // ---- Crouch, then leap out of the top of the frame -----------------
                _body.DirectedWalk = 0f;
                feel?.HoldTint(ringTint);
                await Squash(crouchSeconds, 1.18f, 0.78f, ct);
                feel?.ReleaseTint();

                _untouchable = this.Tags().Add(GameTag.Invincible);
                _vitals.RefreshTags();
                _body.Held = true;
                CameraFeel.Instance?.Shake(0.25f, Vector2.up);
                var top = cam.transform.position.y + cam.orthographicSize + 4f;
                await MoveBody(new Vector3(transform.position.x, top, transform.position.z), leapSeconds, ct);
                if (_sprite != null) _sprite.enabled = false;

                // ---- Loom at the top of the screen ---------------------------------
                var loom = SpawnLoom(cam);
                await Tween(0.45f, t => PlaceLoom(loom, cam, Mathf.Lerp(-2.5f, 0f, EaseOut(t)), 0f), ct);
                CameraFeel.Instance?.Shake(0.35f, Vector2.down);

                // ---- Ultimate 1: shake the head, ink lands on the screen -----------
                var blotsLanded = 0;
                await Tween(spraySeconds, t =>
                {
                    PlaceLoom(loom, cam, 0f, Mathf.Sin(t * spraySeconds * 32f) * 7f);
                    var due = Mathf.FloorToInt(t * blotCount);
                    while (blotsLanded < due) SpawnBlot(cam, blotsLanded++);
                }, ct);
                while (blotsLanded < blotCount) SpawnBlot(cam, blotsLanded++);

                // ---- Ultimate 2: tail stabs, in volleys --------------------------------
                var count = UnityEngine.Random.Range(stabCount.x, stabCount.y + 1);
                var lastPattern = -1;
                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    PlaceLoom(loom, cam, 0f, 0f);
                    // Later volleys warn a little less: the fight tightens as it goes.
                    var warning = Mathf.Lerp(ringWarning, ringWarning * 0.88f, i / Mathf.Max(1f, count - 1f));
                    lastPattern = PickPattern(lastPattern, i);
                    await Volley((StabPattern)lastPattern, cam, loom, warning, ct);
                    await UniTask.Delay(TimeSpan.FromSeconds(volleyRest), cancellationToken: ct);
                }

                // ---- Let go: drop back down away from the player -------------------
                await Tween(0.35f, t => PlaceLoom(loom, cam, Mathf.Lerp(0f, -3f, t * t), 0f), ct);
                Destroy(loom);
                await DropDown(cam, ct);
                FadeInkLater(inkLingers).Forget();
            }
            finally
            {
                if (ct.IsCancellationRequested) Abort();
                else Release();
            }
        }

        public override void Abort()
        {
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
            foreach (var blot in _ink) if (blot != null) Destroy(blot.gameObject);
            _ink.Clear();
            if (_body != null && _body.Held)
            {
                _body.Held = false;
                _motor.Teleport(_groundedAt);
                Physics.SyncTransforms();
            }
            Release();
        }

        private void Release()
        {
            _untouchable?.Dispose();
            _untouchable = null;
            if (_vitals != null) _vitals.RefreshTags();
            if (_sprite != null)
            {
                _sprite.enabled = true;
                _sprite.transform.localScale = new Vector3(Mathf.Sign(_sprite.transform.localScale.x), 1f, 1f);
            }
            if (_body != null) _body.Held = false;
        }

        // ---- Beats --------------------------------------------------------------

        // ---- Stab patterns --------------------------------------------------------
        //
        // Every volley is a small puzzle with an answer, never a coin toss: each
        // pattern leaves a way out (a gap to stand in, a moment to jump, a side to
        // run to), and the rings show all of it before anything lands.

        private enum StabPattern
        {
            Hunt,     // three single stabs, each on where the player is about to be
            Trap,     // both sides of the player first, then the middle: stay and you are hit
            LeadLine, // a row laid down ahead of where the player is running
            Sweep,    // a wave walking across the floor toward the player: jump it
            Scatter,  // several at once with one guaranteed safe gap near the player
            Pincer,   // two walls walk in from both sides and stop one gap short: stand still in the middle
            Checker,  // every other spot, then the spots between: step across once, at the right time
            Rain,     // singles falling one after another all over, one always on the player
            Cage,     // a ring of stabs around the player, then the centre: get out through the side that opens late
        }

        private int PickPattern(int last, int volleyIndex)
        {
            // The first volley is always readable (a hunt or a trap); after that
            // anything but a repeat.
            if (volleyIndex == 0) return UnityEngine.Random.value < 0.5f ? (int)StabPattern.Hunt : (int)StabPattern.Trap;
            var count = Enum.GetValues(typeof(StabPattern)).Length;
            int pick;
            do pick = UnityEngine.Random.Range(0, count); while (pick == last);
            return pick;
        }

        private async UniTask Volley(StabPattern pattern, Camera cam, GameObject loom, float warning, CancellationToken ct)
        {
            var player = PlayerLocator.Player;
            var px = player != null ? player.transform.position.x : transform.position.x;
            var vx = player != null ? player.Motor.Velocity.x : 0f;
            var heading = Mathf.Abs(vx) > 0.5f ? Mathf.Sign(vx) : (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            var gap = stabRadius * 2f + 0.3f;   // rings never overlap

            switch (pattern)
            {
                case StabPattern.Hunt:
                    for (var i = 0; i < 3; i++)
                    {
                        await Strike(cam, loom, new[] { LeadAim(i) }, warning * (i == 0 ? 1f : 0.85f), ct);
                        await UniTask.Delay(TimeSpan.FromSeconds(0.2f), cancellationToken: ct);
                    }
                    break;

                case StabPattern.Trap:
                    // The sides close the exits, then the middle comes down. The answer
                    // is to leave during the first warning, not after.
                    var sides = Strike(cam, loom, new[] { px - gap, px + gap }, warning, ct);
                    await UniTask.Delay(TimeSpan.FromSeconds(warning * 0.45f), cancellationToken: ct);
                    var middle = Strike(cam, loom, new[] { px }, warning * 0.75f, ct);
                    await UniTask.WhenAll(sides, middle);
                    break;

                case StabPattern.LeadLine:
                    var line = new float[3];
                    for (var i = 0; i < line.Length; i++) line[i] = px + heading * (1.2f + i * gap);
                    // One on the player too, so stopping dead is not a free answer.
                    var lineStrike = Strike(cam, loom, line, warning, ct);
                    var stayStrike = Strike(cam, loom, new[] { px }, warning * 1.15f, ct);
                    await UniTask.WhenAll(lineStrike, stayStrike);
                    break;

                case StabPattern.Sweep:
                    // Starts on the far side of the player and walks across them.
                    var from = px - heading * 4f * gap * 0.5f;
                    var tasks = new List<UniTask>();
                    for (var i = 0; i < 5; i++)
                    {
                        tasks.Add(Strike(cam, loom, new[] { from + heading * i * gap * 0.9f }, warning * 0.9f, ct));
                        await UniTask.Delay(TimeSpan.FromSeconds(0.22f), cancellationToken: ct);
                    }
                    await UniTask.WhenAll(tasks);
                    break;

                case StabPattern.Scatter:
                    // Pick the safe gap first, then fill the floor around it.
                    var safe = px + UnityEngine.Random.Range(-2.5f, 2.5f);
                    var spots = new List<float>();
                    for (var x = px - 6f; x <= px + 6f; x += gap)
                    {
                        if (Mathf.Abs(x - safe) < gap) continue;
                        if (UnityEngine.Random.value < 0.75f) spots.Add(x + UnityEngine.Random.Range(-0.3f, 0.3f));
                    }
                    await Strike(cam, loom, spots.ToArray(), warning * 1.1f, ct);
                    break;

                case StabPattern.Pincer:
                    // Two walls close in a step at a time and stop one gap short. Standing still
                    // is right, until a late ring opens on the middle: then step into the
                    // spot a wall already hit.
                    var walls = new List<UniTask>();
                    for (var step = 0; step < 3; step++)
                    {
                        var reach = (4 - step) * gap;
                        walls.Add(Strike(cam, loom, new[] { px - reach, px + reach }, warning * 0.9f, ct));
                        await UniTask.Delay(TimeSpan.FromSeconds(0.3f), cancellationToken: ct);
                    }
                    await UniTask.WhenAll(walls);
                    await Strike(cam, loom, new[] { px }, warning, ct);
                    break;

                case StabPattern.Checker:
                    // Every other spot lands first, the spots between land next. The player
                    // starts on a first wave spot, so it is one step over, then one step back.
                    var first = new List<float>();
                    var second = new List<float>();
                    for (var k = -4; k <= 4; k++) (k % 2 == 0 ? first : second).Add(px + k * gap);
                    var waveA = Strike(cam, loom, first.ToArray(), warning * 1.1f, ct);
                    await UniTask.Delay(TimeSpan.FromSeconds(warning * 0.75f), cancellationToken: ct);
                    var waveB = Strike(cam, loom, second.ToArray(), warning, ct);
                    await UniTask.WhenAll(waveA, waveB);
                    break;

                case StabPattern.Rain:
                    // Singles all over, one after another. Every third one is aimed, so
                    // waiting for the rain to pass is not an answer either.
                    var drops = new List<UniTask>();
                    for (var i = 0; i < 8; i++)
                    {
                        var x = i % 3 == 2 ? LeadAim(0) : px + UnityEngine.Random.Range(-6f, 6f);
                        drops.Add(Strike(cam, loom, new[] { x }, warning * 0.95f, ct));
                        await UniTask.Delay(TimeSpan.FromSeconds(0.3f), cancellationToken: ct);
                        var p = PlayerLocator.Player;
                        if (p != null) px = p.transform.position.x;
                    }
                    await UniTask.WhenAll(drops);
                    break;

                case StabPattern.Cage:
                    // Rings on both sides and further out, the centre next. One side lands
                    // late: that is the door, and it has to be used before it shuts.
                    var door = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                    var cage = Strike(cam, loom, new[] { px - door * gap, px - 2f * gap, px + 2f * gap }, warning, ct);
                    await UniTask.Delay(TimeSpan.FromSeconds(warning * 0.3f), cancellationToken: ct);
                    var centre = Strike(cam, loom, new[] { px }, warning * 0.85f, ct);
                    var shut = Strike(cam, loom, new[] { px + door * gap }, warning * 1.6f, ct);
                    await UniTask.WhenAll(cage, centre, shut);
                    break;
            }
        }

        /// <summary>Warns at every x at once, then lands on all of them together.</summary>
        private async UniTask Strike(Camera cam, GameObject loom, float[] xs, float warning, CancellationToken ct)
        {
            if (xs == null || xs.Length == 0) return;
            var tasks = new UniTask[xs.Length];
            for (var i = 0; i < xs.Length; i++) tasks[i] = Stab(cam, i == 0 ? loom : null, xs[i], warning, ct);
            await UniTask.WhenAll(tasks);
        }

        private async UniTask Stab(Camera cam, GameObject loom, float x, float warning, CancellationToken ct)
        {
            var ground = GroundAt(x, cam.transform.position.y + cam.orthographicSize);
            var ring = SpawnRing(new Vector3(x, ground + 0.03f, -0.6f));
            var fill = ring.transform.GetChild(0);
            var ringSprite = ring.GetComponent<SpriteRenderer>();

            // The ring pulses (a telegraph pulses, a flash blinks) and a fill grows
            // to its edge: the moment the fill touches the rim is the moment it lands.
            await Tween(warning, t =>
            {
                var pulse = 0.55f + 0.45f * Mathf.PingPong(t * warning * 8f, 1f);
                ringSprite.color = new Color(ringTint.r, ringTint.g, ringTint.b, pulse);
                fill.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, t);
                if (loom != null) PlaceLoom(loom, cam, -0.25f * t, (t - 0.5f) * 6f);
            }, ct);

            // The stinger drops from the top of the frame onto the ring.
            var spike = SpawnSpike(cam, x, ground);
            var from = spike.transform.position;
            var to = new Vector3(x, ground + 0.05f, from.z);
            await Tween(0.07f, t => spike.transform.position = Vector3.Lerp(from, to, t * t), ct);

            Impact(x, ground);
            Destroy(ring);
            await UniTask.Delay(TimeSpan.FromSeconds(0.22f), cancellationToken: ct);
            await Tween(0.18f, t => spike.transform.position = Vector3.Lerp(to, from, t), ct);
            Destroy(spike);
            _spawned.Remove(spike);
            await UniTask.Delay(TimeSpan.FromSeconds(0.12f), cancellationToken: ct);
        }

        private void Impact(float x, float ground)
        {
            HitStop.Freeze(5);
            CameraFeel.Instance?.Shake(0.4f, Vector2.down);
            HitSpark.Play(new Vector3(x, ground + 0.1f, -0.5f), Vector2.up, 1.4f, new Color(0.2f, 0.18f, 0.2f));

            var player = PlayerLocator.Player;
            if (player == null || player.Vitals == null || !player.Vitals.IsAlive) return;
            var p = player.transform.position;
            if (Mathf.Abs(p.x - x) > stabRadius || p.y > ground + 2.2f) return;

            var away = Mathf.Sign(p.x - x);
            if (away == 0f) away = 1f;
            player.Vitals.Receive(new DamageInfo
            {
                amount = stabDamage,
                poiseDamage = 999f,
                source = gameObject,
                point = p + Vector3.up * 0.8f,
                knockback = new Vector2(away * 6f, 7f),
                hitStopFrames = 8,
                hitStunFrames = 20,
                screenShake = 0.3f,
                defendable = true,
            });
        }

        private async UniTask DropDown(Camera cam, CancellationToken ct)
        {
            var player = PlayerLocator.Player;
            var px = player != null ? player.transform.position.x : _groundedAt.x;

            // Land on whichever side of the player has more room, at least 4 m away.
            var landX = _groundedAt.x;
            foreach (var side in new[] { Mathf.Sign(_groundedAt.x - px), -Mathf.Sign(_groundedAt.x - px) })
            {
                var x = px + (side == 0f ? 1f : side) * 5f;
                if (!float.IsNaN(GroundAtOrNaN(x, cam.transform.position.y + cam.orthographicSize))) { landX = x; break; }
            }
            var ground = GroundAt(landX, cam.transform.position.y + cam.orthographicSize);

            _motor.Teleport(new Vector3(landX, cam.transform.position.y + cam.orthographicSize + 3f, _groundedAt.z));
            if (_sprite != null) _sprite.enabled = true;
            await MoveBody(new Vector3(landX, ground + 0.02f, _groundedAt.z), 0.28f, ct, accelerate: true);
            _body.Held = false;
            Physics.SyncTransforms();
            CameraFeel.Instance?.Shake(0.45f, Vector2.down);
            HitSpark.Play(new Vector3(landX, ground + 0.1f, -0.5f), Vector2.up, 1.2f, new Color(0.35f, 0.3f, 0.25f));
            GetComponentInChildren<SpriteFeel>()?.Land(12f);
        }

        // ---- Spawned visuals ----------------------------------------------------

        private GameObject SpawnLoom(Camera cam)
        {
            var go = new GameObject("Looming Beetle");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = InkLoom.Get();
            sr.color = loomTint;
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 40;
            go.transform.SetParent(cam.transform, false);
            _spawned.Add(go);
            return go;
        }

        private void PlaceLoom(GameObject loom, Camera cam, float drop, float tilt)
        {
            if (loom == null) return;
            var h = cam.orthographicSize;
            // Sized to the frame rather than the world, so it fills the top the same way at any zoom.
            var sprite = InkLoom.Get();
            var scale = 2f * h * cam.aspect * 0.8f / sprite.bounds.size.x * (loomScale / 3.2f);
            loom.transform.localScale = Vector3.one * scale;
            var height = sprite.bounds.size.y * scale;
            // Pivot is the top centre. drop 0 hangs it fully in view from the top edge,
            // negative pulls it back up out of the frame.
            var y = h + Mathf.Max(0f, -drop) / 2.5f * height;
            loom.transform.localPosition = new Vector3(0f, y, cam.nearClipPlane + 2f);
            loom.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }

        private void SpawnBlot(Camera cam, int index)
        {
            var h = cam.orthographicSize;
            var w = h * cam.aspect;
            var go = new GameObject("Ink Blot");
            go.transform.SetParent(cam.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = InkBlots.Get(index);
            sr.color = new Color(0.04f, 0.035f, 0.05f, inkOpacity);
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 30;

            // Spread over the frame, avoiding the bottom strip near the ground a
            // little, so the stab rings stay findable.
            var pos = new Vector3(UnityEngine.Random.Range(-w * 0.95f, w * 0.95f),
                UnityEngine.Random.Range(-h * 0.5f, h * 0.95f), cam.nearClipPlane + 1.5f + index * 0.001f);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            var size = UnityEngine.Random.Range(h * 0.45f, h * 0.85f);
            Splat(go.transform, size).Forget();
            _ink.Add(sr);
        }

        private async UniTaskVoid Splat(Transform blot, float size)
        {
            // Lands big and settles: overshoot then back, the way a splash does.
            var t = 0f;
            while (t < 0.18f && blot != null)
            {
                t += Time.deltaTime;
                var k = t / 0.18f;
                blot.localScale = Vector3.one * size * (k < 0.7f ? Mathf.Lerp(0.2f, 1.12f, k / 0.7f) : Mathf.Lerp(1.12f, 1f, (k - 0.7f) / 0.3f));
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
            if (blot != null) blot.localScale = Vector3.one * size;
        }

        private async UniTaskVoid FadeInkLater(float delay)
        {
            var mine = new List<SpriteRenderer>(_ink);
            _ink.Clear();
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: destroyCancellationToken);
            var t = 0f;
            const float fade = 1.6f;
            while (t < fade)
            {
                t += Time.deltaTime;
                foreach (var sr in mine)
                {
                    if (sr == null) continue;
                    var c = sr.color;
                    c.a = inkOpacity * (1f - t / fade);
                    sr.color = c;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
            foreach (var sr in mine) if (sr != null) Destroy(sr.gameObject);
        }

        private GameObject SpawnRing(Vector3 at)
        {
            var go = new GameObject("Stab Warning");
            go.transform.position = at;
            // Flattened: a circle lying on the floor seen from the side.
            go.transform.localScale = new Vector3(stabRadius * 2f, stabRadius * 0.7f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ProceduralSprites.Ring(96, 0.14f);
            sr.color = ringTint;
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 50; // above the ink, always readable

            var fill = new GameObject("Fill");
            fill.transform.SetParent(go.transform, false);
            var fsr = fill.AddComponent<SpriteRenderer>();
            fsr.sprite = ProceduralSprites.Dot(64);
            fsr.color = new Color(ringTint.r, ringTint.g, ringTint.b, 0.35f);
            fsr.sortingLayerName = "Foreground";
            fsr.sortingOrder = 49;
            _spawned.Add(go);
            return go;
        }

        private GameObject SpawnSpike(Camera cam, float x, float ground)
        {
            var go = new GameObject("Tail Stinger");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = StingerSprite.Get();
            sr.color = loomTint;
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 45;
            var top = cam.transform.position.y + cam.orthographicSize + 6f;
            go.transform.position = new Vector3(x, top, -0.7f);
            go.transform.localScale = new Vector3(1f, 1f, 1f);
            _spawned.Add(go);
            return go;
        }

        // ---- Helpers -------------------------------------------------------------

        private float LeadAim(int index)
        {
            var player = PlayerLocator.Player;
            if (player == null) return transform.position.x;
            var x = player.transform.position.x;
            // The first stab is honest, later ones read where the player is going.
            if (index > 0) x += player.Motor.Velocity.x * 0.45f;
            return x + UnityEngine.Random.Range(-0.4f, 0.4f);
        }

        private float GroundAt(float x, float fromY)
        {
            var y = GroundAtOrNaN(x, fromY);
            return float.IsNaN(y) ? _groundedAt.y : y;
        }

        private float GroundAtOrNaN(float x, float fromY)
        {
            var mask = groundMask & ~LayerMask.GetMask("Player", "Enemy", "EnemyHitbox", "Ignore Raycast");
            return Physics.Raycast(new Vector3(x, fromY, 0f), Vector3.down, out var hit, 60f, mask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : float.NaN;
        }

        private async UniTask MoveBody(Vector3 to, float seconds, CancellationToken ct, bool accelerate = false)
        {
            var from = transform.position;
            await Tween(seconds, t =>
            {
                var k = accelerate ? t * t : EaseOut(t);
                _motor.Teleport(Vector3.Lerp(from, to, k));
            }, ct);
        }

        private async UniTask Squash(float seconds, float sx, float sy, CancellationToken ct)
        {
            if (_sprite == null) { await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct); return; }
            var t = _sprite.transform;
            var face = Mathf.Sign(t.localScale.x);
            await Tween(seconds, k => t.localScale = new Vector3(face * Mathf.Lerp(1f, sx, k), Mathf.Lerp(1f, sy, k), 1f), ct);
            t.localScale = new Vector3(face * 0.85f, 1.25f, 1f);
        }

        private static async UniTask Tween(float seconds, Action<float> step, CancellationToken ct)
        {
            var t = 0f;
            while (t < seconds)
            {
                ct.ThrowIfCancellationRequested();
                t += Time.deltaTime;
                step(Mathf.Clamp01(t / seconds));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            step(1f);
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    }

    /// <summary>Ink blot shapes painted once in code: a soft core with ragged splash arms and droplets.</summary>
    internal static class InkBlots
    {
        private static readonly Sprite[] Cache = new Sprite[4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Array.Clear(Cache, 0, Cache.Length);

        public static Sprite Get(int index)
        {
            var i = Mathf.Abs(index) % Cache.Length;
            if (Cache[i] != null) return Cache[i];

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(1000 + i * 37);
            var arms = 7 + i * 2;
            var armAngle = new float[arms];
            var armLen = new float[arms];
            for (var a = 0; a < arms; a++)
            {
                armAngle[a] = (float)rng.NextDouble() * Mathf.PI * 2f;
                armLen[a] = 0.55f + (float)rng.NextDouble() * 0.4f;
            }
            var drops = new Vector3[10];
            for (var d = 0; d < drops.Length; d++)
            {
                var ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                var r = 0.6f + (float)rng.NextDouble() * 0.35f;
                drops[d] = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0.03f + (float)rng.NextDouble() * 0.05f);
            }

            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = x / (size - 1f) * 2f - 1f;
                var v = y / (size - 1f) * 2f - 1f;
                var d = Mathf.Sqrt(u * u + v * v);
                var ang = Mathf.Atan2(v, u);

                // Core edge wobbles with noise, arms push it out in a few directions.
                var edge = 0.38f + 0.07f * Mathf.PerlinNoise(Mathf.Cos(ang) * 2f + i * 3f, Mathf.Sin(ang) * 2f + i * 5f);
                for (var a = 0; a < arms; a++)
                {
                    var diff = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, armAngle[a] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    edge = Mathf.Max(edge, armLen[a] * Mathf.Exp(-diff * diff * 90f));
                }
                var alpha = Mathf.Clamp01((edge - d) * 40f);
                foreach (var drop in drops)
                {
                    var dd = Vector2.Distance(new Vector2(u, v), drop);
                    alpha = Mathf.Max(alpha, Mathf.Clamp01((drop.z - dd) * 60f));
                }
                // A little grain inside, so it reads as ink, not as a flat shape.
                var grain = 0.85f + 0.15f * Mathf.PerlinNoise(u * 9f + i, v * 9f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * grain * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            Cache[i] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / 2f);
            return Cache[i];
        }
    }

    /// <summary>The tail's stinger seen from the side: a long tapering segmented spike, point down.</summary>
    internal static class StingerSprite
    {
        private static Sprite _cache;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _cache = null;

        public static Sprite Get()
        {
            if (_cache != null) return _cache;
            const int w = 64, h = 512;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                var k = y / (h - 1f);                       // 0 at the point, 1 at the top
                var half = Mathf.Lerp(0.02f, 0.42f, Mathf.Sqrt(k));
                // Segments: the width pinches every so often, like a scorpion tail.
                half *= 0.82f + 0.18f * Mathf.Abs(Mathf.Sin(k * 22f));
                for (var x = 0; x < w; x++)
                {
                    var u = Mathf.Abs(x / (w - 1f) * 2f - 1f) * 0.5f;
                    var a = Mathf.Clamp01((half - u) * 90f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            // Pivot at the point, 1 m wide, 8 m long.
            _cache = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), w);
            return _cache;
        }
    }
}
