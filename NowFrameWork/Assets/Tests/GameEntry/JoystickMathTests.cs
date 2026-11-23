using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Tests
{
    /// <summary>摇杆取值：死区、归一化、超界钳制。</summary>
    public class JoystickMathTests
    {
        [Test]
        public void SecondPointerCannotMoveOrReleaseActiveJoystick()
        {
            var host = new GameObject("Joystick test", typeof(RectTransform));
            try
            {
                host.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 100f);
                var joystick = host.AddComponent<VirtualJoystick>();
                var first = new PointerEventData(null) { pointerId = 1, position = new Vector2(50f, 0f) };
                var second = new PointerEventData(null) { pointerId = 2, position = new Vector2(-50f, 0f) };

                joystick.OnPointerDown(first);
                Assert.That(joystick.Value.x, Is.GreaterThan(0f));
                joystick.OnPointerDown(second);
                joystick.OnDrag(second);
                joystick.OnPointerUp(second);
                Assert.That(joystick.IsPressed, Is.True);
                Assert.That(joystick.Value.x, Is.GreaterThan(0f));

                joystick.OnPointerUp(first);
                Assert.That(joystick.IsPressed, Is.False);
                Assert.That(joystick.Value, Is.EqualTo(Vector2.zero));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void InsideDeadzone_ReturnsZero()
        {
            Vector2 value = JoystickMath.Evaluate(new Vector2(10f, 0f), 100f, 0.2f);

            Assert.AreEqual(Vector2.zero, value);
        }

        [Test]
        public void FullDeflection_ReturnsUnitDirection()
        {
            Vector2 value = JoystickMath.Evaluate(new Vector2(100f, 0f), 100f, 0.2f);

            Assert.AreEqual(1f, value.magnitude, 0.001f);
            Assert.AreEqual(1f, value.x, 0.001f);
        }

        [Test]
        public void BeyondRadius_IsClamped()
        {
            Vector2 value = JoystickMath.Evaluate(new Vector2(1000f, 0f), 100f, 0.2f);

            Assert.AreEqual(1f, value.magnitude, 0.001f);
        }

        [Test]
        public void DeadzoneEdge_IsRemappedToZero()
        {
            // 半径 100、死区 0.2：位移 40 → 0.4，重映射后 (0.4-0.2)/(0.8)=0.25
            Vector2 value = JoystickMath.Evaluate(new Vector2(40f, 0f), 100f, 0.2f);

            Assert.AreEqual(0.25f, value.magnitude, 0.001f);
        }

        [Test]
        public void ZeroRadius_ReturnsZero()
        {
            Assert.AreEqual(Vector2.zero, JoystickMath.Evaluate(new Vector2(50f, 0f), 0f, 0.2f));
        }
    }
}
