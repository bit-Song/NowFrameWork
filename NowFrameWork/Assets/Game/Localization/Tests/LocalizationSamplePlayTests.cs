#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Localization;
using NowFrameWork.Boot;
using NowFrameWork.Core.Storage;
using NowFrameWork.Unity.Storage;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NowFrameWork.Tests
{
    public sealed class LocalizationSamplePlayTests
    {
        [UnityTest]
        public IEnumerator SwitchUpdatesOpenPanelAndReopenedSettingsRestoreLocale() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            string directory = Path.Combine(Path.GetTempPath(), "NowFrameWork-Locale-" + Guid.NewGuid().ToString("N"));
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/UILocalization.scene",
                new LoadSceneParameters(LoadSceneMode.Single));
            await load.ToUniTask(cancellationToken: ct);
            var boot = Object.FindObjectOfType<BootFlow>();
            try
            {
                await UniTask.WaitUntil(() => Object.FindObjectOfType<LocalizedSampleText>() != null, cancellationToken: ct);
                var label = Object.FindObjectOfType<LocalizedSampleText>().GetComponent<Text>();
                var initialLocale = LocalizationSettings.SelectedLocale;
                try
                {
                    var store = CreateStore(directory);
                    var language = new LanguageSelection(store);
                    await language.InitializeAsync(store.Load(() => new BootPreferences()).Source, ct);
                    await language.SwitchAsync(LanguageSelection.English, ct);
                    await UniTask.WaitUntil(() => label.text.Contains("Localized UI (3 items)"), cancellationToken: ct);
                    await language.SwitchAsync(LanguageSelection.Chinese, ct);
                    await UniTask.WaitUntil(() => label.text.Contains("本地化界面（3 项）") &&
                        label.text.Contains("Fallback: English text"), cancellationToken: ct);

                    await language.SwitchAsync(LanguageSelection.English, ct);
                    store.Value.Language = LanguageSelection.Chinese;
                    store.Save();
                    var reopened = CreateStore(directory);
                    await new LanguageSelection(reopened).InitializeAsync(reopened.Load(() => new BootPreferences()).Source, ct);
                    Assert.That(LocalizationSettings.SelectedLocale.Identifier.Code, Is.EqualTo(LanguageSelection.Chinese));
                }
                finally { LocalizationSettings.SelectedLocale = initialLocale; }
            }
            finally
            {
                if (boot != null) await boot.Stop();
                if (boot != null) Object.Destroy(boot.gameObject);
                if (Directory.Exists(directory))
                {
                    var store = CreateStore(directory);
                    File.Delete(store.FilePath);
                    File.Delete(store.BackupPath);
                    File.Delete(store.TemporaryPath);
                    Directory.Delete(directory);
                }
                await UniTask.NextFrame();
            }
        });

        static FileSettingsStore<BootPreferences> CreateStore(string directory)
            => new FileSettingsStore<BootPreferences>(directory, "preferences", new JsonSettingsCodec<BootPreferences>());
    }
}
#endif
