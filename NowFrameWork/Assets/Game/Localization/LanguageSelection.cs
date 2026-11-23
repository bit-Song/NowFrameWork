using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Boot;
using NowFrameWork.Core.Storage;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Localization
{
    /// <summary>项目侧语言选择与持久化。Unity Localization 持有表和加载句柄。</summary>
    public sealed class LanguageSelection
    {
        public const string Table = "FrameworkSample";
        public const string English = "en-US";
        public const string Chinese = "zh-CN";

        readonly FileSettingsStore<BootPreferences> _settings;
        bool _switching;

        public LanguageSelection(FileSettingsStore<BootPreferences> settings)
            => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public async UniTask InitializeAsync(SettingsLoadSource source, CancellationToken ct)
        {
            await AwaitAsync(LocalizationSettings.InitializationOperation, ct);
            var locales = LocalizationSettings.AvailableLocales;
            Locale saved = source == SettingsLoadSource.Defaults ? null : locales.GetLocale(_settings.Value.Language);
            Locale system = saved == null ? new SystemLocaleSelector().GetStartupLocale(locales) : null;
            Locale selected = saved ?? system ?? locales.GetLocale(English);
            if (selected == null) throw new InvalidOperationException("本地化样例缺少默认语言 " + English);
            await ApplyAsync(selected, persist: false, ct);
        }

        public UniTask SwitchAsync(string code, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale == null) throw new ArgumentException("未配置语言 " + code, nameof(code));
            return ApplyAsync(locale, persist: true, ct);
        }

        async UniTask ApplyAsync(Locale locale, bool persist, CancellationToken ct)
        {
            if (_switching) throw new InvalidOperationException("正在切换语言");
            _switching = true;
            try
            {
                await AwaitAsync(LocalizationSettings.StringDatabase.GetTableAsync(Table, locale), ct);
                ct.ThrowIfCancellationRequested();
                if (persist)
                {
                    BootPreferences preferences = _settings.Value;
                    string previous = preferences.Language;
                    preferences.Language = locale.Identifier.Code;
                    try { _settings.Save(); }
                    catch { preferences.Language = previous; throw; }
                }
                LocalizationSettings.SelectedLocale = locale;
            }
            finally { _switching = false; }
        }

        static async UniTask AwaitAsync<T>(AsyncOperationHandle<T> handle, CancellationToken ct) where T : class
        {
            if (!handle.IsDone) await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: ct);
            ct.ThrowIfCancellationRequested();
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                throw new InvalidOperationException("本地化资源加载失败", handle.OperationException);
        }
    }
}
