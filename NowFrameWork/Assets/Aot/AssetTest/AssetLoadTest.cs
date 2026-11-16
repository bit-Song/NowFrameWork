using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using NowFrameWork.Core;
using NowFrameWork.Core.Diagnostics;
using NowFrameWork.Core.Logging;
using NowFrameWork.Unity;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement;
using UnityEngine.SceneManagement;
using Logger = NowFrameWork.Core.Logging.Logger;

namespace NowFrameWork.AssetTest
{
    /// <summary>PC Player 回归入口。夹具由 Editor 菜单生成；支持命令行和屏幕报告，不含业务流程。</summary>
    public sealed class AssetLoadTest : MonoBehaviour
    {
        [Serializable] sealed class CaseResult
        {
            public string name;
            public bool passed;
            public double seconds;
            public string detail;
        }
        [Serializable] sealed class Report
        {
            public string utc;
            public string unity;
            public string mode;
            public string marker;
            public bool finished;
            public bool passed;
            public int activeLeases;
            public int pendingRequests;
            public int errorCount;
            public List<CaseResult> cases = new List<CaseResult>();
        }
        readonly StringBuilder _screen = new StringBuilder();
        Report _report;
        string _reportPath;
        bool _running;
        AssetService _assets;
        ErrorBudget _errors;
        Logger _log;
        CancellationToken _ct;

        void Start()
        {
            Application.runInBackground = true;
            RunAsync(Arg("--nw-mode", "suite")).Forget();
        }

        async UniTask RunAsync(string mode)
        {
            if (_running) return;
            _running = true;
            _ct = destroyCancellationToken;
            _screen.Clear();
            _reportPath = Arg("--nw-report", Path.Combine(Application.persistentDataPath, "nw-pc-regression.json"));
            _report = new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, mode = mode };
            _log = new Logger(LogLevel.Info, 64);
            _errors = new ErrorBudget();
            _log.AddSink(_errors);
            _assets = new AssetService(new AddressablesProvider(TimeSpan.FromSeconds(5)), _log);
            var module = new AssetsModule(_assets, _log);
            var previousHandler = ResourceManager.ExceptionHandler;
            Cache previousCache = Caching.currentCacheForWriting;
            ResourceManager.ExceptionHandler = (_, e) => _log.Write(LogLevel.Error, "PROBE-NETWORK", e.Message);
            try
            {
                ConfigureCache();
                await CaseAsync("INIT", () => module.InitializeAsync(_ct));
                if (_report.cases[0].passed) await RunModeAsync(mode);
                await CaseAsync("CLEANUP", async () =>
                {
                    await WaitUntilAsync(() => _assets.PendingRequests == 0, 10);
                    Check(_assets.ActiveLeases == 0, "残留租约=" + _assets.ActiveLeases);
                    await module.ShutdownAsync(CancellationToken.None);
                });
            }
            catch (Exception e)
            {
                _report.cases.Add(new CaseResult { name = "RUNNER", detail = e.ToString() });
                Write("FAIL RUNNER: " + e.Message);
            }
            finally
            {
                ResourceManager.ExceptionHandler = previousHandler;
                if (previousCache.valid) Caching.currentCacheForWriting = previousCache;
                _report.activeLeases = _assets.ActiveLeases;
                _report.pendingRequests = _assets.PendingRequests;
                _report.errorCount = _errors.TotalCount;
                _report.finished = true;
                _report.passed = _report.cases.Count > 0 && _report.cases.TrueForAll(c => c.passed);
                _running = false;
                Save();
                Write("RESULT=" + (_report.passed ? "PASS" : "FAIL") + " report=" + _reportPath);
                if (HasArg("--nw-quit")) Application.Quit(_report.passed ? 0 : 2);
            }
        }

        async UniTask RunModeAsync(string mode)
        {
            switch (mode)
            {
                case "suite":
                    await CaseAsync("A-LEASE", LeaseAsync);
                    await CaseAsync("B-POOL", PoolAsync);
                    await CaseAsync("C-SCOPE", ScopeAsync);
                    await CaseAsync("D-CONFIG", ConfigAsync);
                    await CaseAsync("E-SCENE", SceneAsync);
                    await CaseAsync("F-ATLAS", AtlasAsync);
                    await CaseAsync("G-POOL-TIMEOUT", PoolTimeoutAsync);
                    await CaseAsync("H-MISSING", MissingAsync);
                    break;
                case "hot": await CaseAsync("HOT-DLL", HotAsync); break;
                case "offline": await CaseAsync("OFFLINE", OfflineAsync); break;
                case "remote-cancel": await CaseAsync("REMOTE-CANCEL", RemoteCancelAsync); break;
                case "remote-timeout": await CaseAsync("REMOTE-TIMEOUT-RETRY", RemoteTimeoutAsync); break;
                case "scene-cancel": await CaseAsync("SCENE-CANCEL", SceneCancelAsync); break;
                default: throw new ArgumentException("未知回归模式: " + mode);
            }
        }

        async UniTask CaseAsync(string name, Func<UniTask> body)
        {
            double start = Time.realtimeSinceStartupAsDouble;
            var result = new CaseResult { name = name };
            _report.cases.Add(result);
            try { await body(); result.passed = true; result.detail = "OK"; }
            catch (Exception e) { result.detail = e.ToString(); }
            result.seconds = Time.realtimeSinceStartupAsDouble - start;
            Write((result.passed ? "PASS " : "FAIL ") + name + " " + result.seconds.ToString("F2") + "s " + result.detail);
            Save();
        }

        async UniTask LeaseAsync()
        {
            AssetLease<GameObject> a = null, b = null;
            try
            {
                var pending = _assets.AcquireAsync<GameObject>(PcProbeAssets.Prefab, _ct);
                var other = _assets.AcquireAsync<GameObject>(PcProbeAssets.Prefab, _ct);
                a = await pending;
                b = await other;
                Check(a.Asset == b.Asset && _assets.ActiveLeases == 2, "双租约未共享资源");
                a.Dispose();
                Check(b.Asset != null && _assets.ActiveLeases == 1, "提前卸载仍被使用的资源");
            }
            finally { a?.Dispose(); b?.Dispose(); }
            Check(_assets.ActiveLeases == 0, "租约未归零");
        }

        async UniTask PoolAsync()
        {
            AssetPool<GameObject> pool = null;
            using (var prefab = await _assets.AcquireAsync<GameObject>(PcProbeAssets.Prefab, _ct))
            {
                GameObject source = prefab.Asset;
                pool = await AssetPool<GameObject>.CreateAsync(_assets, PcProbeAssets.Prefab,
                    () => Instantiate(source), UnityEngine.Object.Destroy, new UnityClock(), ct: _ct);
            }
            try
            {
                Check(_assets.ActiveLeases == 1, "保活租约不正确");
                GameObject a = pool.Rent();
                GameObject b = await pool.RentAsync(_ct);
                pool.Release(a);
                Check(pool.ActiveCount == 1 && pool.InactiveCount == 1, "池计数不正确");
                await pool.DisposeAsync();
                Check(a == null && b == null, "实例未实际销毁");
                Check(_assets.ActiveLeases == 0, "池保活租约未归还");
            }
            finally { await pool.DisposeAsync(); }
        }

        sealed class ScopeModule : IAppModule
        {
            readonly AssetService _service;
            AssetLease<GameObject> _lease;
            GameObject _instance;
            public ScopeModule(AssetService service) { _service = service; }
            public string Id => "pc-scope-probe";
            public IReadOnlyList<string> DependsOn => Array.Empty<string>();
            public async UniTask InitializeAsync(CancellationToken ct) { _lease = await _service.AcquireAsync<GameObject>(PcProbeAssets.Prefab, ct); }
            public UniTask StartAsync(CancellationToken ct) { _instance = Instantiate(_lease.Asset); return UniTask.CompletedTask; }
            public async UniTask ShutdownAsync(CancellationToken ct)
            {
                if (_instance != null) UnityEngine.Object.Destroy(_instance);
                while (_instance != null) await UniTask.NextFrame();
                _lease?.Dispose();
            }
        }

        async UniTask ScopeAsync()
        {
            var root = new Scope("pc-root", null, new UnityClock(), _log);
            root.Modules.Register(new ScopeModule(_assets));
            try
            {
                await root.StartupAsync(_ct);
                var child = root.CreateChild("pc-child");
                child.Modules.Register(new ScopeModule(_assets));
                await child.StartupAsync(_ct);
                Check(_assets.ActiveLeases == 2, "Scope 未持有资源");
                var a = root.DisposeAsync();
                var b = root.DisposeAsync();
                await a;
                await b;
                Check(root.State == ScopeState.Stopped && child.State == ScopeState.Stopped, "Scope 未收敛");
                Check(_assets.ActiveLeases == 0, "Scope 关闭后残留租约");
            }
            finally { await root.DisposeAsync(); }
        }

        async UniTask ConfigAsync()
        {
            var config = new ConfigService(_assets);
            try
            {
                var a = config.LoadAsync<TextAsset>(PcProbeAssets.Config, _ct);
                var b = config.LoadAsync<TextAsset>(PcProbeAssets.Config, _ct);
                Check(await a == await b, "配置未复用");
                Check(_assets.ActiveLeases == 1, "配置重复持有租约");
                bool rejected = false;
                try { await config.LoadAsync<GameObject>(PcProbeAssets.Config, _ct); }
                catch (InvalidCastException) { rejected = true; }
                Check(rejected && _assets.ActiveLeases == 1, "配置类型错误清理失败");
            }
            finally { await config.DisposeAsync(); }
            Check(_assets.ActiveLeases == 0, "配置关闭后残留租约");
        }

        async UniTask SceneAsync()
        {
            int before = SceneManager.sceneCount;
            var scenes = new AddressablesSceneService(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 3; i++)
            {
                SceneLease lease = await scenes.LoadAdditiveAsync(PcProbeAssets.Scene, _ct);
                try { Check(SceneManager.sceneCount == before + 1, "附加场景未加载"); }
                finally
                {
                    var a = lease.UnloadAsync();
                    var b = lease.UnloadAsync();
                    await a;
                    await b;
                }
                Check(lease.IsUnloaded && SceneManager.sceneCount == before, "场景卸载不完整");
            }
        }

        async UniTask AtlasAsync()
        {
            AssetLease<Sprite> red = null, blue = null;
            try
            {
                red = await _assets.AcquireAsync<Sprite>(new AssetKey(PcProbeAssets.Atlas, "ProbeRed"), _ct);
                blue = await _assets.AcquireAsync<Sprite>(new AssetKey(PcProbeAssets.Atlas, "ProbeBlue"), _ct);
                Check(red.Asset != blue.Asset && red.CanonicalKey != blue.CanonicalKey, "不同 Sprite 混为同一资源");
                red.Dispose();
                Check(blue.Asset != null, "释放一个 Sprite 导致另一个失效");
            }
            finally { red?.Dispose(); blue?.Dispose(); }
        }

        async UniTask PoolTimeoutAsync()
        {
            AssetPool<GameObject> pool;
            using (var prefab = await _assets.AcquireAsync<GameObject>(PcProbeAssets.Prefab, _ct))
            {
                GameObject source = prefab.Asset;
                pool = await AssetPool<GameObject>.CreateAsync(_assets, PcProbeAssets.Prefab,
                    () => Instantiate(source), _ => { }, new UnityClock(), settleFrames: 2, ct: _ct);
            }
            GameObject instance = pool.Rent();
            try
            {
                bool timedOut = false;
                try { await pool.DisposeAsync(); }
                catch (TimeoutException) { timedOut = true; }
                Check(timedOut && instance != null && _assets.ActiveLeases == 1, "超时提前释放保活租约");
            }
            finally
            {
                Destroy(instance);
                await UniTask.NextFrame();
                await pool.DisposeAsync();
            }
            Check(_assets.ActiveLeases == 0, "超时后重试清理失败");
        }

        async UniTask MissingAsync()
        {
            bool rejected = false;
            try { using (await _assets.AcquireAsync<TextAsset>("PCProbe/DoesNotExist", _ct)) { } }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && _assets.ActiveLeases == 0 && _assets.PendingRequests == 0, "缺失资源失败路径不正确");
        }

        async UniTask HotAsync()
        {
            Check(!Application.isEditor, "DLL 热更必须在 Player 验证");
            foreach (Assembly existing in AppDomain.CurrentDomain.GetAssemblies())
                Check(existing.GetName().Name != "HotUpdate", "热更程序集已经被加载，须重启 Player");
            var updater = new HotUpdateService(_assets, _log);
            HotUpdatePlan plan = await updater.PrepareAsync(PcProbeAssets.Dll, _ct);
            Write("CatalogChanged=" + plan.HasUpdate + " downloadBytes=" + plan.DownloadBytes);
            await updater.DownloadAsync(PcProbeAssets.Dll, null, _ct);
            using (var dll = await _assets.AcquireAsync<TextAsset>(PcProbeAssets.Dll, _ct))
            {
                Assembly assembly = Assembly.Load(dll.Asset.bytes);
                MethodInfo entry = assembly.GetType("Hello", true).GetMethod("GetProbeMarker", BindingFlags.Public | BindingFlags.Static);
                Check(entry != null, "DLL 缺少 GetProbeMarker；请重新编译热更程序集");
                _report.marker = entry.Invoke(null, null) as string;
                Check(_report.marker == Arg("--nw-expect", "PC-PROBE-V1"), "补丁标记不匹配: " + _report.marker);
                Write("DLL=" + assembly.FullName + " marker=" + _report.marker);
            }
        }

        async UniTask OfflineAsync()
        {
            double start = Time.realtimeSinceStartupAsDouble;
            bool failed = false;
            try { await new HotUpdateService(_assets, _log).PrepareAsync(PcProbeAssets.Dll, _ct); }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                failed = true;
                _log.Write(LogLevel.Error, "PROBE-OFFLINE", e.Message);
            }
            Check(failed && Time.realtimeSinceStartupAsDouble - start < 8, "离线检查未快速失败");
            Check(_errors.TotalCount > 0 && _assets.ActiveLeases == 0, "离线失败未记账或残留租约");
            // 远端不可达也必须能继续使用包内夹具。
            using (var local = await _assets.AcquireAsync<TextAsset>(PcProbeAssets.Config, _ct))
                Check(local.Asset != null, "离线本地资源不可用");
        }

        async UniTask RemoteCancelAsync()
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_ct))
            {
                var pending = _assets.AcquireAsync<TextAsset>(PcProbeAssets.RemoteData, cts.Token);
                await UniTask.NextFrame();
                cts.Cancel();
                bool cancelled = false;
                try { using (await pending) { } }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "没有覆盖到在途请求，须使用全新缓存和延迟服务器");
                await WaitUntilAsync(() => _assets.PendingRequests == 0, 10);
                Check(_assets.ActiveLeases == 0, "取消后残留租约");
            }
            using (var retry = await _assets.AcquireAsync<TextAsset>(PcProbeAssets.RemoteData, _ct))
                Check(retry.Asset.bytes.Length > 1000, "取消后重试失败");
        }

        async UniTask RemoteTimeoutAsync()
        {
            bool timedOut = false;
            try { using (await _assets.AcquireAsync<TextAsset>(PcProbeAssets.RemoteData, _ct)) { } }
            catch (TimeoutException) { timedOut = true; }
            Check(timedOut && _assets.ActiveLeases == 0 && _assets.PendingRequests == 0, "超时清理不完整");
            using (var reset = UnityWebRequest.Get(PcProbeAssets.Control + "?delayMs=0"))
            {
                await reset.SendWebRequest();
                Check(reset.result == UnityWebRequest.Result.Success, "测试服务器复位失败");
            }
            using (var retry = await _assets.AcquireAsync<TextAsset>(PcProbeAssets.RemoteData, _ct))
                Check(retry.Asset != null, "超时后同一服务重试失败");
        }

        async UniTask SceneCancelAsync()
        {
            int before = SceneManager.sceneCount;
            var scenes = new AddressablesSceneService(TimeSpan.FromSeconds(5));
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_ct))
            {
                var pending = scenes.LoadAdditiveAsync(PcProbeAssets.RemoteScene, cts.Token);
                await UniTask.NextFrame();
                cts.Cancel();
                bool cancelled = false;
                try { var lease = await pending; await lease.UnloadAsync(); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "未覆盖到场景在途加载");
                await UniTask.Delay(TimeSpan.FromSeconds(2), ignoreTimeScale: true, cancellationToken: _ct);
                Check(SceneManager.sceneCount == before, "晚到场景未自动卸载");
            }
            SceneLease retry = await scenes.LoadAdditiveAsync(PcProbeAssets.RemoteScene, _ct);
            await retry.UnloadAsync();
            Check(SceneManager.sceneCount == before, "场景重试后残留");
        }

        async UniTask WaitUntilAsync(Func<bool> condition, double seconds)
        {
            double start = Time.realtimeSinceStartupAsDouble;
            while (!condition())
            {
                _ct.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartupAsDouble - start > seconds) throw new TimeoutException("回归等待超时");
                await UniTask.NextFrame();
            }
        }

        void ConfigureCache()
        {
            string path = Arg("--nw-cache", "");
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(path);
            Cache cache = Caching.GetCacheByPath(path);
            if (!cache.valid) cache = Caching.AddCache(path);
            Check(cache.valid, "无法创建隔离缓存: " + path);
            Caching.currentCacheForWriting = cache;
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static string Arg(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return fallback;
        }
        static bool HasArg(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;
        void Write(string text) { _screen.AppendLine(text); Debug.Log("[PCRegression] " + text); }
        void Save()
        {
            string fullPath = Path.GetFullPath(_reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonUtility.ToJson(_report, true), Encoding.UTF8);
        }
        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, Screen.width - 20, Screen.height - 80), _screen.ToString());
            if (!_running && GUI.Button(new Rect(10, Screen.height - 60, 160, 40), "重跑基础回归")) RunAsync("suite").Forget();
        }
    }
}
