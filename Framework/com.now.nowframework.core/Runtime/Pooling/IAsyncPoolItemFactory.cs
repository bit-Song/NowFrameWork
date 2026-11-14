using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Core.Pooling
{
    /// <summary>
    /// 异步创建工厂：用于「对象创建需要等待」（例如 Instantiate 前的资源准备）。
    ///
    /// 注意：池本身不加载资源。资源租约应由资源层持有的**池级保活租约**负责，
    /// 这里只做实例创建；否则每次租用都申请一次租约，计数会随实例数抖动。
    ///
    /// 实现约定：
    /// 1. `CreateAsync` 必须在取消时抛出 `OperationCanceledException`；
    /// 2. `Destroy` 必须幂等、不抛异常（清理路径调用）。
    /// </summary>
    public interface IAsyncPoolItemFactory<T> where T : class
    {
        UniTask<T> CreateAsync(CancellationToken ct);

        void Destroy(T item);
    }
}