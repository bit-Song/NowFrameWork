using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;

namespace NowFrameWork.Boot
{
    /// <summary>可选插件接缝；主线程加载元数据/DLL，不承担项目生命周期。调用后保守要求进程重启才能重试。</summary>
    public interface IBootCodeLoader
    {
        UniTask<Assembly> LoadAsync(IAssetService assets, CancellationToken ct);
    }
}
