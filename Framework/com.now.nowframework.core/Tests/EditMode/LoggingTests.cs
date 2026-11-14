using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    /// <summary>
    /// C03 日志用例。重点验证两件事：
    /// 1. 级别过滤正确（含 Sink 自己的过滤）；
    /// 2. 关闭的级别零分配——这是 06 §4.2 R1/R3 的基础。
    /// </summary>
    public class LoggingTests
    {
        sealed class RecordingSink : ILogSink
        {
            public LogLevel MinAccept = LogLevel.Trace;
            public int Count;
            public LogLevel LastLevel;
            public string LastTag;
            public string LastMessage;

            public bool IsEnabled(LogLevel level) => level >= MinAccept;

            public void Write(in LogEntry entry)
            {
                Count++;
                LastLevel = entry.Level;
                LastTag = entry.Tag;
                LastMessage = entry.Message;
            }
        }

        [Test]
        public void IsEnabled_RespectsMinLevel()
        {
            var logger = new Logger(LogLevel.Warn, 0);

            Assert.IsFalse(logger.IsEnabled(LogLevel.Debug));
            Assert.IsFalse(logger.IsEnabled(LogLevel.Info));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Warn));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Error));
        }

        [Test]
        public void Write_BelowMinLevel_DoesNotReachSink()
        {
            var sink = new RecordingSink();
            var logger = new Logger(LogLevel.Warn, 0);
            logger.AddSink(sink);

            logger.Write(LogLevel.Info, "C01", "不应出现");

            Assert.AreEqual(0, sink.Count);
        }

        [Test]
        public void Write_PassesLevelTagAndMessage()
        {
            var sink = new RecordingSink();
            var logger = new Logger(LogLevel.Trace, 0);
            logger.AddSink(sink);

            logger.Write(LogLevel.Error, "C01", "模块关闭失败");

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(LogLevel.Error, sink.LastLevel);
            Assert.AreEqual("C01", sink.LastTag);
            Assert.AreEqual("模块关闭失败", sink.LastMessage);
        }

        [Test]
        public void Sink_FilteringIsAppliedPerSink()
        {
            var quiet = new RecordingSink { MinAccept = LogLevel.Error };
            var logger = new Logger(LogLevel.Trace, 0);
            logger.AddSink(quiet);

            logger.Write(LogLevel.Info, "C01", "info");
            Assert.AreEqual(0, quiet.Count);

            logger.Write(LogLevel.Error, "C01", "error");
            Assert.AreEqual(1, quiet.Count);
        }

        [Test]
        public void DumpRecent_KeepsNewestEntriesInOrder()
        {
            var logger = new Logger(LogLevel.Trace, 3);

            for (int i = 1; i <= 5; i++) logger.Write(LogLevel.Info, "T", "m" + i);

            var buffer = new List<LogEntry>(4);
            logger.DumpRecent(buffer);

            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual("m3", buffer[0].Message);
            Assert.AreEqual("m4", buffer[1].Message);
            Assert.AreEqual("m5", buffer[2].Message);
        }

        [Test]
        public void LogsNull_IsAlwaysDisabledAndSilent()
        {
            Assert.IsFalse(Logs.Null.IsEnabled(LogLevel.Error));
            Logs.Null.Write(LogLevel.Error, "T", "noop");   // 不应抛、不应分配
        }
    }
}
