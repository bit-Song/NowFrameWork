using NUnit.Framework;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Input.Tests
{
    /// <summary>设备类型去重：只在真正变化时通知一次。</summary>
    public class InputDeviceTrackerTests
    {
        [SetUp]
        public void SetUp() => MainThreadGuard.BindCurrentThread();

        [Test]
        public void Initial_IsNone()
        {
            var tracker = new InputDeviceTracker();

            Assert.AreEqual(InputDeviceKind.None, tracker.Current);
        }

        [Test]
        public void Set_NewKind_RaisesOnce()
        {
            var tracker = new InputDeviceTracker();
            int raised = 0;
            InputDeviceKind last = InputDeviceKind.None;

            tracker.Changed += kind => { raised++; last = kind; };

            Assert.IsTrue(tracker.Set(InputDeviceKind.Gamepad));
            Assert.AreEqual(1, raised);
            Assert.AreEqual(InputDeviceKind.Gamepad, last);
        }

        [Test]
        public void Set_SameKind_DoesNotRaise()
        {
            var tracker = new InputDeviceTracker();
            int raised = 0;
            tracker.Changed += _ => raised++;

            tracker.Set(InputDeviceKind.KeyboardMouse);
            Assert.IsFalse(tracker.Set(InputDeviceKind.KeyboardMouse), "同类型不应报告变化");

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void Set_AlternatingKinds_RaisesEachChange()
        {
            var tracker = new InputDeviceTracker();
            int raised = 0;
            tracker.Changed += _ => raised++;

            tracker.Set(InputDeviceKind.KeyboardMouse);
            tracker.Set(InputDeviceKind.Gamepad);
            tracker.Set(InputDeviceKind.KeyboardMouse);

            Assert.AreEqual(3, raised);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, tracker.Current);
        }
    }
}