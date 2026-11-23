using System.Reflection;
using NowFrameWork.Core.Threading;
using NowFrameWork.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests
{
    public sealed class InputDeviceWatcherTests
    {
        [Test]
        public void IdleGamepadStateDoesNotReplaceKeyboardButButtonPressDoes()
        {
            MainThreadGuard.BindCurrentThread();
            var host = new GameObject("Device watcher test");
            var pad = InputSystem.AddDevice<Gamepad>();
            var watcher = host.AddComponent<InputSystemDeviceWatcher>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(InputSystemDeviceWatcher).GetMethod("OnEnable", flags).Invoke(watcher, null);
            try
            {
                watcher.Tracker.Set(InputDeviceKind.KeyboardMouse);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                InputSystem.Update();
                Assert.That(watcher.Tracker.Current, Is.EqualTo(InputDeviceKind.KeyboardMouse));

                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                Assert.That(watcher.Tracker.Current, Is.EqualTo(InputDeviceKind.Gamepad));
            }
            finally
            {
                typeof(InputSystemDeviceWatcher).GetMethod("OnDisable", flags).Invoke(watcher, null);
                InputSystem.RemoveDevice(pad);
                Object.DestroyImmediate(host);
            }
        }
    }
}
