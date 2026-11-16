using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using NowFrameWork.Diagnostics;
using UnityEngine;

namespace NowFrameWork.Boot
{
    /// <summary>
    /// Boot 流程样例：演示资源层怎么组装、怎么用、怎么关。
    ///
    /// 顺序（新项目照这个顺序抄）：
    /// 1. 组装：Provider → AssetService → ConfigService / SceneService；
    /// 2. 初始化：`AssetsModule.InitializeAsync`（内部会加载 catalog）；
    /// 3. 使用：加载配置 → 加载场景；
    /// 4. 收尾：归还配置租约 → 关闭模块。
    ///
    /// 这是**样例**而不是框架：项目有自己的 Entry 时应把这段逻辑搬过去，
    /// 等两三个项目跑下来再提炼成框架基类。
    /// </summary>
    public sealed class BootFlow : MonoBehaviour
    {
        [SerializeField] string _configAddress = "Assets/Config/GameConfig.asset";
        [SerializeField] string _sceneAddress = "";
        [SerializeField] bool _runOnStart = true;

        readonly StringBuilder _log = new StringBuilder();

        void Start()
        {
            if (_runOnStart) Run().Forget();
        }

        public async UniTaskVoid Run()
        {
            _log.Clear();
            Write("== Boot 开始");

            // 1. 组装：业务只拿到 IAssetService，不接触 Addressables
            (IAssetService assets, AssetsModule module) = AddressablesAssets.Create();
            var config = new ConfigService(assets);

            // 诊断面板（F1 呼出）：直接在屏幕上看到租约与在途加载
            DiagnosticsOverlay.Install(assets);
            var scenes = new AddressablesSceneService();
            SceneLease sceneLease = null;
            CancellationToken ct = destroyCancellationToken;

            try
            {
                // 2. 初始化（加载 catalog）
                Write("初始化资源…");
                await module.InitializeAsync(ct);
                Write("初始化完成");

                // 3. 配置：常驻 + 类型校验
                if (!string.IsNullOrEmpty(_configAddress))
                {
                    try
                    {
                        UnityEngine.Object asset = await config.LoadAsync<UnityEngine.Object>(_configAddress, ct);
                        Write("配置加载: " + asset.name);
                    }
                    catch (Exception e)
                    {
                        Write("配置加载失败（样例可不配）: " + e.GetType().Name);
                    }
                }

                // 4. 场景（可选）
                if (!string.IsNullOrEmpty(_sceneAddress))
                {
                    sceneLease = await scenes.LoadAdditiveAsync(_sceneAddress, ct);
                    Write("场景已加载: " + sceneLease.Key);
                }

                Write("== Boot 完成，ActiveLeases=" + assets.ActiveLeases);
            }
            catch (Exception e)
            {
                Write("== Boot 失败: " + e.GetType().Name + " " + e.Message);
            }
            finally
            {
                // 5. 收尾：先还配置租约，再关模块
                try
                {
                    // 本样例加载后即验证收尾；场景必须先停止使用配置。
                    if (sceneLease != null) await sceneLease.UnloadAsync();
                }
                finally
                {
                    await config.DisposeAsync();
                    await module.ShutdownAsync(CancellationToken.None);
                }

                Write("== 收尾完成，ActiveLeases=" + assets.ActiveLeases);
            }
        }

        void Write(string line)
        {
            _log.AppendLine(line);
            Debug.Log("[Boot] " + line);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 1200, 400), _log.ToString());
        }
    }
}
