using System;
using System.Collections.Generic;
using StoryKit.Flow;
using UnityEngine;

namespace StoryKit.Bt
{
    public enum BtStatus { Success, Failure, Running }

    /// <summary>Context handed to every BT node each tick.</summary>
    public sealed class BtContext
    {
        public BehaviorTreeRunner Runner { get; }
        public Blackboard Board { get; }
        public GameObject Owner { get; }

        public BtContext(BehaviorTreeRunner runner)
        {
            Runner = runner;
            Board = runner.Board;
            Owner = runner.gameObject;
        }
    }

    [Serializable]
    public abstract class BtNode
    {
        [SerializeField] private string _id = Guid.NewGuid().ToString("N");
        [SerializeField] private string _title;
        [SerializeField] private Vector2 _position;

        public string Id => _id;
        public string Title { get => _title; set => _title = value; }
        public Vector2 Position { get => _position; set => _position = value; }

        public abstract BtStatus Tick(BtContext context);

#if UNITY_EDITOR
        /// <summary>Result of the last tick, so the editor can colour the tree.
        /// Editor only: it costs nothing in a build.</summary>
        [NonSerialized] public BtStatus LastStatus = BtStatus.Failure;
        [NonSerialized] public int LastTickFrame = -1;
#endif

        /// <summary>
        /// Ticks the node and records the result for the editor. Composites and
        /// decorators call this instead of Tick so the whole tree is visible.
        /// </summary>
        public BtStatus Run(BtContext context)
        {
#if UNITY_EDITOR
            BtStatus status = Tick(context);
            LastStatus = status;
            LastTickFrame = Time.frameCount;
            return status;
#else
            return Tick(context);
#endif
        }

        /// <summary>Called when an ancestor aborts this subtree while it was Running.</summary>
        public virtual void OnAbort(BtContext context) { }
    }

    // ---------- Composites ----------

    [Serializable]
    public abstract class BtComposite : BtNode
    {
        [SerializeReference] private List<BtNode> _children = new();
        public List<BtNode> Children => _children;

        protected int currentChild;

        public override void OnAbort(BtContext context)
        {
            if (currentChild >= 0 && currentChild < _children.Count)
                _children[currentChild]?.OnAbort(context);
            currentChild = 0;
        }
    }

    /// <summary>Runs children in order until one FAILS. Success if all succeed.</summary>
    [Serializable]
    [NodeMenu("Composite",
        "Runs children left to right until one fails. Use it for do this, then this, then this.")]
    public sealed class BtSequence : BtComposite
    {
        public override BtStatus Tick(BtContext context)
        {
            while (currentChild < Children.Count)
            {
                BtStatus status = Children[currentChild].Run(context);
                if (status == BtStatus.Running) return BtStatus.Running;
                if (status == BtStatus.Failure) { currentChild = 0; return BtStatus.Failure; }
                currentChild++;
            }
            currentChild = 0;
            return BtStatus.Success;
        }
    }

    /// <summary>Runs children in order until one SUCCEEDS. Failure if all fail.</summary>
    [Serializable]
    [NodeMenu("Composite",
        "Tries children left to right until one succeeds. Use it for try this, otherwise that.")]
    public sealed class BtSelector : BtComposite
    {
        public override BtStatus Tick(BtContext context)
        {
            while (currentChild < Children.Count)
            {
                BtStatus status = Children[currentChild].Run(context);
                if (status == BtStatus.Running) return BtStatus.Running;
                if (status == BtStatus.Success) { currentChild = 0; return BtStatus.Success; }
                currentChild++;
            }
            currentChild = 0;
            return BtStatus.Failure;
        }
    }

    /// <summary>Ticks all children every tick. Policy decides success/failure.</summary>
    [Serializable]
    [NodeMenu("Composite", "Ticks every child each tick. Good for moving and shooting at the same time.")]
    public sealed class BtParallel : BtComposite
    {
        public enum Policy { RequireAll, RequireOne }

        [SerializeField] private Policy _successPolicy = Policy.RequireAll;
        public Policy SuccessPolicy { get => _successPolicy; set => _successPolicy = value; }

        public override BtStatus Tick(BtContext context)
        {
            int successes = 0, failures = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                BtStatus status = Children[i].Run(context);
                if (status == BtStatus.Success) successes++;
                else if (status == BtStatus.Failure) failures++;
            }

            if (_successPolicy == Policy.RequireOne && successes > 0) return BtStatus.Success;
            if (_successPolicy == Policy.RequireAll && successes == Children.Count) return BtStatus.Success;
            if (failures == Children.Count) return BtStatus.Failure;
            return BtStatus.Running;
        }
    }

    // ---------- Decorators ----------

    [Serializable]
    public abstract class BtDecorator : BtNode
    {
        [SerializeReference] private BtNode _child;
        public BtNode Child { get => _child; set => _child = value; }

        public override void OnAbort(BtContext context) => _child?.OnAbort(context);
    }

    [Serializable]
    [NodeMenu("Decorator", "Flips success and failure of its child.")]
    public sealed class BtInverter : BtDecorator
    {
        public override BtStatus Tick(BtContext context)
        {
            return Child.Run(context) switch
            {
                BtStatus.Success => BtStatus.Failure,
                BtStatus.Failure => BtStatus.Success,
                _ => BtStatus.Running,
            };
        }
    }

    [Serializable]
    [NodeMenu("Decorator", "Always reports success, so a failing child does not stop the branch.")]
    public sealed class BtSucceeder : BtDecorator
    {
        public override BtStatus Tick(BtContext context)
        {
            return Child.Run(context) == BtStatus.Running ? BtStatus.Running : BtStatus.Success;
        }
    }

    /// <summary>Repeats the child N times (0 = forever). Fails through.</summary>
    [Serializable]
    [NodeMenu("Decorator", "Repeats the child a number of times. Zero means forever.")]
    public sealed class BtRepeat : BtDecorator
    {
        [SerializeField, Min(0)] private int _times;
        [NonSerialized] private int _count;

        public int Times { get => _times; set => _times = Mathf.Max(0, value); }

        public override BtStatus Tick(BtContext context)
        {
            BtStatus status = Child.Run(context);
            if (status == BtStatus.Running) return BtStatus.Running;
            if (status == BtStatus.Failure) { _count = 0; return BtStatus.Failure; }

            _count++;
            if (_times > 0 && _count >= _times) { _count = 0; return BtStatus.Success; }
            return BtStatus.Running;
        }

        public override void OnAbort(BtContext context)
        {
            _count = 0;
            base.OnAbort(context);
        }
    }

    /// <summary>Child may only run once per cooldown window. Uses the mockable clock.</summary>
    [Serializable]
    [NodeMenu("Decorator", "Blocks the child until the cooldown has passed. Stops ability spam.")]
    public sealed class BtCooldown : BtDecorator
    {
        [SerializeField, Min(0f)] private float _seconds = 1f;
        [NonSerialized] private float _readyAt;

        public float Seconds { get => _seconds; set => _seconds = Mathf.Max(0f, value); }

        public override BtStatus Tick(BtContext context)
        {
            if (StoryTime.Time < _readyAt) return BtStatus.Failure;

            BtStatus status = Child.Run(context);
            if (status != BtStatus.Running)
                _readyAt = StoryTime.Time + _seconds;
            return status;
        }
    }

    /// <summary>
    /// NPBehave-style guard: child runs only while a blackboard comparison holds.
    /// If the value changes and the condition breaks while the child is Running,
    /// the child is aborted immediately (reactive AI: stop patrolling, start chasing).
    /// </summary>
    [Serializable]
    [NodeMenu("Decorator", "Child only runs while a value holds. Breaks it off the moment it stops holding.")]
    public sealed class BtBlackboardGuard : BtDecorator
    {
        [SerializeField] private string _key;
        [SerializeField] private VarComparison _comparison = VarComparison.GreaterOrEqual;
        [SerializeField] private float _target = 1f;

        [NonSerialized] private bool _childWasRunning;

        public string Key { get => _key; set => _key = value; }
        public VarComparison Comparison { get => _comparison; set => _comparison = value; }
        public float Target { get => _target; set => _target = value; }

        public override BtStatus Tick(BtContext context)
        {
            bool passes = _comparison.Evaluate(context.Board.Get<float>(_key), _target);
            if (!passes)
            {
                if (_childWasRunning)
                {
                    Child?.OnAbort(context);
                    _childWasRunning = false;
                }
                return BtStatus.Failure;
            }

            BtStatus status = Child.Run(context);
            _childWasRunning = status == BtStatus.Running;
            return status;
        }
    }

    // ---------- Leaves ----------

    [Serializable]
    [NodeMenu("Leaf", "Waits, then succeeds.")]
    public sealed class BtWait : BtNode
    {
        [SerializeField, Min(0f)] private float _seconds = 1f;
        [NonSerialized] private float _endAt = -1f;

        public float Seconds { get => _seconds; set => _seconds = Mathf.Max(0f, value); }

        public override BtStatus Tick(BtContext context)
        {
            if (_endAt < 0f) _endAt = StoryTime.Time + _seconds;
            if (StoryTime.Time >= _endAt) { _endAt = -1f; return BtStatus.Success; }
            return BtStatus.Running;
        }

        public override void OnAbort(BtContext context) => _endAt = -1f;
    }

    [Serializable]
    [NodeMenu("Leaf", "Writes a number to the blackboard.")]
    public sealed class BtSetVar : BtNode
    {
        [SerializeField] private string _key;
        [SerializeField] private float _value;

        public string Key { get => _key; set => _key = value; }
        public float Value { get => _value; set => _value = value; }

        public override BtStatus Tick(BtContext context)
        {
            context.Board.Set(_key, _value);
            return BtStatus.Success;
        }
    }

    [Serializable]
    [NodeMenu("Leaf", "Succeeds while a blackboard value passes the comparison.")]
    public sealed class BtCondition : BtNode
    {
        [SerializeField] private string _key;
        [SerializeField] private VarComparison _comparison = VarComparison.GreaterOrEqual;
        [SerializeField] private float _target = 1f;

        public string Key { get => _key; set => _key = value; }
        public VarComparison Comparison { get => _comparison; set => _comparison = value; }
        public float Target { get => _target; set => _target = value; }

        public override BtStatus Tick(BtContext context)
        {
            return _comparison.Evaluate(context.Board.Get<float>(_key), _target)
                ? BtStatus.Success : BtStatus.Failure;
        }
    }

    [Serializable]
    [NodeMenu("Leaf", "Raises a StoryEvent your game code can listen for.")]
    public sealed class BtRaiseEvent : BtNode
    {
        [SerializeField] private string _eventName;
        [SerializeField] private string _payload;

        public string EventName { get => _eventName; set => _eventName = value; }
        public string Payload { get => _payload; set => _payload = value; }

        public override BtStatus Tick(BtContext context)
        {
            StoryEvents.Raise(_eventName, string.IsNullOrEmpty(_payload) ? null : _payload);
            return BtStatus.Success;
        }
    }

    /// <summary>Escape hatch for game-specific leaves without touching the package.</summary>
    public interface IBtLeafProvider : IStoryService
    {
        BtStatus Execute(string leafId, BtContext context);
    }

    [Serializable]
    [NodeMenu("Leaf", "Calls your own C# through IBtLeafProvider. The escape hatch for anything game specific.")]
    public sealed class BtCustomLeaf : BtNode
    {
        [SerializeField] private string _leafId;
        public string LeafId { get => _leafId; set => _leafId = value; }

        public override BtStatus Tick(BtContext context)
        {
            var provider = StoryServices.Get<IBtLeafProvider>();
            return provider?.Execute(_leafId, context) ?? BtStatus.Failure;
        }
    }

    // ---------- Asset + Runner ----------


    public enum BtTickMode
    {
        EveryFrame,
        Interval,
        /// <summary>Ticks once, then only re-ticks when the blackboard changes
        /// or an interval safety re-check elapses. Cheapest for idle crowds.</summary>
        EventDriven,
    }

}
