using NowFrameWork.Core.Threading;
using System;

using NowFrameWork.Core;
using NowFrameWork.Core.Logging;

namespace NowFrameWork.Core.Time
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
        readonly ILog _log;

        Slot[] _slots;
        int[] _free;
        int _freeCount;
        int _slotCount;
        int _activeCount;
        int _frame;

        struct Slot
        {
            /// <summary>下次到期的时间点。</summary>
            public double Due;
            /// <summary>重复间隔（秒）。</summary>
            public double Interval;
            /// <summary>到期回调。</summary>
            public Action Callback;
            /// <summary>句柄版本，用于识别复用槽位。</summary>
            public int Version;
            /// <summary>登记时的帧号。</summary>
            public int Frame;
            /// <summary>是否仍在计时。</summary>
            public bool Active;
            /// <summary>是否使用非缩放时间。</summary>
            public bool Unscaled;
            /// <summary>是否重复触发。</summary>
            public bool Repeating;
        }

        /// <summary>创建计时器服务；注入时钟与日志。</summary>
        public TimerService(IClock clock, ILog log = null, int initialCapacity = 16)
        {
            MainThreadGuard.Assert("TimerService.TimerService");
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? Logs.Null;
            if (initialCapacity < 4) initialCapacity = 4;

            _slots = new Slot[initialCapacity];
            _free = new int[initialCapacity];
        }

        /// <summary>待触发的计时器数量。</summary>
        public int PendingCount
        {
            get
            {
                MainThreadGuard.Assert("TimerService.PendingCount");
                return _activeCount;
            }
        }

        /// <summary>延迟执行一次，返回可取消的句柄。</summary>
        public TimerHandle Delay(double seconds, Action callback)
            => Add(seconds, 0d, repeating: false, unscaled: false, callback);

        /// <summary>延迟执行一次，使用非缩放时间。</summary>
        public TimerHandle DelayUnscaled(double seconds, Action callback)
            => Add(seconds, 0d, repeating: false, unscaled: true, callback);

        /// <summary>按间隔重复执行，可选择立即触发首次。</summary>
        public TimerHandle Interval(double seconds, Action callback, bool immediate = false)
            => Add(immediate ? 0d : seconds, seconds, repeating: true, unscaled: false, callback);

        /// <summary>按间隔重复执行，使用非缩放时间。</summary>
        public TimerHandle IntervalUnscaled(double seconds, Action callback, bool immediate = false)
            => Add(immediate ? 0d : seconds, seconds, repeating: true, unscaled: true, callback);

        TimerHandle Add(double delay, double interval, bool repeating, bool unscaled, Action callback)
        {
            MainThreadGuard.Assert("TimerService.Add");
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

        /// <summary>取消句柄对应的计时器。</summary>
        public bool Cancel(in TimerHandle handle)
        {
            MainThreadGuard.Assert("TimerService.Cancel");
            int index = handle.Index;
            if (index < 0 || index >= _slotCount) return false;
            if (!_slots[index].Active) return false;
            if (_slots[index].Version != handle.Version) return false;   // 槽位已被别的计时器复用

            Release(index);
            return true;
        }

        /// <summary>清空全部计时器。</summary>
        public void Clear()
        {
            MainThreadGuard.Assert("TimerService.Clear");
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

        /// <summary>由宿主每帧调用，触发到期的回调。</summary>
        public void Update()
        {
            MainThreadGuard.Assert("TimerService.Update");
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
                try
                {
                    callback?.Invoke();
                }
                catch (Exception e)
                {
                    // 一个计时器回调失败不能中断同一帧的其它计时器
                    if (_log.IsEnabled(LogLevel.Error))
                        _log.Write(LogLevel.Error, "C05", "计时器回调失败 -> " + e.GetType().Name + ": " + e.Message);
                }
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
