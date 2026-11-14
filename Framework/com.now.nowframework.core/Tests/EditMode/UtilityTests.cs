using System;
using NUnit.Framework;
using NowFrameWork.Core.Utility;

namespace NowFrameWork.Core.Tests
{
    /// <summary>C08 工具最小集：断言、安全转换、确定性哈希。</summary>
    public class UtilityTests
    {
        [Test]
        public void NotNull_ReturnsValue()
        {
            var value = new object();
            Assert.AreSame(value, Guard.NotNull(value, "value"));
        }

        [Test]
        public void NotNull_ThrowsWithParameterName()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => Guard.NotNull<object>(null, "服务"));
            Assert.AreEqual("服务", ex.ParamName);
        }

        [Test]
        public void NotNullOrEmpty_RejectsNullAndEmpty()
        {
            Assert.Throws<ArgumentException>(() => Guard.NotNullOrEmpty(null, "key"));
            Assert.Throws<ArgumentException>(() => Guard.NotNullOrEmpty("", "key"));
            Assert.AreEqual("abc", Guard.NotNullOrEmpty("abc", "key"));
        }

        [Test]
        public void Positive_RejectsZeroAndNegative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(0, "count"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(-1, "count"));
            Assert.AreEqual(1, Guard.Positive(1, "count"));
        }

        [Test]
        public void InRange_RejectsOutside()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(-0.1f, 0f, 1f, "t"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(1.1f, 0f, 1f, "t"));
            Assert.AreEqual(0.5f, Guard.InRange(0.5f, 0f, 1f, "t"));
        }

        [Test]
        public void TryCast_HandlesMatchAndMismatch()
        {
            object source = "text";

            Assert.IsTrue(SafeCast.TryCast<string>(source, out string text));
            Assert.AreEqual("text", text);

            Assert.IsFalse(SafeCast.TryCast<int[]>(source, out int[] numbers));
            Assert.IsNull(numbers);
        }

        [Test]
        public void As_ReturnsNullOnMismatch()
        {
            object source = new object();

            Assert.IsNull(SafeCast.As<string>(source));
            Assert.IsNull(SafeCast.As<string>(null));
        }

        [Test]
        public void Hash_IsDeterministic()
        {
            int first = Hash.Of("asset:enemy_01");
            int second = Hash.Of("asset:enemy_01");

            Assert.AreEqual(first, second, "同一字符串必须得到同一个值");
            Assert.AreNotEqual(0, first);
        }

        [Test]
        public void Hash_DifferentStrings_Differ()
        {
            Assert.AreNotEqual(Hash.Of("enemy_01"), Hash.Of("enemy_02"));
        }

        [Test]
        public void Hash_EmptyOrNull_ReturnsZero()
        {
            Assert.AreEqual(0, Hash.Of(null));
            Assert.AreEqual(0, Hash.Of(""));
        }

        [Test]
        public void Hash_Combine_IsOrderSensitive()
        {
            Assert.AreNotEqual(Hash.Combine(1, 2), Hash.Combine(2, 1), "组合顺序不同应得到不同结果");
            Assert.AreEqual(Hash.Combine(1, 2), Hash.Combine(1, 2));
        }
    }
}