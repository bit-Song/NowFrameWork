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

        public UiModule(IAssetService assets, UiRoot rootPrefab, IEnumerable<UiDefinition> definitions,
            IUiTransition transition = null, UnityEngine.EventSystems.EventSystem eventSystem = null,
            Transform parent = null, ILog log = null, Func<UniTask> frameWaiter = null, Action<GameObject> destroy = null)
        {
            MainThreadGuard.Assert("UiModule.ctor");
            _prefab = rootPrefab != null ? rootPrefab : throw new ArgumentNullException(nameof(rootPrefab));
            _parent = parent; _eventSystem = eventSystem; _log = log ?? Logs.Null;
            _destroy = destroy ?? (go => UnityEngine.Object.Destroy(go));
            _frameWaiter = frameWaiter;
            _factory = new UnityUiViewFactory(assets, frameWaiter: frameWaiter, destroy: _destroy);
            Service = new UiService(_factory, definitions, transition, startReady: false);
        }

        public string Id => "ui";
        public IReadOnlyList<string> DependsOn => Array.Empty<string>();
        public UiService Service { get; }
        public UiRoot Root => _root;

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("UiModule.InitializeAsync");
            if (_initialized || _disposing != null) throw new InvalidOperationException("UiModule 不能重复初始化");
            try
            {
                ct.ThrowIfCancellationRequested();
                _staging = new GameObject("UI Root Staging"); _staging.SetActive(false);
                _root = UnityEngine.Object.Instantiate(_prefab, _staging.transform, false);
                if (_eventSystem != null) _root.SetEventSystem(_eventSystem);
                _root.Validate();
                ct.ThrowIfCancellationRequested();
                _root.transform.SetParent(_parent, false);
                _root.gameObject.SetActive(true);
                _factory.AttachRoot(_root);
                _initialized = true;
            }
            catch (Exception error)
            {
                try { await DisposeAsync(); }
                catch (Exception cleanupError) { throw new AggregateException("UI 初始化和回滚均失败", error, cleanupError); }
                throw;
            }
        }

        public UniTask StartAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("UiModule.StartAsync");
            ct.ThrowIfCancellationRequested();
            if (!_initialized) throw new InvalidOperationException("UiModule 未初始化");
            Service.StartService();
            return UniTask.CompletedTask;
        }

        public async UniTask ShutdownAsync(CancellationToken ct)
        {
            try { await DisposeAsync(); }
            catch (Exception error)
            {
                if (_log.IsEnabled(LogLevel.Error)) _log.Write(LogLevel.Error, "UI", "模块清理未完成；须显式重试 UiModule.DisposeAsync: " + error);
            }
        }

        public UniTask DisposeAsync()
        {
            MainThreadGuard.Assert("UiModule.DisposeAsync");
            if (_disposing != null) return _disposing.Task;
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
