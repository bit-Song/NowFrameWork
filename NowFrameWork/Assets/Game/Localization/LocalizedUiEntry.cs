using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Boot;
using UnityEngine;

namespace Game.Localization
{
    /// <summary>复用 UiFlowSample 的 Boot 入口，负责本地化样例的装配。</summary>
    public sealed class LocalizedUiEntry : MonoBehaviour, IBootEntry
    {
        [SerializeField] UiFlowSample _ui;
        LanguageSelection _languages;
        CancellationTokenSource _lifetime;
        UniTaskCompletionSource _switch;
        string _error;

        public async UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            if (_lifetime != null) throw new InvalidOperationException("本地化样例尚未退出");
            if (_ui == null) throw new InvalidOperationException("本地化样例缺少 UI 入口");
            _lifetime = new CancellationTokenSource();
            _error = null;
            var languages = new LanguageSelection(context.Settings);
            await languages.InitializeAsync(context.SettingsResult.Source, ct);
            await _ui.EnterAsync(context, ct);
            _languages = languages;
        }

        public async UniTask ExitAsync()
        {
            if (_lifetime == null) return;
            _languages = null;
            _lifetime.Cancel();
            if (_switch != null) await _switch.Task;
            await _ui.ExitAsync();
            _lifetime.Dispose();
            _lifetime = null;
        }

        void OnGUI()
        {
            if (_languages == null) return;
            using (new GUILayout.AreaScope(new Rect(10, 120, 520, 100)))
            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = _switch == null;
                if (GUILayout.Button("English", GUILayout.Width(105))) SwitchAsync(LanguageSelection.English).Forget();
                if (GUILayout.Button("简体中文", GUILayout.Width(105))) SwitchAsync(LanguageSelection.Chinese).Forget();
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(_error)) GUILayout.Label(_error);
            }
        }

        async UniTask SwitchAsync(string code)
        {
            var done = new UniTaskCompletionSource();
            _switch = done;
            _error = null;
            try { await _languages.SwitchAsync(code, _lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { _error = error.Message; Debug.LogException(error, this); }
            finally { _switch = null; done.TrySetResult(); }
        }
    }
}
