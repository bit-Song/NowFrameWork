using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.UI
{
    /// <summary>
    /// 仅主线程的页面历史。Push 隐藏前页，Pop 关闭当前页并恢复前页；根页不弹出。
    /// 操作在途时拒绝新的 Push/Pop；调用者取消仅取消等待，服务关闭才取消内部工作。
    /// 导航拥有页面显隐/关闭，业务不要同时通过原始句柄修改导航页面。
    /// </summary>
    public sealed class UiNavigator
    {
        readonly UiService _service;
        readonly List<UiHandle> _pages = new List<UiHandle>();
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        UniTaskCompletionSource _finished;
        bool _busy, _closed;
        UiHandle _rollbackNew, _rollbackPrevious;
        UiHandle _pendingPop;

        internal UiNavigator(UiService service) => _service = service;
        /// <summary>是否有切页操作进行中。</summary>
        public bool IsBusy { get { MainThreadGuard.Assert(); return _busy; } }
        /// <summary>页面栈深度。</summary>
        public int PageCount { get { MainThreadGuard.Assert(); Prune(); return _pages.Count; } }
        /// <summary>栈顶页面句柄。</summary>
        public UiHandle CurrentPage { get { MainThreadGuard.Assert(); Prune(); return _pages.Count == 0 ? default : _pages[_pages.Count - 1]; } }

        /// <summary>入栈一个页面。</summary>
        public UniTask<UiOpenResult> PushAsync<TArgs>(UiKey<TArgs> key, TArgs args, CancellationToken ct = default) where TArgs : class
        {
            MainThreadGuard.Assert("UiNavigator.PushAsync");
            ct.ThrowIfCancellationRequested();
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (!_service.IsPageKey(key.Id)) throw new InvalidOperationException("导航只接管已注册的 Page 界面。");
            Begin();
            return PushCoreAsync(key, args).AttachExternalCancellation(ct);
        }

        async UniTask<UiOpenResult> PushCoreAsync<TArgs>(UiKey<TArgs> key, TArgs args) where TArgs : class
        {
            try
            {
                await RepairAsync();
                if (_pendingPop.IsValid) await CompletePopAsync();
                Prune();
                for (int i = 0; i < _pages.Count; i++)
                    if (_service.UiIdOf(_pages[i]) == key.Id)
                    {
                        if (i == _pages.Count - 1) return new UiOpenResult(UiOpenStatus.AlreadyOpen, _pages[i]);
                        throw new InvalidOperationException("页面已在导航历史中，请使用 Pop 返回。");
                    }
                UiHandle previous = CurrentPage;
                UiOpenResult opened = await _service.OpenAsync(key, args, _lifetime.Token);
                if (opened.Status != UiOpenStatus.Ok) throw new InvalidOperationException("导航不能接管已经由其他入口打开或正在清理的页面。");
                _rollbackNew = opened.Handle; _rollbackPrevious = previous;
                try
                {
                    await _service.HideAsync(previous, _lifetime.Token);
                    _lifetime.Token.ThrowIfCancellationRequested();
                    _pages.Add(opened.Handle);
                    _rollbackNew = _rollbackPrevious = default;
                    return opened;
                }
                catch (Exception error)
                {
                    try { await RepairAsync(); }
                    catch (Exception rollback) { throw new AggregateException("页面进入失败，回滚未完成；下一次导航先重试回滚。", error, rollback); }
                    throw;
                }
            }
            finally { End(); }
        }

        /// <summary>出栈当前页面。</summary>
        public UniTask<bool> PopAsync(CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiNavigator.PopAsync");
            ct.ThrowIfCancellationRequested(); Begin();
            return PopCoreAsync().AttachExternalCancellation(ct);
        }

        async UniTask<bool> PopCoreAsync()
        {
            try
            {
                await RepairAsync();
                if (!_pendingPop.IsValid)
                {
                    Prune();
                    if (_pages.Count <= 1) return false;
                    UiHandle current = _pages[_pages.Count - 1], previous = _pages[_pages.Count - 2];
                    // 恢复背景页时保留原排序，让当前页在前景完成退场。
                    await _service.RestorePageAsync(previous, _lifetime.Token);
                    _pendingPop = current;
                }
                await CompletePopAsync();
                return true;
            }
            finally { End(); }
        }

        /// <summary>先处理弹窗，再返回上一页。</summary>
        public async UniTask<bool> BackAsync(CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiNavigator.BackAsync");
            ct.ThrowIfCancellationRequested();
            if (_closed) return false;
            if (_busy) return true;
            if (await _service.RequestPopupBackAsync(ct)) return true;
            if (!_pendingPop.IsValid && _service.ConsumeNavigationBack(CurrentPage)) return true;
            return await PopAsync(ct);
        }

        async UniTask CompletePopAsync()
        {
            // 前页已恢复。清理成功后才能把它作为下一次 Push 的出发页。
            await _service.CloseAsync(_pendingPop, _lifetime.Token);
            _pages.Remove(_pendingPop);
            _pendingPop = default;
        }

        async UniTask RepairAsync()
        {
            if (_rollbackNew.IsValid) await _service.CloseAsync(_rollbackNew);
            if (!_closed && _rollbackPrevious.IsValid) await _service.ShowAsync(_rollbackPrevious);
            _rollbackNew = _rollbackPrevious = default;
        }

        void Prune()
        {
            for (int i = _pages.Count - 1; i >= 0; i--)
                if (_service.GetVisibilityState(_pages[i]) == UiVisibilityState.Unknown) _pages.RemoveAt(i);
        }

        void Begin()
        {
            if (_closed) throw new InvalidOperationException("UI 导航已停止。");
            if (_busy) throw new InvalidOperationException("UI 导航正在过渡，请等待当前操作完成。");
            if (_service.TopModal.IsValid) throw new InvalidOperationException("请先关闭模态窗口，再切换页面。");
            _busy = true; _finished = new UniTaskCompletionSource();
            try { _service.SetNavigationBusy(true); }
            catch { _busy = false; _finished.TrySetResult(); throw; }
        }

        void End()
        {
            try { _service.SetNavigationBusy(false); }
            finally { _busy = false; _finished.TrySetResult(); }
        }

        internal async UniTask StopAsync()
        {
            if (!_closed)
            {
                _closed = true;
                Exception cancellationError = null;
                try { _lifetime.Cancel(); } catch (Exception error) { cancellationError = error; }
                if (_busy) await _finished.Task;
                _lifetime.Dispose();
                if (cancellationError != null) throw cancellationError;
            }
            _pages.Clear(); _rollbackNew = _rollbackPrevious = _pendingPop = default;
        }
    }
}
