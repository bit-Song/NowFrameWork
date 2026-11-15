using System.Collections.Generic;
using System.Text;
using NowFrameWork.Assets;
using NowFrameWork.Core.Diagnostics;
using UnityEngine;

namespace NowFrameWork.Diagnostics
{
    /// <summary>
    /// 运行时诊断面板（左下角常驻）。
    ///
    /// 显示：资源租约、在途加载、租约明细、错误预算、FPS。
    ///
    /// 不依赖任何输入系统：呼出/隐藏通过 <see cref="Visible"/> 由业务控制，
    /// 避免 ENABLE_INPUT_SYSTEM / ENABLE_LEGACY_INPUT_MANAGER 的兼容问题。
    ///
    /// 只在 Editor / Development Build 生效，正式包不会创建。
    /// </summary>
    public sealed class DiagnosticsOverlay : MonoBehaviour
    {
        static DiagnosticsOverlay _instance;

        IAssetService _assets;
        ErrorBudget _errors;

        float _fps;
        float _fpsTimer;
        int _fpsFrames;

        readonly List<LeaseInfo> _leaseBuffer = new List<LeaseInfo>(32);
        readonly List<ErrorEntry> _errorBuffer = new List<ErrorEntry>(16);
        readonly StringBuilder _text = new StringBuilder(512);

        public bool Visible { get; set; } = true;

        /// <summary>创建（或复用）诊断面板。重复调用安全。</summary>
        public static DiagnosticsOverlay Install(IAssetService assets, ErrorBudget errors = null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_instance != null)
            {
                _instance._assets = assets;
                _instance._errors = errors;
                return _instance;
            }

            var go = new GameObject("[DiagnosticsOverlay]");
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<DiagnosticsOverlay>();
            _instance._assets = assets;
            _instance._errors = errors;

            return _instance;
#else
            return null;
#endif
        }

        void Update()
        {
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;

            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrames / _fpsTimer;
                _fpsFrames = 0;
                _fpsTimer = 0f;
            }
        }

        void OnGUI()
        {
            if (!Visible) return;

            _text.Clear();
            _text.AppendLine("NowFrameWork 诊断");
            _text.AppendLine("FPS: " + _fps.ToString("F0"));

            if (_assets != null)
            {
                _text.AppendLine("资源租约: " + _assets.ActiveLeases);
                _text.AppendLine("在途加载: " + _assets.PendingRequests);

                _assets.DumpLeases(_leaseBuffer);

                for (int i = 0; i < _leaseBuffer.Count && i < 8; i++)
                    _text.AppendLine("  " + _leaseBuffer[i]);
            }
            else
            {
                _text.AppendLine("（未接入资源服务）");
            }

            if (_errors != null)
            {
                _errors.Snapshot(_errorBuffer);

                _text.AppendLine("错误 " + _errors.TotalCount + "（去重 " + _errors.DistinctCount + "）");

                for (int i = 0; i < _errorBuffer.Count && i < 4; i++)
                {
                    ErrorEntry entry = _errorBuffer[i];
                    _text.AppendLine("  " + (entry.Fatal ? "[致命] " : "") + entry.Module + " x" + entry.Count);
                }
            }

            GUI.Box(new Rect(8, Screen.height - 260, 420, 250), _text.ToString());
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}