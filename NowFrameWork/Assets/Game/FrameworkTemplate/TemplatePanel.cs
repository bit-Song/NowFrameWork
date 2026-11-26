using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.FrameworkTemplate
{
    /// <summary>
    /// 首页、模块页和设置弹窗共用的视图脚本。Prefab 保存控件引用，
    /// 入口每次打开时传入按钮命令和状态文本读取函数。
    /// </summary>
    public sealed class TemplatePanel : UiView
    {
        /// <summary>一次打开会话的参数。按钮按序号分发，入口负责执行框架调用。</summary>
        public sealed class Data
        {
            public Action<int, CancellationToken> Execute { get; }
            public Func<string> ReadStatus { get; }

            public Data(Action<int, CancellationToken> execute, Func<string> readStatus)
            {
                Execute = execute;
                ReadStatus = readStatus;
            }
        }

        [SerializeField] Button[] _buttons;
        [SerializeField] Text _status;
        UnityAction[] _listeners;
        Data _data;
        float _nextRefresh;

        /// <summary>由场景生成器写入 Prefab。按钮数组也决定方向导航的上下顺序。</summary>
        public void Configure(Button[] buttons, Text status)
        {
            _buttons = buttons;
            _status = status;
        }

        protected override void OnCreate()
        {
            // 一个实例只注册一次监听。记录准确的委托实例，销毁时可逐个解绑。
            _listeners = new UnityAction[_buttons.Length];
            for (int i = 0; i < _buttons.Length; i++)
            {
                int index = i;
                _listeners[i] = () => _data?.Execute(index, VisibleToken);
                _buttons[i].onClick.AddListener(_listeners[i]);
            }
        }

        protected override UniTask OnOpenAsync(object args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // 缓存面板每次重开仍会执行此方法，使用本次会话的新命令。
            _data = (Data)args;
            ConfigureVerticalNavigation(_buttons);
            Refresh();
            return UniTask.CompletedTask;
        }

        // 退栈恢复或主动 Show 时立即同步文本，后续按间隔刷新。
        protected override void OnShown() => Refresh();

        void Update()
        {
            // 隐藏时停止读取状态。可见时限制刷新频率，避免每帧拼接状态文本。
            if (_data == null || VisibleToken.IsCancellationRequested || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.15f;
            Refresh();
        }

        void Refresh()
        {
            if (_data == null) return;
            string value = _data.ReadStatus();
            if (_status.text != value) _status.text = value;
        }

        protected override UniTask OnCloseAsync()
        {
            // 结束会话时清除它的参数。对象可能进入缓存，按钮监听留到实例销毁统一解绑。
            _data = null;
            _status.text = "";
            return UniTask.CompletedTask;
        }

        protected override void OnDispose()
        {
            if (_listeners == null) return;
            for (int i = 0; i < _buttons.Length; i++)
                _buttons[i].onClick.RemoveListener(_listeners[i]);
        }
    }
}
