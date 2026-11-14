using System;
using System.Collections.Generic;

namespace NowFrameWork.Core
{
    /// <summary>
    /// C03 日志实现：级别过滤 + 多 Sink + 固定容量的近期日志环形缓冲（供崩溃报告取用）。
    ///
    /// 分配约定：
    /// - 级别低于 MinLevel 时直接返回，不构造 LogEntry、不调时钟、不碰 Sink；
    /// - 消息字符串由调用方在 IsEnabled 为真时构造，本类不再拼接；
    /// - 环形缓冲在构造时分配一次，写入只改数组元素，不产生垃圾。
    ///
    /// 线程：只允许主线程调用。
    /// </summary>
    public sealed class Logger : ILog
    {
        static readonly Func<double> s_zeroClock = () => 0d;

        readonly List<ILogSink> _sinks = new List<ILogSink>(4);
        readonly LogEntry[] _ring;
        readonly Func<double> _clock;
        int _ringStart;
        int _ringCount;
        LogLevel _minLevel;

        /// <param name="minLevel">低于该级别的日志直接丢弃。</param>
        /// <param name="ringCapacity">近期日志保留条数；0 表示不保留（生产环境可关掉省内存）。</param>
        /// <param name="clock">时间源；为空时使用常量 0，由平台包注入真实时间。</param>
        public Logger(LogLevel minLevel = LogLevel.Info, int ringCapacity = 256, Func<double> clock = null)
        {
            if (ringCapacity < 0) throw new ArgumentOutOfRangeException(nameof(ringCapacity));

            _minLevel = minLevel;
            _ring = ringCapacity == 0 ? Array.Empty<LogEntry>() : new LogEntry[ringCapacity];
            _clock = clock ?? s_zeroClock;
        }

        public LogLevel MinLevel => _minLevel;

        public bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.Off;

        public void SetMinLevel(LogLevel level) => _minLevel = level;

        public void AddSink(ILogSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (_sinks.Contains(sink)) return;
            _sinks.Add(sink);
        }

        public void RemoveSink(ILogSink sink) => _sinks.Remove(sink);

        public void Write(LogLevel level, string tag, string message)
        {
            if (!IsEnabled(level)) return;

            var entry = new LogEntry(level, tag, message ?? string.Empty, _clock());

            for (int i = 0; i < _sinks.Count; i++)
            {
                ILogSink sink = _sinks[i];
                if (sink.IsEnabled(level)) sink.Write(in entry);
            }

            Store(in entry);
        }

        /// <summary>把近期日志按「旧 → 新」写入调用方缓冲（内部先 Clear），用于崩溃报告。</summary>
        public void DumpRecent(List<LogEntry> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            for (int i = 0; i < _ringCount; i++)
                buffer.Add(_ring[(_ringStart + i) % _ring.Length]);
        }

        void Store(in LogEntry entry)
        {
            if (_ring.Length == 0) return;

            int index = (_ringStart + _ringCount) % _ring.Length;
            _ring[index] = entry;

            if (_ringCount < _ring.Length) _ringCount++;
            else _ringStart = (_ringStart + 1) % _ring.Length;   // 满了就覆盖最旧的
        }
    }
}