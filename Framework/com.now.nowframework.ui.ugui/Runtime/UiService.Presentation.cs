using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Async;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.UI
{
    public sealed partial class UiService
    {
        readonly List<UiSession> _presentation = new List<UiSession>();
        readonly List<UiSession> _presentationSnapshot = new List<UiSession>();
        long _presentationOrder;
        UiRoot _root;
        UiHandle _topPopup, _topModal;
        bool _navigationBusy, _refreshing, _refreshAgain;
        UniTaskCompletionSource<bool> _back;
        TaskRunner _presentationTasks;

        public UiNavigator Navigation { get; }
        public UiHandle TopPopup { get { MainThreadGuard.Assert(); return _topPopup; } }
        public UiHandle TopModal { get { MainThreadGuard.Assert(); return _topModal; } }

        internal void AttachPresentation(UiRoot root, ILog log)
        {
            _root = root;
            _presentationTasks = new TaskRunner(log);
            root.PreparePresentation(OnBackdropClicked);
            RefreshPresentation();
        }

        void OnBackdropClicked()
        {
            if (_shuttingDown || _navigationBusy || !_topModal.IsValid) return;
            if (!TryGet(_topModal, out UiSession top) || top.State != UiState.Open
                || top.Visibility != UiVisibilityState.Visible || !top.Definition.CloseOnBackdrop) return;
            _presentationTasks.Run(CloseAsync(top.Handle), "关闭 UI 模态遮罩所有者");
        }

        internal void SetNavigationBusy(bool busy) { _navigationBusy = busy; RefreshPresentation(); }
        internal bool IsPageKey(int id) => _definitions.TryGetValue(id, out var definition) && definition.Layer == UiLayer.Page;
        internal int UiIdOf(UiHandle handle) => TryGet(handle, out var session) ? session.Definition.Id : 0;
        internal bool ConsumeNavigationBack(UiHandle handle)
        {
            if (!TryGet(handle, out var session)) return false;
            return session.Definition.BackBehavior == UiBackBehavior.Block || session.View != null && session.View.ConsumeBack();
        }

        static bool Presented(UiSession session) => session.View != null && session.State != UiState.Faulted
            && session.State != UiState.Closed && session.Visibility != UiVisibilityState.Hidden;

        void RefreshPresentation()
        {
            if (_refreshing) { _refreshAgain = true; return; }
            _refreshing = true;
            try
            {
                do
                {
                    _refreshAgain = false;
                    _presentation.Clear(); _presentationSnapshot.Clear();
                    foreach (var session in _sessions.Values)
                    {
                        _presentationSnapshot.Add(session);
                        if (Presented(session)) _presentation.Add(session);
                    }
                    _presentation.Sort(ComparePresentation);
                    int modal = -1;
                    _topPopup = _topModal = default;
                    for (int i = 0; i < _presentation.Count; i++)
                    {
                        if (_presentation[i].Definition.Layer == UiLayer.Popup) _topPopup = _presentation[i].Handle;
                        if (_presentation[i].Definition.Modal) { modal = i; _topModal = _presentation[i].Handle; }
                    }
                    foreach (var session in _presentationSnapshot)
                        if (session.View != null && !Presented(session)) session.View.SetInputBlocked(true);
                    int[] slots = { 0, 0, 0 };
                    int maskOrder = 0;
                    UiView focus = null;
                    int rootOrder = _root != null ? _root.Canvas.sortingOrder : 0;
                    int sortingLayer = _root != null ? _root.Canvas.sortingLayerID : 0;
                    for (int i = 0; i < _presentation.Count; i++)
                    {
                        UiSession session = _presentation[i];
                        if (session.View == null) continue;
                        int layer = (int)session.Definition.Layer;
                        int order = rootOrder + layer * 8192 + ++slots[layer] * 32;
                        session.View.SetSorting(order, sortingLayer);
                        bool blocked = _shuttingDown || _navigationBusy || i < modal || session.Definition.Layer == UiLayer.Toast
                            || session.State != UiState.Open || session.Visibility != UiVisibilityState.Visible;
                        session.View.SetInputBlocked(blocked);
                        if (!blocked) focus = session.View;
                        if (i == modal) maskOrder = order - 1;
                    }
                    if (_root != null) _root.PresentBackdrop(modal >= 0, maskOrder, _navigationBusy || _shuttingDown);
                    if (focus != null) focus.RestoreFocus();
                } while (_refreshAgain);
            }
            finally { _refreshing = false; }
        }

        static int ComparePresentation(UiSession a, UiSession b)
        {
            int layer = a.Definition.Layer.CompareTo(b.Definition.Layer);
            return layer != 0 ? layer : a.Order.CompareTo(b.Order);
        }

        /// <summary>返回请求按视觉顺序分发，Toast 不参与。动画中消费但不继续向下；ct 只取消等待。</summary>
        public UniTask<bool> RequestBackAsync(CancellationToken ct = default)
            => Navigation.IsBusy || Navigation.PageCount > 0 ? Navigation.BackAsync(ct) : RequestBackAsync(false, ct);

        internal UniTask<bool> RequestPopupBackAsync(CancellationToken ct)
            => RequestBackAsync(true, ct);

        UniTask<bool> RequestBackAsync(bool popupsOnly, CancellationToken ct)
        {
            MainThreadGuard.Assert("UiService.RequestBackAsync");
            ct.ThrowIfCancellationRequested();
            if (_shuttingDown || !_ready) return UniTask.FromResult(false);
            if (_back != null) return _back.Task.AttachExternalCancellation(ct);
            var result = new UniTaskCompletionSource<bool>(); _back = result;
            BackCoreAsync(result, popupsOnly).Forget();
            return result.Task.AttachExternalCancellation(ct);
        }

        async UniTask BackCoreAsync(UniTaskCompletionSource<bool> result, bool popupsOnly)
        {
            bool handled = false;
            Exception failure = null;
            try
            {
                UiSession target = null;
                for (int i = _presentation.Count - 1; i >= 0; i--)
                {
                    var session = _presentation[i];
                    if (popupsOnly && session.Definition.Layer != UiLayer.Popup) continue;
                    if (session.Definition.Layer == UiLayer.Toast || session.Definition.BackBehavior == UiBackBehavior.Ignore) continue;
                    target = session; break;
                }
                if (target != null)
                {
                    handled = true;
                    if (target.State == UiState.Open && target.Visibility == UiVisibilityState.Visible
                        && target.Definition.BackBehavior != UiBackBehavior.Block && !target.View.ConsumeBack())
                        await CloseAsync(target.Handle);
                }
            }
            catch (Exception error) { failure = error; }
            finally { _back = null; }
            if (failure == null) result.TrySetResult(handled); else result.TrySetException(failure);
        }
    }
}
