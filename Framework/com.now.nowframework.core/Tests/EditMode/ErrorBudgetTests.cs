using System.Collections.Generic;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    /// <summary>G-02 错误预算：去重聚合、阈值升级、上限丢弃。</summary>
    public class ErrorBudgetTests
    {
        [Test]
        public void Report_SameModuleAndMessage_AggregatesIntoOne()
        {
            var budget = new ErrorBudget();

            budget.Report("C01", "模块回调失败");
            budget.Report("C01", "模块回调失败");
            budget.Report("C01", "模块回调失败");

            Assert.AreEqual(3, budget.TotalCount);
            Assert.AreEqual(1, budget.DistinctCount);

            var buffer = new List<ErrorEntry>();
            budget.Snapshot(buffer);
            Assert.AreEqual(3, buffer[0].Count);
        }

        [Test]
        public void Report_DifferentMessages_AreSeparateBuckets()
        {
            var budget = new ErrorBudget();

            budget.Report("C01", "错误甲");
            budget.Report("C01", "错误乙");
            budget.Report("C03", "错误甲");

            Assert.AreEqual(3, budget.DistinctCount, "模块或消息不同都应分开统计");
        }

        [Test]
        public void Report_ReachesThreshold_EscalatesOnce()
        {
            var budget = new ErrorBudget(fatalThreshold: 3);
            int raised = 0;
            budget.FatalRaised += _ => raised++;

            for (int i = 0; i < 6; i++) budget.Report("C05", "计时器回调失败");

            Assert.AreEqual(1, raised, "越过阈值只应上报一次");
            Assert.IsTrue(budget.AnyFatal);
        }

        [Test]
        public void Report_BeyondMaxBuckets_IsDroppedButCounted()
        {
            var budget = new ErrorBudget(maxBuckets: 2);

            budget.Report("A", "1");
            budget.Report("B", "2");
            budget.Report("C", "3");   // 超出上限

            Assert.AreEqual(2, budget.DistinctCount);
            Assert.AreEqual(1, budget.DroppedCount);
            Assert.AreEqual(3, budget.TotalCount, "被丢弃的也要计入总数");
        }

        [Test]
        public void AsLogSink_IgnoresBelowError()
        {
            var budget = new ErrorBudget();

            Assert.IsFalse(budget.IsEnabled(LogLevel.Warn));
            Assert.IsTrue(budget.IsEnabled(LogLevel.Error));
        }

        [Test]
        public void Logger_WritesError_ReachesBudget()
        {
            var budget = new ErrorBudget();
            var logger = new Logger(LogLevel.Trace, 0);
            logger.AddSink(budget);

            logger.Write(LogLevel.Error, "C06", "池回收失败");
            logger.Write(LogLevel.Info, "C06", "不该计入");

            Assert.AreEqual(1, budget.TotalCount, "只应收集 Error 及以上");
        }

        [Test]
        public void Reset_ClearsState()
        {
            var budget = new ErrorBudget(fatalThreshold: 1);
            budget.Report("A", "x");

            budget.Reset();

            Assert.AreEqual(0, budget.TotalCount);
            Assert.AreEqual(0, budget.DistinctCount);
            Assert.IsFalse(budget.AnyFatal);
        }
    }
}