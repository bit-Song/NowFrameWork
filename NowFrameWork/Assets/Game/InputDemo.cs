using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Threading;
using NowFrameWork.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>
    /// 输入模块演示：键鼠/手柄映射 + 摇杆 + UI 模态屏蔽 + 返回键。
    ///
    /// 场景里建一个空物体挂上即可，不需要预先制作 Input Actions 资产：
    /// 未指定 `_actions` 时会在运行时建一套默认绑定（WASD / 手柄左摇杆 / 空格 / E）。
    ///
    /// 演示内容：
    /// - WASD 或手柄左摇杆（或挂载的摇杆组件）移动方块；
    /// - E 打开"模态"：此时 Movement 被 `ActionInput` 屏蔽，方块停止移动；
    /// - Esc / Android 返回键关闭模态；没有模态时走兜底提示。
    /// </summary>
    public sealed class InputDemo : MonoBehaviour
    {
        public const int Move = 1;
        public const int Jump = 2;
        public const int Interact = 3;

        [SerializeField] InputActionAsset _actions;
        [SerializeField] VirtualJoystick _joystick;
        [SerializeField] float _speed = 4f;

        ActionInput _input;
        InputModule _back;
        InputSystemDeviceWatcher _watcher;
        InputActionAsset _ownedAsset;
        Transform _cube;

        bool _modal;
        string _log = "";

        void Awake()
        {
            // 裸场景没有 Boot 时，宿主登记由本演示补上（同线程重复登记幂等）
            if (!MainThreadGuard.IsMainThread) MainThreadGuard.BindCurrentThread();

            InputActionAsset asset = _actions != null ? _actions : BuildDefaultActions();
            if (_actions == null) _ownedAsset = asset;

            asset.Enable();

            var sources = new List<IActionSource>
            {
                new InputActionsSource(asset,
                    (Move, "Player/Move"), (Jump, "Player/Jump"), (Interact, "Player/Interact")),
            };

            if (_joystick != null) sources.Add(new JoystickActionSource(_joystick, Move));

            _input = new ActionInput(sources.ToArray());

            _back = new InputModule(
                _ => UniTask.FromResult(CloseModalIfOpen()),
                new MessageFallback(message => _log = message),
                lifecycle: null,
                log: null,
                sources: new IBackSource[] { new InputSystemBackSource() });

            _back.InitializeAsync(default).GetAwaiter().GetResult();
            _back.StartAsync(default).GetAwaiter().GetResult();

            _watcher = GetComponent<InputSystemDeviceWatcher>();
            if (_watcher == null) _watcher = gameObject.AddComponent<InputSystemDeviceWatcher>();

            _cube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            _cube.name = "InputDemoCube";
            _cube.localScale = Vector3.one * 0.8f;
        }

        void OnDestroy()
        {
            if (_back != null) _back.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (_ownedAsset != null) Destroy(_ownedAsset);
        }

        void Update()
        {
            if (_input == null) return;

            _back.Tick(Time.deltaTime, Time.unscaledDeltaTime);

            // 模态打开时屏蔽游戏动作（幂等，状态不变不分配）
            _input.SetGate("demo.modal", _modal);

            Vector2 move = _input.GetVector(Move);
            _cube.position += new Vector3(move.x, 0f, move.y) * (_speed * Time.deltaTime);

            if (_input.WasPressed(Jump)) _log = "Jump（方块上跳一下）";
            if (_input.WasPressed(Jump)) _cube.position += Vector3.up * 0.5f;

            if (_input.WasPressed(Interact))
            {
                _modal = true;
                _log = "打开模态：移动已被 ActionInput 屏蔽，按 Esc/返回键关闭";
            }
        }

        bool CloseModalIfOpen()
        {
            if (!_modal) return false;

            _modal = false;
            _log = "模态已关闭（返回键由 InputModule 分发）";
            return true;
        }

        internal static InputActionAsset BuildDefaultActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");

            InputAction move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector");
            AddMovePart(move, "Up", "w", "8e3ba508-aac8-4bef-8741-f068050fd3d6");
            AddMovePart(move, "Down", "s", "5c87da16-1882-4eb7-bc40-97aaf92c6a4e");
            AddMovePart(move, "Left", "a", "c4cc3b94-6bb1-4acb-a80a-87e227c7c2d3");
            AddMovePart(move, "Right", "d", "191cbbb9-b15d-4987-823d-680f8c00c68d");
            move.AddBinding(new InputBinding { path = "<Gamepad>/leftStick", id = new Guid("e71029c7-4f32-4aed-a503-b44f4c251174") });

            // 可重绑定的演示按键使用稳定 ID，保存的覆盖能在重新创建动作后找到原绑定。
            InputAction jump = map.AddAction("Jump", InputActionType.Button);
            jump.AddBinding(new InputBinding { path = "<Keyboard>/space", id = new Guid("134f2595-b051-4768-bd11-2651e3ca8ae8") });
            jump.AddBinding(new InputBinding { path = "<Gamepad>/buttonSouth", id = new Guid("2fb6d932-a907-43a0-856b-b647bde578b5") });

            InputAction interact = map.AddAction("Interact", InputActionType.Button);
            interact.AddBinding(new InputBinding { path = "<Keyboard>/e", id = new Guid("c1727771-32ad-42c2-aa85-8d118dd933ab") });
            interact.AddBinding(new InputBinding { path = "<Gamepad>/buttonWest", id = new Guid("5b75cb36-cb2f-45a3-bf63-326b58011860") });

            return asset;
        }

        static void AddMovePart(InputAction move, string part, string key, string id)
        {
            move.AddBinding(new InputBinding { name = part, path = "<Keyboard>/" + key,
                isPartOfComposite = true, id = new Guid(id) });
        }

        sealed class MessageFallback : IBackFallback
        {
            readonly Action<string> _report;

            public MessageFallback(Action<string> report) => _report = report;

            public bool OnBack()
            {
                _report("没有可关闭的界面：这里是兜底（可替换为「再按一次退出」）");
                return true;
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 520, 320), GUI.skin.box);

            GUILayout.Label("输入模块演示");
            GUILayout.Label("移动: WASD / 手柄左摇杆" + (_joystick != null ? " / 摇杆" : ""));
            GUILayout.Label("跳跃: 空格 / A键    模态: E / X键    关闭模态: Esc / 返回键");
            GUILayout.Label("模态状态: " + (_modal ? "打开（动作被屏蔽）" : "关闭"));
            Vector2 vector = _input != null ? _input.GetVector(Move) : Vector2.zero;
            GUILayout.Label("Move: " + vector.ToString("F2"));
            GUILayout.Label("屏蔽中: " + (_input != null && _input.IsSuppressed ? "是" : "否"));
            GUILayout.Label("日志: " + _log);
            GUILayout.Label("当前设备: " + (_watcher != null ? _watcher.Tracker.Current.ToString() : "-"));

            if (GUILayout.Button("关闭模态（等价于返回键）")) CloseModalIfOpen();

            GUILayout.EndArea();
        }
    }
}
