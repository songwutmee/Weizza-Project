using System;
using System.Collections.Generic;
using StoryKit.Flow;
using UnityEngine;

namespace StoryKit.Bt
{
    public sealed class BehaviorTreeRunner : MonoBehaviour
    {
        [SerializeField] private BehaviorTreeAsset _asset;
        [SerializeField] private BtTickMode _tickMode = BtTickMode.EveryFrame;
        [SerializeField, Min(0.02f)] private float _interval = 0.2f;

        /// <summary>Instance board (this monster's hp/target), parented to Global.
        /// Assign SharedBoard before Awake to insert a squad-shared board between them.</summary>
        public Blackboard SharedBoard { get; set; }
        public Blackboard Board { get; private set; }

        public BtStatus LastStatus { get; private set; } = BtStatus.Running;
        public bool IsPaused { get; set; }

        /// <summary>The asset being run, for inspectors and save code.</summary>
        public BehaviorTreeAsset Asset => _asset;

        /// <summary>The per-instance clone that actually ticks. Editor use.</summary>
        public BehaviorTreeAsset RuntimeTree => _runtimeTree;

        private BtContext _context;
        private float _nextTickAt;
        private bool _dirty = true;

        /// <summary>Per-instance clone: BT nodes keep tick state (currentChild,
        /// cooldowns), so two monsters must never share one tree instance.</summary>
        private BehaviorTreeAsset _runtimeTree;

        private void Awake()
        {
            var parent = SharedBoard ?? StoryState.Global;
            Board = new Blackboard(parent);
            _context = new BtContext(this);
            Board.KeyChanged += _ => _dirty = true;

            if (_asset != null)
                _runtimeTree = Instantiate(_asset);
        }

        private void OnDestroy()
        {
            if (_runtimeTree != null)
            {
                Destroy(_runtimeTree);
                _runtimeTree = null;
            }
        }

        private void Update()
        {
            if (IsPaused || _runtimeTree == null || _runtimeTree.Root == null) return;

            switch (_tickMode)
            {
                case BtTickMode.EveryFrame:
                    RunTick();
                    break;

                case BtTickMode.Interval:
                    if (StoryTime.Time >= _nextTickAt)
                    {
                        _nextTickAt = StoryTime.Time + _interval;
                        RunTick();
                    }
                    break;

                case BtTickMode.EventDriven:
                    bool safetyRecheck = StoryTime.Time >= _nextTickAt;
                    if (_dirty || LastStatus == BtStatus.Running || safetyRecheck)
                    {
                        if (safetyRecheck) _nextTickAt = StoryTime.Time + _interval;
                        _dirty = false;
                        RunTick();
                    }
                    break;
            }
        }

        private void RunTick()
        {
            LastStatus = _runtimeTree.Root.Run(_context);
        }

        public void SetTree(BehaviorTreeAsset asset)
        {
            _asset = asset;
            if (_runtimeTree != null) Destroy(_runtimeTree);
            _runtimeTree = asset != null ? Instantiate(asset) : null;
            _dirty = true;
            LastStatus = BtStatus.Running;
        }
    }
}
