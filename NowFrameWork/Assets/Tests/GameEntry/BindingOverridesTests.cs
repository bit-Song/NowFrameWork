using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests
{
    /// <summary>绑定覆盖的保存与恢复（不模拟真实按键，直接写覆盖）。</summary>
    public class BindingOverridesTests
    {
        static InputActionAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            return asset;
        }

        [TestCase("Player/Jump", "<Gamepad>/buttonSouth")]
        [TestCase("Player/Interact", "<Gamepad>/buttonWest")]
        public void KeyboardOverride_SurvivesRecreatingDemoActions(string actionPath, string gamepadPath)
        {
            var factory = typeof(InputDemo).GetMethod("BuildDefaultActions",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var original = (InputActionAsset)factory.Invoke(null, null);
            var reopened = (InputActionAsset)factory.Invoke(null, null);
            try
            {
                original.FindAction(actionPath).ApplyBindingOverride(0, "<Keyboard>/j");
                reopened.LoadBindingOverridesFromJson(original.SaveBindingOverridesAsJson());
                var action = reopened.FindAction(actionPath);
                Assert.That(action.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/j"));
                Assert.That(action.bindings[1].effectivePath, Is.EqualTo(gamepadPath));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(reopened);
            }
        }

        [Test]
        public void ResetAll_RestoresDefaultBinding()
        {
            InputActionAsset asset = CreateAsset();
            InputAction jump = asset.FindAction("Player/Jump");

            jump.ApplyBindingOverride(0, "<Keyboard>/j");
            asset.RemoveAllBindingOverrides();

            StringAssert.Contains("Space", jump.GetBindingDisplayString(), "清空覆盖后应回到默认按键");

            Object.DestroyImmediate(asset);
        }
    }
}
