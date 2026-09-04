using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// The three schools, side by side, with the numbers that actually decide
    /// whether a fight is a choice.
    ///
    /// Reading one stance asset at a time tells you nothing: a stance is only
    /// fast or fragile relative to the other two. So everything here is a
    /// comparison, and the rules from the design bible are checked rather than
    /// remembered, because a rule nobody can see is a rule that quietly stops
    /// being true.
    /// </summary>
    public sealed class BalanceWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Balance Lab", priority = 1)]
        private static void Open()
        {
            var window = GetWindow<BalanceWindow>();
            window.titleContent = new GUIContent("Balance Lab");
            window.minSize = new Vector2(560f, 480f);
            window.Show();
        }

        [BoxGroup("Source")]
        [Required]
        [OnValueChanged(nameof(Refresh))]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public PlayerTuning tuning;

        [BoxGroup("Source")]
        [Tooltip("Health of the enemy the time to kill column is measured against.")]
        [OnValueChanged(nameof(Refresh))]
        [MinValue(1)]
        public float referenceEnemyHealth = 160f;

        [BoxGroup("Source")]
        [Tooltip("Damage per second the enemy deals, for the survival column.")]
        [OnValueChanged(nameof(Refresh))]
        [MinValue(0)]
        public float referenceEnemyDps = 18f;

        protected override void Initialize()
        {
            if (tuning == null) tuning = FindTuning();
            Refresh();
        }

        private static PlayerTuning FindTuning()
        {
            var guids = AssetDatabase.FindAssets("t:PlayerTuning");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<PlayerTuning>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        [Button("Refresh", ButtonSizes.Medium), PropertyOrder(-1)]
        private void Refresh()
        {
            _rows.Clear();
            _warnings.Clear();
            if (tuning == null) return;

            foreach (var id in new[] { StanceId.Than, StanceId.Se, StanceId.In })
            {
                var stance = tuning.Get(id);
                if (stance == null)
                {
                    _warnings.Add($"{id} has no stance asset. The player cannot switch to it.");
                    continue;
                }
                _rows.Add(new Row(stance, referenceEnemyHealth, referenceEnemyDps));
            }

            CheckDesignRules();
        }

        // ---- Comparison ------------------------------------------------------

        [TableList(IsReadOnly = true, AlwaysExpanded = true, ShowIndexLabels = false)]
        [ShowInInspector, LabelText("Side by side")]
        private readonly List<Row> _rows = new();

        [System.Serializable]
        private struct Row
        {
            [TableColumnWidth(70, Resizable = false)]
            [DisplayAsString] public string School;

            [TableColumnWidth(80)]
            [DisplayAsString, Tooltip("Health divided by the damage taken multiplier. " +
                                      "What the stance is really worth defensively.")]
            public string Effective;

            [TableColumnWidth(70)]
            [DisplayAsString] public string Speed;

            [TableColumnWidth(80)]
            [DisplayAsString, Tooltip("Damage per second of the full light combo.")]
            public string ComboDps;

            [TableColumnWidth(80)]
            [DisplayAsString, Tooltip("Seconds to kill the reference enemy on light combo alone.")]
            public string Ttk;

            [TableColumnWidth(90)]
            [DisplayAsString, Tooltip("Seconds the reference enemy needs to kill this stance.")]
            public string Survives;

            [DisplayAsString(EnableRichText = true)] public string Tools;

            public Row(StanceDefinition stance, float enemyHealth, float enemyDps)
            {
                School = stance.displayName;

                var effective = stance.damageTaken <= 0f
                    ? stance.maxHealth
                    : stance.maxHealth / stance.damageTaken;
                Effective = $"{effective:0} ehp";

                Speed = $"{stance.runSpeed:0.0} m/s";

                var frames = 0;
                var damage = 0f;
                if (stance.lightCombo != null)
                {
                    foreach (var attack in stance.lightCombo)
                    {
                        if (attack == null) continue;
                        frames += attack.TotalFrames;
                        damage += attack.damage;
                    }
                }

                var seconds = Timing.Frames(frames);
                var dps = seconds > 0f ? damage / seconds : 0f;
                ComboDps = dps > 0f ? $"{dps:0.0}" : "-";
                Ttk = dps > 0f ? $"{enemyHealth / dps:0.0} s" : "-";

                var incoming = enemyDps * stance.damageTaken;
                Survives = incoming > 0f ? $"{stance.maxHealth / incoming:0.0} s" : "-";

                var tools = new List<string>(4);
                if (stance.canJump) tools.Add("jump");
                if (stance.canDash) tools.Add(stance.dashIsBlink ? "blink" : "dash");
                if (stance.canParry) tools.Add("parry");
                if (stance.lifesteal > 0f) tools.Add($"lifesteal {stance.lifesteal:P0}");
                Tools = tools.Count > 0 ? string.Join(", ", tools) : "-";
            }
        }

        // ---- Design rules -----------------------------------------------------

        [ShowInInspector, HideLabel]
        [ListDrawerSettings(IsReadOnly = true, ShowFoldout = false, DefaultExpandedState = true)]
        [InfoBox("Rules taken from the design bible. These are not opinions " +
                 "about balance, they are things the game has already decided.",
            InfoMessageType.None)]
        private readonly List<string> _warnings = new();

        private void CheckDesignRules()
        {
            var than = tuning.Get(StanceId.Than);
            var se = tuning.Get(StanceId.Se);
            var inSchool = tuning.Get(StanceId.In);

            if (than != null)
            {
                if (than.canDash) Warn("Than has a dash. The brief says Than has none.");
                if (!than.canParry) Warn("Than cannot parry. Parry is Than's whole defensive identity.");
                if (than.heavy != null && !than.heavy.superArmor)
                {
                    Warn("Than's heavy has no super armor. That is the passive.");
                }
            }

            if (se != null)
            {
                if (se.lifesteal <= 0f)
                {
                    Warn("Se has no lifesteal. It is Se's only healing outside a shrine, " +
                         "so without it the stance is strictly worse than the others.");
                }
                if (se.canParry) Warn("Se can parry. Parry belongs to Than.");
            }

            if (inSchool != null)
            {
                if (inSchool.canJump) Warn("In can jump. The brief says In moves by a ward, not by legs.");
                if (!inSchool.canDash) Warn("In has no blink. That is the only way In moves vertically.");
                if (inSchool.canDash && !inSchool.dashIsBlink)
                {
                    Warn("In's dash is a slide, not a blink. A blink passes through what is in the way.");
                }
                if (inSchool.canDash && inSchool.iFrameEnd >= inSchool.dashFrames)
                {
                    Warn("In is invincible for the whole blink. Ending the i-frames " +
                         "before the move does is what makes it readable.");
                }
            }

            if (than != null && se != null && inSchool != null)
            {
                if (!(than.runSpeed < se.runSpeed && se.runSpeed < inSchool.runSpeed))
                {
                    Warn("Speed order is not Than < Se < In. The three schools should " +
                         "read differently the moment you walk in them.");
                }

                var thanEhp = Effective(than);
                var seEhp = Effective(se);
                var inEhp = Effective(inSchool);
                if (!(thanEhp > seEhp && seEhp > inEhp))
                {
                    Warn("Toughness order is not Than > Se > In.");
                }
            }

            var switchSeconds = tuning.SwitchSeconds;
            if (switchSeconds < 0.3f || switchSeconds > 0.5f)
            {
                Warn($"Stance switch is {switchSeconds * 1000f:0} ms. The brief says " +
                     "roughly 400 ms, and this number is the price of the whole system.");
            }

            if (_warnings.Count == 0) _warnings.Add("Everything matches the brief.");
        }

        private static float Effective(StanceDefinition stance) =>
            stance.damageTaken <= 0f ? stance.maxHealth : stance.maxHealth / stance.damageTaken;

        private void Warn(string message) => _warnings.Add(message);
    }
}
