using Cysharp.Threading.Tasks;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.UI;
using System.Threading;
using UnityEngine;

public sealed class TestPanelEntry : MonoBehaviour, IBootEntry
{
    [SerializeField] UiCatalog _catalog;
    [SerializeField] int _panelId = 10;
    [SerializeField] bool _openOnStart = true;
    UiOpenResult opened;

    public async UniTask EnterAsync(BootContext context, CancellationToken ct)
    {
        await GameEntry.EnableUIAsync(context, _catalog, ct: ct);

        if (_openOnStart)
             opened = await GameEntry.OpenPanelAsync(_panelId, null, ct);
    }

    public async UniTask ExitAsync() => await GameEntry.ShutdownUIAsync();

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            Close();
    }

    async void  Close()
    {
        await GameEntry.ClosePanelAsync(_panelId);

    }
}
