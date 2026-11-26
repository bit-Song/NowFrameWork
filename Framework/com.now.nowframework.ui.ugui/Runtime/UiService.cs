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
    /// 显隐按操作版本提交，反向等待旧过渡退出；内置模态呈现、基础页面历史和有界缓存。
    /// </summary>
    public sealed partial class UiService : IUiService
    {
        sealed class UiSession
        {
            /// <summary>面板定义 ID。</summary>
            public int Id;
            /// <summary>面板定义。</summary>
            public UiDefinition Definition;
            /// <summary>打开/关闭状态。</summary>
            public UiState State;
            /// <summary>当前视图实例。</summary>
            public UiView View;
            /// <summary>本会话句柄。</summary>
            public UiHandle Handle;
            /// <summary>是否处于可见状态。</summary>
            public bool Visible;
            /// <summary>是否因异常失败。</summary>
            public bool Faulted;
            /// <summary>是否已请求销毁。</summary>
            public bool DestroyRequested;
            /// <summary>打开阶段取消源。</summary>
            public CancellationTokenSource OpeningCancellation;
            /// <summary>打开流程完成信号。</summary>
            public readonly UniTaskCompletionSource OpeningFinished = new UniTaskCompletionSource();
            /// <summary>关闭流程完成信号。</summary>
            public UniTaskCompletionSource Closing;
            /// <summary>进行中的显隐操作。</summary>
            public VisibilityOperation Transition;
            /// <summary>操作版本，用于丢弃过期结果。</summary>
            public long OperationVersion;
            public UiVisibilityState Visibility = UiVisibilityState.Hidden;
            /// <summary>打开顺序，用于层级排序。</summary>
            public long Order;
            /// <summary>本会话使用的过渡实现。</summary>
            public IUiTransition VisualTransition;
        }

        sealed class VisibilityOperation
        {
            public bool Visible;
            public long Version;
            /// <summary>本次显隐操作的取消源。</summary>
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            /// <summary>显隐操作完成信号。</summary>
            public readonly UniTaskCompletionSource Completion = new UniTaskCompletionSource();
            /// <summary>过渡收尾完成信号。</summary>
            public readonly UniTaskCompletionSource Finished = new UniTaskCompletionSource();
            /// <summary>本次操作的取消令牌。</summary>
            public CancellationToken Token;

            /// <summary>取消本次显隐操作。</summary>
            public Exception Cancel()
            {
                if (Finished.Task.Status != UniTaskStatus.Pending) return null;
                try { Cancellation.Cancel(); return null; }
                catch (Exception error) { return error; }
            }
        }

        static int _ownerSeed;
        readonly int _owner;
        readonly IUiViewFactory _factory;
        readonly IUiTransition _defaultTransition;
        readonly CancellationTokenSource _closeTransitions = new CancellationTokenSource();
        readonly Dictionary<int, UiDefinition> _definitions = new Dictionary<int, UiDefinition>(16);
        readonly Dictionary<int, UiSession> _sessions = new Dictionary<int, UiSession>(16);
        int _nextSessionId;
        int _sessionCounter;
        int _openingCount;
        int _visibleCount;
        int _faultedCount;
        bool _shuttingDown;
        bool _ready;
        UniTaskCompletionSource _shutdown;

        /// <summary>创建 UI 服务；工厂负责实例化，定义表决定面板。</summary>
        public UiService(IUiViewFactory factory, IEnumerable<UiDefinition> definitions, IUiTransition defaultTransition = null,
            bool startReady = true, UiCacheOptions cacheOptions = null)
        {
            MainThreadGuard.Assert("UiService.ctor");
            _factory = Guard.NotNull(factory, nameof(factory));
            _defaultTransition = defaultTransition ?? ImmediateUiTransition.Instance;
            _cacheOptions = cacheOptions ?? UiCacheOptions.Default;
            _ready = startReady;
            _owner = Interlocked.Increment(ref _ownerSeed);
            Navigation = new UiNavigator(this);
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (UiDefinition definition in definitions)
            {
                if (definition == null) throw new ArgumentException("定义集合包含 null", nameof(definitions));
                Guard.Positive(definition.Id, nameof(definitions));
                if (_definitions.ContainsKey(definition.Id))
                    throw new ArgumentException("界面 Id 重复: " + definition.Id, nameof(definitions));
                if (!Enum.IsDefined(typeof(UiLayer), definition.Layer) || !Enum.IsDefined(typeof(UiBackBehavior), definition.BackBehavior))
                    throw new ArgumentException("无效的 UI 层或返回行为: " + definition);
                if (definition.Modal && (definition.Layer != UiLayer.Popup || definition.BackBehavior == UiBackBehavior.Ignore))
                    throw new ArgumentException("模态窗口必须属于 Popup 层，且不能向下透传返回: " + definition);
                if (definition.CloseOnBackdrop && !definition.Modal) throw new ArgumentException("仅模态窗口可以配置遮罩关闭: " + definition);
                _definitions.Add(definition.Id, new UiDefinition(definition.Id, definition.Name, definition.Prefab)
                {
                    Layer = definition.Layer, MultiInstance = definition.MultiInstance, CacheOnClose = definition.CacheOnClose,
                    Modal = definition.Modal, CloseOnBackdrop = definition.CloseOnBackdrop, BackBehavior = definition.BackBehavior
                });
            }
        }

        /// <summary>正在打开的数量。</summary>
        public int OpeningCount { get { MainThreadGuard.Assert(); return _openingCount; } }
        /// <summary>当前可见数量。</summary>
        public int VisibleCount { get { MainThreadGuard.Assert(); return _visibleCount; } }
        /// <summary>失败的数量。</summary>
        public int FaultedCount { get { MainThreadGuard.Assert(); return _faultedCount; } }
        /// <summary>活动会话数。</summary>
        public int SessionCount { get { MainThreadGuard.Assert(); return _sessions.Count; } }

        /// <summary>打开面板；每次打开产生新句柄。</summary>
        public UniTask<UiOpenResult> OpenAsync<TArgs>(UiKey<TArgs> key, TArgs args, CancellationToken ct = default)
            where TArgs : class
        {
            MainThreadGuard.Assert("UiService.OpenAsync");
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (args == null) throw new ArgumentNullException(nameof(args));
            ct.ThrowIfCancellationRequested();
            if (_shuttingDown) throw new InvalidOperationException("UI 服务已开始关闭");
            if (!_ready) throw new InvalidOperationException("UI 模块尚未启动");
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
            int layerCount = 0;
            foreach (UiSession existing in _sessions.Values) if (existing.Definition.Layer == definition.Layer) layerCount++;
            if (layerCount >= 128) throw new InvalidOperationException("每层最多持有 128 个 UI 会话，请先关闭或重试清理。");
            return OpenCoreAsync(definition, args, ct);
        }

        async UniTask<UiOpenResult> OpenCoreAsync(UiDefinition definition, object args, CancellationToken ct)
        {
            var session = new UiSession
            {
                Id = ++_nextSessionId,
                Definition = definition,
                State = UiState.Opening,
                Order = ++_presentationOrder,
                OpeningCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct),
            };
            session.Handle = new UiHandle(_owner, session.Id, ++_sessionCounter);
            _sessions.Add(session.Id, session);
            _openingCount++;
            CancellationToken openingToken = session.OpeningCancellation.Token;
            try
            {
                session.View = TakeCachedView(definition.Id);
                if (session.View == null) session.View = await _factory.CreateAsync(definition, null, openingToken);
                openingToken.ThrowIfCancellationRequested();
                if (session.View == null) throw new InvalidOperationException("UI 工厂返回空视图: " + definition);
                session.View.UiId = definition.Id;
                session.View.Session = session.Handle.Session;
                session.View.PrepareHidden();
                await session.View.OpenSessionAsync(args, openingToken);
                session.VisualTransition = session.View.ResolveTransition(_defaultTransition);
                session.Visibility = UiVisibilityState.Showing;
                RefreshPresentation();
                session.View.BeginVisibility(true);
                await session.VisualTransition.ShowAsync(session.View, openingToken);
                openingToken.ThrowIfCancellationRequested();
                session.State = UiState.Open;
                SetVisible(session, true);
                session.Visibility = UiVisibilityState.Visible;
                session.View.CommitVisible();
                RefreshPresentation();
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

        /// <summary>显示指定句柄。</summary>
        public UniTask ShowAsync(UiHandle handle, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiService.ShowAsync");
            return ChangeVisibilityAsync(handle, true, ct);
        }

        /// <summary>隐藏指定句柄（保留会话）。</summary>
        public UniTask HideAsync(UiHandle handle, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiService.HideAsync");
            return ChangeVisibilityAsync(handle, false, ct);
        }

        internal UniTask RestorePageAsync(UiHandle handle, CancellationToken ct)
            => ChangeVisibilityAsync(handle, true, ct, raiseOnShow: false);

        UniTask ChangeVisibilityAsync(UiHandle handle, bool visible, CancellationToken ct, bool raiseOnShow = true)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryGet(handle, out UiSession session) || session.State != UiState.Open) return UniTask.CompletedTask;
            VisibilityOperation previous = session.Transition;
            if (previous != null && previous.Visible == visible)
                return previous.Completion.Task.AttachExternalCancellation(ct);
            UiVisibilityState target = visible ? UiVisibilityState.Visible : UiVisibilityState.Hidden;
            if (previous == null && session.Visibility == target) return UniTask.CompletedTask;
            var operation = new VisibilityOperation { Visible = visible, Version = ++session.OperationVersion };
            operation.Token = operation.Cancellation.Token;
            session.Transition = operation; // 先交出提交权，再取消旧回调（取消可能同步完成旧操作）。
            Exception stopError = previous?.Cancel();
            RunVisibilityAsync(session, operation, previous, stopError, raiseOnShow).Forget();
            return operation.Completion.Task.AttachExternalCancellation(ct);
        }

        async UniTask RunVisibilityAsync(UiSession session, VisibilityOperation operation,
            VisibilityOperation previous, Exception stopError, bool raiseOnShow)
        {
            Exception failure = null;
            try
            {
                if (previous != null) await previous.Finished.Task;
                if (stopError != null) throw stopError;
                operation.Token.ThrowIfCancellationRequested();
                if (!IsCurrent(session, operation)) throw new OperationCanceledException("UI 操作已替代");
                SetVisible(session, false);
                session.Visibility = operation.Visible ? UiVisibilityState.Showing : UiVisibilityState.Hiding;
                if (operation.Visible && raiseOnShow) session.Order = ++_presentationOrder;
                RefreshPresentation();
                session.View.BeginVisibility(operation.Visible);
                operation.Token.ThrowIfCancellationRequested();
                if (operation.Visible) await session.VisualTransition.ShowAsync(session.View, operation.Token);
                else await session.VisualTransition.HideAsync(session.View, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                if (!IsCurrent(session, operation)) throw new OperationCanceledException("旧 UI 过渡不能提交");
                SetVisible(session, operation.Visible);
                session.Visibility = operation.Visible ? UiVisibilityState.Visible : UiVisibilityState.Hidden;
                if (operation.Visible) session.View.CommitVisible(); else session.View.CommitHidden();
                RefreshPresentation();
            }
            catch (Exception error)
            {
                failure = error;
                if (IsCurrent(session, operation))
                {
                    SetVisible(session, false);
                    session.Visibility = UiVisibilityState.Hidden;
                    try { session.View.ApplyHidden(); }
                    catch (Exception cleanupError) { failure = new AggregateException(error, cleanupError); }
                    RefreshPresentation();
                }
            }
            finally
            {
                if (ReferenceEquals(session.Transition, operation)) session.Transition = null;
                operation.Cancellation.Dispose();
                operation.Finished.TrySetResult(); // 确认属性写入已停止后，才允许下一次过渡/销毁。
            }
            if (failure == null) operation.Completion.TrySetResult();
            else if (failure is OperationCanceledException) operation.Completion.TrySetCanceled(operation.Token);
            else operation.Completion.TrySetException(failure);
        }

        static bool IsCurrent(UiSession session, VisibilityOperation operation)
            => session.State == UiState.Open && session.OperationVersion == operation.Version
                && ReferenceEquals(session.Transition, operation);

        /// <summary>查询句柄当前可见性。</summary>
        public UiVisibilityState GetVisibilityState(UiHandle handle)
        {
            MainThreadGuard.Assert("UiService.GetVisibilityState");
            return TryGet(handle, out UiSession session) ? session.Visibility : UiVisibilityState.Unknown;
        }

        /// <summary>隐藏中的数量。</summary>
        public int HiddenCount
        {
            get
            {
                MainThreadGuard.Assert();
                int count = 0;
                foreach (UiSession session in _sessions.Values)
                    if (session.State == UiState.Open && session.Visibility == UiVisibilityState.Hidden) count++;
                return count;
            }
        }

        internal void StartService()
        {
            MainThreadGuard.Assert();
            if (_shuttingDown) throw new InvalidOperationException("已关闭的 UI 服务不能重新启动");
            _ready = true;
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
                bool canCache = session.State == UiState.Open;
                session.State = UiState.Closing;
                SetVisible(session, false);
                RefreshPresentation();
                Exception cancelError = null;
                try { session.OpeningCancellation?.Cancel(); }
                catch (Exception error) { cancelError = error; }
                VisibilityOperation transition = session.Transition;
                Exception transitionCancelError = transition?.Cancel();
                if (cancelError == null) cancelError = transitionCancelError;
                await session.OpeningFinished.Task;
                if (transition != null) await transition.Finished.Task;
                if (session.State != UiState.Closed)
                {
                    await HideForCloseAsync(session);
                    if (!canCache || cancelError != null || !await TryCacheAsync(session))
                        await DiscardAsync(session);
                }
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

        async UniTask HideForCloseAsync(UiSession session)
        {
            if (session.Visibility == UiVisibilityState.Hidden || session.View == null) return;
            bool alreadyHiding = session.Visibility == UiVisibilityState.Hiding;
            session.Visibility = UiVisibilityState.Hiding;
            RefreshPresentation();
            try
            {
                if (!alreadyHiding) session.View.BeginVisibility(false);
                if (!_shuttingDown)
                {
                    try { await session.VisualTransition.HideAsync(session.View, _closeTransitions.Token); }
                    catch (OperationCanceledException) when (_closeTransitions.IsCancellationRequested)
                    {
                        // 停止服务时仍提交隐藏并销毁，过渡返回后才可释放租约。
                    }
                }
                session.Visibility = UiVisibilityState.Hidden;
                session.View.CommitHidden();
                RefreshPresentation();
            }
            catch
            {
                session.Visibility = UiVisibilityState.Hidden;
                session.View.ApplyHidden();
                RefreshPresentation();
                throw;
            }
        }

        async UniTask DiscardAsync(UiSession session)
        {
            try
            {
                await DestroyViewAsync(session);
                FinishSession(session);
            }
            catch
            {
                MarkFaulted(session);
                throw;
            }
        }

        async UniTask DestroyViewAsync(UiSession session)
        {
            // Unity 伪空不代表工厂已经归还租约。
            if (ReferenceEquals(session.View, null)) return;
            if (!session.DestroyRequested)
            {
                if (session.View != null)
                {
                    await session.View.CloseSessionAsync();
                    if (session.View != null) session.View.DisposeInstance();
                }
                _factory.Destroy(session.View);
                session.DestroyRequested = true;
            }
            if (!await _factory.ReleaseAsync(session.View))
                throw new TimeoutException("UI 实例尚未销毁，保留租约供重试: " + session.Handle);
            session.View = null;
        }

        void FinishSession(UiSession session)
        {
            SetVisible(session, false);
            ClearFault(session);
            session.Visibility = UiVisibilityState.Hidden;
            session.State = UiState.Closed;
            _sessions.Remove(session.Id);
            RefreshPresentation();
        }

        void ClearFault(UiSession session)
        {
            if (!session.Faulted) return;
            session.Faulted = false;
            _faultedCount--;
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
            var errors = new List<Exception>();
            UniTask stopNavigation = Navigation.StopAsync();
            // 导航可能正在等待 Close 或回滚，先取消退场，再等待导航退出。
            try { _closeTransitions.Cancel(); } catch (Exception error) { errors.Add(error); }
            try { await stopNavigation; } catch (Exception error) { errors.Add(error); }
            var snapshot = new List<UiSession>(_sessions.Values);
            var pending = new List<UniTask>(snapshot.Count);
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
            await TrimCacheCoreAsync(errors);
            if (errors.Count == 0)
            {
                _closeTransitions.Dispose();
                _presentationTasks?.Dispose();
                completion.TrySetResult();
            }
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
            RefreshPresentation();
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
