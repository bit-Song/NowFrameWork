using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Threading;
using NowFrameWork.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 相机相对移动演示：WASD/手柄/摇杆 → 相对相机方向移动与转身。
    ///
    /// 相机由 Cinemachine 负责（场景里挂 Brain + Virtual Camera），本脚本只读相机朝向。
    /// 不做重力、碰撞与动画——这是演示，不是角色控制器实现。
    /// </summary>
    public sealed class CharacterDemo : MonoBehaviour
    {
        [SerializeField] float _speed = 4f;
        [SerializeField] float _turnSpeed = 12f;
        [SerializeField] Transform _camera;
        [SerializeField] VirtualJoystick _joystick;
        [Tooltip("运行时生成屏幕摇杆与跳跃按钮（手机上必须开启）")]
        [SerializeField] bool _buildTouchControls = true;

        ActionInput _input;
        InputModule _back;
        InputAction _jump;
        Canvas _touchCanvas;
        bool _touchJump;

        void Awake()
        {
            if (!MainThreadGuard.IsMainThread) MainThreadGuard.BindCurrentThread();

            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;

            var rebind = GetComponent<InputRebindingDemo>();
            if (rebind == null) rebind = gameObject.AddComponent<InputRebindingDemo>();
            InputActionAsset asset = rebind.Initialize();
            _jump = asset.FindAction("Player/Jump", throwIfNotFound: true);

            if (_buildTouchControls && _joystick == null) BuildTouchControls();

            var sources = new List<IActionSource>
            {
                new InputActionsSource(asset,
                    (InputDemo.Move, "Player/Move"), (InputDemo.Jump, "Player/Jump"), (InputDemo.Interact, "Player/Interact")),
            };

            if (_joystick != null) sources.Add(new JoystickActionSource(_joystick, InputDemo.Move));

            _input = new ActionInput(sources.ToArray());

            _back = new InputModule(_ => UniTask.FromResult(false), lifecycle: null, log: null,
                sources: new IBackSource[] { new InputSystemBackSource() });
            _back.InitializeAsync(default).GetAwaiter().GetResult();
            _back.StartAsync(default).GetAwaiter().GetResult();
        }

        void Update()
        {
            if (_input == null) return;

            _back.Tick(Time.deltaTime, Time.unscaledDeltaTime);

            Vector2 move = _input.GetVector(InputDemo.Move);

            Vector3 forward = _camera != null ? Vector3.ProjectOnPlane(_camera.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = _camera != null ? Vector3.ProjectOnPlane(_camera.right, Vector3.up).normalized : Vector3.right;

            Vector3 direction = forward * move.y + right * move.x;
            if (direction.sqrMagnitude > 1f) direction.Normalize();

            transform.position += direction * (_speed * Time.deltaTime);

            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(direction), _turnSpeed * Time.deltaTime);

            if (_input.WasPressed(InputDemo.Jump) || _touchJump)
            {
                transform.position += Vector3.up * 0.5f;
                _touchJump = false;
            }
        }

        void OnDestroy()
        {
            if (_back != null) _back.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (_touchCanvas != null) Destroy(_touchCanvas.gameObject);
        }

        // ---------------- 触屏控件 ----------------

        void BuildTouchControls()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("Touch Controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            _touchCanvas = canvasGo.GetComponent<Canvas>();
            _touchCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _touchCanvas.sortingOrder = 50;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform area = CreateRect(canvasGo.transform, "Joystick",
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(240f, 240f), new Vector2(340f, 340f));

            var background = area.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.12f);

            RectTransform knob = CreateRect(area, "Knob",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130f, 130f));

            var knobImage = knob.gameObject.AddComponent<Image>();
            knobImage.color = new Color(1f, 1f, 1f, 0.55f);
            knobImage.raycastTarget = false;

            _joystick = area.gameObject.AddComponent<VirtualJoystick>();
            _joystick.Configure(area, knob);

            RectTransform jump = CreateRect(canvasGo.transform, "Jump",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-170f, 170f), new Vector2(160f, 160f));

            var jumpImage = jump.gameObject.AddComponent<Image>();
            jumpImage.color = new Color(0.20f, 0.48f, 0.90f, 0.65f);

            var button = jump.gameObject.AddComponent<Button>();
            button.targetGraphic = jumpImage;
            button.onClick.AddListener(() => _touchJump = true);

            Text label = CreateText(jump, "跳");
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
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

        static Text CreateText(Transform parent, string caption)
        {
            RectTransform rect = CreateRect(parent, "Text", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 30;
            text.color = Color.white;
            text.text = caption;
            text.raycastTarget = false;

            return text;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(Mathf.Max(10f, Screen.width - 430f), 10f, 420f, 90f), GUI.skin.box);
            GUILayout.Label("角色演示：WASD / 手柄左摇杆（相机相对）");
            GUILayout.Label((_jump?.GetBindingDisplayString(0) ?? "-") + "：跳一下（演示用位移）");
            GUILayout.Label("Move: " + (_input != null ? _input.GetVector(InputDemo.Move).ToString("F2") : "-"));
            GUILayout.EndArea();
        }
    }
}
