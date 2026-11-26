using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Storage;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Assets
{
    [Serializable]
    public sealed class ReleaseState
    {
        // Unity's inline JSON serialization can materialize a default DTO for a null reference.
        /// <summary>已确认可用的发布清单。</summary>
        public ResourceRelease ConfirmedManifest;
        /// <summary>已暂存、待下次启动试用的清单。</summary>
        public ResourceRelease PendingManifest;
        /// <summary>本次启动正在试用的清单。</summary>
        public ResourceRelease TrialManifest;

        /// <summary>已确认版本（持久化用）。</summary>
        public ResourceRelease KnownGood { get => Present(ConfirmedManifest); set => ConfirmedManifest = value; }
        public ResourceRelease Pending { get => Present(PendingManifest); set => PendingManifest = value; }
        public ResourceRelease Trial { get => Present(TrialManifest); set => TrialManifest = value; }

        static ResourceRelease Present(ResourceRelease release) => string.IsNullOrEmpty(release?.Id) ? null : release;
    }

    public sealed class ReleaseSelection
    {
        /// <summary>发布集 ID。</summary>
        public string Id { get; }
        /// <summary>发布集落盘目录。</summary>
        public string Directory { get; }
        /// <summary>Catalog 文件路径。</summary>
        public string CatalogPath { get; }
        /// <summary>本次选择是否为试用。</summary>
        public bool IsTrial { get; }
        readonly HashSet<string> _files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal ReleaseSelection(ResourceRelease release, string directory, bool trial)
        {
            Id = release.Id;
            Directory = directory;
            CatalogPath = Path.Combine(directory, release.CatalogPath);
            IsTrial = trial;
            foreach (ReleaseFile file in release.Files) _files.Add(Path.GetFullPath(Path.Combine(directory, file.Path)));
        }

        /// <summary>路径是否属于本发布集（防目录穿越）。</summary>
        public bool ContainsFile(string path) => Path.IsPathRooted(path) && _files.Contains(Path.GetFullPath(path));
    }

    /// <summary>单写入者。发布目录安装后保持只读，已知可用版本由 Boot 到达 Running 后确认。</summary>
    public sealed class ReleaseStore
    {
        readonly string _root;
        readonly ReleaseCompatibility _compatibility;
        readonly FileSettingsStore<ReleaseState> _state;
        readonly ISettingsCodec<ReleaseState> _codec;
        bool _busy;
        ReleaseSelection _trial;

        /// <summary>创建发布存储；root 下按发布 ID 分目录。</summary>
        public ReleaseStore(string root, ReleaseCompatibility compatibility, ISettingsCodec<ReleaseState> codec)
        {
            _root = Path.GetFullPath(root);
            _compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _state = new FileSettingsStore<ReleaseState>(_root, "release-state", codec);
            _state.Load(() => new ReleaseState());
        }

        /// <summary>copy 将相对路径对应的文件内容写入流。它需要遵守取消请求。</summary>
        public async UniTask StageAsync(ResourceRelease release,
            Func<string, Stream, CancellationToken, UniTask> copy, CancellationToken ct = default)
        {
            CheckIdle();
            ct.ThrowIfCancellationRequested();
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            // 在异步下载期间使用调用方无法修改的快照。
            var snapshot = _codec.Decode(_codec.Encode(new ReleaseState { Pending = release }), _codec.Version).Pending;
            if (snapshot == null) throw new InvalidDataException("发布清单缺少标识");
            _compatibility.Check(snapshot);
            string destination = ReleasePath(snapshot.Id);
            if (_state.Value.KnownGood?.Id.Equals(snapshot.Id, StringComparison.OrdinalIgnoreCase) == true ||
                _state.Value.Trial?.Id.Equals(snapshot.Id, StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException("不可替换正在使用的发布集: " + snapshot.Id);

            _busy = true;
            string staging = Path.Combine(_root, "staging", Guid.NewGuid().ToString("N"));
            try
            {
                bool installed = System.IO.Directory.Exists(destination) && await IsUsableAsync(snapshot, ct);
                if (!installed)
                {
                    foreach (ReleaseFile file in snapshot.Files)
                    {
                        ct.ThrowIfCancellationRequested();
                        string target = Path.Combine(staging, file.Path);
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        await copy(file.Path, output, ct);
                        output.Flush(true);
                    }
                    await VerifyAsync(snapshot, staging, ct);
                    ct.ThrowIfCancellationRequested();
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    if (System.IO.Directory.Exists(destination))
                    {
                        string quarantine = Path.Combine(_root, "quarantine");
                        System.IO.Directory.CreateDirectory(quarantine);
                        System.IO.Directory.Move(destination, Path.Combine(quarantine, Guid.NewGuid().ToString("N")));
                    }
                    System.IO.Directory.Move(staging, destination);
                }
                ct.ThrowIfCancellationRequested();
                Commit(new ReleaseState { KnownGood = _state.Value.KnownGood, Pending = snapshot, Trial = _state.Value.Trial });
            }
            finally
            {
                _busy = false;
                // 中断时其它进程可留下未登记的暂存目录。它们永远不会参与启动选择。
                try { if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>每次进程启动调用一次。null 表示使用随包内容。</summary>
        public async UniTask<ReleaseSelection> SelectForBootAsync(CancellationToken ct = default)
        {
            CheckIdle();
            _busy = true;
            try
            {
                if (_trial != null) throw new InvalidOperationException("当前进程已开始试用发布集");
                ReleaseState state = _state.Value;
                ResourceRelease pending = state.Pending;
                if (pending != null && await IsUsableAsync(pending, ct))
                {
                    // 在创建 Provider 之前消耗试用资格。进程中断后只能回到已知可用版本。
                    Commit(new ReleaseState { KnownGood = state.KnownGood, Trial = pending });
                    return _trial = new ReleaseSelection(pending, ReleasePath(pending.Id), true);
                }

                ResourceRelease knownGood = state.KnownGood;
                bool usable = knownGood != null && await IsUsableAsync(knownGood, ct);
                if (state.Pending != null || state.Trial != null || (knownGood != null && !usable))
                    Commit(new ReleaseState { KnownGood = usable ? knownGood : null });
                return usable ? new ReleaseSelection(knownGood, ReleasePath(knownGood.Id), false) : null;
            }
            finally { _busy = false; }
        }

        /// <summary>Boot 进入 Running 后确认试用版本。</summary>
        public void ConfirmRunning(ReleaseSelection selection)
        {
            CheckIdle();
            if (selection == null || !selection.IsTrial) return;
            if (!ReferenceEquals(selection, _trial) || _state.Value.Trial == null)
                throw new InvalidOperationException("仅能确认当前启动选中的试用发布集");

            Commit(new ReleaseState { KnownGood = _state.Value.Trial, Pending = _state.Value.Pending });
            _trial = null;
        }

        async UniTask<bool> IsUsableAsync(ResourceRelease release, CancellationToken ct)
        {
            try
            {
                _compatibility.Check(release);
                await VerifyAsync(release, ReleasePath(release.Id), ct);
                return true;
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        static UniTask VerifyAsync(ResourceRelease release, string directory, CancellationToken ct)
            => UniTask.RunOnThreadPool(() =>
            {
                using var hash = SHA256.Create();
                foreach (ReleaseFile file in release.Files)
                {
                    ct.ThrowIfCancellationRequested();
                    using var stream = new FileStream(Path.Combine(directory, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length != file.Length || !string.Equals(
                        BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("资源长度或 SHA-256 不匹配: " + file.Path);
                }
            }, cancellationToken: ct);

        string ReleasePath(string id)
        {
            ResourceRelease.ValidateSegment(id);
            return Path.Combine(_root, "releases", id);
        }

        void Commit(ReleaseState next)
        {
            ReleaseState old = _state.Value;
            _state.SetValue(next);
            try { _state.Save(); }
            catch { _state.SetValue(old); throw; }
        }

        void CheckIdle()
        {
            MainThreadGuard.Assert();
            if (_busy) throw new InvalidOperationException("发布存储操作正在进行");
        }
    }
}
