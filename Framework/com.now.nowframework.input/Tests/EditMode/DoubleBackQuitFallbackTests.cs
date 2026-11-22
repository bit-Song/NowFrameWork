using NUnit.Framework;

namespace NowFrameWork.Input.Tests
{
    /// <summary>"再按一次退出"的时间窗口行为。</summary>
    public class DoubleBackQuitFallbackTests
    {
        [Test]
        public void FirstBack_PromptsWithoutQuitting()
        {
            float now = 10f;
            int quits = 0;
            var fallback = new DoubleBackQuitFallback(2f, () => quits++, () => now);

            Assert.IsTrue(fallback.OnBack());
            Assert.AreEqual(0, quits, "第一次不应退出");
            Assert.IsTrue(fallback.IsPrompting);
            Assert.AreEqual(2f, fallback.RemainingSeconds, 0.001f);
        }

        [Test]
        public void SecondBackWithinWindow_Quits()
        {
            float now = 10f;
            int quits = 0;
            var fallback = new DoubleBackQuitFallback(2f, () => quits++, () => now);

            fallback.OnBack();
            now = 11.5f;
            fallback.OnBack();

            Assert.AreEqual(1, quits);
            Assert.IsFalse(fallback.IsPrompting, "退出后应清除提示状态");
        }

        [Test]
        public void SecondBackAfterWindow_RestartsPromptWithoutQuitting()
        {
            float now = 10f;
            int quits = 0;
            var fallback = new DoubleBackQuitFallback(2f, () => quits++, () => now);

            fallback.OnBack();
            now = 13f;   // 超出窗口
            fallback.OnBack();

            Assert.AreEqual(0, quits, "超时后不应退出");
            Assert.IsTrue(fallback.IsPrompting, "应重新开始提示");
            Assert.AreEqual(2f, fallback.RemainingSeconds, 0.001f);
        }
    }
}