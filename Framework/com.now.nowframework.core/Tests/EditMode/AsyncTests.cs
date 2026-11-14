using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Async;

namespace NowFrameWork.Core.Tests
{
    /// <summary>C07 取消范围与异常归属。</summary>
    public class AsyncTests
    {
        sealed class RecordingSink : ILogSink
        {
            public int Count;
            public string LastMessage;

            public bool IsEnabled(LogLevel level) => true;

            public void Write(in LogEntry entry)
            {
                Count++;
                LastMessage = entry.Message;
            }
        }

        static async UniTask FaultedAsync()
        {
            await UniTask.CompletedTask;
            throw new InvalidOperationException("boom");
        }

        static async UniTask CanceledAsync()
        {
            await UniTask.CompletedTask;
            throw new OperationCanceledException();
        }

        [Test]
        public void CancellationScope_ParentCancel_PropagatesToChild()
        {
            using var parent = new CancellationScope();
            using var child = parent.CreateChild();

            parent.Cancel();

            Assert.IsTrue(parent.IsCancellationRequested);
            Assert.IsTrue(child.IsCancellationRequested, "父取消必须传导到子");
        }

        [Test]
        public void CancellationScope_ChildCancel_DoesNotAffectParent()
        {
            using var parent = new CancellationScope();
            using var child = parent.CreateChild();

            child.Cancel();

            Assert.IsTrue(child.IsCancellationRequested);
            Assert.IsFalse(parent.IsCancellationRequested, "子取消不能影响父");
        }

        [Test]
        public void CancellationScope_Dispose_CancelsAndIsIdempotent()
        {
            var scope = new CancellationScope();
            CancellationToken token = scope.Token;

            scope.Dispose();
            scope.Dispose();   // 幂等

            Assert.IsTrue(token.IsCancellationRequested, "Dispose 等价于取消");
        }

        [Test]
        public void CancellationScope_LinkedToExternalToken()
        {
            using var external = new CancellationTokenSource();
            using var scope = new CancellationScope(external.Token);

            external.Cancel();

            Assert.IsTrue(scope.IsCancellationRequested, "外部令牌取消应传导进来");
        }

        [Test]
        public void TaskRunner_Canceled_DoesNotLogError()
        {
            var sink = new RecordingSink();
            var logger = new Logger(LogLevel.Trace, 0);
            logger.AddSink(sink);
            var runner = new TaskRunner(logger);

            var awaiter = runner.RunAsync(CanceledAsync(), "canceled").GetAwaiter();
            awaiter.GetResult();

            Assert.AreEqual(0, sink.Count, "预期取消不应该记 Error");
            Assert.AreEqual(0, runner.Running);
        }

        [Test]
        public void TaskRunner_Faulted_LogsError()
        {
            var sink = new RecordingSink();
            var logger = new Logger(LogLevel.Trace, 0);
            logger.AddSink(sink);
            var runner = new TaskRunner(logger);

            var awaiter = runner.RunAsync(FaultedAsync(), "battle").GetAwaiter();
            awaiter.GetResult();   // 异常被 Runner 兜住，不应向外抛

            Assert.AreEqual(1, sink.Count);
            StringAssert.Contains("battle", sink.LastMessage);
            StringAssert.Contains("boom", sink.LastMessage);
        }

        [Test]
        public void TaskRunner_RunningCount_ReturnsToZero()
        {
            var runner = new TaskRunner();

            Assert.AreEqual(0, runner.Running);
            runner.RunAsync(UniTask.CompletedTask).GetAwaiter().GetResult();
            Assert.AreEqual(0, runner.Running);
        }

        [Test]
        public void TaskRunner_Dispose_CancelsToken()
        {
            var runner = new TaskRunner();
            CancellationToken token = runner.Token;

            runner.CancelAll();
            Assert.IsTrue(token.IsCancellationRequested);

            runner.Dispose();
            runner.Dispose();   // 幂等
        }
    }
}