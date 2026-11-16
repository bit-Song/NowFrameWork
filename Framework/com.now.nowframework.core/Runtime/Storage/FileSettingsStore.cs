using System;
using System.IO;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Core.Storage
{
    /// <summary>
    /// 仅主线程。由项目启动器创建并持有：同一路径只能有一个写入者，不支持多进程并发。
    /// 用于小型设置/存档；同步显式保存，不在 Tick、Dispose 或退出钩子中自动写盘。
    /// </summary>
    public sealed class FileSettingsStore<T> where T : class
    {
        readonly ISettingsCodec<T> _codec;
        readonly int _version;
        readonly int _maxPayloadBytes;
        bool _loaded;
        bool _busy;
        bool _primaryValid;
        T _value;

        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public string TemporaryPath => FilePath + ".tmp";

        public FileSettingsStore(string directory, string id, ISettingsCodec<T> codec, int maxPayloadBytes = 1024 * 1024)
        {
            MainThreadGuard.Assert("FileSettingsStore.ctor");
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("必须指定可写目录", nameof(directory));
            ValidateId(id);
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _version = codec.Version;
            if (_version < 1) throw new ArgumentOutOfRangeException(nameof(codec), "schema 从 1 开始");
            if (maxPayloadBytes < 1 || maxPayloadBytes > int.MaxValue - SettingsEnvelope.Overhead)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
            _maxPayloadBytes = maxPayloadBytes;
            FilePath = Path.Combine(Path.GetFullPath(directory), "settings-" + id + ".nwf");
        }

        /// <summary>借用的可变 DTO；宿主修改后显式 Save。Load 成功前不可访问。</summary>
        public T Value
        {
            get
            {
                MainThreadGuard.Assert("FileSettingsStore.Value");
                EnsureLoaded();
                if (_busy) throw new InvalidOperationException("Settings 操作期间不允许重入");
                return _value;
            }
        }

        /// <summary>仅允许成功加载一次；两个文件都不存在才创建默认值。失败不使实例进入可写状态。</summary>
        public SettingsLoadResult Load(Func<T> createDefault)
        {
            MainThreadGuard.Assert("FileSettingsStore.Load");
            if (createDefault == null) throw new ArgumentNullException(nameof(createDefault));
            if (_loaded) throw new InvalidOperationException("Settings 已加载；重新读取请创建新宿主实例");
            Enter();
            try
            {
                Exception primaryError = null;
                T value;
                int version;
                try
                {
                    if (TryRead(FilePath, out value, out version))
                        return CompleteLoad(value, SettingsLoadSource.Primary, version, null);
                }
                catch (InvalidDataException error) { primaryError = error; }
                try
                {
                    if (TryRead(BackupPath, out value, out version))
                        return CompleteLoad(value, SettingsLoadSource.Backup, version, primaryError);
                }
                catch (InvalidDataException backupError)
                {
                    throw new AggregateException("Settings 主文件缺失/损坏，备份也损坏；原文件已保留",
                        primaryError ?? new FileNotFoundException("Settings 主文件不存在", FilePath), backupError);
                }
                if (primaryError != null)
                    throw new InvalidDataException("Settings 主文件损坏且没有备份；原文件已保留", primaryError);
                value = createDefault();
                if (value == null) throw new InvalidDataException("Settings 默认工厂返回了 null");
                return CompleteLoad(value, SettingsLoadSource.Defaults, _version, null);
            }
            finally { _busy = false; }
        }

        /// <summary>只替换内存对象，不写盘。对象的语义校验由 codec 在 Save 时执行。</summary>
        public void SetValue(T value)
        {
            MainThreadGuard.Assert("FileSettingsStore.SetValue");
            EnsureLoaded();
            if (_busy) throw new InvalidOperationException("Settings 操作期间不允许重入");
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>完整临时文件关闭后轮换备份，再安装主文件；任何失败向宿主传播，可重试。</summary>
        public void Save()
        {
            MainThreadGuard.Assert("FileSettingsStore.Save");
            EnsureLoaded();
            Enter();
            try
            {
                byte[] bytes = SettingsEnvelope.Encode(_codec.Encode(_value), _version, _maxPayloadBytes);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var stream = new FileStream(TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                InstallTemporary();
            }
            finally { _busy = false; }
        }

        void InstallTemporary()
        {
            if (_primaryValid)
            {
                // 删除旧备份失败时主文件仍完整；移动主文件后备份成为恢复点。
                File.Delete(BackupPath);
                File.Move(FilePath, BackupPath);
                _primaryValid = false;
            }
            else
            {
                // 从备份恢复时不让损坏的主文件覆盖唯一有效备份。
                File.Delete(FilePath);
            }
            File.Move(TemporaryPath, FilePath);
            _primaryValid = true;
        }

        bool TryRead(string file, out T value, out int version)
        {
            value = null;
            version = 0;
            byte[] bytes;
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    long length = stream.Length;
                    if (length > (long)_maxPayloadBytes + SettingsEnvelope.Overhead)
                        throw new SettingsCompatibilityException("Settings 文件超出当前读取上限，拒绝自动回退或覆盖：" + file);
                    if (length < SettingsEnvelope.Overhead)
                        throw new InvalidDataException("Settings 文件被截断：" + file);
                    bytes = new byte[(int)length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) throw new InvalidDataException("Settings 文件读取不完整：" + file);
                        offset += read;
                    }
                }
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            byte[] payload = SettingsEnvelope.Decode(bytes, _version, out version);
            value = _codec.Decode(payload, version);
            if (value == null) throw new InvalidDataException("Settings codec 返回了 null 对象");
            return true;
        }

        SettingsLoadResult CompleteLoad(T value, SettingsLoadSource source, int version, Exception error)
        {
            _value = value;
            _primaryValid = source == SettingsLoadSource.Primary;
            _loaded = true;
            return new SettingsLoadResult(source, version, version != _version, error);
        }

        void EnsureLoaded()
        {
            if (!_loaded) throw new InvalidOperationException("必须先成功 Load Settings 才能访问或保存");
        }

        void Enter()
        {
            if (_busy) throw new InvalidOperationException("Settings 操作期间不允许重入");
            _busy = true;
        }

        static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) throw new ArgumentException("Settings id 长度必须为 1~64", nameof(id));
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    throw new ArgumentException("Settings id 只允许 ASCII 字母、数字、短横线和下划线", nameof(id));
            }
        }
    }
}
