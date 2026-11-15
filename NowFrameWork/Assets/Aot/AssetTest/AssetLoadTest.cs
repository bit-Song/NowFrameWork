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

                // R-1：逐帧观察资源到底在第几帧失效（Addressables 可能异步卸载）
                int frames = 0;

                while (frames < 120 && instanceId != 0 && Resources.InstanceIDToObject(instanceId) != null)
                {
                    await UniTask.Yield();
                    frames++;
                }

                bool idAlive = instanceId != 0 && Resources.InstanceIDToObject(instanceId) != null;
                bool managedNull = asset == null;

                Write("6. released; alive after " + frames + " frames: " + idAlive + " managedNull=" + managedNull);

                if (idAlive)
                {
                    Write("   => 120 帧内仍未卸载，可能有其它引用（或该资源所在包被常驻）");
                }
                else if (frames == 0)
                {
                    Write("   => R-1 证据成立：释放当帧即失效，必须跨帧再释放租约");
                }
                else
                {
                    Write("   => R-1 证据成立：异步卸载，失效于第 " + frames + " 帧；租约释放需覆盖该窗口");
                }
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