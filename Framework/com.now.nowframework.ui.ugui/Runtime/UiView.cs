using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>界面实例的基类（UI-1 最小版；UI-3 再补生命周期回调与资源登记）。</summary>
    public class UiView : MonoBehaviour
    {
        public int UiId { get; internal set; }

        public int Session { get; internal set; }
    }
}