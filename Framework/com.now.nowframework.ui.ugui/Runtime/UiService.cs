using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    /// UI 服务（UI-1 最小版）：注册表、打开/关闭、显隐、计数。
    ///
    /// 已实现的约定：
    /// 1. 单实例：正在打开的返回 Busy，已打开的返回 AlreadyOpen（不重复创建）；
    /// 2. 取消：打开被取消时回收晚到的实例与租约，再抛 OperationCanceledException；
    /// 3. 晚到结果：实例创建完成后状态已不是 Opening（被关闭/超时），直接回收；
    /// 4. 句柄：`UiHandle.Session` 每次会话递增，旧句柄无法操作新会话。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class UiService
    {
        sealed class UiSession
        {
            public int Id;
            public int Session;
            public UiDefinition Definition;
            public object Args;
            public UiState State;
            public UiView View;
            public UiHandle Handle;
        }

        readonly IUiViewFactory _factory;
        readonly IUiTransition _defaultTransition;
        readonly Dictionary<int, UiDefinition> _definitions = new Dictionary<int, UiDefinition>(16);
        readonly Dictionary<int, UiSession> _sessions = new Dictionary<int, UiSession>(16);

        int _nextSessionId;
        int _sessionCounter;
        int _openingCount;
        int _visibleCount;
        int _faultedCount;

        public UiService(IUiViewFactory factory, IEnumerable<UiDefinition> definitions, IUiTransition defaultTransition = null)
        {
            _factory = Guard.NotNull(factory, nameof(factory));
            _defaultTransition = defaultTransition ?? ImmediateUiTransition.Instance;

            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            foreach (UiDefinition definition in definitions)
            {
                if (definition == null) throw new ArgumentException("定义集合包含 null", nameof(definitions));
                if (_definitions.ContainsKey(definition.Id))
                    throw new ArgumentException("界面 Id 重复: " + definition.Id, nameof(definitions));

                _definitions.Add(definition.Id, definition);
            }
        }

        public int OpeningCount => _openingCount;

        public int VisibleCount => _visibleCount;

        public int FaultedCount => _faultedCount;

        public int SessionCount => _sessions.Count;

        public UniTask<UiOpenResult> OpenAsync<TArgs>(UiKey<TArgs> key, TArgs args, CancellationToken ct = default)
            where TArgs : class
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (args == null) throw new ArgumentNullException(nameof(args));

            if (!_definitions.TryGetValue(key.Id, out UiDefinition definition))
                throw new InvalidOperationException("未注册的界面: " + key);

            if (!definition.MultiInstance)
            {
                foreach (UiSession existing in _sessions.Values)
                {
                    if (existing.Definition.Id != definition.Id) continue;

                    if (existing.State == UiState.Opening) return UniTask.FromResult(new UiOpenResult(UiOpenStatus.Busy, default));

                    if (existing.State == UiState.Open)
                        return UniTask.FromResult(new UiOpenResult(UiOpenStatus.AlreadyOpen, existing.Handle));
                }
            }

            return OpenCoreAsync(definition, args, ct);
        }

        async UniTask<UiOpenResult> OpenCoreAsync(UiDefinition definition, object args, CancellationToken ct)
        {
            var session = new UiSession
            {
                Id = ++_nextSessionId,
                Session = ++_sessionCounter,
                Definition = definition,
                Args = args,
                State = UiState.Opening,
            };

            session.Handle = new UiHandle(1, session.Id, session.Session);
            _sessions.Add(session.Id, session);
            _openingCount++;

            try
            {
                session.View = await _factory.CreateAsync(definition, null, ct);

                // 晚到结果：等待期间可能已被关闭
                if (session.State != UiState.Opening)
                {
                    await DiscardAsync(session);
                    return new UiOpenResult(UiOpenStatus.Busy, default);
                }

                session.View.UiId = definition.Id;
                session.View.Session = session.Session;

                await GetTransition(definition).ShowAsync(session.View, ct);

                _openingCount--;
                session.State = UiState.Open;
                _visibleCount++;

                return new UiOpenResult(UiOpenStatus.Ok, session.Handle);
            }
            catch (OperationCanceledException)
            {
                _openingCount--;
                session.State = UiState.Closed;
                await DiscardAsync(session);
                throw;
            }
            catch (Exception)
            {
                _openingCount--;
                _faultedCount++;
                session.State = UiState.Faulted;
                await DiscardAsync(session);
                throw;
            }
        }

        public async UniTask ShowAsync(UiHandle handle, CancellationToken ct = default)
        {
            if (!TryGetOpen(handle, out UiSession session)) return;

            await GetTransition(session.Definition).ShowAsync(session.View, ct);
        }

        public async UniTask HideAsync(UiHandle handle, CancellationToken ct = default)
        {
            if (!TryGetOpen(handle, out UiSession session)) return;

            await GetTransition(session.Definition).HideAsync(session.View, ct);
        }

        public async UniTask CloseAsync(UiHandle handle, CancellationToken ct = default)
        {
            if (!TryGet(handle, out UiSession session)) return;
            if (session.State == UiState.Closing || session.State == UiState.Closed) return;

            session.State = UiState.Closing;
            if (_visibleCount > 0) _visibleCount--;

            await DiscardAsync(session);
        }

        async UniTask DiscardAsync(UiSession session)
        {
            if (session.View != null)
            {
                await _factory.ReleaseAsync(session.View);
                session.View = null;
            }

            session.State = UiState.Closed;
            _sessions.Remove(session.Id);
        }

        IUiTransition GetTransition(UiDefinition definition) => _defaultTransition;

        bool TryGet(UiHandle handle, out UiSession session)
        {
            session = null;
            if (!handle.IsValid) return false;
            if (!_sessions.TryGetValue(handle.Id, out session)) return false;

            // 会话代数不符：旧句柄，忽略
            return session.Session == handle.Session;
        }

        bool TryGetOpen(UiHandle handle, out UiSession session)
            => TryGet(handle, out session) && session.State == UiState.Open;
    }
}