using System;

namespace NowFrameWork.Core
{
    /// <summary>
    /// C05 计时器实现：槽位数组 + 空闲栈 + 版本号。
    ///
    /// GC 设计：
    /// - Update 只遍历已分配的槽位，不分配；
    /// - 槽位复用靠版本号区分新旧句柄，不需要字典；
    /// - 释放时清空 Callback 引用，避免持有业务对象；
    /// - 循环计时器若因长时间未 Update 落后多轮，只补一次（防补帧爆炸）。
    /// </summary>
    public sealed class TimerService : ITimerService
    {
        readonly IClock _clock;

        Slot[] _slots;
        int[] _free;
        int _freeCount;
        int _slotCount;
        int _activeCount;
        int _frame;

        struct Slot
        {
            public double Due;
            public double Interval;
            public Action Callback;
            public int Version;
            public int Frame;
            public bool Active;
            public bool Unscaled;
            public bool Repeating;
        }

        public TimerService(IClock clock, int initialCapacity = 16)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (initialCapacity < 4) initialCapacity = 4;

            _slots = new Slot[initialCapacity];
            _free = new int[initialCapacity];
        }

        public int PendingCount => _activeCount;

        public TimerHandle Delay(double seconds, Action callback)
            => Add(seconds, 0d, repeating: false, unscaled: false, callback);

        public TimerHandle DelayUnscaled(double seconds, Action callback)
            => Add(seconds, 0d, repeating: false, unscaled: true, callback);

        public TimerHandle Interval(double seconds, Action callback, bool immediate = false)
            => Add(immediate ? 0d : seconds, seconds, repeating: true, unscaled: false, callback);

        public TimerHandle IntervalUnscaled(double seconds, Action callback, bool immediate = false)
            => Add(immediate ? 0d : seconds, seconds, repeating: true, unscaled: true, callback);

        TimerHandle Add(double delay, double interval, bool repeating, bool unscaled, Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (delay < 0d) delay = 0d;
            if (repeating && interval <= 0d) throw new ArgumentOutOfRangeException(nameof(interval), "循环计时器的间隔必须大于 0");

            int index = AcquireIndex();
            double now = unscaled ? _clock.UnscaledNow : _clock.Now;

            _slots[index].Due = now + delay;
            _slots[index].Interval = interval;
            _slots[index].Callback = callback;
            _slots[index].Unscaled = unscaled;
            _slots[index].Repeating = repeating;
            _slots[index].Active = true;
            _slots[index].Version++;
            _slots[index].Frame = _frame;   // 本帧内新注册的计时器最早下一帧触发
            _activeCount++;

            return new TimerHandle(index, _slots[index].Version);
        }

        public bool Cancel(in TimerHandle handle)
        {
            int index = handle.Index;
            if (index < 0 || index >= _slotCount) return false;
            if (!_slots[index].Active) return false;
            if (_slots[index].Version != handle.Version) return false;   // 槽位已被别的计时器复用

            Release(index);
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < _slotCount; i++)
            {
                if (!_slots[i].Active) continue;
                _slots[i].Active = false;
                _slots[i].Callback = null;
                _slots[i].Version++;
            }

            _freeCount = 0;
            for (int i = _slotCount - 1; i >= 0; i--) _free[_freeCount++] = i;
            _activeCount = 0;
        }

        public void Update()
        {
            if (_activeCount == 0) return;

            double now = _clock.Now;
            double unscaledNow = _clock.UnscaledNow;
            int frame = ++_frame;

            for (int i = 0; i < _slotCount; i++)
            {
                if (!_slots[i].Active) continue;
                if (_slots[i].Frame == frame) continue;   // 本帧内新注册的，下一帧再处理

                double currentNow = _slots[i].Unscaled ? unscaledNow : now;
                if (_slots[i].Due > currentNow) continue;

                Action callback = _slots[i].Callback;

                if (_slots[i].Repeating)
                {
                    _slots[i].Due += _slots[i].Interval;
                    if (_slots[i].Due <= currentNow) _slots[i].Due = currentNow + _slots[i].Interval;   // 落后多轮只补一次
                }
                else
                {
                    Release(i);
                }

                // 先改状态再回调：回调里取消自己是安全的；
                // 回调里新增的计时器带本帧标记，不会被这一轮二次处理（见循环开头的 Frame 判断）
                callback?.Invoke();
            }
        }

        int AcquireIndex()
        {
            if (_freeCount > 0) return _free[--_freeCount];

            if (_slotCount == _slots.Length) Grow();
            return _slotCount++;
        }

        void Grow()
        {
            int capacity = _slots.Length * 2;
            Array.Resize(ref _slots, capacity);
            Array.Resize(ref _free, capacity);
        }

        void Release(int index)
        {
            _slots[index].Active = false;
            _slots[index].Callback = null;   // 不持有业务对象
            _slots[index].Version++;

            if (_freeCount < _free.Length) _free[_freeCount++] = index;
            _activeCount--;
        }
    }
}