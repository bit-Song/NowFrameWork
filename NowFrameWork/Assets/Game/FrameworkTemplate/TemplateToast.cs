using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.FrameworkTemplate
{
    /// <summary>Toast 层的纯展示视图。入口持有会话句柄，负责停留计时和关闭。</summary>
    public sealed class TemplateToast : UiView
    {
        [SerializeField] Text _message;

        /// <summary>由生成器把文本组件写入 Prefab。</summary>
        public void Configure(Text message) => _message = message;

        protected override UniTask OnOpenAsync(object args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // TemplateContent.ToastKey 约定本面板的打开参数为字符串。
            _message.text = (string)args;
            return UniTask.CompletedTask;
        }
    }
}
