using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Logging;

namespace NowFrameWork.Core.Async
{
    /// <summary>
    /// 后台任务归属：谁启动、谁负责兜住异常。
    ///
    /// 解决的问题：`async void` / `Forget()` 把异常丢进静态调度器，业务看不到。
    /// 这里统一处理：
    /// - `OperationCanceledException` 视为预期取消，**不记 Error**；
    /// - 其余异常记 Error（经 ErrorBudget 聚合），任务不会静默消失。
    ///
    /// `Running` 用于诊断与关闭时等待。
    /// </summary>
    public sealed class TaskRunner : IDisposable
    {
        readonly ILog _log;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();

        int _running;
        bool _disposed;

        public TaskRunner(ILog log = null)
        {
            _log = log ?? Logs.Null;
        }

        /// <summary>传给任务的取消源；`CancelAll` 会取消它。</summary>
        public CancellationToken Token => _cts.Token;

        public int Running => _running;

        /// <summary>启动并立即返回；异常由内部兜住并记录。</summary>
        public void Run(UniTask task, string context = null) => RunAsync(task, context).Forget();

        /// <summary>可等待版本，便于测试与"关闭时等待"。</summary>
        public async UniTask RunAsync(UniTask task, string context = null)
        {
            _running++;
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // 预期取消，不是错误
            }
            catch (Exception e)
            {
                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, "C07",
                        (context ?? "task") + " 失败 -> " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                _running--;
            }
        }

        public void CancelAll()
        {
            if (!_disposed) _cts.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}