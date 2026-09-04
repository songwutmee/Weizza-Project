using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StoryKit.Flow;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Compiles .story text files into FlowGraphAssets: the Yarn/Ink-style
    /// writer workflow. Save the file, Unity re-imports, the graph updates.
    ///
    /// SYNTAX
    ///   === section_name ===          section (jump target)
    ///   [speaker]: text  #KEY         dialogue line (KEY optional, auto-generated)
    ///   * choice text  #KEY {mood >= 1}
    ///       indented body lines...    (4 spaces or 1 tab per level)
    ///       -> other_section
    ///   << command args >>            var.set / var.add / event / quest.start /
    ///                                 quest.complete / give / wait / bgm / fade / save
    ///   -> section_name               divert
    ///   -> END                        end the graph
    ///   // comment
    ///
    /// Line keys: text after # is the localization key; if omitted one is
    /// generated deterministically (FILE_SECTION_N). The author text is stored
    /// as fallback so the story plays before any translation exists.
    /// Export all keys via Tools > StoryKit > Export Story Lines CSV.
    /// </summary>
    [ScriptedImporter(1, "story")]
    public sealed class StoryScriptImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = System.IO.File.ReadAllText(ctx.assetPath);
            string fileId = System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath)
                .ToUpperInvariant().Replace(" ", "_");

            var graph = ScriptableObject.CreateInstance<FlowGraphAsset>();
            graph.name = System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath);

            try
            {
                new StoryCompiler(graph, fileId).Compile(text);
            }
            catch (Exception ex)
            {
                ctx.LogImportError($"[StoryKit] Failed to compile {ctx.assetPath}: {ex.Message}");
            }

            ctx.AddObjectToAsset("graph", graph);
            ctx.SetMainObject(graph);
        }
    }

    /// <summary>Parser/compiler. Kept separate so tests can run it on raw strings.</summary>
    public sealed class StoryCompiler
    {
        private readonly FlowGraphAsset _graph;
        private readonly string _fileId;
        private readonly Dictionary<string, string> _sectionEntry = new(StringComparer.Ordinal);
        private readonly List<(Action<string> connect, string section)> _pendingDiverts = new();
        private EndNode _sharedEnd;
        private int _keyCounter;
        private int _layoutY;

        private static readonly Regex SectionRx = new(@"^===\s*(\w+)\s*===\s*$");
        private static readonly Regex SayRx = new(@"^\[([^\]\s]+)(?:\s+(\w+))?\]\s*:\s*(.*)$");
        private static readonly Regex KeyRx = new(@"\s#([A-Za-z0-9_]+)\s*$");
        private static readonly Regex GuardRx = new(@"\{\s*([\w\.]+)\s*(>=|<=|==|!=|>|<)\s*(-?\d+(?:\.\d+)?)\s*\}");
        private static readonly Regex CommandRx = new(@"^<<\s*(.+?)\s*>>$");

        public StoryCompiler(FlowGraphAsset graph, string fileId)
        {
            _graph = graph;
            _fileId = fileId;
        }

        public void Compile(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');

            // Pass 1: split into sections.
            var sections = new List<(string name, List<string> body)>();
            List<string> current = null;
            foreach (string raw in lines)
            {
                var match = SectionRx.Match(raw.Trim());
                if (match.Success)
                {
                    current = new List<string>();
                    sections.Add((match.Groups[1].Value, current));
                }
                else
                {
                    current?.Add(raw);
                }
            }
            if (sections.Count == 0)
                sections.Add(("main", new List<string>(lines)));

            // Pass 2: compile each section.
            foreach (var (name, body) in sections)
            {
                _layoutY += 3;
                var block = ParseBlock(body, 0, name);
                _sectionEntry[name] = block.entryId;
            }

            // Pass 3: resolve diverts.
            foreach (var (connect, section) in _pendingDiverts)
            {
                if (section == "END")
                {
                    connect(GetSharedEnd().Id);
                }
                else if (_sectionEntry.TryGetValue(section, out string target))
                {
                    connect(target);
                }
                else
                {
                    Debug.LogWarning($"[StoryKit] .story divert to unknown section '{section}'.");
                }
            }

            // Entry = StartNode → first section.
            var start = _graph.AddNode(new StartNode { Position = new Vector2(-250, 0) });
            _graph.SetEntry(start);
            if (sections.Count > 0 && _sectionEntry.TryGetValue(sections[0].name, out string first))
                start.NextNodeIds.Add(first);
        }

        private EndNode GetSharedEnd()
        {
            if (_sharedEnd == null)
                _sharedEnd = _graph.AddNode(new EndNode { Position = new Vector2(2200, 0) });
            return _sharedEnd;
        }

        private sealed class Block
        {
            public string entryId;
            /// <summary>Callbacks that connect loose chain ends to whatever comes next.</summary>
            public readonly List<Action<string>> openEnds = new();
        }

        /// <summary>Parses lines at one indent level into a node chain.</summary>
        private Block ParseBlock(List<string> lines, int startIndex, string sectionName,
            int indentLevel = 0, int? endIndexOut = null)
        {
            var block = new Block();
            int column = 0;

            void Append(StoryNode node)
            {
                node.Position = new Vector2(column * 260, _layoutY * 130);
                column++;
                _graph.AddNode(node);

                foreach (var connect in block.openEnds)
                    connect(node.Id);
                block.openEnds.Clear();

                block.entryId ??= node.Id;
                block.openEnds.Add(id => node.NextNodeIds.Add(id));
            }

            int i = startIndex;
            while (i < lines.Count)
            {
                string raw = lines[i];
                int indent = IndentOf(raw);
                string line = raw.Trim();

                if (line.Length == 0 || line.StartsWith("//")) { i++; continue; }
                if (indent < indentLevel) break;              // dedent: block over
                if (indent > indentLevel) { i++; continue; }  // deeper lines handled by choices

                // Divert
                if (line.StartsWith("->"))
                {
                    string target = line.Substring(2).Trim();
                    foreach (var connect in block.openEnds)
                        _pendingDiverts.Add((connect, target));
                    block.openEnds.Clear();
                    if (block.entryId == null)
                    {
                        // Section that immediately diverts: use a passthrough log node.
                        var hop = new CommentNode { Title = "→ " + target };
                        Append(hop);
                        foreach (var connect in block.openEnds)
                            _pendingDiverts.Add((connect, target));
                        block.openEnds.Clear();
                    }
                    i++;
                    continue;
                }

                // Choice group
                if (line.StartsWith("*"))
                {
                    var choice = new ChoiceNode { Title = "Choice" };
                    Append(choice);
                    // Each option owns its own branch, so the choice consumes
                    // the open end and hands one out per option instead.
                    block.openEnds.Clear();

                    while (i < lines.Count)
                    {
                        string optionRaw = lines[i];
                        if (IndentOf(optionRaw) != indentLevel || !optionRaw.Trim().StartsWith("*"))
                            break;

                        string optionLine = optionRaw.Trim().Substring(1).Trim();
                        var option = ParseOptionHeader(optionLine, sectionName);
                        choice.Options.Add(option);
                        int slot = choice.Options.Count - 1;

                        // Option body = following lines at deeper indent.
                        int bodyStart = i + 1;
                        int bodyEnd = bodyStart;
                        while (bodyEnd < lines.Count)
                        {
                            string bodyLine = lines[bodyEnd];
                            if (bodyLine.Trim().Length == 0) { bodyEnd++; continue; }
                            if (IndentOf(bodyLine) <= indentLevel) break;
                            bodyEnd++;
                        }

                        if (bodyEnd > bodyStart)
                        {
                            _layoutY++;
                            var body = ParseBlock(lines.GetRange(bodyStart, bodyEnd - bodyStart),
                                0, sectionName, IndentOf(lines[bodyStart]));
                            if (body.entryId != null)
                                choice.SetOptionTarget(slot, body.entryId);
                            block.openEnds.AddRange(body.openEnds);
                        }
                        else
                        {
                            // Empty option: falls through to whatever follows the choice.
                            int captured = slot;
                            block.openEnds.Add(id => choice.SetOptionTarget(captured, id));
                        }

                        i = bodyEnd;
                    }
                    continue;
                }

                // Command
                var command = CommandRx.Match(line);
                if (command.Success)
                {
                    var node = BuildCommandNode(command.Groups[1].Value);
                    if (node != null) Append(node);
                    i++;
                    continue;
                }

                // Dialogue line
                var say = SayRx.Match(line);
                if (say.Success)
                {
                    string content = say.Groups[3].Value;
                    string key = ExtractKey(ref content, sectionName);
                    Append(new SayNode
                    {
                        SpeakerId = say.Groups[1].Value,
                        Emotion = say.Groups[2].Success ? say.Groups[2].Value : null,
                        LineKey = key,
                        FallbackText = content.Trim(),
                        Title = say.Groups[1].Value,
                    });
                    i++;
                    continue;
                }

                Debug.LogWarning($"[StoryKit] .story unrecognized line: \"{line}\"");
                i++;
            }

            return block;
        }

        private ChoiceNode.Option ParseOptionHeader(string optionLine, string sectionName)
        {
            var option = new ChoiceNode.Option();

            var guard = GuardRx.Match(optionLine);
            if (guard.Success)
            {
                option.guardKey = guard.Groups[1].Value;
                option.guardComparison = guard.Groups[2].Value switch
                {
                    ">" => VarComparison.Greater,
                    ">=" => VarComparison.GreaterOrEqual,
                    "<" => VarComparison.Less,
                    "<=" => VarComparison.LessOrEqual,
                    "!=" => VarComparison.NotEqual,
                    _ => VarComparison.Equal,
                };
                option.guardValue = float.Parse(guard.Groups[3].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                optionLine = GuardRx.Replace(optionLine, "").Trim();
            }

            string text = optionLine;
            option.lineKey = ExtractKey(ref text, sectionName);
            option.fallbackText = text.Trim();
            return option;
        }

        private string ExtractKey(ref string content, string sectionName)
        {
            var match = KeyRx.Match(content);
            if (match.Success)
            {
                content = KeyRx.Replace(content, "");
                return match.Groups[1].Value;
            }
            _keyCounter++;
            return $"{_fileId}_{sectionName.ToUpperInvariant()}_{_keyCounter:000}";
        }

        private StoryNode BuildCommandNode(string command)
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            float F(int index, float fallback = 0f) =>
                parts.Length > index &&
                float.TryParse(parts[index], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : fallback;
            string S(int index) => parts.Length > index ? parts[index] : string.Empty;

            switch (parts[0].ToLowerInvariant())
            {
                case "var.set":
                    return new SetVarNode { Key = S(1), NumberValue = F(2), Title = "Set " + S(1) };
                case "var.add":
                    return new ModifyVarNode { Key = S(1), Delta = F(2, 1f), Title = "Mod " + S(1) };
                case "event":
                    return new RaiseEventNode { EventName = S(1), Payload = S(2), Title = S(1) };
                case "quest.start":
                    return new QuestNode { Operation = QuestNode.Op.Start, QuestId = S(1) };
                case "quest.complete":
                    return new QuestNode { Operation = QuestNode.Op.Complete, QuestId = S(1) };
                case "quest.fail":
                    return new QuestNode { Operation = QuestNode.Op.Fail, QuestId = S(1) };
                case "give":
                    return new GiveItemNode { ItemId = S(1), Amount = (int)F(2, 1f) };
                case "wait":
                    return new WaitNode { Seconds = F(1, 1f) };
                case "bgm":
                    return new PlayBgmNode { BgmId = S(1) };
                case "fade":
                    return new FadeNode { ToAlpha = F(1, 1f), Duration = F(2, 0.5f) };
                case "save":
                    return new AutoSaveNode();
                case "call":
                {
                    // << call weather.set storm >> → CustomCommandNode
                    string commandArgs = parts.Length > 2
                        ? string.Join(" ", parts, 2, parts.Length - 2) : "";
                    return new CustomCommandNode { Command = S(1), Arguments = commandArgs, Title = S(1) };
                }
                case "bark":
                    // << bark guard_01 Who's there?! >>
                    return new BarkNode
                    {
                        StageId = S(1),
                        FallbackText = parts.Length > 2
                            ? string.Join(" ", parts, 2, parts.Length - 2) : "",
                        Title = "Bark " + S(1),
                    };
                default:
                    Debug.LogWarning($"[StoryKit] .story unknown command '{parts[0]}'.");
                    return new LogNode { Message = $"(unknown command: {command})" };
            }
        }

        private static int IndentOf(string line)
        {
            int spaces = 0;
            foreach (char c in line)
            {
                if (c == ' ') spaces++;
                else if (c == '\t') spaces += 4;
                else break;
            }
            return spaces / 4;
        }
    }
}
