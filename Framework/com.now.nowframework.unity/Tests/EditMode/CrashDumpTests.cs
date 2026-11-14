using System.IO;
using NUnit.Framework;
using NowFrameWork.Core;

namespace NowFrameWork.Unity.Tests
{
    /// <summary>G-06 崩溃落盘：内容正确、失败不抛。</summary>
    public class CrashDumpTests
    {
        [Test]
        public void Dump_WritesRecentLogsToFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "nw-crash-" + System.Guid.NewGuid().ToString("N"));

            try
            {
                var logger = new Logger(LogLevel.Trace, 16);
                logger.Write(LogLevel.Info, "C01", "启动完成");
                logger.Write(LogLevel.Error, "C05", "计时器回调失败");

                var dump = new CrashDump(logger, directory);
                string path = dump.Dump("test");

                Assert.IsNotNull(path);
                Assert.IsTrue(File.Exists(path));

                string content = File.ReadAllText(path);
                StringAssert.Contains("reason: test", content);
                StringAssert.Contains("启动完成", content);
                StringAssert.Contains("计时器回调失败", content);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Install_AndDispose_AreIdempotent()
        {
            var logger = new Logger(LogLevel.Error, 0);
            var dump = new CrashDump(logger, Path.GetTempPath());

            Assert.DoesNotThrow(() => dump.Install());
            Assert.DoesNotThrow(() => dump.Install());
            Assert.DoesNotThrow(() => dump.Dispose());
            Assert.DoesNotThrow(() => dump.Dispose());
        }
    }
}