using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>
    /// 虚拟摇杆：把指针按下/拖动换算成 [-1,1] 的方向。
    ///
    /// 只负责产出方向；相机相对换算与角色移动不在这里。
    /// 需要摇杆区域（通常是自身 RectTransform）与可选的摇杆头。
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] RectTransform _area;
        [SerializeField] RectTransform _knob;
        [Tooltip("0 = 按区域宽度的一半自动计算；>0 为固定本地单位半径")]
        [SerializeField] float _radius = 0f;
        [SerializeField, Range(0f, 0.9f)] float _deadzone = 0.15f;

        /// <summary>当前方向，长度 0..1。</summary>
        public Vector2 Value { get; private set; }

        public bool IsPressed { get; private set; }

        public void OnPointerDown(PointerEventData eventData) => UpdateFrom(eventData);

        public void OnDrag(PointerEventData eventData) => UpdateFrom(eventData);

        public void OnPointerUp(PointerEventData eventData) => Reset();

        void UpdateFrom(PointerEventData eventData)
        {
            RectTransform area = _area != null ? _area : transform as RectTransform;
            if (area == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    area, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;

            Vector2 delta = local - area.rect.center;

            float radius = _radius > 0f ? _radius : area.rect.width * 0.5f;

            Value = JoystickMath.Evaluate(delta, radius, _deadzone);
            IsPressed = true;

            if (_knob != null) _knob.anchoredPosition = Vector2.ClampMagnitude(delta, radius);
        }

        void Reset()
        {
            Value = Vector2.zero;
            IsPressed = false;

            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
        }

        void OnDisable() => Reset();
    }
}