using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Core.Logging;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NowFrameWork.UI
{
    /// <summary>一行装配 UI：Catalog + 可选自定义根，其余自动创建。</summary>
    public static class UiBootstrap
    {
        public static async UniTask<UiModule> CreateAsync(IAssetService assets, UiCatalog catalog,
            UiRoot rootPrefab = null, EventSystem eventSystem = null, Transform parent = null, ILog log = null,
            Func<UniTask> frameWaiter = null, Action<GameObject> destroy = null, CancellationToken ct = default)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            EventSystem events = eventSystem;

            bool eventsCreated = false;

            if (events == null) events = UiRootFactory.EnsureEventSystem(parent, out eventsCreated);

            UiRoot createdRoot = rootPrefab == null ? UiRootFactory.Create(parent, events) : null;

            // Boot 场景通常常驻；场景切换或 Boot 重启时根节点仍需存在。
            if (createdRoot != null && createdRoot.transform.parent == null)
                UnityEngine.Object.DontDestroyOnLoad(createdRoot.gameObject);

            if (eventsCreated && events.transform.parent == null)
                UnityEngine.Object.DontDestroyOnLoad(events.gameObject);

            Action onDispose = eventsCreated
                ? (Action)(() => { if (events != null) UnityEngine.Object.Destroy(events.gameObject); })
                : null;

            var module = new UiModule(assets, rootPrefab, catalog.BuildDefinitions(),
                new CanvasGroupUiTransition(), events, parent, log, frameWaiter, destroy,
                rootInstance: createdRoot, onDispose: onDispose);

            try
            {
                await module.InitializeAsync(ct);
                await module.StartAsync(ct);
                return module;
            }
            catch
            {
                await module.DisposeAsync();
                throw;
            }
        }
    }
}
