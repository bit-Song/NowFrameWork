using NowFrameWork.Core;

namespace NowFrameWork.Core.Pooling
{
    /// <summary>
    /// 池化对象的创建/销毁工厂。
    ///
    /// 为什么要工厂而不是两个委托：资源加载回收的关键约束是「创建时可能持有资源租约，
    /// 销毁时必须释放同一个租约」。把它收成一个对象，契约更硬，也方便按资源类型复用实现。
    ///
    /// 实现约定：
    /// 1. `Create` 不得返回 null；需要资源时在这里申请租约，失败则抛异常（池会把异常原样抛出）；
    /// 2. `Destroy` 必须释放 `Create` 持有的**全部**外部资源，且必须幂等；
    /// 3. `Destroy` 不得抛异常——池会在清理路径调用它，抛异常会中断后续对象的释放。
    /// </summary>
    public interface IPoolItemFactory<T> where T : class
    {
        T Create();

        void Destroy(T item);
    }
}
