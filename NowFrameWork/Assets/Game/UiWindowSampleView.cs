using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public sealed class UiWindowSampleView : UiView
    {
        [SerializeField] Text _title;
        [SerializeField] Image _background;
        [SerializeField] RectTransform _motion;
        [SerializeField] Button _next, _settings, _confirm, _back;
        UiWindowSampleArgs _args;

        public void Configure(Text title, Image background, RectTransform motion, Button next, Button settings, Button confirm, Button back)
        {
            _title = title; _background = background; _motion = motion;
            _next = next; _settings = settings; _confirm = confirm; _back = back; DefaultFocus = back;
        }
        protected override void OnCreate()
        {
            _next.onClick.AddListener(Next); _settings.onClick.AddListener(Settings);
            _confirm.onClick.AddListener(Confirm); _back.onClick.AddListener(Back);
        }
        protected override UniTask OnOpenAsync(object args, CancellationToken ct)
        {
            _args = args as UiWindowSampleArgs ?? throw new ArgumentException("窗口样例需要 UiWindowSampleArgs");
            _title.text = _args.Title; _background.color = _args.Color;
            _motion.anchorMin = _args.Fullscreen ? Vector2.zero : new Vector2(0.5f, 0.5f);
            _motion.anchorMax = _args.Fullscreen ? Vector2.one : new Vector2(0.5f, 0.5f);
            _motion.sizeDelta = _args.Fullscreen ? Vector2.zero : new Vector2(760, 500);
            _motion.anchoredPosition = Vector2.zero;
            _next.gameObject.SetActive(_args.Next != null); _settings.gameObject.SetActive(_args.Settings != null);
            _confirm.gameObject.SetActive(_args.Confirm != null); _back.gameObject.SetActive(_args.Back != null);
            return UniTask.CompletedTask;
        }
        void Next() => _args?.Next?.Invoke();
        void Settings() => _args?.Settings?.Invoke();
        void Confirm() => _args?.Confirm?.Invoke();
        void Back() => _args?.Back?.Invoke();

        protected override UniTask OnCloseAsync()
        {
            _args = null;
            return UniTask.CompletedTask;
        }

        protected override void OnDispose()
        {
            _next.onClick.RemoveListener(Next); _settings.onClick.RemoveListener(Settings);
            _confirm.onClick.RemoveListener(Confirm); _back.onClick.RemoveListener(Back);
        }
    }
}
