using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using StoryKit;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Weizza
{
    /// <summary>
    /// A typed command line for play testing, opened with the backquote key.
    /// IMGUI on purpose, the same reason DebugPanel is: it has to work in a
    /// build, and Odin cannot.
    ///
    /// Commands are registered in one table below, each with a line of help,
    /// so "help" always tells the truth about what exists.
    /// </summary>
    [AddComponentMenu("Weizza/Developer Console")]
    public sealed class DeveloperConsole : MonoBehaviour
    {
        private sealed class Command
        {
            public string help;
            public Action<string[]> run;
        }

        private readonly Dictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _log = new();
        private readonly List<string> _history = new();
        private string _input = "";
        private bool _open;
        private int _historyIndex;
        private bool _focusNext;
        private IDisposable _inputLock;
        private Vector2 _scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            if (FindFirstObjectByType<DeveloperConsole>() != null) return;
            new GameObject("Developer Console").AddComponent<DeveloperConsole>();
        }

        private void Awake()
        {
            Register("help", "list every command", _ =>
            {
                var names = new List<string>(_commands.Keys);
                names.Sort();
                foreach (var name in names) Print($"{name,-10} {_commands[name].help}");
            });
            // God is for walking through a test quickly, so it also kills in one hit.
            // "onehit" alone toggles just the killing half.
            Register("god", "toggle god mode: no damage taken, and every hit kills", _ => WithPlayer(p =>
            {
                p.Vitals.GodMode = !p.Vitals.GodMode;
                Vitals.PlayerOneHitKill = p.Vitals.GodMode;
                Print("god " + (p.Vitals.GodMode ? "on, one hit kill on" : "off, one hit kill off"));
            }));
            Register("onehit", "toggle one hit kill: everything the player hits dies at once", _ =>
            {
                Vitals.PlayerOneHitKill = !Vitals.PlayerOneHitKill;
                Print("one hit kill " + (Vitals.PlayerOneHitKill ? "on" : "off"));
            });
            Register("heal", "full health", _ => WithPlayer(p => p.Vitals.SetHealth(p.Vitals.Max)));
            Register("kill", "kill the player", _ => WithPlayer(p => p.Vitals.Kill()));
            Register("hurt", "hurt <amount>", a => WithPlayer(p => p.Vitals.SetHealth(p.Vitals.Current - Num(a, 1, 10f))));
            Register("stance", "stance <Normal|Than|Se|In>, no switch window", a => WithPlayer(p =>
            {
                if (a.Length > 1 && Enum.TryParse<StanceId>(a[1], true, out var id)) p.ForceStance(id);
                else Print("usage: stance Than");
            }));
            Register("learn", "learn <stance|all>", a =>
            {
                if (a.Length > 1 && a[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    StanceProgress.Learn(StanceId.Than);
                    StanceProgress.Learn(StanceId.Se);
                    StanceProgress.Learn(StanceId.In);
                }
                else if (a.Length > 1 && Enum.TryParse<StanceId>(a[1], true, out var id)) StanceProgress.Learn(id);
            });
            Register("tp", "tp <x> <y>, or tp cp to the current checkpoint", a => WithPlayer(p =>
            {
                if (a.Length > 1 && a[1] == "cp" && RespawnDirector.Instance != null)
                    p.Motor.Teleport(RespawnDirector.Instance.RespawnPoint);
                else
                    p.Motor.Teleport(new Vector3(Num(a, 1, p.transform.position.x), Num(a, 2, p.transform.position.y), 0f));
                Physics.SyncTransforms();
            }));
            Register("time", "time <scale>, the base time scale", a => StoryTimeScale.SetBase(Num(a, 1, 1f)));
            Register("give", "give <itemId> [amount]", a =>
            {
                if (a.Length < 2) return;
                StoryServices.Get<IInventoryService>()?.GiveItem(a[1], Mathf.RoundToInt(Num(a, 2, 1f)));
            });
            Register("save", "save the current slot", _ => SaveSystem.Instance?.Save());
            Register("load", "load the current slot", _ => Print(SaveSystem.Instance != null && SaveSystem.Instance.Load() ? "loaded" : "no save"));
            Register("poison", "poison the player", _ => WithPlayer(p => p.GetComponent<EffectReceiver>()?.Apply(DefaultEffects.Poison)));
            Register("cleanse", "remove every effect", _ => WithPlayer(p => p.GetComponent<EffectReceiver>()?.ClearAll()));
            Register("numbers", "toggle damage numbers", _ => GameSettings.DamageNumbers = !GameSettings.DamageNumbers);
            Register("frames", "toggle the frame data overlay", _ => GameSettings.FrameDataOverlay = !GameSettings.FrameDataOverlay);
            Register("boss", "reset the boss and stand just outside its range, loading its chunk if needed", _ => GoToBossAsync().Forget());
            Register("bossmove", "bossmove <id>, make the boss play one move now. No id lists them", a =>
            {
                if (a.Length < 2) { Print(BossMoveList()); return; }
                BossMoveAsync(a[1]).Forget();
            });
            Register("spawn", "spawn <enemy> [count], drop enemies from Prefabs/Enemies in front of you (Editor only)", a =>
            {
#if UNITY_EDITOR
                if (a.Length < 2) { Print("usage: spawn BombFly 3"); return; }
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Enemies/{a[1]}.prefab");
                if (prefab == null) { Print($"no prefab Assets/Prefabs/Enemies/{a[1]}.prefab"); return; }
                var count = Mathf.Clamp(Mathf.RoundToInt(Num(a, 2, 1f)), 1, 12);
                WithPlayer(p =>
                {
                    var ahead = p.Motor.Facing.Sign();
                    for (var i = 0; i < count; i++)
                    {
                        // Spread out ahead, at a few heights, the way a swarm perches.
                        var at = p.transform.position + new Vector3(ahead * (6f + i * 1.3f), 0.6f + (i % 3) * 0.7f, 0f);
                        Instantiate(prefab, at, Quaternion.identity);
                    }
                });
                Print($"spawned {count} {a[1]}");
#else
                Print("spawn only works in the Editor");
#endif
            });
            Register("bosshp", "bosshp <percent>, set boss health (60 starts phase 2, 30 the finisher)", a =>
            {
                var boss = FindFirstObjectByType<MiniBossBrain>();
                if (boss == null) { Print("no boss loaded, type boss first"); return; }
                boss.Vitals.SetHealth(boss.Vitals.Max * Mathf.Clamp01(Num(a, 1, 100f) / 100f));
                Print($"boss health {boss.Vitals.Current:0} / {boss.Vitals.Max:0}");
            });
            Register("event", "event <name> [payload], raise a StoryKit event", a =>
            {
                if (a.Length > 1) StoryEvents.Raise(a[1], a.Length > 2 ? a[2] : null);
            });
            Register("settings", "open the settings screen", _ =>
            {
                SetOpen(false);
                FindFirstObjectByType<SettingsScreen>()?.Open();
            });
            Action<string[]> respawn = _ => WithPlayer(p =>
            {
                if (RespawnDirector.Instance != null) p.Motor.Teleport(RespawnDirector.Instance.RespawnPoint);
                Physics.SyncTransforms();
                p.Revive(1f);
            });
            Register("respawn", "back on your feet at the current checkpoint, full health", respawn);
            Register("renew", "same as respawn", respawn);
            Register("restart", "restart [scene], reload the world from the start (Act0_Core by default)", a =>
            {
                SetOpen(false);
                StoryTimeScale.SetBase(1f);
                UnityEngine.SceneManagement.SceneManager.LoadScene(a.Length > 1 ? a[1] : "Act0_Core");
            });
            Register("clear", "clear this log", _ => _log.Clear());

            Print("Weizza dev console. Type help.");
        }

        // The boss lives in a chunk that is only loaded once the player walks near
        // it, so "boss" from the start of the map found nothing. Rooms with a Boss
        // map pin are tried first, then every chunk in turn until one holds a boss.
        private async UniTaskVoid GoToBossAsync()
        {
            var ct = destroyCancellationToken;
            var boss = FindFirstObjectByType<MiniBossBrain>(FindObjectsInactive.Include);
            var atlas = Resources.Load<WorldAtlas>("World/WorldAtlas");
            var streamer = WorldStreamer.Instance;

            if (boss == null && atlas != null && streamer != null)
            {
                Print("looking for the boss...");
                var order = new List<RoomDefinition>();
                foreach (var room in atlas.rooms)
                {
                    if (room == null || room.pins == null) continue;
                    foreach (var pin in room.pins)
                        if (pin != null && pin.kind == MapPin.Kind.Boss) { order.Add(room); break; }
                }
                foreach (var room in atlas.rooms)
                    if (room != null && !order.Contains(room)) order.Add(room);

                foreach (var room in order)
                {
                    await streamer.EnsureLoadedAsync(room.id, ct);
                    boss = FindFirstObjectByType<MiniBossBrain>(FindObjectsInactive.Include);
                    if (boss != null) break;
                }
            }

            if (boss == null)
            {
                Print("no boss anywhere on the map");
                return;
            }

            WithPlayer(p =>
            {
                boss.ResetForTest();
                var at = boss.transform.position + Vector3.left * 11f;
                var ground = Physics.Raycast(at + Vector3.up * 8f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.point.y : at.y;
                p.Motor.Teleport(new Vector3(at.x, ground + 0.1f, 0f));
                Physics.SyncTransforms();
            });
            if (streamer != null) await streamer.EnsureLoadedAtAsync(boss.transform.position, ct);
            // Left asleep: walking in is what wakes it, so the disguise and the intro play.
            SetOpen(false);
            Print("boss ready, walk right to wake it");
        }

        // Plays one boss move by id, waiting up to a few seconds for the current one to end.
        private async UniTaskVoid BossMoveAsync(string id)
        {
            var boss = FindFirstObjectByType<MiniBossBrain>();
            if (boss == null) { Print("no boss loaded, type boss first"); return; }
            var until = Time.time + 4f;
            while (Time.time < until)
            {
                if (!boss.SkillRunning && boss.ForceMove(id)) { Print("boss: " + id); return; }
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
            Print($"boss could not start '{id}' (busy, or no such move)");
        }

        private string BossMoveList()
        {
            var boss = FindFirstObjectByType<MiniBossBrain>();
            if (boss == null) return "no boss loaded, type boss first";
            var ids = new List<string>();
            if (boss.Body.Data != null) foreach (var m in boss.Body.Data.moves) if (m != null) ids.Add(m.id);
            foreach (var s in boss.GetComponents<BossSkill>()) ids.Add(s.Id);
            return "moves: " + string.Join(", ", ids);
        }

        public void Register(string name, string help, Action<string[]> run) =>
            _commands[name] = new Command { help = help, run = run };

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // F3 as well as backquote: on a Thai Windows keyboard the backquote key
            // is the language switch, so pressing it also flips the input language.
            if (keyboard.backquoteKey.wasPressedThisFrame || keyboard.f3Key.wasPressedThisFrame) SetOpen(!_open);
        }

        private void SetOpen(bool open)
        {
            _open = open;
            _focusNext = open;
            if (open && _inputLock == null) _inputLock = StoryControl.Lock("Dev console");
            if (!open)
            {
                _inputLock?.Dispose();
                _inputLock = null;
            }
        }

        private void OnDisable() => SetOpen(false);

        private void OnGUI()
        {
            if (!_open) return;

            const float height = 260f;
            GUI.Box(new Rect(0, 0, Screen.width, height), GUIContent.none);

            GUILayout.BeginArea(new Rect(8, 6, Screen.width - 16, height - 12));
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(height - 46));
            foreach (var line in _log) GUILayout.Label(line);
            GUILayout.EndScrollView();

            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    Execute(_input);
                    _input = "";
                    e.Use();
                }
                else if (e.keyCode == KeyCode.UpArrow && _history.Count > 0)
                {
                    _historyIndex = Mathf.Clamp(_historyIndex - 1, 0, _history.Count - 1);
                    _input = _history[_historyIndex];
                    e.Use();
                }
                else if (e.keyCode is KeyCode.BackQuote or KeyCode.F3)
                {
                    // Update already toggled it closed this frame; only swallow the
                    // key so it does not type into the field.
                    e.Use();
                }
            }

            GUI.SetNextControlName("DeveloperConsoleInput");
            _input = GUILayout.TextField(_input);
            if (_focusNext)
            {
                GUI.FocusControl("DeveloperConsoleInput");
                _focusNext = false;
            }
            GUILayout.EndArea();
        }

        private void Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            _history.Add(line);
            _historyIndex = _history.Count;
            Print("> " + line);

            var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!_commands.TryGetValue(parts[0], out var command))
            {
                Print("unknown command, type help");
                return;
            }

            try
            {
                command.run(parts);
            }
            catch (Exception ex)
            {
                Print("error: " + ex.Message);
            }
        }

        private void Print(string line)
        {
            _log.Add(line);
            if (_log.Count > 200) _log.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void WithPlayer(Action<PlayerController> action)
        {
            var player = PlayerLocator.Player;
            if (player == null)
            {
                Print("no player in the scene");
                return;
            }
            action(player);
        }

        private static float Num(string[] args, int index, float fallback) =>
            args.Length > index && float.TryParse(args[index], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}

