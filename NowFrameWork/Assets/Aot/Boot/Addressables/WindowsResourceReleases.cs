using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Unity.Storage;
using UnityEngine;

namespace NowFrameWork.Boot
{
    /// <summary>可选的普通 AOT 发布接入。每个进程由一个 BootFlow 装配一次。</summary>
    public sealed class WindowsResourceReleases : MonoBehaviour
    {
        [SerializeField] string _clientBuildId = "windows-aot-v1";
        [SerializeField] string _contract = "1";
        [SerializeField] int _saveSchemaMin = 1;
        [SerializeField] int _saveSchemaMax = 1;
        [SerializeField] int _saveSchemaWrite = 1;

        ReleaseStore _store;
        ReleaseSelection _selection;
        bool _selected;
        bool _failedTrial;

        ReleaseStore Store => _store ??= new ReleaseStore(Path.Combine(Application.persistentDataPath, "WindowsReleases"),
            new ReleaseCompatibility("Windows", "x64", _clientBuildId, _contract, "builtin",
                _saveSchemaMin, _saveSchemaMax, _saveSchemaWrite),
            new JsonSettingsCodec<ReleaseState>());

        internal async UniTask<ReleaseSelection> SelectAsync(CancellationToken ct)
        {
            RequireWindows();
            if (_failedTrial) throw new InvalidOperationException("试用启动失败，请重启以恢复上一版");
            if (_selected) return _selection;
            try { _selection = await Store.SelectForBootAsync(ct); }
            catch (Exception error) when (error is IOException || error is InvalidDataException ||
                                          error is UnauthorizedAccessException || error is AggregateException ||
                                          error is NowFrameWork.Core.Storage.SettingsCompatibilityException)
            {
                Debug.LogWarning("发布状态不可用，使用随包内容: " + error.Message, this);
            }
            _selected = true;
            return _selection;
        }

        internal void Running()
        {
            if (_selection?.IsTrial != true) return;
            Store.ConfirmRunning(_selection);
            _selection = null; // BootFlow 已持有本进程选中的发布集。
        }

        internal void Failed()
        {
            if (_selection?.IsTrial == true) _failedTrial = true;
        }

        /// <summary>把已解析的发布清单和传输函数接入框架。成功后在下一次进程启动试用。</summary>
        public UniTask StageAsync(ResourceRelease release, Func<string, Stream, CancellationToken, UniTask> copy,
            CancellationToken ct = default)
        {
            RequireWindows();
            return Store.StageAsync(release, copy, ct);
        }

        /// <summary>本地发布目录接入，用于离线安装和 Windows 验收。</summary>
        public UniTask StageFromDirectoryAsync(string directory, CancellationToken ct = default)
        {
            RequireWindows();
            ct.ThrowIfCancellationRequested();
            string root = Path.GetFullPath(directory);
            var manifest = JsonUtility.FromJson<ResourceRelease>(File.ReadAllText(Path.Combine(root, "release.json")));
            return StageAsync(manifest, async (path, output, token) =>
            {
                using var input = new FileStream(Path.Combine(root, path), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, useAsync: true);
                await input.CopyToAsync(output, 65536, token);
            }, ct);
        }

        /// <summary>从发布清单所在目录下载整套内容。正式发布应使用可信的 HTTPS 地址。</summary>
        public async UniTask StageFromServerAsync(Uri manifestUri, CancellationToken ct = default)
        {
            RequireWindows();
            if (manifestUri == null || !manifestUri.IsAbsoluteUri ||
                (manifestUri.Scheme != Uri.UriSchemeHttps && manifestUri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("须提供 HTTP(S) 发布清单地址", nameof(manifestUri));
            using var client = new HttpClient { MaxResponseContentBufferSize = 1024 * 1024 };
            using var response = await client.GetAsync(manifestUri, ct);
            response.EnsureSuccessStatusCode();
            var manifest = JsonUtility.FromJson<ResourceRelease>(await response.Content.ReadAsStringAsync());
            await StageAsync(manifest, async (path, output, token) =>
            {
                using var file = await client.GetAsync(new Uri(manifestUri, path), HttpCompletionOption.ResponseHeadersRead, token);
                file.EnsureSuccessStatusCode();
                using var input = await file.Content.ReadAsStreamAsync();
                await input.CopyToAsync(output, 65536, token);
            }, ct);
        }

        static void RequireWindows()
        {
            if ((Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) ||
                RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("发布样例仅支持 Windows x64 普通 AOT");
        }
    }
}
