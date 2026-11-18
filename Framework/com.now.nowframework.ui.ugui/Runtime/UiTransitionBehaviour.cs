using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>挂在 UiView 同一根节点的面板过渡。禁用组件则使用模块默认；一次打开选定后不在会话中切换。</summary>
    [DisallowMultipleComponent]
    public abstract class UiTransitionBehaviour : MonoBehaviour, IUiTransition
    {
        public abstract UniTask ShowAsync(UiView view, CancellationToken ct);
        public abstract UniTask HideAsync(UiView view, CancellationToken ct);

        internal void ResetForCache(UiView view) => OnStoredInCache(view);

        /// <summary>退场和会话清理完成后调用，复原动画姿态并丢弃本次会话的快照。</summary>
        protected virtual void OnStoredInCache(UiView view) { }
    }
}
