using NUnit.Framework;
using NowFrameWork.Input.InputSystem;
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
        public void Rebinding_CompositePartSurvivesAssetRecreationAndReportsConflicts()
        {
            var factory = typeof(InputDemo).GetMethod("BuildDefaultActions",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var original = (InputActionAsset)factory.Invoke(null, null);
            var reopened = (InputActionAsset)factory.Invoke(null, null);
            try
            {
                InputAction move = original.FindAction("Player/Move");
                Assert.That(move.bindings[1].isPartOfComposite, Is.True);
                Assert.That(InputBindingConflicts.TryFind(move, 1, "<Keyboard>/s", null, out var sibling, out int siblingIndex), Is.True);
                Assert.That(sibling, Is.SameAs(move));
                Assert.That(siblingIndex, Is.EqualTo(2));
                Assert.That(InputBindingConflicts.TryFind(move, 1, "<Keyboard>/space", null, out var jump, out _), Is.True);
                Assert.That(jump.name, Is.EqualTo("Jump"));
                Assert.That(InputBindingConflicts.TryFind(move, 1, "<Keyboard>/w", null, out _, out _), Is.False);
                move.ApplyBindingOverride(1, "<Keyboard>/i");
                reopened.LoadBindingOverridesFromJson(original.SaveBindingOverridesAsJson());
                var restored = reopened.FindAction("Player/Move");
                Assert.That(restored.bindings[1].effectivePath, Is.EqualTo("<Keyboard>/i"));
                Assert.That(restored.bindings[2].effectivePath, Is.EqualTo("<Keyboard>/s"));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(reopened);
            }
        }

        [Test]
        public void Rebinding_FindsGenericAndSpecificGamepadOverlapWithOrWithoutDevice()
        {
            const string layout = "NowFrameworkBindingTestPad";
            InputSystem.RegisterLayout("{\"name\":\"NowFrameworkBindingTestPad\",\"extend\":\"Gamepad\"}");
            var genericPad = InputSystem.AddDevice<Gamepad>();
            var specificPad = (Gamepad)InputSystem.AddDevice(layout);
            using var map = new InputActionMap("Player");
            try
            {
                var attack = map.AddAction("Attack", InputActionType.Button, "<Gamepad>/buttonWest");
                var interact = map.AddAction("Interact", InputActionType.Button, "<NowFrameworkBindingTestPad>/y");
                const string candidate = "<Gamepad>/buttonNorth";
                Assert.That(InputBindingConflicts.TryFind(attack, 0, candidate, genericPad.buttonNorth,
                    out var connected, out int index), Is.True);
                Assert.That(connected, Is.SameAs(interact));
                Assert.That(index, Is.Zero);

                InputSystem.RemoveDevice(specificPad);
                interact.ApplyBindingOverride(0, "<NowFrameworkBindingTestPad>/buttonNorth");
                Assert.That(InputBindingConflicts.TryFind(attack, 0, candidate, genericPad.buttonNorth,
                    out var disconnected, out _), Is.True);
                Assert.That(disconnected, Is.SameAs(interact));
                Assert.That(InputBindingConflicts.TryFind(attack, 0, "<Gamepad>/buttonSouth", genericPad.buttonSouth,
                    out _, out _), Is.False);
            }
            finally
            {
                if (specificPad.added) InputSystem.RemoveDevice(specificPad);
                InputSystem.RemoveDevice(genericPad);
                InputSystem.RemoveLayout(layout);
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
