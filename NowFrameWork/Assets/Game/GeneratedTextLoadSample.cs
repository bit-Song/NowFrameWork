using System;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using NowFrameWork.Generated;
using UnityEngine;

namespace NowFrameWork.AssetKeySamples
{
    /// <summary>独立加载示例：挂到空对象上，Play 后加载生成键对应的文本并归还租约。</summary>
    public sealed class GeneratedTextLoadSample : MonoBehaviour
    {
        // Unity 的 Start 事件允许 async void；在这里统一处理异常。
        async void Start()
        {
            try
            {
                var assets = new AssetService(new AddressablesProvider(TimeSpan.FromSeconds(10)));
                var ct = destroyCancellationToken;
                await assets.InitializeAsync(ct);

                // 资源键来自已经生成的 AssetKeys，不再手写 "PCProbe/Config"。
                using (var lease = await assets.AcquireAsync<TextAsset>(
                    AssetKeys.Tests.PCRegression.Config_txt, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    Debug.Log("[资源键加载示例] 文本内容：" + lease.Asset.text, this);
                }

                Debug.Log("[资源键加载示例] 租约已释放，ActiveLeases=" + assets.ActiveLeases, this);
            }
            catch (OperationCanceledException) { /* 对象销毁或停止 Play 时，取消是正常退出。 */ }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
