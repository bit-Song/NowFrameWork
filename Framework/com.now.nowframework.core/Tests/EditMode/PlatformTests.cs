using System.Collections.Generic;
using NUnit.Framework;
using NowFrameWork.Core.Platform;

namespace NowFrameWork.Core.Tests
{
    /// <summary>C12 生命周期：四状态 + 信号幂等。</summary>
    public class PlatformTests
    {
        [Test]
        public void Lifecycle_StartsInStartingState()
        {
            var lifecycle = new AppLifecycle();
            Assert.AreEqual(AppState.Starting, lifecycle.State);
        }

        [Test]
        public void PauseThenResume_EmitsBothTransitions()
        {
            var lifecycle = new AppLifecycle();
            var states = new List<AppState>();
            lifecycle.StateChanged += s => states.Add(s);

            lifecycle.NotifyPause();
            lifecycle.NotifyResume();

            CollectionAssert.AreEqual(new[] { AppState.Background, AppState.Foreground }, states);
        }

        [Test]
        public void RepeatedPause_EmitsOnce()
        {
            var lifecycle = new AppLifecycle();
            int count = 0;
            lifecycle.StateChanged += _ => count++;

            lifecycle.NotifyPause();
            lifecycle.NotifyPause();   // 移动端常见的重复信号
            lifecycle.NotifyPause();

            Assert.AreEqual(1, count, "重复的暂停信号必须去重");
            Assert.AreEqual(AppState.Background, lifecycle.State);
        }

        [Test]
        public void ResumeWhileForeground_IsIgnored()
        {
            var lifecycle = new AppLifecycle();
            lifecycle.NotifyResume();

            int count = 0;
            lifecycle.StateChanged += _ => count++;
            lifecycle.NotifyResume();

            Assert.AreEqual(0, count);
        }

        [Test]
        public void Quit_IsFinal()
        {
            var lifecycle = new AppLifecycle();
            lifecycle.NotifyQuit();

            int count = 0;
            lifecycle.StateChanged += _ => count++;

            lifecycle.NotifyQuit();
            lifecycle.NotifyResume();
            lifecycle.NotifyPause();

            Assert.AreEqual(AppState.Quitting, lifecycle.State);
            Assert.AreEqual(0, count, "Quitting 之后不应再有状态变化");
        }

        [Test]
        public void PauseFromStarting_GoesStraightToBackground()
        {
            var lifecycle = new AppLifecycle();
            var states = new List<AppState>();
            lifecycle.StateChanged += s => states.Add(s);

            lifecycle.NotifyPause();

            CollectionAssert.AreEqual(new[] { AppState.Background }, states, "第一次信号是 pause 时不应先发 Foreground");
        }
    }
}