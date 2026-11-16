using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using NowFrameWork.Core.Storage;
using NowFrameWork.Core.Threading;
using NowFrameWork.Unity.Storage;
using UnityEngine;

namespace NowFrameWork.Validation.Storage
{
    // 仅用于隔离消费工程；不向框架内置业务设置或自动保存策略。
    public sealed class StoragePlayerProbe : MonoBehaviour
    {
        [Serializable] public sealed class Options
        {
            public int Schema;
            public int Volume;
            public string Language;
            public bool Music;
        }
        [Serializable] public sealed class Report
        {
            public string phase;
            public string utc;
            public string unity;
            public string backend;
            public bool development;
            public bool finished;
            public bool passed;
            public string error;
            public List<string> checks = new List<string>();
        }
        Report _report;
        string _directory;

        void Start()
        {
            string output = Arg("--nw-report");
            _report = new Report { phase = Arg("--nw-phase"), utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                development = Debug.isDebugBuild };
#if ENABLE_IL2CPP
            _report.backend = "IL2CPP";
#else
            _report.backend = "Mono";
#endif
            try
            {
                _directory = Path.GetFullPath(Arg("--nw-root"));
                if (File.ReadAllText(Path.Combine(_directory, ".nw-storage-regression")) != "NowFrameWork.StorageRegression.v1")
                    throw new InvalidOperationException("缺少隔离数据目录标记，拒绝执行故障实验");
                Check(_report.backend == "IL2CPP", "IL2CPP backend");
                Check(_report.development && MainThreadGuard.IsMainThread, "development main-thread host bound");
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = assembly.GetName().Name;
                    if (name.IndexOf("HybridCLR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Addressable", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException("普通消费工程意外加载可选依赖：" + name);
                }
                Check(true, "no HybridCLR/Addressables assembly loaded");
                RunPhase(_report.phase);
                _report.passed = true;
            }
            catch (Exception error) { _report.error = error.ToString(); }
            _report.finished = true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllText(output, JsonUtility.ToJson(_report, true));
                Debug.Log("[StorageProbe] " + _report.phase + " " + (_report.passed ? "PASS" : "FAIL"));
            }
            catch (Exception error) { Debug.LogException(error); _report.passed = false; }
            Application.Quit(_report.passed ? 0 : 2);
        }

        FileSettingsStore<Options> Store(int schema = 1, int limit = 1024 * 1024)
        {
            var codec = new JsonSettingsCodec<Options>(schema, schema == 2 ? Migrate : (Func<string, int, Options>)null,
                dto => { if (dto.Schema != schema || dto.Volume < 0 || dto.Volume > 100 || string.IsNullOrEmpty(dto.Language))
                    throw new InvalidDataException("invalid fixture settings"); });
            return new FileSettingsStore<Options>(_directory, "player-probe", codec, limit);
        }

        static Options Migrate(string json, int from)
        {
            if (from != 1) throw new SettingsCompatibilityException("only schema 1 -> 2 supported");
            var value = JsonUtility.FromJson<Options>(json);
            value.Schema = 2; value.Music = true;
            return value;
        }

        static Options NoDefaults() => throw new InvalidOperationException("不得重置已有数据");

        void RunPhase(string phase)
        {
            switch (phase)
            {
                case "seed":
                    var seed = Store();
                    Check(!File.Exists(seed.FilePath) && !File.Exists(seed.BackupPath), "fresh isolated data files");
                    Check(seed.Load(() => new Options { Schema = 1, Volume = 25, Language = "简体中文" }).Source == SettingsLoadSource.Defaults, "defaults source");
                    Check(!File.Exists(seed.FilePath), "load defaults does not write");
                    seed.Save(); seed.Value.Volume = 75; seed.Save();
                    Check(File.Exists(seed.FilePath) && File.Exists(seed.BackupPath), "primary and backup committed");
                    break;
                case "read":
                    var read = Store();
                    Check(read.Load(NoDefaults).Source == SettingsLoadSource.Primary, "new process reads primary");
                    Check(read.Value.Volume == 75 && read.Value.Language == "简体中文", "saved fields and UTF-8 persist");
                    break;
                case "recover":
                    var recover = Store(); var backup = File.ReadAllBytes(recover.BackupPath);
                    File.WriteAllText(recover.FilePath, "injected truncated primary");
                    var result = recover.Load(NoDefaults);
                    Check(result.Source == SettingsLoadSource.Backup && result.PrimaryError is InvalidDataException, "corrupt primary reports backup recovery");
                    Check(recover.Value.Volume == 25, "previous committed value recovered");
                    recover.Value.Volume = 60; recover.Save();
                    Check(Equal(backup, File.ReadAllBytes(recover.BackupPath)), "repair preserves valid backup");
                    break;
                case "read-repaired":
                    var repaired = Store();
                    Check(repaired.Load(NoDefaults).Source == SettingsLoadSource.Primary && repaired.Value.Volume == 60, "repair survives process restart");
                    break;
                case "migrate":
                    var migrated = Store(2); var old = File.ReadAllBytes(migrated.FilePath);
                    var migration = migrated.Load(NoDefaults);
                    Check(migration.Migrated && migration.StoredVersion == 1 && migrated.Value.Music, "schema 1 migrates to 2");
                    Check(Equal(old, File.ReadAllBytes(migrated.FilePath)), "migration remains in memory until explicit save");
                    migrated.Save();
                    Check(Equal(old, File.ReadAllBytes(migrated.BackupPath)), "migration keeps old-schema backup");
                    break;
                case "read-v2":
                    var v2 = Store(2); var loaded = v2.Load(NoDefaults);
                    Check(!loaded.Migrated && loaded.StoredVersion == 2, "saved schema 2 reads without migration");
                    Check(v2.Value.Schema == 2 && v2.Value.Volume == 60 && v2.Value.Music, "migrated fields persist across process restart");
                    break;
                case "reject-old": RejectLoad(Store(), "future schema blocks old client"); break;
                case "limit": RejectLoad(Store(2, 1), "read limit cannot rollback to backup"); break;
                case "thread":
                    var threaded = Store(2); threaded.Load(NoDefaults);
                    var before = File.ReadAllBytes(threaded.FilePath); Exception workerError = null;
                    var worker = new Thread(() => { try { threaded.Save(); } catch (Exception error) { workerError = error; } }) { IsBackground = true };
                    worker.Start(); Check(worker.Join(3000), "worker assertion completes synchronously");
                    Check(workerError is InvalidOperationException && workerError.Message.Contains("FileSettingsStore.Save"), "development player rejects worker save");
                    Check(Equal(before, File.ReadAllBytes(threaded.FilePath)), "worker misuse leaves file unchanged");
                    break;
                case "corrupt-both":
                    var broken = Store(2); File.WriteAllText(broken.FilePath, "broken primary"); File.WriteAllText(broken.BackupPath, "broken backup");
                    var primaryBytes = File.ReadAllBytes(broken.FilePath); var backupBytes = File.ReadAllBytes(broken.BackupPath);
                    bool rejected = false; try { broken.Load(NoDefaults); } catch (AggregateException) { rejected = true; }
                    Check(rejected, "two corrupt files do not reset defaults");
                    bool noSave = false; try { broken.Save(); } catch (InvalidOperationException) { noSave = true; }
                    Check(noSave && Equal(primaryBytes, File.ReadAllBytes(broken.FilePath)) && Equal(backupBytes, File.ReadAllBytes(broken.BackupPath)), "failed load blocks overwrite and preserves corrupt evidence");
                    break;
                default: throw new ArgumentException("unknown storage phase: " + phase);
            }
        }

        void RejectLoad(FileSettingsStore<Options> store, string label)
        {
            var primary = File.ReadAllBytes(store.FilePath); var backup = File.ReadAllBytes(store.BackupPath);
            bool rejected = false; try { store.Load(NoDefaults); } catch (SettingsCompatibilityException) { rejected = true; }
            Check(rejected, label);
            bool noSave = false; try { store.Save(); } catch (InvalidOperationException) { noSave = true; }
            Check(noSave && Equal(primary, File.ReadAllBytes(store.FilePath)) && Equal(backup, File.ReadAllBytes(store.BackupPath)), "incompatible load blocks overwrite and preserves both files");
        }

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            _report.checks.Add(label);
        }

        static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            throw new ArgumentException("missing " + name);
        }
    }
}
