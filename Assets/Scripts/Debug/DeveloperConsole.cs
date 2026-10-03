using System;
using System.Collections.Generic;
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
            Register("god", "toggle god mode", _ => WithPlayer(p =>
            {
                p.Vitals.GodMode = !p.Vitals.GodMode;
                Print("god " + (p.Vitals.GodMode ? "on" : "off"));
            }));
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
            Register("boss", "wake the boss in this scene", a =>
            {
                var boss = FindFirstObjectByType<MiniBossBrain>();
                if (boss == null)
                {
                    Print("no boss in the scene");
                    return;
                }
                boss.Activate();
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
            Register("clear", "clear this log", _ => _log.Clear());

            Print("Weizza dev console. Type help.");
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

