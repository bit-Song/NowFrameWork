using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using NowFrameWork.Core.Storage;
using NowFrameWork.Unity.Storage;
using UnityEngine;

namespace NowFrameWork.Unity.Tests
{
    public class JsonSettingsCodecTests
    {
        [Serializable] public sealed class Options
        {
            public int Volume;
            public string Language;
        }
        [Serializable] sealed class OldOptions { public int Percent; }
        static Options Defaults() => new Options { Volume = 50, Language = "简体中文" };
        static void Validate(Options value)
        {
            if (value.Volume < 0 || value.Volume > 100 || string.IsNullOrEmpty(value.Language))
                throw new InvalidDataException("Settings 字段缺失或越界");
        }

        [Test] public void Json_RoundTripsUnicodeAndFields()
        {
            var codec = new JsonSettingsCodec<Options>(validate: Validate);
            var decoded = codec.Decode(codec.Encode(Defaults()), 1);
            Assert.AreEqual(50, decoded.Volume); Assert.AreEqual("简体中文", decoded.Language);
        }

        [Test] public void JsonMissingRequiredFields_IsSemanticCorruption()
        {
            var codec = new JsonSettingsCodec<Options>(validate: Validate);
            Assert.Throws<InvalidDataException>(() => codec.Decode(Encoding.UTF8.GetBytes("{}"), 1));
        }

        [Test] public void InvalidJson_IsReportedAsCorruption()
        {
            var codec = new JsonSettingsCodec<Options>(validate: Validate);
            Assert.Throws<InvalidDataException>(() => codec.Decode(Encoding.UTF8.GetBytes("{broken"), 1));
        }

        [Test] public void InvalidUtf8_IsNotSilentlyReplaced()
        {
            var codec = new JsonSettingsCodec<Options>();
            Assert.Throws<InvalidDataException>(() => codec.Decode(new byte[] { 0xFF }, 1));
        }

        [Test] public void Encode_ValidatesBeforeSerialization()
        {
            var codec = new JsonSettingsCodec<Options>(validate: Validate);
            Assert.Throws<InvalidDataException>(() => codec.Encode(new Options { Volume = 101, Language = "en" }));
        }

        [Test] public void OldSchema_RequiresExplicitMigration()
        {
            var codec = new JsonSettingsCodec<Options>(2);
            Assert.Throws<SettingsCompatibilityException>(() => codec.Decode(Encoding.UTF8.GetBytes("{}"), 1));
        }

        [Test] public void Migration_ProducesValidatedCurrentDto()
        {
            var codec = new JsonSettingsCodec<Options>(2, (json, from) =>
            {
                Assert.AreEqual(1, from);
                return new Options { Volume = JsonUtility.FromJson<OldOptions>(json).Percent, Language = "en-US" };
            }, Validate);
            var value = codec.Decode(Encoding.UTF8.GetBytes("{\"Percent\":75}"), 1);
            Assert.AreEqual(75, value.Volume); Assert.AreEqual("en-US", value.Language);
        }

        [Test] public void MigrationResult_IsValidated()
        {
            var codec = new JsonSettingsCodec<Options>(2, (_, __) => new Options { Volume = -1 }, Validate);
            Assert.Throws<InvalidDataException>(() => codec.Decode(Encoding.UTF8.GetBytes("{}"), 1));
        }

        [Test] public void FutureSchema_IsRejectedBeforeParsingOrMigration()
        {
            var codec = new JsonSettingsCodec<Options>(1, (_, __) => throw new Exception("must not migrate"));
            Assert.Throws<SettingsCompatibilityException>(() => codec.Decode(new byte[0], 2));
        }

        [Test] public void UnityObjectRoot_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new JsonSettingsCodec<ScriptableObject>());
        }

        [Test] public void CollectionRoot_IsRejectedInsteadOfSerializingEmptyObject()
        {
            Assert.Throws<ArgumentException>(() => new JsonSettingsCodec<System.Collections.Generic.List<Options>>());
        }

        [Test] public void JsonStore_EndToEnd_SaveLoadAndMigrate()
        {
            string directory = Path.Combine(Path.GetTempPath(), "nwf-json-" + Guid.NewGuid().ToString("N"));
            try
            {
                var first = new FileSettingsStore<Options>(directory, "options", new JsonSettingsCodec<Options>(validate: Validate));
                first.Load(Defaults); first.Save();
                var second = new FileSettingsStore<Options>(directory, "options", new JsonSettingsCodec<Options>(2,
                    (json, _) => { var dto = JsonUtility.FromJson<Options>(json); dto.Volume = 70; return dto; }, Validate));
                Assert.IsTrue(second.Load(Defaults).Migrated); Assert.AreEqual(70, second.Value.Volume); second.Save();
                var third = new FileSettingsStore<Options>(directory, "options", new JsonSettingsCodec<Options>(2, validate: Validate));
                Assert.IsFalse(third.Load(Defaults).Migrated); Assert.AreEqual(70, third.Value.Volume);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
