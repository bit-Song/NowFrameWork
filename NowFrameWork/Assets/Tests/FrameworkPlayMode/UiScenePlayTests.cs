#if UNITY_EDITOR
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NowFrameWork.Tests
{
    public sealed class UiScenePlayTests
    {
        [UnityTest]
        public IEnumerator IntegrationOpensAndReleases() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(System.TimeSpan.FromSeconds(90));
            await OpenSceneAsync("UIIntegration", timeout.Token);
            var sample = Object.FindObjectOfType<UiFlowSample>();
            Assert.That(sample, Is.Not.Null);
            await VerifyAsync(sample.GetComponent<BootFlow>(), sample.Run100Async, timeout.Token);
        });

        [UnityTest]
        public IEnumerator TweenNavigatesAndReleases() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(System.TimeSpan.FromSeconds(60));
            await OpenSceneAsync("UITween", timeout.Token);
            var sample = Object.FindObjectOfType<UiWindowsSample>();
            Assert.That(sample, Is.Not.Null);
            await VerifyAsync(sample.GetComponent<BootFlow>(), sample.VerifyFlowAsync, timeout.Token);
        });

        [UnityTest]
        public IEnumerator WindowsScene_NativeKeyboardAndGamepadMoveAndRestoreModalFocus() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(System.TimeSpan.FromSeconds(35));
            CancellationToken ct = timeout.Token;
            var originalSettings = InputSystem.settings;
            var inputSettings = Object.Instantiate(originalSettings);
            inputSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = inputSettings;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                await OpenSceneAsync("UIWindows", ct);
                var sample = Object.FindObjectOfType<UiWindowsSample>();
                Assert.That(sample, Is.Not.Null);
                await VerifyAsync(sample.GetComponent<BootFlow>(), async token =>
                {
                    await WaitForFocusAsync("Next page", token);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow));
                    await WaitForFocusAsync("Settings", token);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    await UniTask.NextFrame(cancellationToken: token);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                    await WaitForFocusAsync("Confirm", token);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());

                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadDown));
                    await WaitForFocusAsync("Back", token);
                    InputSystem.QueueStateEvent(gamepad, new GamepadState());
                    await UniTask.NextFrame(cancellationToken: token);
                    InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.up });
                    await WaitForFocusAsync("Confirm", token);
                    InputSystem.QueueStateEvent(gamepad, new GamepadState());
                    await UniTask.NextFrame(cancellationToken: token);
                    InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.down });
                    await WaitForFocusAsync("Back", token);
                    InputSystem.QueueStateEvent(gamepad, new GamepadState());
                    await UniTask.NextFrame(cancellationToken: token);
                    Assert.That(sample.Service.TopPopup.IsValid, Is.True);
                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                    await WaitForFocusAsync("Settings", token);
                    InputSystem.QueueStateEvent(gamepad, new GamepadState());
                    Assert.That(sample.Service.TopPopup.IsValid, Is.False);
                }, ct);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(gamepad);
                InputSystem.settings = originalSettings;
                Object.Destroy(inputSettings);
            }
        });

        static UniTask WaitForFocusAsync(string expected, CancellationToken token) =>
            UniTask.WaitUntil(() => EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.name == expected, cancellationToken: token);

        static async UniTask OpenSceneAsync(string name, CancellationToken ct)
        {
            await EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/" + name + ".scene", new LoadSceneParameters(LoadSceneMode.Single))
                .ToUniTask(cancellationToken: ct);
        }

        static async UniTask VerifyAsync(BootFlow flow, System.Func<CancellationToken, UniTask> exercise,
            CancellationToken ct)
        {
            Assert.That(flow, Is.Not.Null);
            try
            {
                var sessionField = typeof(BootFlow).GetField("_session",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                BootSession session = null;
                await UniTask.WaitUntil(() =>
                {
                    session = (BootSession)sessionField.GetValue(flow);
                    return session?.State == BootState.Running;
                }, cancellationToken: ct);
                Assert.That(GameEntry.HasUI, Is.True);
                IAssetService assets = GameEntry.Assets;
                await exercise(ct);
                await flow.Stop();
                Assert.That(session.State, Is.EqualTo(BootState.Stopped));
                Assert.That(GameEntry.IsReady, Is.False);
                Assert.That(assets.PendingRequests, Is.Zero);
                Assert.That(assets.ActiveLeases, Is.Zero);
            }
            finally
            {
                if (flow != null) await flow.Stop();
                if (flow != null) Object.Destroy(flow.gameObject);
                await UniTask.NextFrame();
            }
        }
    }
}
#endif
