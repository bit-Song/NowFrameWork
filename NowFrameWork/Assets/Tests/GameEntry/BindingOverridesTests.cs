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

        [Test]
        public void Override_SurvivesSaveAndLoad()
        {
            InputActionAsset asset = CreateAsset();
            InputAction jump = asset.FindAction("Player/Jump");

            jump.ApplyBindingOverride(0, "<Keyboard>/j");
            string json = asset.SaveBindingOverridesAsJson();

            // 清空后按保存内容恢复
            asset.RemoveAllBindingOverrides();
            asset.LoadBindingOverridesFromJson(json);

            StringAssert.Contains("J", jump.GetBindingDisplayString(), "恢复后应显示新按键");

            Object.DestroyImmediate(asset);
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