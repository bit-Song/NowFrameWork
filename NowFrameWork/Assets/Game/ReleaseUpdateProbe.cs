using System;
using NowFrameWork.Boot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>
    /// Windows 资源发布验收探针：按 F9 从本地 HTTP 服务下载并暂存指定发布集。
    ///
    /// 只用于端到端验收：StageFromServerAsync 带 RequireWindows()，编辑器里调用会抛异常，
    /// 必须出 Windows 包后使用。暂存成功后要重启进程才会试用新版。
    /// </summary>
    public sealed class ReleaseUpdateProbe : MonoBehaviour
    {
        [SerializeField] string _serverRoot = "http://127.0.0.1:8080";
        [SerializeField] string _release = "v2";
        [Tooltip("备用版本，F10 触发；用于坏文件/回退测试（同 ID 不能覆盖已确认版本）")]
        [SerializeField] string _alternateRelease = "v3";
        [SerializeField] bool _showHud = true;

        WindowsResourceReleases _releases;
        string _status = "待下载（F9 开始）";

        void Awake()
        {
            _releases = FindObjectOfType<WindowsResourceReleases>(true);

            if (_releases == null)
                _status = "场景里没有 WindowsResourceReleases";
        }

        void Update()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.f9Key.wasPressedThisFrame) Download(_release);
            if (Keyboard.current.f10Key.wasPressedThisFrame) Download(_alternateRelease);
        }

        public void Download() => Download(_release);

        public async void Download(string release)
        {
            if (_releases == null)
            {
                _status = "找不到 WindowsResourceReleases";
                return;
            }

            var uri = new Uri(_serverRoot.TrimEnd('/') + "/" + release + "/release.json");

            _status = "下载中: " + uri;
            Debug.Log("[ReleaseProbe] 开始 " + uri);

            try
            {
                await _releases.StageFromServerAsync(uri);

                _status = "已暂存 " + release + "，重启进程后试用";
                Debug.Log("[ReleaseProbe] 暂存完成: " + release);
            }
            catch (Exception error)
            {
                _status = "失败: " + error.GetType().Name + " " + error.Message;
                Debug.LogError("[ReleaseProbe] 失败: " + error);
            }
        }

        void OnGUI()
        {
            if (!_showHud) return;

            GUILayout.BeginArea(new Rect(10f, 10f, 560f, 110f), GUI.skin.box);
            GUILayout.Label("资源发布验收探针");
            GUILayout.Label("服务: " + _serverRoot + " / " + _release);
            GUILayout.Label(_status);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("下载 " + _release + "（F9）")) Download(_release);
            if (GUILayout.Button("下载 " + _alternateRelease + "（F10）")) Download(_alternateRelease);

            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}