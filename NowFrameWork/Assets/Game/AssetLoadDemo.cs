using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using NowFrameWork.Generated;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>
    /// 框架加载方式的最小示例：AddressablesAssets 组装 → InitializeAsync → AcquireAsync → Dispose。
    ///
    /// 用法：任意场景建空物体 → 挂本组件 → Inspector 填 Addressable 地址 → Play → 点「加载 / 卸载」。
    /// 屏幕左下角实时显示 ActiveLeases，卸载后必须回到 0。
    /// </summary>
    public class AssetLoadDemo : MonoBehaviour
    {
        GameObject instance;
        AssetLease<GameObject> lease;
        private async void Start()
        {
            var created = AddressablesAssets.Create();
            IAssetService assets = created.Service;
            AssetsModule module = created.Module;
            // ② 初始化
            await module.InitializeAsync(CancellationToken.None);

            lease = await assets.AcquireAsync<GameObject>(AssetKeys.Tests.PCRegression.Probe_prefab, CancellationToken.None);
            // 克隆一份实例（这才是"你的"对象）
            instance = Instantiate(lease.Asset);
            Debug.Log("loaded bundles: " + AssetBundle.GetAllLoadedAssetBundles().Count());
            // 卸载：先销毁实例，跨一帧，再还租约
            Destroy(instance);
            await UniTask.NextFrame();
            lease.Dispose();
            Debug.Log("loaded bundles: " + AssetBundle.GetAllLoadedAssetBundles().Count());
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
               
            }
        }
    }
}