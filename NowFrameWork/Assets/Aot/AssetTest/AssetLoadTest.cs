using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using UnityEngine;

namespace NowFrameWork.AssetTest
{
    /// <summary>
    /// Player 内的资源加载验证：走的是框架自己的 AddressablesProvider + AssetService，
    /// 不是裸 Addressables 调用。
    ///
    /// 验证点：
    /// 1. Provider 初始化与 canonical 键解析；
    /// 2. 同址双租约只加载一次；
    /// 3. 逐个释放后 ActiveLeases 归零；
    /// 4. **最后一个租约释放并跨一帧后，资源是否真的不可用**（R-1 的实证）。
    ///
    /// 报告写到 Application.persistentDataPath/nw-asset-probe.txt，屏幕上也显示。
    /// </summary>
    public sealed class AssetLoadTest : MonoBehaviour
    {
        [SerializeField] string _address = "Assets/HotUpdateDlls/HotUpdate.dll.bytes";

        readonly StringBuilder _report = new StringBuilder();
        string _path;

        async void Start()
        {
            _path = Path.Combine(Application.persistentDataPath, "nw-asset-probe.txt");
            Write("unity: " + Application.unityVersion);
            Write("address: " + _address);

            try
            {
                var provider = new AddressablesProvider();
                await provider.InitializeAsync(CancellationToken.None);
                Write("1. provider init: OK");

                var service = new AssetService(provider);

                AssetLease<UnityEngine.Object> first =
                    await service.AcquireAsync<UnityEngine.Object>(_address, CancellationToken.None);

                UnityEngine.Object asset = first.Asset;
                int instanceId = asset != null ? asset.GetInstanceID() : 0;

                Write("2. load: " + (asset != null) + " canonical=" + first.CanonicalKey + " leases=" + service.ActiveLeases);

                AssetLease<UnityEngine.Object> second =
                    await service.AcquireAsync<UnityEngine.Object>(_address, CancellationToken.None);

                Write("3. second lease: active=" + service.ActiveLeases);

                first.Dispose();
                Write("4. release first: active=" + service.ActiveLeases);

                second.Dispose();
                Write("5. release all: active=" + service.ActiveLeases);

                // R-1：跨一帧再看资源是否可用
                await UniTask.Yield();

                bool idAlive = instanceId != 0 && Resources.InstanceIDToObject(instanceId) != null;
                bool managedNull = asset == null;

                Write("6. next frame: instanceId alive=" + idAlive + " managedNull=" + managedNull);
                Write("   => " + ((!idAlive) ? "资源已卸载：R-1 必须保留" : "资源仍存活：可能仍有其它引用"));
            }
            catch (Exception e)
            {
                Write("EXCEPTION: " + e);
            }

            Dump();
        }

        void Write(string line) => _report.AppendLine(line);

        void Dump()
        {
            string text = _report.ToString();
            Debug.Log("[AssetTest]\n" + text);

            try
            {
                File.WriteAllText(_path, text, Encoding.UTF8);
                Debug.Log("[AssetTest] 报告: " + _path);
            }
            catch (Exception e)
            {
                Debug.LogError("[AssetTest] 写报告失败: " + e.Message);
            }
        }

        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 1000, 400), _report.ToString());
        }
    }
}