using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Threading;
using NowFrameWork.Core.Utility;

namespace NowFrameWork.UI
{
    public enum UiState
    {
        Opening = 0,
        Open = 1,
        Closing = 2,
        Closed = 3,
        Faulted = 4,
    }

    /// <summary>
    /// 仅主线程的 UI-1 服务；服务拥有实例和清理任务，调用者只拥有等待。
    /// 单实例在打开、关闭或清理失败期间保留预留；失败资源由 Close/Shutdown 重试。
    /// 无每帧热路径。动画反向、导航和缓存仍待后续实现。
    /// </summary>
    public sealed class UiService
    {
        sealed class UiSession
        {
            public int Id;
            public UiDefinition Definition;
            public UiState State;
            public UiView View;
            public UiHandle Handle;
            public bool Visible;
            public bool Faulted;
            public bool DestroyRequested;
            public CancellationTokenSource OpeningCancellation;
            public readonly UniTaskCompletionSource OpeningFinished = new UniTaskCompletionSource();
            public UniTaskCompletionSource Closing;
            public UniTaskCompletionSource TransitionFinished;
        }

        static int _ownerSeed;
        readonly int _owner;
        readonly IUiViewFactory _factory;
        readonly IUiTransition _defaultTransition;
        readonly Dictionary<int, UiDefinition> _definitions = new Dictionary<int, UiDefinition>(16);
        readonly Dictionary<int, UiSession> _sessions = new Dictionary<int, UiSession>(16);
        int _nextSessionId;
        int _sessionCounter;
        int _openingCount;
        int _visibleCount;
        int _faultedCount;
        bool _shuttingDown;
        UniTaskCompletionSource _shutdown;

        public UiService(IUiViewFactory factory, IEnumerable<UiDefinition> definitions, IUiTransition defaultTransition = null)
        {
            MainThreadGuard.Assert("UiService.ctor");
            _factory = Guard.NotNull(factory, nameof(factory));
            _defaultTransition = defaultTransition ?? ImmediateUiTransition.Instance;
            _owner = Interlocked.Increment(ref _ownerSeed);
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (UiDefinition definition in definitions)
            {
                if (definition == null) throw new ArgumentException("定义集合包含 null", nameof(definitions));
                Guard.Positive(definition.Id, nameof(definitions));
                if (_definitions.ContainsKey(definition.Id))
                    throw new ArgumentException("界面 Id 重复: " + definition.Id, nameof(definitions));
                _definitions.Add(definition.Id, definition);
            }
        }

        public int OpeningCount { get { MainThreadGuard.Assert(); return _openingCount; } }
        public int VisibleCount { get { MainThreadGuard.Assert(); return _visibleCount; } }
        public int FaultedCount { get { MainThreadGuard.Assert(); return _faultedCount; } }
        public int SessionCount { get { MainThreadGuard.Assert(); return _sessions.Count; } }

        public UniTask<UiOpenResult> OpenAsync<TArgs>(UiKey<TArgs> key, TArgs args, CancellationToken ct = default)
            where TArgs : class
        {
            MainThreadGuard.Assert("UiService.OpenAsync");
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (args == null) throw new ArgumentNullException(nameof(args));
            ct.ThrowIfCancellationRequested();
            if (_shuttingDown) throw new InvalidOperationException("UI 服务已开始关闭");
            if (!_definitions.TryGetValue(key.Id, out UiDefinition definition))
                throw new InvalidOperationException("未注册的界面: " + key);
            if (!definition.MultiInstance)
            {
                foreach (UiSession existing in _sessions.Values)
                {
                    if (existing.Definition.Id != definition.Id) continue;
                    if (existing.State == UiState.Open)
                        return UniTask.FromResult(new UiOpenResult(UiOpenStatus.AlreadyOpen, existing.Handle));
                    return UniTask.FromResult(new UiOpenResult(UiOpenStatus.Busy, default));
                }
            }
            return OpenCoreAsync(definition, ct);
        }

        async UniTask<UiOpenResult> OpenCoreAsync(UiDefinition definition, CancellationToken ct)
        {
            var session = new UiSession
            {
                Id = ++_nextSessionId,
                Definition = definition,
                State = UiState.Opening,
                OpeningCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct),
            };
            session.Handle = new UiHandle(_owner, session.Id, ++_sessionCounter);
            _sessions.Add(session.Id, session);
            _openingCount++;
            CancellationToken openingToken = session.OpeningCancellation.Token;
            try
            {
                session.View = await _factory.CreateAsync(definition, null, openingToken);
                openingToken.ThrowIfCancellationRequested();
                if (session.View == null) throw new InvalidOperationException("UI 工厂返回空视图: " + definition);
                session.View.UiId = definition.Id;
                session.View.Session = session.Handle.Session;
                await _defaultTransition.ShowAsync(session.View, openingToken);
                openingToken.ThrowIfCancellationRequested();
                session.State = UiState.Open;
                SetVisible(session, true);
                return new UiOpenResult(UiOpenStatus.Ok, session.Handle);
            }
            catch (Exception error)
            {
                try { await DiscardAsync(session); }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("UI 打开失败且清理未完成: " + definition, error, cleanupError);
                }
                throw;
            }
            finally
            {
                _openingCount--;
                session.OpeningCancellation.Dispose();
                session.OpeningCancellation = null;
                session.OpeningFinished.TrySetResult();
            }
        }

        public UniTask ShowAsync(UiHandle handle, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiService.ShowAsync");
            return ChangeVisibilityAsync(handle, true, ct);
        }

        public UniTask HideAsync(UiHandle handle, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiService.HideAsync");
            return ChangeVisibilityAsync(handle, false, ct);
        }

        async UniTask ChangeVisibilityAsync(UiHandle handle, bool visible, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryGet(handle, out UiSession session) || session.State != UiState.Open) return;
            // UI-1 暂不反向播放；在旧回调退出前拒绝同对象的新过渡，避免并发写属性。
            if (session.TransitionFinished != null) throw new InvalidOperationException("UI 过渡忙: " + handle);
            var finished = new UniTaskCompletionSource();
            session.TransitionFinished = finished;
            try
            {
                if (visible) await _defaultTransition.ShowAsync(session.View, ct);
                else await _defaultTransition.HideAsync(session.View, ct);
                ct.ThrowIfCancellationRequested();
                if (session.State == UiState.Open) SetVisible(session, visible);
            }
            finally
            {
                session.TransitionFinished = null;
                finished.TrySetResult();
            }
        }

        /// <summary>取消仅结束本次等待；服务持有的清理仍继续。失败后可再次调用。</summary>
        public UniTask CloseAsync(UiHandle handle, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiService.CloseAsync");
            ct.ThrowIfCancellationRequested();
            if (!TryGet(handle, out UiSession session)) return UniTask.CompletedTask;
            if (session.Closing == null)
            {
                var completion = new UniTaskCompletionSource();
                session.Closing = completion;
                // 泵内部捕获所有异常并交给 completion；不会把清理错误丢给静态调度器。
                CloseCoreAsync(session, completion).Forget();
                return completion.Task.AttachExternalCancellation(ct);
            }
            return session.Closing.Task.AttachExternalCancellation(ct);
        }

        async UniTask CloseCoreAsync(UiSession session, UniTaskCompletionSource completion)
        {
            try
            {
                session.State = UiState.Closing;
                SetVisible(session, false);
                Exception cancelError = null;
                try { session.OpeningCancellation?.Cancel(); }
                catch (Exception error) { cancelError = error; }
                await session.OpeningFinished.Task;
                if (session.TransitionFinished != null) await session.TransitionFinished.Task;
                if (session.State != UiState.Closed) await DiscardAsync(session);
                if (cancelError != null) throw cancelError;
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                if (session.State != UiState.Closed) MarkFaulted(session);
                session.Closing = null;
                completion.TrySetException(error);
            }
        }

        async UniTask DiscardAsync(UiSession session)
        {
            try
            {
                // Unity 伪空只代表对象已销毁，不代表工厂已经归还租约。
                if (!ReferenceEquals(session.View, null))
                {
                    if (!session.DestroyRequested)
                    {
                        _factory.Destroy(session.View);
                        session.DestroyRequested = true;
                    }
                    if (!await _factory.ReleaseAsync(session.View))
                        throw new TimeoutException("UI 实例未销毁，保留资源与会话供重试: " + session.Handle);
                    session.View = null;
                }
                SetVisible(session, false);
                if (session.Faulted) { session.Faulted = false; _faultedCount--; }
                session.State = UiState.Closed;
                _sessions.Remove(session.Id);
            }
            catch
            {
                MarkFaulted(session);
                throw;
            }
        }

        /// <summary>拒绝新打开，取消并等待在途打开，逐项清理。失败保留记录，再次调用可重试。</summary>
        public UniTask ShutdownAsync()
        {
            MainThreadGuard.Assert("UiService.ShutdownAsync");
            if (_shutdown != null) return _shutdown.Task;
            _shuttingDown = true;
            var completion = new UniTaskCompletionSource();
            _shutdown = completion;
            ShutdownCoreAsync(completion).Forget();
            return completion.Task;
        }

        async UniTask ShutdownCoreAsync(UniTaskCompletionSource completion)
        {
            var snapshot = new List<UiSession>(_sessions.Values);
            var pending = new List<UniTask>(snapshot.Count);
            var errors = new List<Exception>();
            // 先启动所有清理，使所有未提交的打开立即收到取消，再等待各自收尾。
            foreach (UiSession session in snapshot)
            {
                try { pending.Add(CloseAsync(session.Handle)); }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (UniTask task in pending)
            {
                try { await task; }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count == 0) completion.TrySetResult();
            else
            {
                _shutdown = null;
                completion.TrySetException(new AggregateException("UI 服务清理未完成", errors));
            }
        }

        void SetVisible(UiSession session, bool visible)
        {
            if (session.Visible == visible) return;
            session.Visible = visible;
            _visibleCount += visible ? 1 : -1;
        }

        void MarkFaulted(UiSession session)
        {
            session.State = UiState.Faulted;
            if (session.Faulted) return;
            session.Faulted = true;
            _faultedCount++;
        }

        bool TryGet(UiHandle handle, out UiSession session)
        {
            session = null;
            return handle.IsValid && handle.Owner == _owner
                && _sessions.TryGetValue(handle.Id, out session) && session.Handle == handle;
        }
    }
}
