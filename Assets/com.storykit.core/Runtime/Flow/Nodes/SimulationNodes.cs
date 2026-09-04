using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Life-sim / farming-sim nodes.
    ///
    /// Stardew-likes are built on a calendar (day / season / year), NPC daily
    /// schedules, gift-driven friendship "hearts", festivals on fixed dates,
    /// weather, and shops. All of it is world state that story graphs read and
    /// write, so it belongs here rather than in each game's own code.
    /// </summary>

    // ---------------- Calendar & time ----------------

    /// <summary>
    /// The shared in-game clock. Everything is stored on the global board so it
    /// saves with the game and any condition can read it:
    ///   time.minutes (0..1439) · time.day (1..N) · time.season (0..3) · time.year
    /// </summary>
    public static class StoryClock
    {
        public const string MinutesKey = "time.minutes";
        public const string DayKey = "time.day";
        public const string SeasonKey = "time.season";
        public const string YearKey = "time.year";
        public const string WeatherKey = "time.weather";

        public static int Minutes => Mathf.RoundToInt(StoryState.Global.Get<float>(MinutesKey));
        public static int Hour => Minutes / 60;
        public static int Day => Mathf.Max(1, Mathf.RoundToInt(StoryState.Global.Get(DayKey, 1f)));
        public static int Season => Mathf.RoundToInt(StoryState.Global.Get<float>(SeasonKey));
        public static int Year => Mathf.Max(1, Mathf.RoundToInt(StoryState.Global.Get(YearKey, 1f)));
        public static string Weather => StoryState.Global.Get(WeatherKey, "clear");

        /// <summary>Days per season. 28 by default, like the genre standard.</summary>
        public static int DaysPerSeason { get; set; } = 28;

        /// <summary>Day of week 0..6, derived from the absolute day count.</summary>
        public static int DayOfWeek => (AbsoluteDay - 1) % 7;

        public static int AbsoluteDay =>
            (Year - 1) * DaysPerSeason * 4 + Season * DaysPerSeason + Day;

        public static void SetTime(int minutes)
        {
            StoryState.Global.Set(MinutesKey, (float)Mathf.Clamp(minutes, 0, 1439));
            StoryEvents.Raise("Time.Changed", minutes.ToString());
        }

        public static void AdvanceMinutes(int amount)
        {
            int total = Minutes + amount;
            while (total >= 1440) { total -= 1440; AdvanceDay(); }
            StoryState.Global.Set(MinutesKey, (float)total);
            StoryEvents.Raise("Time.Changed", total.ToString());
        }

        public static void AdvanceDay()
        {
            int day = Day + 1;
            int season = Season;
            int year = Year;

            if (day > DaysPerSeason)
            {
                day = 1;
                season++;
                if (season > 3) { season = 0; year++; StoryEvents.Raise("Time.YearChanged", year.ToString()); }
                StoryEvents.Raise("Time.SeasonChanged", season.ToString());
            }

            StoryState.Global.Set(DayKey, (float)day);
            StoryState.Global.Set(SeasonKey, (float)season);
            StoryState.Global.Set(YearKey, (float)year);
            StoryEvents.Raise("Time.DayChanged", day.ToString());
        }
    }

    /// <summary>Moves the clock forward (an action took time, or the player slept).</summary>
    [Serializable]
    [NodeMenu("Time",
        "Advance the in-game clock by minutes, or skip to the next day (sleeping).")]
    public sealed class AdvanceTimeNode : ActionNode
    {
        public enum Mode { Minutes, NextDay, SetTimeOfDay }

        [SerializeField] private Mode _mode = Mode.Minutes;
        [SerializeField] private int _minutes = 30;
        [Tooltip("Hour to wake up at when skipping to the next day.")]
        [SerializeField, Range(0, 23)] private int _wakeHour = 6;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _mode switch
        {
            Mode.Minutes => $"+{_minutes} min",
            Mode.NextDay => $"sleep → {_wakeHour}:00",
            _ => $"set {_minutes / 60}:{_minutes % 60:00}",
        };

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            switch (_mode)
            {
                case Mode.Minutes:
                    StoryClock.AdvanceMinutes(_minutes);
                    break;
                case Mode.NextDay:
                    StoryClock.AdvanceDay();
                    StoryClock.SetTime(_wakeHour * 60);
                    StoryEvents.Raise("Time.Slept", null);
                    break;
                case Mode.SetTimeOfDay:
                    StoryClock.SetTime(_minutes);
                    break;
            }
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Branches on the clock: time of day, day of week, season, weather.</summary>
    [Serializable]
    [NodeMenu("Time",
        "Go True/False by the clock: time window, day of week, season, or weather.")]
    public sealed class TimeBranchNode : ConditionNode
    {
        public enum Check { TimeWindow, DayOfWeek, Season, Weather, DayNumber }

        [SerializeField] private Check _check = Check.TimeWindow;
        [SerializeField, Range(0, 23)] private int _fromHour;
        [SerializeField, Range(0, 23)] private int _toHour = 12;
        [Tooltip("0=Mon .. 6=Sun for day of week; 0..3 for season.")]
        [SerializeField] private int _value;
        [SerializeField] private string _weather = "rain";

        public override string GetSummary() => _check switch
        {
            Check.TimeWindow => $"{_fromHour}:00: {_toHour}:00",
            Check.DayOfWeek => $"day of week = {_value}",
            Check.Season => $"season = {_value}",
            Check.Weather => $"weather = {_weather}",
            _ => $"day = {_value}",
        };

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
        {
            bool result = _check switch
            {
                Check.TimeWindow => InWindow(StoryClock.Hour, _fromHour, _toHour),
                Check.DayOfWeek => StoryClock.DayOfWeek == _value,
                Check.Season => StoryClock.Season == _value,
                Check.Weather => string.Equals(StoryClock.Weather, _weather,
                                    StringComparison.OrdinalIgnoreCase),
                _ => StoryClock.Day == _value,
            };
            return UniTask.FromResult(result);
        }

        /// <summary>Handles windows that wrap past midnight (22:00: 02:00).</summary>
        private static bool InWindow(int hour, int from, int to) =>
            from <= to ? hour >= from && hour < to : hour >= from || hour < to;
    }

    /// <summary>Holds until the clock reaches a given hour: "meet me at dusk".</summary>
    [Serializable]
    [NodeMenu("Time",
        "Hold until the in-game clock reaches this hour.")]
    public sealed class WaitUntilTimeNode : TaskNode
    {
        [SerializeField, Range(0, 23)] private int _hour = 18;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"until {_hour}:00";

        [NonSerialized] private UniTaskCompletionSource _signal;
        [NonSerialized] private IDisposable _subscription;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _signal = new UniTaskCompletionSource();
            _subscription = StoryEvents.Subscribe("Time.Changed", _ =>
            {
                if (StoryClock.Hour == _hour) _signal.TrySetResult();
            });
            if (StoryClock.Hour == _hour) _signal.TrySetResult();
            return UniTask.CompletedTask;
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => await _signal.Task.AttachExternalCancellation(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            _subscription?.Dispose();
            _subscription = null;
            _signal = null;
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Sets the weather and tells the game to update visuals.</summary>
    [Serializable]
    [NodeMenu("Time",
        "Set the weather (clear / rain / storm / snow). The game listens for World.WeatherChanged.")]
    public sealed class SetWeatherNode : ActionNode
    {
        [SerializeField] private string _weather = "rain";

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _weather;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryState.Global.Set(StoryClock.WeatherKey, _weather);
            StoryEvents.Raise("World.WeatherChanged", _weather);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Friendship / hearts ----------------

    /// <summary>
    /// Gift reaction: looks up whether the character loves/likes/hates the item
    /// and adjusts friendship accordingly. The game supplies the taste table
    /// through blackboard keys, so no item database is required here:
    ///   taste.&lt;npc&gt;.&lt;item&gt; = 2 (love) / 1 (like) / -1 (dislike) / -2 (hate)
    /// </summary>
    [Serializable]
    [NodeMenu("Character",
        "Give a gift: reads the character's taste for the item and adjusts friendship. Outputs by reaction.")]
    public sealed class GiveGiftNode : StoryNode, IFixedOutputs
    {
        [SerializeField] private string _characterId;
        [SerializeField] private string _itemId;

        [Tooltip("Friendship points for each reaction tier.")]
        [SerializeField] private float _lovePoints = 80f;
        [SerializeField] private float _likePoints = 45f;
        [SerializeField] private float _neutralPoints = 20f;
        [SerializeField] private float _dislikePoints = -20f;
        [SerializeField] private float _hatePoints = -40f;

        [Tooltip("Remove the item from the inventory when gifted.")]
        [SerializeField] private bool _consumeItem = true;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_itemId} → {_characterId}";

        public string[] OutputNames =>
            new[] { "Love", "Like", "Neutral", "Dislike", "Hate" };

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (_consumeItem)
                StoryServices.Get<IInventoryService>()?.TakeItem(_itemId, 1);

            int taste = Mathf.RoundToInt(
                StoryState.Global.Get<float>($"taste.{_characterId}.{_itemId}"));

            float points = taste switch
            {
                >= 2 => _lovePoints,
                1 => _likePoints,
                -1 => _dislikePoints,
                <= -2 => _hatePoints,
                _ => _neutralPoints,
            };

            string key = Friendship.KeyFor(_characterId);
            StoryState.Global.Set(key, StoryState.Global.Get<float>(key) + points);
            StoryState.Global.Set($"gifted.{_characterId}.week", 1f);
            StoryEvents.Raise("Character.Gifted", _characterId);

            // Route to the matching named output.
            int index = taste switch { >= 2 => 0, 1 => 1, -1 => 3, <= -2 => 4, _ => 2 };
            FollowFixedOutput(index);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Friendship helpers shared by nodes and game code.</summary>
    public static class Friendship
    {
        /// <summary>Points per heart. 250 is the genre standard.</summary>
        public static int PointsPerHeart { get; set; } = 250;

        public static string KeyFor(string characterId) => "friend." + characterId;

        public static float GetPoints(string characterId) =>
            StoryState.Global.Get<float>(KeyFor(characterId));

        public static int GetHearts(string characterId) =>
            Mathf.FloorToInt(GetPoints(characterId) / PointsPerHeart);
    }

    /// <summary>Branches on how many hearts a character has with the player.</summary>
    [Serializable]
    [NodeMenu("Character",
        "Go True/False by a character's heart level (friendship).")]
    public sealed class HeartLevelBranchNode : ConditionNode
    {
        [SerializeField] private string _characterId;
        [SerializeField] private VarComparison _comparison = VarComparison.GreaterOrEqual;
        [SerializeField] private int _hearts = 2;

        public override string GetSummary() => $"{_characterId} hearts {_comparison} {_hearts}";

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
            => UniTask.FromResult(
                _comparison.Evaluate(Friendship.GetHearts(_characterId), _hearts));
    }

    // ---------------- Shops ----------------

    /// <summary>
    /// Opens a shop and holds until the player closes it. The game's shop UI
    /// listens for Shop.Open and raises Shop.Closed when done: so any shop
    /// implementation plugs in without the package knowing about items.
    /// </summary>
    [Serializable]
    [NodeMenu("Inventory",
        "Open a shop and wait until the player closes it. Your shop UI listens for Shop.Open.")]
    public sealed class OpenShopNode : TaskNode
    {
        [SerializeField] private string _shopId = "general_store";

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _shopId;

        [NonSerialized] private UniTaskCompletionSource _closed;
        [NonSerialized] private IDisposable _subscription;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _closed = new UniTaskCompletionSource();
            _subscription = StoryEvents.Subscribe("Shop.Closed", _ => _closed.TrySetResult());
            StoryEvents.Raise("Shop.Open", _shopId);
            return UniTask.CompletedTask;
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => await _closed.Task.AttachExternalCancellation(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            _subscription?.Dispose();
            _subscription = null;
            _closed = null;
            StoryEvents.Raise("Shop.ForceClose", _shopId);   // never leave a shop stuck open
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Charges the player money. Branches on whether they could afford it.</summary>
    [Serializable]
    [NodeMenu("Inventory",
        "Try to spend currency. True if the player could afford it, False if not.")]
    public sealed class SpendCurrencyNode : ConditionNode
    {
        [SerializeField] private string _currencyKey = "gold";
        [SerializeField] private int _amount = 100;

        public override string GetSummary() => $"-{_amount} {_currencyKey}";

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
        {
            float balance = StoryState.Global.Get<float>(_currencyKey);
            if (balance < _amount) return UniTask.FromResult(false);

            StoryState.Global.Set(_currencyKey, balance - _amount);
            StoryEvents.Raise("Currency.Spent", _amount.ToString());
            return UniTask.FromResult(true);
        }
    }

    // ---------------- Festivals / scheduled events ----------------

    /// <summary>
    /// Fires once on a specific calendar date: festivals, birthdays, deadlines.
    /// Put it on a graph that runs for the whole game; it waits for the day to
    /// arrive and then continues.
    /// </summary>
    [Serializable]
    [NodeMenu("Time",
        "Hold until a specific calendar date arrives (festival, birthday, deadline).")]
    public sealed class WaitForDateNode : TaskNode
    {
        [SerializeField, Range(0, 3)] private int _season;
        [SerializeField, Min(1)] private int _day = 1;
        [Tooltip("0 = any year.")]
        [SerializeField, Min(0)] private int _year;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"season {_season}, day {_day}"
            + (_year > 0 ? $", year {_year}" : "");

        [NonSerialized] private UniTaskCompletionSource _signal;
        [NonSerialized] private IDisposable _subscription;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _signal = new UniTaskCompletionSource();
            _subscription = StoryEvents.Subscribe("Time.DayChanged", _ => Check());
            Check();
            return UniTask.CompletedTask;
        }

        private void Check()
        {
            if (StoryClock.Season == _season && StoryClock.Day == _day &&
                (_year <= 0 || StoryClock.Year == _year))
                _signal?.TrySetResult();
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => await _signal.Task.AttachExternalCancellation(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            _subscription?.Dispose();
            _subscription = null;
            _signal = null;
            return UniTask.CompletedTask;
        }
    }
}
