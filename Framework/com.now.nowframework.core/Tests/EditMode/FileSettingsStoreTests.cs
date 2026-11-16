using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using NowFrameWork.Core.Storage;

namespace NowFrameWork.Core.Tests
{
    public class FileSettingsStoreTests
    {
        sealed class Options { public int Number { get; set; } }
        sealed class Codec : ISettingsCodec<Options>
        {
            public int Version { get; set; } = 1;
            public Func<Options, byte[]> Encoder { get; set; }
            public Func<byte[], int, Options> Decoder { get; set; }
            public byte[] Encode(Options value) => Encoder != null ? Encoder(value) : Encoding.UTF8.GetBytes(value.Number.ToString());
            public Options Decode(byte[] payload, int version)
            {
                if (Decoder != null) return Decoder(payload, version);
                if (!int.TryParse(Encoding.UTF8.GetString(payload), out int number)) throw new InvalidDataException("invalid options");
                return new Options { Number = number };
            }
        }

        string _directory;
        [SetUp] public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "nwf-settings-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        FileSettingsStore<Options> Store(Codec codec = null, int limit = 1024) => new FileSettingsStore<Options>(_directory, "test", codec ?? new Codec(), limit);
        static Options Defaults() => new Options { Number = 10 };
        FileSettingsStore<Options> SavedTwice()
        {
            var store = Store();
            store.Load(Defaults);
            store.Save();
            store.Value.Number = 20;
            store.Save();
            return store;
        }

        [Test] public void MissingFiles_CreateDefaultsWithoutWriting()
        {
            var store = Store();
            var result = store.Load(Defaults);
            Assert.AreEqual(SettingsLoadSource.Defaults, result.Source);
            Assert.AreEqual(10, store.Value.Number);
            Assert.IsFalse(Directory.Exists(_directory));
        }

        [Test] public void AccessAndSave_RequireSuccessfulLoad()
        {
            var store = Store();
            Assert.Throws<InvalidOperationException>(() => store.Save());
            Assert.Throws<InvalidOperationException>(() => { var ignored = store.Value; });
            Assert.Throws<InvalidOperationException>(() => store.SetValue(Defaults()));
            Assert.IsFalse(Directory.Exists(_directory));
        }

        [Test] public void SaveRoundTrip_RotatesPreviousValidPrimaryToBackup()
        {
            var saved = SavedTwice();
            var store = Store();
            Assert.AreEqual(SettingsLoadSource.Primary, store.Load(Defaults).Source);
            Assert.AreEqual(20, store.Value.Number);
            File.Delete(saved.FilePath);
            var recovered = Store();
            Assert.AreEqual(SettingsLoadSource.Backup, recovered.Load(Defaults).Source);
            Assert.AreEqual(10, recovered.Value.Number);
        }

        [Test] public void SetValue_ChangesMemoryUntilExplicitSave()
        {
            var saved = SavedTwice();
            saved.SetValue(new Options { Number = 42 });
            var disk = Store(); disk.Load(Defaults);
            Assert.AreEqual(20, disk.Value.Number);
            saved.Save();
            var updated = Store(); updated.Load(Defaults);
            Assert.AreEqual(42, updated.Value.Number);
        }

        [Test] public void CorruptPrimary_RecoversBackupAndPreservesItOnRepair()
        {
            var saved = SavedTwice();
            var backup = File.ReadAllBytes(saved.BackupPath);
            File.WriteAllBytes(saved.FilePath, new byte[] { 1, 2, 3 });
            var recovered = Store();
            var result = recovered.Load(Defaults);
            Assert.AreEqual(SettingsLoadSource.Backup, result.Source);
            Assert.IsInstanceOf<InvalidDataException>(result.PrimaryError);
            Assert.AreEqual(10, recovered.Value.Number);
            recovered.Value.Number = 30; recovered.Save();
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(saved.BackupPath));
            var next = Store(); next.Load(Defaults);
            Assert.AreEqual(30, next.Value.Number);
        }

        [Test] public void ChecksumMismatch_RecoversBackup()
        {
            var saved = SavedTwice();
            var bytes = File.ReadAllBytes(saved.FilePath); bytes[16] ^= 1;
            File.WriteAllBytes(saved.FilePath, bytes);
            var store = Store();
            Assert.AreEqual(SettingsLoadSource.Backup, store.Load(Defaults).Source);
            Assert.AreEqual(10, store.Value.Number);
        }

        [Test] public void CorruptWithoutBackup_DoesNotCreateDefaultsOrPermitSave()
        {
            var store = Store(); Directory.CreateDirectory(_directory);
            File.WriteAllText(store.FilePath, "broken");
            Assert.Throws<InvalidDataException>(() => store.Load(() => throw new Exception("must not default")));
            Assert.Throws<InvalidOperationException>(() => store.Save());
            Assert.AreEqual("broken", File.ReadAllText(store.FilePath));
        }

        [Test] public void BothCorrupt_PreserveFilesAndReportBothErrors()
        {
            var store = SavedTwice();
            File.WriteAllText(store.FilePath, "primary-broken"); File.WriteAllText(store.BackupPath, "backup-broken");
            var error = Assert.Throws<AggregateException>(() => Store().Load(Defaults));
            Assert.AreEqual(2, error.InnerExceptions.Count);
            Assert.AreEqual("primary-broken", File.ReadAllText(store.FilePath));
            Assert.AreEqual("backup-broken", File.ReadAllText(store.BackupPath));
        }

        [Test] public void OnlyCorruptBackup_DoesNotResetDefaults()
        {
            var store = SavedTwice(); File.Delete(store.FilePath); File.WriteAllText(store.BackupPath, "bad");
            Assert.Throws<AggregateException>(() => Store().Load(Defaults));
        }

        [Test] public void OrphanTemporaryFile_IsNeverTreatedAsCommitted()
        {
            var saved = SavedTwice();
            File.Copy(saved.FilePath, saved.TemporaryPath);
            File.Delete(saved.FilePath); File.Delete(saved.BackupPath);
            var store = Store();
            Assert.AreEqual(SettingsLoadSource.Defaults, store.Load(Defaults).Source);
            Assert.AreEqual(10, store.Value.Number);
        }

        [Test] public void InterruptedBeforeRotation_UsesPrimaryAndIgnoresNewTemporary()
        {
            var saved = SavedTwice(); File.Copy(saved.BackupPath, saved.TemporaryPath);
            var store = Store(); Assert.AreEqual(SettingsLoadSource.Primary, store.Load(Defaults).Source);
            Assert.AreEqual(20, store.Value.Number);
        }

        [Test] public void InterruptedAfterRotation_UsesBackupAndCanSaveAgain()
        {
            var saved = SavedTwice(); File.Delete(saved.BackupPath); File.Move(saved.FilePath, saved.BackupPath);
            File.WriteAllText(saved.TemporaryPath, "partial");
            var store = Store(); Assert.AreEqual(SettingsLoadSource.Backup, store.Load(Defaults).Source);
            Assert.AreEqual(20, store.Value.Number);
            store.Value.Number = 30; store.Save();
            var next = Store(); next.Load(Defaults); Assert.AreEqual(30, next.Value.Number);
            File.Delete(next.FilePath);
            var old = Store(); old.Load(Defaults); Assert.AreEqual(20, old.Value.Number);
        }

        [Test] public void TemporaryWriteFailure_PreservesBothCommittedFiles_AndCanRetry()
        {
            var saved = SavedTwice();
            var primary = File.ReadAllBytes(saved.FilePath); var backup = File.ReadAllBytes(saved.BackupPath);
            Directory.CreateDirectory(saved.TemporaryPath);
            Assert.Catch(() => saved.Save());
            CollectionAssert.AreEqual(primary, File.ReadAllBytes(saved.FilePath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(saved.BackupPath));
            Directory.Delete(saved.TemporaryPath);
            saved.Value.Number = 40; saved.Save();
            var next = Store(); next.Load(Defaults); Assert.AreEqual(40, next.Value.Number);
        }

        [Test] public void BackupRotationFailure_DoesNotRemovePrimary_AndCanRetry()
        {
            var saved = SavedTwice(); var primary = File.ReadAllBytes(saved.FilePath);
            File.Delete(saved.BackupPath); Directory.CreateDirectory(saved.BackupPath);
            Assert.Catch(() => saved.Save());
            CollectionAssert.AreEqual(primary, File.ReadAllBytes(saved.FilePath));
            Directory.Delete(saved.BackupPath);
            saved.Value.Number = 40; saved.Save();
            var next = Store(); next.Load(Defaults); Assert.AreEqual(40, next.Value.Number);
        }

        [Test] public void EncodeFailure_DoesNotTouchCommittedFiles()
        {
            var saved = SavedTwice(); var codec = new Codec { Encoder = _ => throw new InvalidDataException("encode failed") };
            var store = Store(codec); store.Load(Defaults);
            var primary = File.ReadAllBytes(saved.FilePath); var backup = File.ReadAllBytes(saved.BackupPath);
            Assert.Throws<InvalidDataException>(() => store.Save());
            CollectionAssert.AreEqual(primary, File.ReadAllBytes(saved.FilePath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(saved.BackupPath));
        }

        [Test] public void OversizeEncode_FailsBeforeWriting()
        {
            var codec = new Codec { Encoder = _ => new byte[17] };
            var store = Store(codec, 16); store.Load(Defaults);
            Assert.Throws<InvalidDataException>(() => store.Save());
            Assert.IsFalse(Directory.Exists(_directory));
        }

        [Test] public void OversizeFile_IsRejectedBeforeCodec()
        {
            var codec = new Codec { Decoder = (_, __) => throw new Exception("must not decode") };
            var store = Store(codec, 16); Directory.CreateDirectory(_directory);
            File.WriteAllBytes(store.FilePath, new byte[65]);
            Assert.Throws<SettingsCompatibilityException>(() => store.Load(Defaults));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ReadLimit_DoesNotRollbackLargerValidPrimaryToSmallerBackup(int schema)
        {
            var saved = Store(new Codec { Version = schema }); saved.Load(Defaults); saved.Save();
            saved.Value.Number = 100000; saved.Save();
            var primary = File.ReadAllBytes(saved.FilePath); var backup = File.ReadAllBytes(saved.BackupPath);
            var limited = Store(limit: 3);
            Assert.Throws<SettingsCompatibilityException>(() => limited.Load(Defaults));
            Assert.Throws<InvalidOperationException>(() => limited.Save());
            CollectionAssert.AreEqual(primary, File.ReadAllBytes(saved.FilePath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(saved.BackupPath));
        }

        [Test] public void FutureSchema_DoesNotFallbackToOlderBackupOrAllowOverwrite()
        {
            SavedTwice();
            var future = Store(new Codec { Version = 2 }); future.Load(Defaults);
            future.Value.Number = 50; future.Save();
            var bytes = File.ReadAllBytes(future.FilePath);
            var old = Store();
            Assert.Throws<SettingsCompatibilityException>(() => old.Load(Defaults));
            Assert.Throws<InvalidOperationException>(() => old.Save());
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(future.FilePath));
        }

        [Test] public void FutureBackup_WithMissingPrimary_IsNotReset()
        {
            var saved = Store(new Codec { Version = 2 }); saved.Load(Defaults); saved.Save(); saved.Save();
            File.Delete(saved.FilePath);
            Assert.Throws<SettingsCompatibilityException>(() => Store().Load(Defaults));
        }

        [Test] public void Migration_IsInMemoryUntilSave_AndRunsOnlyForOldSchema()
        {
            var old = SavedTwice(); var bytes = File.ReadAllBytes(old.FilePath); int migrations = 0;
            var codec = new Codec { Version = 2, Decoder = (payload, version) =>
            {
                int n = int.Parse(Encoding.UTF8.GetString(payload));
                if (version == 1) { migrations++; n += 100; }
                return new Options { Number = n };
            } };
            var store = Store(codec); var result = store.Load(Defaults);
            Assert.IsTrue(result.Migrated); Assert.AreEqual(1, result.StoredVersion); Assert.AreEqual(120, store.Value.Number);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(old.FilePath));
            store.Save();
            var next = Store(codec); Assert.IsFalse(next.Load(Defaults).Migrated);
            Assert.AreEqual(120, next.Value.Number); Assert.AreEqual(1, migrations);
        }

        [Test] public void UnsupportedMigration_DoesNotFallbackOrOverwrite()
        {
            var saved = SavedTwice();
            var codec = new Codec { Version = 2, Decoder = (_, __) => throw new SettingsCompatibilityException("no migration") };
            var store = Store(codec); var bytes = File.ReadAllBytes(saved.FilePath);
            Assert.Throws<SettingsCompatibilityException>(() => store.Load(Defaults));
            Assert.Throws<InvalidOperationException>(() => store.Save());
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(saved.FilePath));
        }

        [Test] public void FutureEnvelopeFormat_IsNotOverwrittenOrTreatedAsCorruption()
        {
            var saved = SavedTwice(); var bytes = File.ReadAllBytes(saved.FilePath);
            bytes[4] = 2; // 未来完整格式：校验仍然正确，而不是损坏的头。
            using (var hash = System.Security.Cryptography.SHA256.Create())
                Buffer.BlockCopy(hash.ComputeHash(bytes, 0, bytes.Length - 32), 0, bytes, bytes.Length - 32, 32);
            File.WriteAllBytes(saved.FilePath, bytes);
            var store = Store();
            Assert.Throws<SettingsCompatibilityException>(() => store.Load(Defaults));
            Assert.Throws<InvalidOperationException>(() => store.Save());
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(saved.FilePath));
        }

        [Test] public void FileAccessFailure_DoesNotSilentlyFallbackToBackup()
        {
            var saved = SavedTwice(); File.Delete(saved.FilePath); Directory.CreateDirectory(saved.FilePath);
            var store = Store();
            var error = Assert.Catch(() => store.Load(Defaults));
            Assert.IsTrue(error is IOException || error is UnauthorizedAccessException, error.ToString());
            Assert.Throws<InvalidOperationException>(() => store.Save());
            Assert.IsTrue(File.Exists(saved.BackupPath));
        }

        [Test] public void UnexpectedCodecError_IsNotMisreportedAsCorruption()
        {
            SavedTwice();
            var codec = new Codec { Decoder = (_, __) => throw new InvalidOperationException("bug") };
            Assert.Throws<InvalidOperationException>(() => Store(codec).Load(Defaults));
        }

        [Test] public void SemanticCorruption_UsesBackup()
        {
            SavedTwice();
            var codec = new Codec { Decoder = (payload, _) =>
            {
                int n = int.Parse(Encoding.UTF8.GetString(payload));
                if (n == 20) throw new InvalidDataException("invalid setting value");
                return new Options { Number = n };
            } };
            var store = Store(codec); Assert.AreEqual(SettingsLoadSource.Backup, store.Load(Defaults).Source);
            Assert.AreEqual(10, store.Value.Number);
        }

        [Test] public void FailedDefaultFactory_CanRetryLoadButCannotSave()
        {
            var store = Store();
            Assert.Throws<InvalidOperationException>(() => store.Load(() => throw new InvalidOperationException("factory")));
            Assert.Throws<InvalidOperationException>(() => store.Save());
            Assert.AreEqual(SettingsLoadSource.Defaults, store.Load(Defaults).Source);
        }

        [Test] public void NullDefaultAndNullEncodedPayload_AreRejected()
        {
            var store = Store(new Codec { Encoder = _ => null });
            Assert.Throws<InvalidDataException>(() => store.Load(() => null));
            store.Load(Defaults);
            Assert.Throws<InvalidDataException>(() => store.Save());
        }

        [Test] public void SuccessfulLoad_CannotBeRepeated()
        {
            var store = Store(); store.Load(Defaults);
            Assert.Throws<InvalidOperationException>(() => store.Load(Defaults));
        }

        [Test] public void ReentrantDefaultFactory_IsRejectedWithoutMarkingLoaded()
        {
            var store = Store();
            Assert.Throws<InvalidOperationException>(() => store.Load(() => { store.Load(Defaults); return Defaults(); }));
            Assert.Throws<InvalidOperationException>(() => store.Save());
            store.Load(Defaults);
        }

        [Test] public void WorkerSave_IsRejectedBeforeEncodingOrFileAccess()
        {
            int encodes = 0;
            var store = Store(new Codec { Encoder = _ => { encodes++; return new byte[1]; } }); store.Load(Defaults);
            Exception error = null;
            var thread = new Thread(() => { try { store.Save(); } catch (Exception e) { error = e; } }) { IsBackground = true };
            thread.Start(); Assert.IsTrue(thread.Join(3000));
            Assert.IsInstanceOf<InvalidOperationException>(error); Assert.AreEqual(0, encodes);
            Assert.IsFalse(Directory.Exists(_directory));
        }

        [TestCase("../escape")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("a.txt")]
        [TestCase("")]
        [TestCase("中文")]
        public void Id_CannotEscapeOrAliasStorageFiles(string id)
        {
            Assert.Throws<ArgumentException>(() => new FileSettingsStore<Options>(_directory, id, new Codec()));
        }
    }
}
