using System.Collections.Generic;
using NowFrameWork.Core.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 重绑定演示（UGUI）：交互式改键、取消、保存与恢复。
    ///
    /// 运行时自建 Canvas/Button，不依赖预制体，也不与其它 OnGUI 调试面板抢点击。
    /// 保存用 PlayerPrefs（演示）；正式项目应写入设置存储。
    /// </summary>
    public sealed class InputRebindingDemo : MonoBehaviour
    {
        const string PrefsKey = "nowframework.input.bindingOverrides";

        // 只列单一 binding 的动作；Move 是 2D 复合（W/S/A/D 四条），
        // 直接重绑定会命中复合节点，Input System 内部会因无单一状态而抛异常。
        static readonly string[] ActionPaths =
        {
            "Player/Jump",
            "Player/Interact",
        };

        readonly List<(InputAction Action, Text Label)> _rows = new List<(InputAction, Text)>(3);

        InputActionAsset _asset;
        InputActionRebindingExtensions.RebindingOperation _operation;
        Text _log;
        Font _font;

        void Awake()
        {
            if (!MainThreadGuard.IsMainThread) MainThreadGuard.BindCurrentThread();

            _asset = InputDemo.BuildDefaultActions();
            Restore();
            _asset.Enable();

            BuildUI();
        }

        void OnDestroy()
        {
            _operation?.Dispose();
            if (_asset != null) Destroy(_asset);
        }

        // ---------------- 重绑定逻辑 ----------------

        public void StartRebind(string actionPath)
        {
            if (_operation != null) return;

            InputAction action = _asset.FindAction(actionPath, throwIfNotFound: false);
            if (action == null)
            {
                SetLog("找不到动作: " + actionPath);
                return;
            }

            bool wasEnabled = action.enabled;
            action.Disable();
            SetLog("请按下新按键（Esc 取消）：" + actionPath);

            _operation = action.PerformInteractiveRebinding()
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(operation => Finish(operation, action, wasEnabled))
                .OnCancel(operation => Finish(operation, action, wasEnabled));

            _operation.Start();
        }

        void Finish(InputActionRebindingExtensions.RebindingOperation operation, InputAction action, bool wasEnabled)
        {
            operation.Dispose();
            _operation = null;

            if (wasEnabled) action.Enable();

            Save();
            RefreshLabels();
            SetLog(action.name + " 当前绑定: " + action.GetBindingDisplayString());
        }

        public void ResetAll()
        {
            _asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            RefreshLabels();
            SetLog("已恢复默认绑定");
        }

        void Save()
        {
            PlayerPrefs.SetString(PrefsKey, _asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        void Restore()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json)) _asset.LoadBindingOverridesFromJson(json);
        }

        void RefreshLabels()
        {
            foreach ((InputAction action, Text label) in _rows)
                if (label != null) label.text = action.name + " = " + action.GetBindingDisplayString();
        }

        void SetLog(string message)
        {
            if (_log != null) _log.text = message;
        }

        // ---------------- UGUI ----------------

        void BuildUI()
        {
            EnsureEventSystem();

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("Rebinding Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform panel = CreateRect(canvasGo.transform, "Panel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(620f, 330f));

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.72f);

            _log = CreateText(panel, "Log", new Vector2(20f, -16f), new Vector2(580f, 40f), 18);
            SetLog("点击「改键」后按下新按键，Esc 取消");

            float y = -64f;

            foreach (string path in ActionPaths)
            {
                InputAction action = _asset.FindAction(path, throwIfNotFound: false);

                Text label = CreateText(panel, "Label-" + path, new Vector2(20f, y), new Vector2(430f, 40f), 20);
                label.text = action != null ? action.name + " = " + action.GetBindingDisplayString() : path;

                string captured = path;
                CreateButton(panel, "改键", new Vector2(470f, y), new Vector2(120f, 40f), () => StartRebind(captured));

                _rows.Add((action, label));
                y -= 52f;
            }

            CreateButton(panel, "恢复默认", new Vector2(20f, y - 6f), new Vector2(180f, 40f), ResetAll);

            RefreshLabels();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();

            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return rect;
        }

        Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            RectTransform rect = CreateRect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), position, size);

            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            return text;
        }

        void CreateButton(Transform parent, string caption, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = CreateRect(parent, "Button-" + caption, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), position, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.20f, 0.45f, 0.85f, 0.95f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text label = CreateText(rect, "Text", new Vector2(0f, 0f), size, 18);
            label.text = caption;
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }
    }
}