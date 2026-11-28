using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Core;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>仅主线程。注入已初始化的资源服务；显式 await DisposeAsync 成功后才能释放项目 Scope。</summary>
    public sealed class UiModule : IAppModule
    {
        readonly UiRoot _prefab;
        readonly UiRoot _rootInstance;
        readonly Transform _parent;
        readonly UnityEngine.EventSystems.EventSystem _eventSystem;
        readonly UnityUiViewFactory _factory;
        readonly ILog _log;
        readonly Action<GameObject> _destroy;
        readonly Func<UniTask> _frameWaiter;
        UiRoot _root;
        GameObject _staging;
        UniTaskCompletionSource _disposing;
        bool _initialized;
        bool _listeningForLowMemory;
        bool _lowMemoryTrimRequested;
        UniTaskCompletionSource _lowMemoryTrim;

        /// <summary>创建 UI 模块：根预制体、面板定义与默认过渡。</summary>
        public UiModule(IAssetService assets, UiRoot rootPrefab, IEnumerable<UiDefinition> definitions,
            IUiTransition transition = null, UnityEngine.EventSystems.EventSystem eventSystem = null,
            Transform parent = null, ILog log = null, Func<UniTask> frameWaiter = null, Action<GameObject> destroy = null,
            UiCacheOptions cacheOptions = null, UiRoot rootInstance = null)
        {
            MainThreadGuard.Assert("UiModule.ctor");
            if (rootPrefab == null && rootInstance == null) throw new ArgumentNullException(nameof(rootPrefab));
            _prefab = rootPrefab;
            _rootInstance = rootInstance;
            _parent = parent; _eventSystem = eventSystem; _log = log ?? Logs.Null;
            _destroy = destroy ?? (go => UnityEngine.Object.Destroy(go));
            _frameWaiter = frameWaiter;
            _factory = new UnityUiViewFactory(assets, frameWaiter: frameWaiter, destroy: _destroy);
            Service = new UiService(_factory, definitions, transition, startReady: false, cacheOptions: cacheOptions);
        }

        /// <summary>模块 ID。</summary>
        public string Id => "ui";
        /// <summary>不依赖其他模块。</summary>
        public IReadOnlyList<string> DependsOn => Array.Empty<string>();
        /// <summary>UI 服务入口。</summary>
        public UiService Service { get; }
        /// <summary>运行时创建的根节点。</summary>
        public UiRoot Root => _root;

        /// <summary>实例化根预制体并创建服务。</summary>
        public async UniTask InitializeAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("UiModule.InitializeAsync");
            if (_initialized || _disposing != null) throw new InvalidOperationException("UiModule 不能重复初始化");
            try
            {
                ct.ThrowIfCancellationRequested();
                if (_rootInstance != null)
                {
                    _root = _rootInstance;
                }
                else
                {
                    _staging = new GameObject("UI Root Staging"); _staging.SetActive(false);
                    _root = UnityEngine.Object.Instantiate(_prefab, _staging.transform, false);
                }
                if (_eventSystem != null) _root.SetEventSystem(_eventSystem);
                _root.Validate();
                ct.ThrowIfCancellationRequested();
                if (_root.transform.parent != _parent) _root.transform.SetParent(_parent, false);
                _root.gameObject.SetActive(true);
                _factory.AttachRoot(_root);
                Service.AttachPresentation(_root, _log);
                _initialized = true;
            }
            catch (Exception error)
            {
                try { await DisposeAsync(); }
                catch (Exception cleanupError) { throw new AggregateException("UI 初始化和回滚均失败", error, cleanupError); }
                throw;
            }
        }

        /// <summary>启动服务并注册低内存回调。</summary>
        public UniTask StartAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("UiModule.StartAsync");
            ct.ThrowIfCancellationRequested();
            if (!_initialized) throw new InvalidOperationException("UiModule 未初始化");
            Service.StartService();
            if (!_listeningForLowMemory)
            {
                Application.lowMemory += OnLowMemory;
                _listeningForLowMemory = true;
            }
            return UniTask.CompletedTask;
        }

        void OnLowMemory()
        {
            if (!_listeningForLowMemory) return;
            if (_lowMemoryTrim != null)
            {
                _lowMemoryTrimRequested = true;
                return;
            }
            TrimForLowMemoryAsync().Forget(error =>
            {
                if (_log.IsEnabled(LogLevel.Error)) _log.Write(LogLevel.Error, "UI", "低内存缓存清理失败: " + error);
            });
        }

        /// <summary>处理低内存信号，也可由测试宿主主动触发；只回收已关闭的缓存面板。</summary>
        public UniTask TrimForLowMemoryAsync()
        {
            MainThreadGuard.Assert();
            if (!_listeningForLowMemory) return UniTask.CompletedTask;
            if (_lowMemoryTrim != null)
            {
                _lowMemoryTrimRequested = true;
                return _lowMemoryTrim.Task;
            }
            var completion = new UniTaskCompletionSource();
            _lowMemoryTrim = completion;
            TrimForLowMemoryCoreAsync(completion).Forget();
            return completion.Task;
        }

        async UniTask TrimForLowMemoryCoreAsync(UniTaskCompletionSource completion)
        {
            Exception error;
            do
            {
                _lowMemoryTrimRequested = false;
                error = null;
                try { await Service.TrimCacheAsync(); }
                catch (Exception failure) { error = failure; }
            } while (_listeningForLowMemory && _lowMemoryTrimRequested);
            _lowMemoryTrim = null;
            if (error == null) completion.TrySetResult();
            else completion.TrySetException(error);
        }

        /// <summary>关闭服务并销毁根节点。</summary>
        public async UniTask ShutdownAsync(CancellationToken ct)
        {
            try { await DisposeAsync(); }
            catch (Exception error)
            {
                if (_log.IsEnabled(LogLevel.Error)) _log.Write(LogLevel.Error, "UI", "模块清理未完成；须显式重试 UiModule.DisposeAsync: " + error);
            }
        }

        /// <summary>释放模块持有的资源。</summary>
        public UniTask DisposeAsync()
        {
            MainThreadGuard.Assert("UiModule.DisposeAsync");
            if (_disposing != null) return _disposing.Task;
            if (_listeningForLowMemory)
            {
                Application.lowMemory -= OnLowMemory;
                _listeningForLowMemory = false;
            }
            var completion = new UniTaskCompletionSource(); _disposing = completion;
            DisposeCoreAsync(completion).Forget();
            return completion.Task;
        }

        async UniTask DisposeCoreAsync(UniTaskCompletionSource completion)
        {
            try
            {
                await Service.ShutdownAsync();
                await _factory.DisposeAsync();
                if (_root != null) _destroy(_root.gameObject);
                if (!await UnityObjectLifecycle.WaitDestroyedAsync(_root, frameWaiter: _frameWaiter)) throw new TimeoutException("UI 根未销毁");
                _root = null;
                if (_staging != null) _destroy(_staging);
                if (!await UnityObjectLifecycle.WaitDestroyedAsync(_staging, frameWaiter: _frameWaiter)) throw new TimeoutException("UI root staging 未销毁");
                _staging = null;
                completion.TrySetResult();
            }
            catch (Exception error) { _disposing = null; completion.TrySetException(error); }
        }
    }
}
