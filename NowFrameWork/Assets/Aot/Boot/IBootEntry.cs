using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Boot
{
    /// <summary>
    /// 仅主线程。项目入口拥有自己的模块/Scope；不要向已经启动的基础 Hub 动态注册。
    /// Enter 部分失败也会调用 Exit；Exit 必须幂等，并 await 场景外实例/资源池真正收尾。
    /// 不可在 Enter 内 await 本次启动的 Stop（会自等待）；可请求取消后返回。
    /// </summary>
    public interface IBootEntry
    {
        UniTask EnterAsync(BootContext context, CancellationToken ct);
        UniTask ExitAsync();
    }
}
