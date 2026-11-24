using System;
using System.Collections.Generic;
using System.IO;
using NowFrameWork.Core.Storage;
using NowFrameWork.Core.Threading;
using NowFrameWork.Input.InputSystem;
using NowFrameWork.Unity.Storage;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 重绑定演示（UGUI）：交互式改键、复合方向、冲突提示、保存与恢复。
    ///
    /// 运行时自建 Canvas/Button，不依赖预制体，也不与其它 OnGUI 调试面板抢点击。
    /// 持有动作资产和绑定设置，角色与改键界面共用这份运行时实例。
    /// </summary>
    public sealed class InputRebindingDemo : MonoBehaviour
    {
        const string LegacyPrefsKey = "nowframework.input.bindingOverrides.v2";
        const float RebindTimeoutSeconds = 10f;

        [Serializable]
        sealed class BindingPreferences
        {
            public string Overrides = "";
        }

        static readonly string[] ActionPaths =
        {
            "Player/Move",
            "Player/Jump",
            "Player/Interact",
        };

        readonly List<(InputAction Action, int BindingIndex, string Device, Text Label)> _rows =
            new List<(InputAction, int, string, Text)>(4);

        InputActionAsset _asset;
        FileSettingsStore<BindingPreferences> _settings;
        InputActionRebindingExtensions.RebindingOperation _operation;
        string _settingsWarning;
        Text _log;
        Font _font;

        public bool IsRebinding => _operation != null;

        /// <summary>加载绑定设置并启用动作。返回由本组件持有的资产。</summary>
        public InputActionAsset Initialize(string settingsDirectory = null)
        {
            if (_asset != null) return _asset;
            if (!MainThreadGuard.IsMainThread) MainThreadGuard.BindCurrentThread();

            _asset = InputDemo.BuildDefaultActions();
            try
            {
                string directory = settingsDirectory ?? Path.Combine(Application.persistentDataPath, "InputDemo");
                var store = new FileSettingsStore<BindingPreferences>(directory, "input-bindings",
                    new JsonSettingsCodec<BindingPreferences>());
                SettingsLoadResult loaded = store.Load(() => new BindingPreferences());
                bool migrate = settingsDirectory == null && loaded.Source == SettingsLoadSource.Defaults &&
                    PlayerPrefs.HasKey(LegacyPrefsKey);
                string overrides = migrate ? PlayerPrefs.GetString(LegacyPrefsKey) : store.Value.Overrides;
                if (!string.IsNullOrEmpty(overrides)) _asset.LoadBindingOverridesFromJson(overrides);
                _settings = store;
                if (migrate) Save("已迁移旧版改键");
            }
            catch (Exception error)
            {
                _asset.RemoveAllBindingOverrides();
                _settingsWarning = "无法恢复改键设置，当前使用默认绑定: " + error.Message;
                Debug.LogWarning(_settingsWarning, this);
            }
            _asset.Enable();
            return _asset;
        }

        void Start()
        {
            Initialize();
            BuildUI();
        }

        void OnDisable() => _operation?.Cancel();

        void OnDestroy()
        {
            _operation?.Dispose();
            if (_asset != null) Destroy(_asset);
        }

        // ---------------- 重绑定逻辑 ----------------

        public void StartRebind(string actionPath)
        {
            var action = Initialize().FindAction(actionPath, throwIfNotFound: false);
            int index = 0;
            while (action != null && index < action.bindings.Count && action.bindings[index].isComposite) index++;
            StartRebind(actionPath, index);
        }

        public void StartRebind(string actionPath, int bindingIndex)
        {
            if (_operation != null) return;
            Initialize();

            InputAction action = _asset.FindAction(actionPath, throwIfNotFound: false);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count ||
                action.bindings[bindingIndex].isComposite)
            {
                SetLog("找不到可重绑定的按键: " + actionPath);
                return;
            }

            bool wasEnabled = action.enabled;
            action.Disable();
            SetLog("请按下新按键（Esc / 手柄返回取消）：" + actionPath);

            string conflict = null;
            try
            {
                _operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("*/{Cancel}")
                .WithTimeout(RebindTimeoutSeconds)
                .OnApplyBinding((operation, path) =>
                {
                    if (InputBindingConflicts.TryFind(action, bindingIndex, path, operation.selectedControl, out var other, out int index))
                    {
                        conflict = other.name + " / " + other.GetBindingDisplayString(index);
                        return;
                    }
                    InputBinding binding = action.bindings[bindingIndex];
                    binding.overridePath = path;
                    action.ApplyBindingOverride(bindingIndex, binding);
                })
                .OnComplete(operation => Finish(operation, action, bindingIndex, wasEnabled, completed: true, conflict: conflict))
                .OnCancel(operation => Finish(operation, action, bindingIndex, wasEnabled, completed: false));

                string device = InputControlPath.TryGetDeviceLayout(action.bindings[bindingIndex].path);
                if (!string.IsNullOrEmpty(device)) _operation.WithControlsHavingToMatchPath("<" + device + ">");

                _operation.Start();
            }
            catch (Exception error)
            {
                _operation?.Dispose();
                _operation = null;
                if (wasEnabled) action.Enable();
                SetLog("无法开始改键: " + error.Message);
                Debug.LogWarning(error, this);
            }
        }

        void Finish(InputActionRebindingExtensions.RebindingOperation operation, InputAction action, int index,
            bool wasEnabled, bool completed, string conflict = null)
        {
            operation.Dispose();
            _operation = null;

            if (wasEnabled) action.Enable();

            RefreshLabels();
            if (!completed) SetLog("已取消改键");
            else if (conflict != null) SetLog("按键已被 " + conflict + " 使用，原绑定已保留");
            else Save(action.name + " 当前绑定: " + action.GetBindingDisplayString(index));
        }

        public void ResetAll()
        {
            _operation?.Cancel();
            Initialize();
            _asset.RemoveAllBindingOverrides();
            RefreshLabels();
            Save("已恢复默认绑定");
        }

        void Save(string message)
        {
            if (_settings == null)
            {
                SetLog(message + "（设置不可写，仅本次有效）");
                return;
            }
            try
            {
                _settings.Value.Overrides = _asset.SaveBindingOverridesAsJson();
                _settings.Save();
                SetLog(message);
            }
            catch (Exception error)
            {
                SetLog(message + "（保存失败）");
                Debug.LogWarning("无法保存改键设置: " + error.Message, this);
            }
        }

        void RefreshLabels()
        {
            foreach (var row in _rows)
                if (row.Label != null)
                    row.Label.text = BindingLabel(row.Action, row.BindingIndex) + " (" + row.Device + ") = " +
                        row.Action.GetBindingDisplayString(row.BindingIndex);
        }

        static string BindingLabel(InputAction action, int index)
        {
            InputBinding binding = action.bindings[index];
            return binding.isPartOfComposite ? action.name + " / " + binding.name : action.name;
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
                new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(620f, 355f));

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.72f);

            _log = CreateText(panel, "Log", new Vector2(20f, -16f), new Vector2(580f, 40f), 18);
            SetLog(_settingsWarning ?? "点击「改键」后按下新按键，Esc 或手柄返回取消");

            float y = -64f;

            foreach (string path in ActionPaths)
            {
                InputAction action = _asset.FindAction(path, throwIfNotFound: true);
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    if (action.bindings[i].isComposite) continue;
                    int bindingIndex = i;
                    string device = InputControlPath.TryGetDeviceLayout(action.bindings[i].path);
                    Text label = CreateText(panel, "Label-" + path + "-" + i, new Vector2(20f, y), new Vector2(430f, 40f), 20);
                    CreateButton(panel, "改键", new Vector2(470f, y), new Vector2(120f, 40f),
                        () => StartRebind(path, bindingIndex));
                    _rows.Add((action, bindingIndex, device, label));
                    y -= 52f;
                }
            }

            CreateButton(panel, "恢复默认", new Vector2(20f, y - 6f), new Vector2(180f, 40f), ResetAll);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, -y + 65f);

            RefreshLabels();
        }

        void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(transform, false);
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
