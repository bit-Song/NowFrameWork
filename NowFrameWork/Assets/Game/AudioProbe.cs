using System;
using NowFrameWork.Audio;
using NowFrameWork.Boot;
using UnityEngine;

namespace Game
{
    /// <summary>音频真机验收探针：播放 BGM/SFX、显示暂停状态、记录低内存事件。</summary>
    public sealed class AudioProbe : MonoBehaviour
    {
        const string BgmKey = "audio/bgm";
        const string SfxKey = "audio/sfx";

        BootFlow _boot;
        int _lowMemory;
        string _status = "待播放";

        bool _lastPaused;

        void Start()
        {
            _boot = FindObjectOfType<BootFlow>(true);
            Application.lowMemory += OnLowMemory;
            StartCoroutine(AutoPlay());
        }

        /// <summary>真机验收时免去点击：启动 2 秒后播 BGM，再过 3 秒播一次 SFX。</summary>
        System.Collections.IEnumerator AutoPlay()
        {
            yield return new WaitForSeconds(2f);
            PlayBgm();

            yield return new WaitForSeconds(3f);
            PlaySfx();
        }

        void Update()
        {
            AudioModule audio = Audio;
            if (audio == null) return;

            if (audio.IsPaused != _lastPaused)
            {
                _lastPaused = audio.IsPaused;
                Debug.Log("[AudioProbe] IsPaused=" + _lastPaused);
            }
        }

        void OnDestroy() => Application.lowMemory -= OnLowMemory;

        void OnLowMemory()
        {
            _lowMemory++;
            Debug.Log("[AudioProbe] Application.lowMemory x" + _lowMemory);
        }

        AudioModule Audio => _boot != null && _boot.Context != null ? _boot.Context.Audio : null;

        async void PlayBgm()
        {
            try
            {
                AudioModule audio = Audio;
                if (audio == null) { _status = "音频模块未就绪"; return; }

                bool ok = await audio.PlayMusicAsync(BgmKey);
                _status = ok ? "BGM 播放中" : "BGM 未能播放";
                Debug.Log("[AudioProbe] PlayMusic -> " + ok);
            }
            catch (Exception error)
            {
                _status = "BGM 失败: " + error.Message;
                Debug.LogError("[AudioProbe] PlayMusic 失败: " + error);
            }
        }

        async void PlaySfx()
        {
            try
            {
                AudioModule audio = Audio;
                if (audio == null) { _status = "音频模块未就绪"; return; }

                bool ok = await audio.PlayEffectAsync(SfxKey);
                _status = ok ? "SFX 已播放" : "SFX 被拒（通道满或已停止）";
                Debug.Log("[AudioProbe] PlayEffect -> " + ok);
            }
            catch (Exception error)
            {
                _status = "SFX 失败: " + error.Message;
                Debug.LogError("[AudioProbe] PlayEffect 失败: " + error);
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, Screen.height - 150f, 560f, 140f), GUI.skin.box);

            AudioModule audio = Audio;

            GUILayout.Label("音频验收探针");
            GUILayout.Label("模块: " + (audio == null ? "未就绪" : (audio.IsPaused ? "暂停中" : "运行中")) +
                "  低内存事件: " + _lowMemory);
            GUILayout.Label("状态: " + _status);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("播放 BGM")) PlayBgm();
            if (GUILayout.Button("播放 SFX")) PlaySfx();
            if (GUILayout.Button("停止 BGM")) { if (audio != null) audio.StopMusic(); _status = "BGM 已停止"; }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
