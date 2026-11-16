using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using HybridCLR;
using NowFrameWork.Assets;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.Boot.HotCode
{
    /// <summary>可删除的项目适配组件；不修改 Core/Unity 包，也不调用旧 LoadDllScript 的 Start 流程。</summary>
    public sealed class HybridClrBootLoader : MonoBehaviour, IBootCodeLoader
    {
        [SerializeField] string _dllAddress = "Assets/HotUpdateDlls/HotUpdate.dll.bytes";
        [SerializeField] string _assemblyName = "HotUpdate";
        [SerializeField] string[] _metadataAddresses = Array.Empty<string>();
        static bool _attempted;

        public async UniTask<Assembly> LoadAsync(IAssetService assets, CancellationToken ct)
        {
            MainThreadGuard.Assert("HybridClrBootLoader.LoadAsync");
            if (string.IsNullOrWhiteSpace(_dllAddress) || string.IsNullOrWhiteSpace(_assemblyName) || _metadataAddresses == null)
                throw new ArgumentException("DLL 地址、程序集名称和元数据列表必须有效");
            if (Application.isEditor) throw new InvalidOperationException("DLL 热更须在 Player 验证；编辑器请关闭代码热更");
            if (_attempted) throw new InvalidOperationException("本进程已尝试代码加载，须重启");
            foreach (Assembly existing in AppDomain.CurrentDomain.GetAssemblies())
                if (existing.GetName().Name == _assemblyName) throw new InvalidOperationException("程序集已加载，须重启：" + _assemblyName);
            ct.ThrowIfCancellationRequested();
            _attempted = true;
            foreach (string address in _metadataAddresses)
            {
                using (var lease = await assets.AcquireAsync<TextAsset>(address, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var code = RuntimeApi.LoadMetadataForAOTAssembly(lease.Asset.bytes, HomologousImageMode.Consistent);
                    if (code != LoadImageErrorCode.OK)
                        throw new InvalidOperationException("AOT 元数据失败：" + address + " -> " + code);
                }
            }
            using (var dll = await assets.AcquireAsync<TextAsset>(_dllAddress, ct))
            {
                ct.ThrowIfCancellationRequested();
                Assembly assembly = Assembly.Load(dll.Asset.bytes);
                if (assembly.GetName().Name != _assemblyName) throw new InvalidOperationException("热更程序集名称不匹配");
                return assembly;
            }
        }
    }
}
