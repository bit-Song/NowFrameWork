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

        int? _pointerId;

        /// <summary>运行时装配：区域、摇杆头与手感参数。</summary>
        public void Configure(RectTransform area, RectTransform knob, float radius = 0f, float deadzone = 0.15f)
        {
            _area = area != null ? area : transform as RectTransform;
            _knob = knob;
            _radius = Mathf.Max(0f, radius);
            _deadzone = Mathf.Clamp(deadzone, 0f, 0.9f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_pointerId.HasValue && UpdateFrom(eventData)) _pointerId = eventData.pointerId;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_pointerId == eventData.pointerId) UpdateFrom(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_pointerId == eventData.pointerId) Reset();
        }

        bool UpdateFrom(PointerEventData eventData)
        {
            RectTransform area = _area != null ? _area : transform as RectTransform;
            if (area == null) return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    area, eventData.position, eventData.pressEventCamera, out Vector2 local)) return false;

            Vector2 delta = local - area.rect.center;

            float radius = _radius > 0f ? _radius : area.rect.width * 0.5f;

            Value = JoystickMath.Evaluate(delta, radius, _deadzone);
            IsPressed = true;

            if (_knob != null) _knob.anchoredPosition = Vector2.ClampMagnitude(delta, radius);
            return true;
        }

        void Reset()
        {
            _pointerId = null;
            Value = Vector2.zero;
            IsPressed = false;

            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
        }

        void OnDisable() => Reset();
    }
}
