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

        ActionInput _input;
        InputModule _back;
        InputActionAssetHolder _holder;

        void Awake()
        {
            if (!MainThreadGuard.IsMainThread) MainThreadGuard.BindCurrentThread();

            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;

            var asset = InputDemo.BuildDefaultActions();
            _holder = new InputActionAssetHolder(asset);
            asset.Enable();

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

            if (_input.WasPressed(InputDemo.Jump)) transform.position += Vector3.up * 0.5f;
        }

        void OnDestroy()
        {
            if (_back != null) _back.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
            _holder?.Dispose();
        }

        /// <summary>托管运行时创建的 InputActionAsset 生命周期。</summary>
        sealed class InputActionAssetHolder
        {
            readonly InputActionAsset _asset;

            public InputActionAssetHolder(InputActionAsset asset) => _asset = asset;

            public void Dispose()
            {
                if (_asset != null) Object.Destroy(_asset);
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 420, 90), GUI.skin.box);
            GUILayout.Label("角色演示：WASD / 手柄左摇杆（相机相对）");
            GUILayout.Label("空格：跳一下（演示用位移）");
            GUILayout.Label("Move: " + (_input != null ? _input.GetVector(InputDemo.Move).ToString("F2") : "-"));
            GUILayout.EndArea();
        }
    }
}