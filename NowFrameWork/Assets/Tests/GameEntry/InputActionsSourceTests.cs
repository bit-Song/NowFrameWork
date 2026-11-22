using NUnit.Framework;
using NowFrameWork.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests
{
    /// <summary>Input Actions 适配层：只验证查表与空值语义，不模拟真实按键。</summary>
    public class InputActionsSourceTests
    {
        static InputActionAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");
            map.AddAction("Jump", InputActionType.Button);
            map.AddAction("Move", InputActionType.Value);
            return asset;
        }

        [Test]
        public void Ctor_ThrowsForMissingAction()
        {
            InputActionAsset asset = CreateAsset();

            Assert.Throws<System.ArgumentException>(() =>
                new InputActionsSource(asset, (1, "NotExist")));

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void TryRead_UnknownId_ReturnsFalse()
        {
            InputActionAsset asset = CreateAsset();
            var source = new InputActionsSource(asset, (1, "Jump"));

            Assert.IsFalse(source.TryRead(99, out _));

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void TryRead_KnownId_ReturnsIdleWhenAssetDisabled()
        {
            InputActionAsset asset = CreateAsset();
            var source = new InputActionsSource(asset, (1, "Jump"), (2, "Move"));

            Assert.IsTrue(source.TryRead(1, out ActionState state));
            Assert.IsTrue(state.IsIdle, "资产未启用时应返回空闲而非抛错");

            Object.DestroyImmediate(asset);
        }
    }
}