using NowFrameWork.Core;

namespace NowFrameWork.Core.Pooling
{
    /// <summary>
    /// 池化对象的生命周期。池在取出/归还时自动调用。
    /// 用它替代「在构造时传一堆 Action 回调」——状态重置逻辑跟着类型走，不会漏。
    /// </summary>
    public interface IPoolable
    {
        /// <summary>从池中取出时调用：重置状态、重新激活。</summary>
        void OnSpawn();

        /// <summary>归还池中时调用：清理引用、停止计时、失活。</summary>
        void OnDespawn();
    }
}
