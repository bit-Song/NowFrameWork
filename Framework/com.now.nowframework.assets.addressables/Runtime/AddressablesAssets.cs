using System;
using NowFrameWork.Core.Logging;

namespace NowFrameWork.Assets.Adapter
{
    /// <summary>
    /// 组装入口：一行拿到「Addressables 支撑的资源服务 + 可注册的模块」。
    ///
    /// 用法（Boot 场景的组装代码里）：
    /// <code>
    /// var (assets, assetsModule) = AddressablesAssets.Create();
    /// hub.Register(assetsModule);
    /// </code>
    ///
    /// 业务层只依赖 <see cref="IAssetService"/>，不接触 Addressables 类型。
    /// </summary>
    public static class AddressablesAssets
    {
        public static (IAssetService Service, AssetsModule Module) Create(TimeSpan? timeout = null, ILog log = null)
        {
            var provider = new AddressablesProvider(timeout);
            var service = new AssetService(provider, log);
            var module = new AssetsModule(service, log);

            return (service, module);
        }
    }
}