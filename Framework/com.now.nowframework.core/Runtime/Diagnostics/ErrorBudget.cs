using System;
using System.Collections.Generic;

namespace NowFrameWork.Core
{
    /// <summary>
    /// 错误预算：把同类错误聚合成一条计数，并在越过阈值时升级为 Fatal。
    ///
    /// 为什么需要它：框架在各回调边界做了异常隔离，副作用是**错误会静默消失**。
    /// 没有预算时，表现是「游戏没崩，但行为不对」，而且日志被刷屏刷到看不见。
    ///
    /// 实现为 <see cref="ILogSink"/>：挂到 Logger 上即可收集所有 Error 及以上日志，
    /// 各模块不需要额外的构造参数。
    ///
    /// 线程：主线程。错误路径才分配（拼接聚合键），正常路径零开销。
    /// </summary>
    public sealed class ErrorBudget : ILogSink
    {
        struct Bucket
        {
            public int Count;
            public double First;
            public double Last;
            public bool Fatal;
        }

        readonly Dictionary<string, Bucket> _buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        readonly int _fatalThreshold;
        readonly int _maxBuckets;
        readonly Func<double> _clock;

        int _total;
        int _dropped;

        /// <param name="fatalThreshold">同一错误累计多少次后升级为 Fatal；0 表示不升级。</param>
        /// <param name="maxBuckets">最多跟踪多少种不同错误，超出后只计数不新建桶。</param>
        /// <param name="clock">时间源；为空时使用 0（测试友好）。</param>
        public ErrorBudget(int fatalThreshold = 50, int maxBuckets = 128, Func<double> clock = null)
        {
            _fatalThreshold = fatalThreshold;
            _maxBuckets = maxBuckets < 1 ? 1 : maxBuckets;
            _clock = clock;
        }

        /// <summary>累计错误次数（含被丢弃的）。</summary>
        public int TotalCount => _total;

        /// <summary>已跟踪的不同错误种类数。</summary>
        public int DistinctCount => _buckets.Count;

        /// <summary>因超过 maxBuckets 未被单独跟踪的次数。</summary>
        public int DroppedCount => _dropped;

        /// <summary>是否已有错误被升级为 Fatal。</summary>
        public bool AnyFatal { get; private set; }

        /// <summary>某条错误首次越过阈值时触发一次（不会重复触发）。</summary>
        public event Action<ErrorEntry> FatalRaised;

        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;

        public void Write(in LogEntry entry) => Report(entry.Tag, entry.Message);

        /// <summary>记录一条错误；module 与 message 相同视为同类。</summary>
        public void Report(string module, string message)
        {
            _total++;

            string key = (module ?? string.Empty) + "|" + (message ?? string.Empty);
            double now = _clock != null ? _clock() : 0d;

            if (_buckets.TryGetValue(key, out Bucket bucket))
            {
                bucket.Count++;
                bucket.Last = now;

                _buckets[key] = bucket;
                TryEscalate(key, bucket);
                return;
            }

            if (_buckets.Count >= _maxBuckets)
            {
                _dropped++;
                return;
            }

            bucket = new Bucket { Count = 1, First = now, Last = now };
            _buckets.Add(key, bucket);
            TryEscalate(key, bucket);
        }

        /// <summary>导出全部聚合结果（供崩溃报告或调试面板）。</summary>
        public void Snapshot(List<ErrorEntry> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            foreach (KeyValuePair<string, Bucket> pair in _buckets)
            {
                int split = pair.Key.IndexOf('|');
                string module = split < 0 ? pair.Key : pair.Key.Substring(0, split);
                string message = split < 0 ? string.Empty : pair.Key.Substring(split + 1);

                Bucket b = pair.Value;
                buffer.Add(new ErrorEntry(module, message, b.Count, b.First, b.Last, b.Fatal));
            }
        }

        public void Reset()
        {
            _buckets.Clear();
            _total = 0;
            _dropped = 0;
            AnyFatal = false;
        }

        void TryEscalate(string key, Bucket bucket)
        {
            if (bucket.Fatal) return;
            if (_fatalThreshold <= 0 || bucket.Count < _fatalThreshold) return;

            bucket.Fatal = true;
            _buckets[key] = bucket;
            AnyFatal = true;

            FatalRaised?.Invoke(ToEntry(key, bucket));
        }

        static ErrorEntry ToEntry(string key, Bucket bucket)
        {
            int split = key.IndexOf('|');
            string module = split < 0 ? key : key.Substring(0, split);
            string message = split < 0 ? string.Empty : key.Substring(split + 1);
            return new ErrorEntry(module, message, bucket.Count, bucket.First, bucket.Last, true);
        }
    }
}