using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using StoryKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Weizza.Narrative;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Pulls the writing out of Google Sheets and into assets the game can load.
    ///
    /// The sheet is the source of truth and this is a one way door on purpose.
    /// Anything typed into the generated assets by hand is overwritten by the
    /// next pull, which sounds harsh until the alternative is two copies of a
    /// script that disagree and no way to tell which one shipped.
    ///
    /// Pulling is manual rather than automatic at play, because a build that
    /// reads a spreadsheet at startup stops working the moment the network does,
    /// or the moment someone renames a tab an hour before a showing.
    /// </summary>
    public sealed class SheetSyncWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Sheet Sync", priority = 5)]
        private static void Open()
        {
            var window = GetWindow<SheetSyncWindow>();
            window.titleContent = new GUIContent("Sheet Sync");
            window.minSize = new Vector2(520f, 560f);
            window.Show();
        }

        // ---- Where everything lives ------------------------------------------

        [TitleGroup("Workbook", "One spreadsheet, every tab", TitleAlignments.Centered)]
        [InfoBox("Paste the whole sheet URL or just the id out of the middle of " +
                 "it. The sheet has to be shared as anyone with the link can " +
                 "view, or the pull comes back as a sign in page rather than as " +
                 "an error, which is the confusing way for this to fail.")]
        [LabelText("Sheet URL or id")]
        [SerializeField]
        private string workbook = "https://docs.google.com/spreadsheets/d/1hS3beRDDfJEwsD1g4wina22v9zrh_tVpfw5pyptrCn4/edit";

        [FoldoutGroup("Tabs and assets", expanded: false)]
        [TitleGroup("Tabs and assets/Text")]
        [InfoBox("Merged into one table in the order listed. An id defined twice " +
                 "keeps the last one, which is why the workbook checks for " +
                 "duplicates before it gets this far.")]
        [ListDrawerSettings(ShowFoldout = false)]
        [SerializeField]
        private List<string> textTabs = new() { "Text_Dialogue", "Text_UI", "Text_Item" };

        [TitleGroup("Tabs and assets/Text")]
        [Tooltip("Column headers to take as languages, in order. Every other " +
                 "column in the tab is left alone, so a sheet can carry notes " +
                 "and context without them arriving in game as a language.")]
        [ListDrawerSettings(ShowFoldout = false)]
        [SerializeField] private List<string> languages = new() { "en", "th" };

        [TitleGroup("Tabs and assets/Text")]
        [Required("Make one with Assets > Create > StoryKit > Localization Table.")]
        [SerializeField] private LocalizationTableAsset table;

        // ---- Structure --------------------------------------------------------

        [TitleGroup("Tabs and assets/Structure")]
        [LabelText("Dialogue tab")]
        [SerializeField] private string dialogueTab = "Dialogue";

        [TitleGroup("Tabs and assets/Structure")]
        [LabelText("Speaker tab")]
        [SerializeField] private string speakerTab = "Speaker";

        [TitleGroup("Tabs and assets/Structure")]
        [Required("Make one with Assets > Create > Weizza > Speaker Database.")]
        [SerializeField] private SpeakerDatabase speakers;

        [TitleGroup("Tabs and assets/Structure")]
        [FolderPath]
        [LabelText("Conversations go to")]
        [SerializeField] private string conversationFolder = "Assets/GameData/Conversations";

        /// <summary>
        /// Finds the two assets the pull writes into, if the project has exactly
        /// one of each.
        ///
        /// Dragged in by hand every session otherwise, which is a step that
        /// teaches nothing and fails silently: an empty slot reads as the tool
        /// being broken rather than as a field nobody filled. More than one
        /// candidate is left alone, because guessing which table a project meant
        /// is worse than asking.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();

            if (table == null) table = OnlyOne<LocalizationTableAsset>();
            if (speakers == null) speakers = OnlyOne<SpeakerDatabase>();
        }

        private static T OnlyOne<T>() where T : ScriptableObject
        {
            var found = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (found.Length != 1) return null;

            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(found[0]));
        }

        // ---- Buttons ---------------------------------------------------------

        [PropertyOrder(10)]
        [Button("Pull everything", ButtonSizes.Gigantic)]
        [GUIColor(0.45f, 0.78f, 0.55f)]
        private void PullEverything()
        {
            PullText();
            PullDialogue();
        }

        [PropertyOrder(11)]
        [FoldoutGroup("Pull one part only", expanded: false)]
        [Button("Text only", ButtonSizes.Large)]
        [GUIColor(0.55f, 0.72f, 0.95f)]
        private void PullText()
        {
            if (table == null)
            {
                Report("No localisation table assigned, so there is nowhere to put the text.", true);
                return;
            }

            var merged = new StringBuilder();
            merged.Append("id");
            foreach (var language in languages) merged.Append(',').Append(language);
            merged.Append('\n');

            var pulled = 0;
            foreach (var tab in textTabs)
            {
                if (string.IsNullOrWhiteSpace(tab)) continue;
                if (!Download(workbook, tab, out var csv)) return;

                pulled += AppendText(csv, merged);
            }

            Undo.RecordObject(table, "Pull localisation");
            table.ImportCsv(merged.ToString());
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();

            Report($"Pulled {pulled} line(s) from {textTabs.Count} tab(s) " +
                   $"in {table.Languages.Count} language(s).", false);
        }

        [PropertyOrder(11)]
        [FoldoutGroup("Pull one part only", expanded: false)]
        [Button("Dialogue only", ButtonSizes.Large)]
        [GUIColor(0.55f, 0.72f, 0.95f)]
        private void PullDialogue()
        {
            if (!Download(workbook, speakerTab, out var speakerCsv)) return;
            if (!Download(workbook, dialogueTab, out var dialogueCsv)) return;

            var madeSpeakers = ImportSpeakers(speakerCsv);
            var madeConversations = ImportConversations(dialogueCsv);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Report($"Pulled {madeSpeakers} speaker(s) and {madeConversations} conversation(s).", false);
        }

        [PropertyOrder(12)]
        [ShowInInspector, DisplayAsString(TextAlignment.Left)]
        [HideLabel]
        [GUIColor(nameof(ResultColour))]
        private string _result = "Nothing pulled yet.";

        private bool _failed;
        private Color ResultColour => _failed ? new Color(0.95f, 0.6f, 0.55f) : new Color(0.7f, 0.9f, 0.75f);

        private void Report(string message, bool failed)
        {
            _result = message;
            _failed = failed;

            if (failed) Debug.LogWarning($"[Weizza] Sheet Sync: {message}");
            else Debug.Log($"[Weizza] Sheet Sync: {message}");
        }

        // ---- Import ----------------------------------------------------------

        /// <summary>
        /// Copies one text tab into the merged table, taking only the columns
        /// that are languages.
        ///
        /// By name, because these tabs carry a speaker and a context column for
        /// whoever is writing, and StoryKit's importer treats every column after
        /// the first as a language. Handing it the tab unfiltered would ship a
        /// language called context.
        /// </summary>
        private int AppendText(string csv, StringBuilder into)
        {
            var rows = ParseCsv(csv);
            if (rows.Count < 2) return 0;

            var header = Header(rows[0]);
            var written = 0;

            foreach (var row in Body(rows))
            {
                var id = Cell(row, header, "id");
                if (string.IsNullOrEmpty(id)) continue;

                into.Append(Quote(id));
                foreach (var language in languages) into.Append(',').Append(Quote(Cell(row, header, language)));
                into.Append('\n');
                written++;
            }

            return written;
        }

        /// <summary>
        /// Wraps a cell so that commas, quotes and line breaks inside written
        /// dialogue survive the trip back through a CSV parser.
        /// </summary>
        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private int ImportSpeakers(string csv)
        {
            if (speakers == null)
            {
                Report("No speaker database assigned, so portraits were skipped.", true);
                return 0;
            }

            var rows = ParseCsv(csv);
            if (rows.Count < 2) return 0;

            var header = Header(rows[0]);
            var built = new List<SpeakerDatabase.Speaker>();

            foreach (var row in Body(rows))
            {
                var id = Cell(row, header, "speakerId");
                if (string.IsNullOrEmpty(id)) continue;

                // The portraits already on the asset are kept. The sheet decides
                // who exists and what they are called; which drawing goes with
                // which mood is a thing you do by eye, in the inspector, and it
                // would be maddening to lose it on every pull.
                var existing = speakers.Find(id);

                built.Add(new SpeakerDatabase.Speaker
                {
                    id = id,
                    nameKey = Cell(row, header, "nameId"),
                    portraits = existing?.portraits ?? new List<SpeakerDatabase.Portrait>(),
                });
            }

            Undo.RecordObject(speakers, "Pull speakers");
            speakers.Load(built);
            EditorUtility.SetDirty(speakers);
            return built.Count;
        }

        private int ImportConversations(string csv)
        {
            var rows = ParseCsv(csv);
            if (rows.Count < 2) return 0;

            var header = Header(rows[0]);

            // Grouped by id and then sorted by the order column, rather than
            // trusting the order the rows happen to arrive in. A writer moving a
            // line up in the sheet should not be able to change the sequence by
            // accident, and a writer renumbering should not have to re-sort.
            var grouped = new Dictionary<string, List<(int order, Conversation.Line line)>>();

            foreach (var row in Body(rows))
            {
                var id = Cell(row, header, "conversationId");
                var key = Cell(row, header, "textId");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key)) continue;

                if (!grouped.TryGetValue(id, out var list))
                {
                    list = new List<(int, Conversation.Line)>();
                    grouped[id] = list;
                }

                int.TryParse(Cell(row, header, "order"), out var order);
                list.Add((order, new Conversation.Line
                {
                    speakerId = Cell(row, header, "speakerId"),
                    emotion = Cell(row, header, "emotion"),
                    textKey = key,
                }));
            }

            if (grouped.Count > 0) EnsureFolder(conversationFolder);

            foreach (var pair in grouped)
            {
                pair.Value.Sort((a, b) => a.order.CompareTo(b.order));

                var lines = new List<Conversation.Line>(pair.Value.Count);
                foreach (var entry in pair.Value) lines.Add(entry.line);

                var path = $"{conversationFolder}/{pair.Key}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<Conversation>(path);

                if (asset == null)
                {
                    asset = CreateInstance<Conversation>();
                    asset.Load(pair.Key, lines);
                    AssetDatabase.CreateAsset(asset, path);
                }
                else
                {
                    Undo.RecordObject(asset, "Pull conversation");
                    asset.Load(pair.Key, lines);
                    EditorUtility.SetDirty(asset);
                }
            }

            return grouped.Count;
        }

        // ---- Fetch -----------------------------------------------------------

        /// <summary>
        /// Pulls one tab as CSV.
        ///
        /// The gviz endpoint is used rather than a published link because it
        /// takes the tab by name. Publishing gives a per tab id that has to be
        /// looked up again every time a tab is added, which is exactly the kind
        /// of bookkeeping that goes stale.
        /// </summary>
        private bool Download(string sheet, string tab, out string csv)
        {
            csv = null;

            var id = SheetId(sheet);
            if (string.IsNullOrEmpty(id))
            {
                Report("No sheet id. Paste the sheet URL into the field above.", true);
                return false;
            }

            var url = $"https://docs.google.com/spreadsheets/d/{id}/gviz/tq?tqx=out:csv&sheet={UnityWebRequest.EscapeURL(tab)}";

            using var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();

            try
            {
                while (!operation.isDone)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Sheet Sync", $"Pulling {tab}", request.downloadProgress))
                    {
                        request.Abort();
                        Report("Cancelled.", true);
                        return false;
                    }

                    System.Threading.Thread.Sleep(16);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Report($"Could not reach the sheet: {request.error}", true);
                return false;
            }

            csv = request.downloadHandler.text;

            // Google answers a request it will not serve with a sign in page, at
            // status two hundred. Without this check that HTML lands in the table
            // as if it were dialogue.
            if (csv.StartsWith("<", System.StringComparison.Ordinal))
            {
                Report($"The sheet answered with a web page instead of data. Share '{tab}' " +
                       "as anyone with the link can view.", true);
                return false;
            }

            return true;
        }

        /// <summary>Takes the id out of a pasted URL, or passes an id straight through.</summary>
        private static string SheetId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.Trim();
            const string marker = "/d/";
            var start = raw.IndexOf(marker, System.StringComparison.Ordinal);
            if (start < 0) return raw;

            start += marker.Length;
            var end = raw.IndexOf('/', start);
            return end < 0 ? raw[start..] : raw[start..end];
        }

        // ---- CSV -------------------------------------------------------------

        private static Dictionary<string, int> Header(List<string> row)
        {
            var map = new Dictionary<string, int>(row.Count, System.StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < row.Count; i++)
            {
                var name = row[i].Trim();
                if (!string.IsNullOrEmpty(name)) map[name] = i;
            }
            return map;
        }

        private static IEnumerable<List<string>> Body(List<List<string>> rows)
        {
            for (var i = 1; i < rows.Count; i++) yield return rows[i];
        }

        /// <summary>
        /// Reads a cell by column name.
        ///
        /// By name and not by position, so inserting a column in the middle of
        /// the sheet does not quietly shift every field one to the left.
        /// </summary>
        private static string Cell(List<string> row, Dictionary<string, int> header, string column)
        {
            if (!header.TryGetValue(column, out var index)) return string.Empty;
            return index < row.Count ? row[index].Trim() : string.Empty;
        }

        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;

            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (quoted)
                {
                    if (c != '"') { cell.Append(c); continue; }

                    // A doubled quote inside a quoted cell is one literal quote.
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        break;
                    case ',':
                        row.Add(cell.ToString());
                        cell.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        cell.Append(c);
                        break;
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var built = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
