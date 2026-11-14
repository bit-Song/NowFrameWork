using System;
using System.Threading;

namespace NowFrameWork.Core.Async
{
    /// <summary>
    /// 取消范围：把一组异步操作的生命周期绑在一起。
    ///
    /// 规则：
    /// 1. 子 scope 通过 linked token 挂在父 token 上——父取消，子自动取消；子取消**不影响**父；
    /// 2. `Dispose` 等价于「取消并释放」，因此 `using` 块结束就取消该范围内的全部操作；
    /// 3. 幂等：重复 Dispose 不抛异常。
    ///
    /// 分配：构造时分配一个 CTS（冷路径），运行期零分配。
    /// </summary>
    public sealed class CancellationScope : IDisposable
    {
        readonly CancellationTokenSource _cts;
        bool _disposed;

        public CancellationScope(CancellationToken parent = default)
        {
            _cts = parent.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(parent)
                : new CancellationTokenSource();
        }

        public CancellationToken Token => _cts.Token;

        public bool IsCancellationRequested => _cts.IsCancellationRequested;

        /// <summary>派生一个子范围；父范围取消时它随之取消。</summary>
        public CancellationScope CreateChild() => new CancellationScope(Token);

        /// <summary>主动取消（不释放）。</summary>
        public void Cancel()
        {
            if (_disposed) return;
            _cts.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 父级已释放，忽略
            }

            _cts.Dispose();
        }
    }
}