using Cysharp.Threading.Tasks;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.UI;
using System.Threading;
using UnityEngine;

public sealed class TestPanelEntry : MonoBehaviour, IBootEntry
{
    [SerializeField] int _panelId = 10;
    [SerializeField] bool _openOnStart = true;

    GameEntry.Binding _binding;
    UiOpenResult opened;

    public async UniTask EnterAsync(BootContext context, CancellationToken ct)
    {
        // UI 由 BootFlow 的 UiCatalog 自动初始化，这里只绑定入口。
        _binding = GameEntry.Bind(context);

        if (!_openOnStart) return;

        if (!GameEntry.HasUI)
        {
            Debug.LogWarning("BootFlow 没有配置 UiCatalog，UI 未启用。");
            return;
        }

        opened = await GameEntry.OpenPanelAsync(_panelId, null, ct);
    }

    public UniTask ExitAsync()
    {
        _binding?.BeginStopping();
        _binding?.Dispose();
        _binding = null;
        return UniTask.CompletedTask;
    }

    private void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.pKey.wasPressedThisFrame)
            Close();
    }

    public async void Close()
    {
        try
        {
            await GameEntry.ClosePanelAsync(_panelId);
        }
        catch (System.Exception error)
        {
            Debug.LogException(error);
        }
    }
}
