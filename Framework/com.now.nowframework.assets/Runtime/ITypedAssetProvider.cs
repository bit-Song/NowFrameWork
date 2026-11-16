using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Assets
{
    /// <summary>可选的类型化资源定位。子资源 Provider 可依据请求类型选择 SpriteAtlas 等加载器。</summary>
    public interface ITypedAssetProvider
    {
        UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, Type assetType, CancellationToken ct);
    }
}
