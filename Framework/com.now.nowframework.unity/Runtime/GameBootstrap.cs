using System;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core;
using UnityEngine;

namespace NowFrameWork.Unity
{
    /// <summary>
    /// 场景入口：挂在 Boot 场景的空物体上（菜单 NowFrameWork/创建 Boot 场景 可一键生成）。
    ///
    /// 职责边界：只做「创建 App → 注册模块 → 启动 → 转发 Unity 回调 → 关闭」，不含任何业务逻辑。
    /// 模块注册通过重写 <see cref="Configure"/> 完成（默认不注册任何模块）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] LogLevel _minLogLevel = LogLevel.Info;

        [Tooltip("近期日志环形缓冲容量；0 表示不保留（省内存）")]
        [SerializeField] int _recentLogCapacity = 256;

        [SerializeField] bool _dontDestroyOnLoad = true;

        public App App { get; private set; }

        /// <summary>启动失败后为 true，此时不再驱动帧回调。</summary>
        public bool StartupFailed { get; private set; }

        /// <summary>启动进度接收者（加载条/日志）；为空则忽略。</summary>
        public IStartupProgress StartupProgress { get; set; }

        void Awake() => Initialize();

        /// <summary>
        /// 装配 App 并调用 Configure。幂等；
        /// 抽成公开方法是为了能在 EditMode 用例里直接调用（编辑模式不会触发 Awake）。
        /// </summary>
        public void Initialize()
        {
            if (App != null) return;

            App = NowFrameWork.Unity.App.Create(_minLogLevel, _recentLogCapacity);

            // 编辑模式（用例、编辑器工具）下不调用，只有真正运行时才跨场景保留
            if (Application.isPlaying && _dontDestroyOnLoad && transform.parent == null) DontDestroyOnLoad(gameObject);

            Configure(App);
        }

        /// <summary>在这里 hub.Register(...)。派生类重写即可。</summary>
        protected virtual void Configure(App app) { }

        void Start() => RunStartupAsync().Forget();

        async UniTask RunStartupAsync()
        {
            try
            {
                await App.StartupAsync(destroyCancellationToken, StartupProgress);
            }
            catch (Exception e)
            {
                StartupFailed = true;
                App.Log.Write(LogLevel.Error, "C02", "启动失败: " + e.GetType().Name + ": " + e.Message);
            }
        }

        void Update()
        {
            if (App == null || StartupFailed) return;
            App.Tick(UnityEngine.Time.deltaTime, UnityEngine.Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            if (App == null || StartupFailed) return;
            App.LateTick();
        }

        void FixedUpdate()
        {
            if (App == null || StartupFailed) return;
            App.FixedTick(UnityEngine.Time.fixedDeltaTime);
        }

        void OnDestroy()
        {
            if (App == null) return;
            App.ShutdownAsync().Forget();
        }
    }
}