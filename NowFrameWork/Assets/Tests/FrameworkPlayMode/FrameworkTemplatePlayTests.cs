#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using Game.FrameworkTemplate;
using NowFrameWork.Boot;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NowFrameWork.Tests
{
    public sealed class FrameworkTemplatePlayTests
    {
        [UnityTest]
        public IEnumerator TemplateLoadsAddressablesAndReleasesItsResources() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(TimeSpan.FromSeconds(45));
            CancellationToken ct = timeout.Token;
            await EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/FrameworkTemplate.scene",
                new LoadSceneParameters(LoadSceneMode.Single)).ToUniTask(cancellationToken: ct);
            var entry = Object.FindObjectOfType<FrameworkTemplateEntry>();
            Assert.That(entry, Is.Not.Null);
            var boot = entry.GetComponent<BootFlow>();
            try
            {
                // 从真实场景启动，等待首页设好默认焦点后再触发 Prefab 中的按钮。
                await WaitForFocus("Module lab", ct);
                var assets = GameEntry.Assets;
                Click("Settings");
                await WaitForFocus("English", ct);
                Assert.That(GameEntry.UI.TopModal.IsValid, Is.True);
                await WaitForStatus("blocked by modal", ct);
                Click("Check conflict with I");
                await WaitForStatus("Rejected I: already bound to Other", ct);
                Click("Start / stop BGM");
                await WaitForStatus("Looping BGM started.", ct);
                Click("Close");
                await WaitForFocus("Module lab", ct);
                Assert.That(GameEntry.UI.CachedCount, Is.EqualTo(1));

                // Toast 显示在页面上方，首页原有焦点应保持。
                Click("Show toast");
                await UniTask.WaitUntil(() => Object.FindObjectOfType<TemplateToast>() != null, cancellationToken: ct);
                Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("Module lab"));
                await UniTask.WaitUntil(() => Object.FindObjectOfType<TemplateToast>() == null, cancellationToken: ct);
                await UniTask.NextFrame(cancellationToken: ct);
                Click("Module lab");
                await WaitForFocus("Read config", ct);
                Click("Read config");
                await WaitForStatus("Direct lease returned:", ct);
                Click("Rent / return cube");
                await WaitForStatus("Prefab rented.", ct);
                Click("Rent / return cube");
                await WaitForStatus("Prefab returned. Idle instances: 1", ct);
                Click("Load / unload scene");
                await WaitForStatus("Additive scene loaded:", ct);
                Assert.That(SceneManager.GetSceneByName("FrameworkTemplateFixture").isLoaded, Is.True);
                Click("Event + timer");
                await WaitForStatus("Event received: True", ct);
                Click("Advance HFSM");
                await WaitForStatus("HFSM: Running / First", ct);
                Click("Advance HFSM");
                await WaitForStatus("HFSM: Running / Second", ct);
                Click("Advance HFSM");
                await WaitForStatus("HFSM: Idle", ct);
                Click("Advance HFSM");
                await WaitForStatus("HFSM: Running / Second", ct);
                Click("Trim UI cache");
                await WaitForStatus("UI cache trimmed: 1 → 0", ct);

                // 保留 BGM、空闲池和附加场景直到停止，再检查 Boot 能完整清理。
                await boot.Stop();
                Assert.That(boot.Context, Is.Null);
                Assert.That(GameEntry.IsReady, Is.False);
                Assert.That(assets.ActiveLeases, Is.Zero);
                Assert.That(assets.PendingRequests, Is.Zero);
                Assert.That(SceneManager.GetSceneByName("FrameworkTemplateFixture").isLoaded, Is.False);
            }
            finally
            {
                await boot.Stop();
                Object.Destroy(entry.gameObject);
                await UniTask.NextFrame();
            }
        });

        static void Click(string name)
        {
            var button = Object.FindObjectsOfType<Button>().Single(item => item.name == name && item.IsInteractable());
            button.onClick.Invoke();
        }

        static UniTask WaitForFocus(string name, CancellationToken ct)
            => UniTask.WaitUntil(() => EventSystem.current?.currentSelectedGameObject?.name == name, cancellationToken: ct);

        // 命令由按钮异步触发，等待状态文本确认完成，避免用固定延时推测结果。
        static UniTask WaitForStatus(string value, CancellationToken ct)
            => UniTask.WaitUntil(() => Object.FindObjectsOfType<Text>().Any(text =>
                text.name == "Status" && text.text.Contains(value)), cancellationToken: ct);
    }
}
#endif
