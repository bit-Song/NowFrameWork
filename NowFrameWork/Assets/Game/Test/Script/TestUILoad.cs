using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game
{
    /// <summary>测试用：手动打开 Catalog 中的 10 号面板。</summary>
    public class TestUILoad : MonoBehaviour
    {
        public void OpenPanel()
        {
            if (!GameEntry.HasUI)
            {
                Debug.LogWarning("UI 尚未启用，请确认 Boot 入口已调用 EnableUIAsync。");
                return;
            }

            GameEntry.OpenPanelAsync(10).Forget();
        }
    }
}
