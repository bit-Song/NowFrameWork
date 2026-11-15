using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using NowFrameWork.Core.Time;
using NowFrameWork.Unity;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace NowFrameWork.AssetTest
{
    /// <summary>
    /// Player 内的资源层验证：一次出包跑三段。
    ///
    /// A. 纯租约：加载 → 双租约 → 逐个释放 → 逐帧观察资源何时真正失效；
    /// B. 对象池：AssetPool 保活租约 + 实例销毁；
    /// C. Scope：启动 → 关闭，检查状态机与租约归零。
    ///
    /// EditMode 测不了这些（没有 bundle、没有真实卸载），必须在 Player 里跑。
    /// 报告写到 persistentDataPath/nw-asset-probe.txt，屏幕同步显示。
    /// </summary>
    public sealed class AssetLoadTest : MonoBehaviour
    {
        [SerializeField] string _address = "Assets/HotUpdateDlls/HotUpdate.dll.bytes";

        readonly StringBuilder _report = new StringBuilder();
        string _path;

        async void Start()
        {
            Debug.Log(Application.persistentDataPath);
            _path = Path.Combine(Application.persistentDataPath, "nw-asset-probe.txt");

            // 诊断：缓存位置 + Addressables 真正请求的地址
            Write("CachePath=" + Caching.currentCacheForWriting.path);
            Addressables.InternalIdTransformFunc = location =>
            {
                Write("  Request: " + location.InternalId);
                return location.InternalId;
            };
            Write("unity: " + Application.unityVersion);
            Write("address: " + _address);
            Write("");

            var provider = new AddressablesProvider();
            var service = new AssetService(provider);
            var clock = new UnityClock();

            try
            {
                Write("== 初始化中（超时 30s）…");
                await provider.InitializeAsync(CancellationToken.None);
                Write("== 初始化: OK");
            }
            catch (Exception e)
            {
                Write("== 初始化失败: " + e.Message);
                Dump();
                return;
            }

            await RunLeaseTest(service);
            await RunPoolTest(service, clock);
            await RunScopeTest(clock);

            Write("");
            Write("== 完成，ActiveLeases=" + service.ActiveLeases);
            Dump();
        }

        async UniTask RunLeaseTest(AssetService service)
        {
            Write("-- A. 纯租约");

            // 关键证据：这个地址到底解析成什么（远端 URL / 本地路径）
            AsyncOperationHandle<IList<IResourceLocation>> locations =
                Addressables.LoadResourceLocationsAsync(_address, typeof(UnityEngine.Object));
            IList<IResourceLocation> list = await locations.Task;

            if (list != null && list.Count > 0)
            {
                Write("   A0 InternalId=" + list[0].InternalId);
                Write("      Provider=" + list[0].ProviderId);
                Write("      Bundle=" + (list[0].Dependencies != null && list[0].Dependencies.Count > 0
                    ? list[0].Dependencies[0].InternalId
                    : "(无依赖)"));
            }
            else
            {
                Write("   A0 没有 location（资源不在当前 catalog）");
            }

            Addressables.Release(locations);

            try
            {
                AssetLease<UnityEngine.Object> first =
                    await service.AcquireAsync<UnityEngine.Object>(_address, CancellationToken.None);
                AssetLease<UnityEngine.Object> second =
                    await service.AcquireAsync<UnityEngine.Object>(_address, CancellationToken.None);

                UnityEngine.Object asset = first.Asset;
                int instanceId = asset != null ? asset.GetInstanceID() : 0;

                Write("   A1 加载: " + (asset != null) + " canonical=" + first.CanonicalKey);
                Write("   A2 双租约: active=" + service.ActiveLeases);

                first.Dispose();
                second.Dispose();
                Write("   A3 全部释放: active=" + service.ActiveLeases);

                int frames = 0;
                while (frames < 120 && instanceId != 0 && Resources.InstanceIDToObject(instanceId) != null)
                {
                    await UniTask.Yield();
                    frames++;
                }

                bool alive = instanceId != 0 && Resources.InstanceIDToObject(instanceId) != null;
                Write("   A4 资源失效于第 " + frames + " 帧: " + (!alive));
            }
            catch (Exception e)
            {
                Write("   A 失败: " + e.Message);
            }
        }

        async UniTask RunPoolTest(AssetService service, IClock clock)
        {
            Write("-- B. 对象池");

            AssetPool<ScriptableObject> pool = null;

            try
            {
                pool = await AssetPool<ScriptableObject>.CreateAsync(
                    service,
                    AssetKey.From(_address),
                    () => ScriptableObject.CreateInstance<ScriptableObject>(),
                    UnityEngine.Object.Destroy,
                    clock,
                    maxSize: 4,
                    settleFrames: 10);

                Write("   B1 建池: activeLeases=" + service.ActiveLeases);

                ScriptableObject a = pool.Rent();
                ScriptableObject b = pool.Rent();
                Write("   B2 租用 2 个: active=" + pool.ActiveCount);

                pool.Release(a);
                pool.Release(b);
                Write("   B3 归还: active=" + pool.ActiveCount + " inactive=" + pool.InactiveCount);
            }
            catch (Exception e)
            {
                Write("   B 失败: " + e.Message);
            }
            finally
            {
                if (pool != null)
                {
                    await pool.DisposeAsync();
                    Write("   B4 释放池: activeLeases=" + service.ActiveLeases);
                }
            }
        }

        async UniTask RunScopeTest(IClock clock)
        {
            Write("-- C. Scope");

            Scope scope = null;

            try
            {
                scope = new Scope("probe", null, clock);
                await scope.StartupAsync(CancellationToken.None);
                Write("   C1 启动: state=" + scope.State);
            }
            catch (Exception e)
            {
                Write("   C 启动失败: " + e.Message);
            }
            finally
            {
                if (scope != null)
                {
                    await scope.DisposeAsync();
                    Write("   C2 关闭: state=" + scope.State);
                }
            }
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
            GUI.Label(new Rect(10, 10, 1200, 600), _report.ToString());
        }
    }
}