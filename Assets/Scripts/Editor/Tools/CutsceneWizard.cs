using System;
using System.IO;
using System.Reflection;
using StoryKit;
using StoryKit.Flow;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Makes a ready to play Flow Graph from a template in one click: meeting an
    /// NPC, a pull back reveal, a boss intro, learning a stance. Every graph comes
    /// out already wired the safe way (control locked and always unlocked again,
    /// letterbox in and out, camera handed back), which are the three things a hand
    /// built cutscene most often forgets, and a forgotten unlock is a softlock.
    ///
    /// The result is an ordinary Flow Graph asset: open it in StoryKit's graph
    /// editor to change anything, and drop it on a Story Zone to play it.
    /// </summary>
    public sealed class CutsceneWizard : EditorWindow
    {
        private enum Template
        {
            AreaTitle,
            MeetNpc,
            Reveal,
            BossIntro,
            LearnStance,
            EndOfSlice,
            Empty,
        }

        private Template _template = Template.MeetNpc;
        private string _name = "Act0_SokaMeet";
        private string _folder = "Assets/GameData/Narrative/Cutscenes";
        private string _actorId = "SOKA";
        private string _lineKeyPrefix = "DLG_ACT0_SOKA_MEET_";
        private int _lines = 3;
        private string _targetStageId = "reveal_point";
        private string _bannerKey = "AREA_FOREST";
        private string _bannerFallback = "The Forest";
        private string _bossStageId = "boss";
        private StanceId _stance = StanceId.Se;
        private float _holdSeconds = 2f;

        [MenuItem("Weizza/Story/Cutscene Wizard", priority = 0)]
        private static void Open() => GetWindow<CutsceneWizard>("Cutscene Wizard");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Pick a template, fill the few fields it asks for, press Create. " +
                                    "Text is always a sheet key; the wizard numbers the keys in tens " +
                                    "(010, 020...) so a line can be inserted later without renumbering.", MessageType.Info);

            _template = (Template)EditorGUILayout.EnumPopup("Template", _template);
            EditorGUILayout.LabelField(Describe(_template), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            _name = EditorGUILayout.TextField("Graph name", _name);
            _folder = EditorGUILayout.TextField("Folder", _folder);
            EditorGUILayout.Space();

            switch (_template)
            {
                case Template.MeetNpc:
                    _actorId = EditorGUILayout.TextField("Speaker id", _actorId);
                    _lineKeyPrefix = EditorGUILayout.TextField("Line key prefix", _lineKeyPrefix);
                    _lines = EditorGUILayout.IntSlider("Lines", _lines, 1, 20);
                    EditorGUILayout.LabelField("Keys: " + Key(1) + " ... " + Key(_lines), EditorStyles.miniLabel);
                    break;
                case Template.AreaTitle:
                    _bannerKey = EditorGUILayout.TextField("Title key", _bannerKey);
                    _bannerFallback = EditorGUILayout.TextField("Title fallback", _bannerFallback);
                    _holdSeconds = EditorGUILayout.Slider("Hold seconds", _holdSeconds, 0.5f, 8f);
                    break;
                case Template.Reveal:
                    _targetStageId = EditorGUILayout.TextField("Look at (Story Stage Id)", _targetStageId);
                    _bannerKey = EditorGUILayout.TextField("Banner key", _bannerKey);
                    _bannerFallback = EditorGUILayout.TextField("Banner fallback", _bannerFallback);
                    _holdSeconds = EditorGUILayout.Slider("Hold seconds", _holdSeconds, 0.5f, 8f);
                    break;
                case Template.BossIntro:
                    _bossStageId = EditorGUILayout.TextField("Boss (Story Stage Id)", _bossStageId);
                    _bannerKey = EditorGUILayout.TextField("Boss name key", _bannerKey);
                    _bannerFallback = EditorGUILayout.TextField("Boss name fallback", _bannerFallback);
                    _holdSeconds = EditorGUILayout.Slider("Hold seconds", _holdSeconds, 0.5f, 5f);
                    break;
                case Template.LearnStance:
                    _stance = (StanceId)EditorGUILayout.EnumPopup("Stance", _stance);
                    _bannerKey = EditorGUILayout.TextField("Banner key", _bannerKey);
                    _bannerFallback = EditorGUILayout.TextField("Banner fallback", _bannerFallback);
                    break;
                case Template.EndOfSlice:
                    _bannerKey = EditorGUILayout.TextField("Logo key", _bannerKey);
                    _bannerFallback = EditorGUILayout.TextField("Logo fallback", _bannerFallback);
                    _holdSeconds = EditorGUILayout.Slider("Logo hold seconds", _holdSeconds, 1f, 8f);
                    break;
            }

            EditorGUILayout.Space();
            GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);
            if (GUILayout.Button("Create Graph", GUILayout.Height(34))) Create();
            GUI.backgroundColor = Color.white;
        }

        private static string Describe(Template t) => t switch
        {
            Template.AreaTitle => "Bars in, player stops, a huge area title fades in and out, bars out. The camera does not move.",
            Template.MeetNpc => "Bars in, player locked, each line said with the camera on the speaker, then everything handed back.",
            Template.Reveal => "Bars in, camera pulls wide onto a point, a banner, hold, then back to play.",
            Template.BossIntro => "Bars in, wide shot on the boss, its name, then the fight starts as control returns.",
            Template.LearnStance => "Close on the player, the stance is learned and switched to, a banner names it.",
            Template.EndOfSlice => "Act 0's last beat: bars in, a slow pull back over the player until the forest dwarfs them, the logo, then fade to black. Control is not handed back.",
            _ => "Locked and unlocked, with nothing in between. Add nodes in the graph editor.",
        };

        private string Key(int line) => $"{_lineKeyPrefix}{line * 10:000}";

        // ---- Building ----------------------------------------------------------------

        private FlowGraphAsset _graph;
        private int _row;

        private void Create()
        {
            Directory.CreateDirectory(_folder);
            var path = $"{_folder}/{_name}.asset";
            if (File.Exists(path) && !EditorUtility.DisplayDialog("Cutscene Wizard", $"{path} exists. Replace it?", "Replace", "Cancel"))
                return;

            _graph = CreateInstance<FlowGraphAsset>();
            _row = 0;

            var lockNode = Add(new LockControlNode { Reason = _name }, "Lock player");
            var bars = Add(Letterbox(true), "Bars in");
            _graph.ConnectRunning(lockNode, bars);
            _graph.SetEntry(lockNode);

            StoryNode last = bars;
            switch (_template)
            {
                case Template.MeetNpc:
                    for (var i = 1; i <= _lines; i++)
                    {
                        var say = Add(new SayNode
                        {
                            SpeakerId = _actorId,
                            LineKey = Key(i),
                            FallbackText = $"({Key(i)})",
                            Shot = ShotFraming.Medium,
                        }, $"Line {i * 10:000}");
                        last = Chain(last, say);
                    }
                    break;

                case Template.AreaTitle:
                    last = Chain(last, Add(Title(_bannerKey, _bannerFallback, _holdSeconds), "Area title"));
                    break;

                case Template.Reveal:
                    last = Chain(last, Add(PullBack(_targetStageId, 14f, 4f, 4f), "Pull back"));
                    last = Chain(last, Add(Banner(_bannerKey, _bannerFallback, _holdSeconds), "Banner"));
                    last = Chain(last, Add(new WaitNode { Seconds = _holdSeconds }, "Hold"));
                    last = Chain(last, Add(Frame("", ShotFraming.Default, 1f, true), "Camera back"));
                    break;

                case Template.BossIntro:
                    last = Chain(last, Add(Frame(_bossStageId, ShotFraming.Wide, 1f, false), "Show the boss"));
                    last = Chain(last, Add(Banner(_bannerKey, _bannerFallback, _holdSeconds), "Boss name"));
                    last = Chain(last, Add(new WaitNode { Seconds = _holdSeconds }, "Hold"));
                    last = Chain(last, Add(Frame("", ShotFraming.Default, 0.6f, true), "Camera back"));
                    break;

                case Template.LearnStance:
                    last = Chain(last, Add(Frame("player", ShotFraming.CloseUp, 0.8f, false), "Close on player"));
                    last = Chain(last, Add(Learn(_stance), "Learn " + _stance));
                    last = Chain(last, Add(Banner(_bannerKey, _bannerFallback, 2.5f), "Name the stance"));
                    last = Chain(last, Add(new WaitNode { Seconds = 1.5f }, "Hold"));
                    last = Chain(last, Add(Frame("", ShotFraming.Default, 0.8f, true), "Camera back"));
                    break;
            }

            if (_template == Template.EndOfSlice)
            {
                // The slice ends here: a long pull back, the logo over the wide shot,
                // then black. Control stays locked on purpose.
                last = Chain(last, Add(new WaitNode { Seconds = 0.8f }, "Breath"));
                last = Chain(last, Add(PullBack("player", 18f, 7f, 7f), "Pull back"));
                last = Chain(last, Add(Title(_bannerKey, _bannerFallback, _holdSeconds), "Logo"));
                Chain(last, Add(Fade(1f, 2.5f), "Fade to black"));
                Save();
                return;
            }

            last = Chain(last, Add(Letterbox(false), "Bars out"));
            var unlock = Add(new CancelNode { TargetNodeId = lockNode.Id }, "Unlock player");
            last = Chain(last, unlock);

            if (_template == Template.BossIntro)
                Chain(last, Add(Wake(_bossStageId), "Start the fight"));

            Save();
        }

        private void Save()
        {
            var path = $"{_folder}/{_name}.asset";
            _graph.EnsureIds();
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(_graph, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = _graph;
            EditorGUIUtility.PingObject(_graph);
            Debug.Log($"[Cutscene Wizard] Made {path}. Put it on a Weizza > Story > Story Zone in the level to play it.", _graph);
        }

        private T Add<T>(T node, string title) where T : StoryNode
        {
            node.Id = Guid.NewGuid().ToString("N");
            node.Title = title;
            node.Position = new Vector2(0f, _row++ * 110f);
            return _graph.AddNode(node);
        }

        private StoryNode Chain(StoryNode from, StoryNode to)
        {
            _graph.Connect(from, to);
            return to;
        }

        // Weizza's own nodes keep their fields private for the Inspector; the wizard
        // is the one caller that needs to fill them, so it does it by name here
        // rather than every node growing public setters nobody else uses.
        private static T Fill<T>(T node, string field, object value)
        {
            var f = typeof(T).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(node, value);
            else Debug.LogWarning($"[Cutscene Wizard] {typeof(T).Name} has no field {field}.");
            return node;
        }

        private static LetterboxNode Letterbox(bool show) => new() { Show = show, Duration = 0.5f };

        private static FrameShotNode Frame(string stageId, ShotFraming framing, float blend, bool back)
        {
            var node = new FrameShotNode();
            Fill(node, "_targetStageId", stageId);
            Fill(node, "_framing", framing);
            Fill(node, "_blend", blend);
            Fill(node, "_returnToGameCamera", back);
            return node;
        }

        private static PullBackNode PullBack(string stageId, float size, float rise, float seconds)
        {
            var node = new PullBackNode();
            Fill(node, "_targetStageId", stageId);
            Fill(node, "_toSize", size);
            Fill(node, "_rise", rise);
            Fill(node, "_seconds", seconds);
            return node;
        }

        private static FadeNode Fade(float toAlpha, float seconds)
        {
            var node = new FadeNode();
            Fill(node, "_toAlpha", toAlpha);
            Fill(node, "_duration", seconds);
            return node;
        }

        private static LocalizedBannerNode Banner(string key, string fallback, float seconds)
        {
            var node = new LocalizedBannerNode();
            Fill(node, "_key", key);
            Fill(node, "_fallback", fallback);
            Fill(node, "_seconds", seconds);
            return node;
        }

        private static AreaTitleNode Title(string key, string fallback, float hold)
        {
            var node = new AreaTitleNode();
            Fill(node, "_key", key);
            Fill(node, "_fallback", fallback);
            Fill(node, "_hold", hold);
            return node;
        }

        private static LearnStanceNode Learn(StanceId stance)
        {
            var node = new LearnStanceNode();
            Fill(node, "_stance", stance);
            Fill(node, "_switchToIt", true);
            return node;
        }

        private static WakeBossNode Wake(string stageId)
        {
            var node = new WakeBossNode();
            Fill(node, "_bossStageId", stageId);
            Fill(node, "_waitUntilDefeated", false);
            return node;
        }
    }
}
