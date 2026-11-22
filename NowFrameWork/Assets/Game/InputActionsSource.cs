using System;
using System.Collections.Generic;
using NowFrameWork.Input;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>
    /// 把项目的 Input Actions 适配成框架的 <see cref="IActionSource"/>。
    ///
    /// 只做"动作 Id → InputAction"的查表与取值，不做映射、不做改键：
    /// 映射关系由 `InputActionAsset` 与调用方传入的 `(actionId, actionName)` 表共同决定。
    ///
    /// 资源生命周期由调用方负责：本类只读，不 Enable/Disable/Dispose 资产。
    /// </summary>
    public sealed class InputActionsSource : IActionSource
    {
        readonly Dictionary<int, InputAction> _actions;
        readonly Dictionary<int, bool> _isVector;

        /// <param name="asset">已启用（Enable）的 InputActionAsset。</param>
        /// <param name="bindings">动作 Id 与动作名的对应；名字必须存在于资产中。</param>
        public InputActionsSource(InputActionAsset asset, params (int Id, string Action)[] bindings)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (bindings == null || bindings.Length == 0) throw new ArgumentException("至少需要一个动作绑定", nameof(bindings));

            _actions = new Dictionary<int, InputAction>(bindings.Length);
            _isVector = new Dictionary<int, bool>(bindings.Length);

            for (int i = 0; i < bindings.Length; i++)
            {
                int id = bindings[i].Id;
                string name = bindings[i].Action;

                if (string.IsNullOrEmpty(name)) throw new ArgumentException("动作名不能为空", nameof(bindings));
                if (_actions.ContainsKey(id)) throw new ArgumentException("动作 Id 重复: " + id, nameof(bindings));

                InputAction action = asset.FindAction(name, throwIfNotFound: false);

                if (action == null)
                    throw new ArgumentException("InputActionAsset 中找不到动作: " + name, nameof(bindings));

                _actions.Add(id, action);
                _isVector.Add(id, IsVectorAction(action));
            }
        }

        /// <summary>
        /// 判断动作是否二维：优先看 expectedControlType，
        /// 为空时（运行时用 AddAction 建的动作常见）再看复合绑定路径。
        /// </summary>
        static bool IsVectorAction(InputAction action)
        {
            if (string.Equals(action.expectedControlType, "Vector2", StringComparison.Ordinal)) return true;

            var bindings = action.bindings;

            for (int i = 0; i < bindings.Count; i++)
            {
                if (!bindings[i].isComposite) continue;

                string path = bindings[i].path ?? string.Empty;

                // 复合类型名：2DVector / Dpad / Vector2（注意是 "2DVector"，不包含 "Vector2"）
                if (path.IndexOf("2D", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Vector2", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Dpad", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        public bool TryRead(int actionId, out ActionState state)
        {
            if (!_actions.TryGetValue(actionId, out InputAction action))
            {
                state = default;
                return false;
            }

            if (!action.enabled)
            {
                state = ActionState.Idle;
                return true;
            }

            bool pressed = action.WasPressedThisFrame();
            bool held = action.IsPressed();

            // 二维动作（Vector2 复合/摇杆）必须按 Vector2 读，否则会抛
            // "Cannot read value of type Single from composite Vector2Composite"
            // 运行期再兜一层：有活动控件且控件本身是 Vector2 时也按二维读
            bool isVector = _isVector[actionId]
                || (action.activeControl != null && action.activeControl.valueType == typeof(UnityEngine.Vector2));

            if (isVector)
            {
                UnityEngine.Vector2 vector = action.ReadValue<UnityEngine.Vector2>();

                state = new ActionState(vector.magnitude, pressed, held, vector);
                return true;
            }

            float value = action.ReadValue<float>();

            state = new ActionState(value, pressed, held);
            return true;
        }
    }
}