using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace NowFrameWork.Tests
{
    public sealed class InputRebindingPlayTests
    {
        [UnityTest]
        public IEnumerator CharacterUsesNewBindingImmediatelyAndAfterReopening() => UniTask.ToCoroutine(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "NowFrameWork-Input-" + Guid.NewGuid().ToString("N"));
            InputSettings originalSettings = InputSystem.settings;
            var testSettings = Object.Instantiate(originalSettings);
            testSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testSettings;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            GameObject host = null;
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(TimeSpan.FromSeconds(20));
            CancellationToken ct = timeout.Token;
            try
            {
                host = CreateCharacter(directory, out var panel);
                await UniTask.NextFrame(cancellationToken: ct);
                await PressAsync(keyboard, Key.Space, ct);
                Assert.That(host.transform.position.y, Is.EqualTo(0.5f));

                panel.StartRebind("Player/Jump", 0);
                await PressAsync(keyboard, Key.J, ct, capturedByRebind: true);
                await UniTask.WaitUntil(() => !panel.IsRebinding, cancellationToken: ct);
                Assert.That(File.Exists(Path.Combine(directory, "settings-input-bindings.nwf")), Is.True);

                float height = host.transform.position.y;
                await PressAsync(keyboard, Key.Space, ct);
                Assert.That(host.transform.position.y, Is.EqualTo(height));
                await PressAsync(keyboard, Key.J, ct);
                Assert.That(host.transform.position.y, Is.EqualTo(height + 0.5f));

                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, cancellationToken: ct);
                Assert.That(host.transform.position.y, Is.EqualTo(height + 1f));
                InputSystem.QueueStateEvent(gamepad, new GamepadState());

                panel.StartRebind("Player/Jump", 0);
                await PressAsync(keyboard, Key.Escape, ct, capturedByRebind: true);
                Assert.That(panel.IsRebinding, Is.False);
                await PressAsync(keyboard, Key.J, ct);
                Assert.That(host.transform.position.y, Is.EqualTo(height + 1.5f));

                Object.Destroy(host);
                await UniTask.NextFrame(cancellationToken: ct);
                host = CreateCharacter(directory, out panel);
                await UniTask.NextFrame(cancellationToken: ct);
                await PressAsync(keyboard, Key.J, ct);
                Assert.That(host.transform.position.y, Is.EqualTo(0.5f));

                panel.ResetAll();
                Object.Destroy(host);
                await UniTask.NextFrame(cancellationToken: ct);
                host = CreateCharacter(directory, out _);
                await UniTask.NextFrame(cancellationToken: ct);
                await PressAsync(keyboard, Key.J, ct);
                Assert.That(host.transform.position.y, Is.Zero);
                await PressAsync(keyboard, Key.Space, ct);
                Assert.That(host.transform.position.y, Is.EqualTo(0.5f));
            }
            finally
            {
                if (host != null) Object.Destroy(host);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(gamepad);
                InputSystem.settings = originalSettings;
                Object.Destroy(testSettings);
                if (Directory.Exists(directory))
                {
                    foreach (string file in new[] { "settings-input-bindings.nwf", "settings-input-bindings.nwf.bak",
                                 "settings-input-bindings.nwf.tmp" })
                        File.Delete(Path.Combine(directory, file));
                    Directory.Delete(directory);
                }
            }
        });

        static GameObject CreateCharacter(string directory, out InputRebindingDemo panel)
        {
            var host = new GameObject("Input integration test");
            host.SetActive(false);
            panel = host.AddComponent<InputRebindingDemo>();
            panel.Initialize(directory);
            host.AddComponent<CharacterDemo>();
            host.SetActive(true);
            return host;
        }

        static async UniTask PressAsync(Keyboard keyboard, Key key, CancellationToken ct, bool capturedByRebind = false)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, cancellationToken: ct);
            if (!capturedByRebind)
                Assert.That(keyboard[key].isPressed, Is.True, "The queued key event must reach the player.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, cancellationToken: ct);
        }
    }
}
