using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// One conversation, as a flat ordered list of lines.
    ///
    /// The lines carry keys rather than text. Text lives in the localisation
    /// table, which is generated from the sheet the writer actually edits, so a
    /// rewrite never touches an asset and never needs a programmer. It also
    /// means a line that has not been translated yet shows its key on screen,
    /// which is ugly in the right way: obvious in a screenshot instead of
    /// silently blank.
    ///
    /// Flat, not branching, on purpose. Branching dialogue belongs in a StoryKit
    /// flow graph, and building one of those for an NPC who says three sentences
    /// costs more than it returns. When a conversation needs a choice it
    /// graduates to a flow; until then this stays a list.
    /// </summary>
    [CreateAssetMenu(fileName = "Conversation", menuName = "Weizza/Conversation")]
    public sealed class Conversation : ScriptableObject
    {
        [System.Serializable]
        public sealed class Line
        {
            [HorizontalGroup("row", 90f), HideLabel]
            [Tooltip("Who is talking. Matches a row in the speaker database.")]
            public string speakerId;

            [HorizontalGroup("row", 90f), HideLabel]
            [Tooltip("Which portrait variant. Blank falls back to the first one.")]
            public string emotion;

            [HorizontalGroup("row"), HideLabel]
            [Tooltip("Key into the localisation table.")]
            public string textKey;
        }

        [Tooltip("Stable id, matching the conversationId column in the sheet.")]
        [SerializeField] private string id;

        [TableList(AlwaysExpanded = true, DrawScrollView = false)]
        [SerializeField] private List<Line> lines = new();

        public string Id => id;
        public IReadOnlyList<Line> Lines => lines;

        /// <summary>Replaces the whole conversation. Used by the sheet import.</summary>
        public void Load(string conversationId, List<Line> imported)
        {
            id = conversationId;
            lines = imported;
        }
    }
}
