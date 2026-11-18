using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Assets;
using NowFrameWork.Core.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NowFrameWork.UI.Tests
{
    public sealed class UnityUiViewFactoryTests
    {
        sealed class Provider : IAssetProvider
        {
            public GameObject Prefab;
            public int Released;
            public UniTaskCompletionSource<ProviderHandle> Gate;
            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct) => UniTask.FromResult(key.Address);
            public UniTask<ProviderHandle> LoadAsync(string key, CancellationToken ct) => Gate == null ? UniTask.FromResult(new ProviderHandle(key, Prefab)) : Gate.Task;
            public void Release(ProviderHandle handle) { Released++; }
        }
        GameObject _rootObject, _prefab, _eventObject;
        UiRoot _root;
        Provider _provider;
        AssetService _assets;
        static readonly UiKey<EmptyArgs> Key = new UiKey<EmptyArgs>(1, "Panel");
        UiDefinition Definition => new UiDefinition(1, "Panel", AssetKey.From("panel"));
        static void ImmediateDestroy(GameObject go) { if (go != null) UnityEngine.Object.DestroyImmediate(go); }

        [SetUp]
        public void SetUp()
        {
            MainThreadGuard.BindCurrentThread();
            _rootObject = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(UiRoot));
            _root = _rootObject.GetComponent<UiRoot>();
            _rootObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var safe = new GameObject("Safe", typeof(RectTransform)); safe.transform.SetParent(_root.transform, false);
            Transform page = new GameObject("Page").transform; page.SetParent(safe.transform);
            Transform popup = new GameObject("Popup").transform; popup.SetParent(safe.transform);
            Transform toast = new GameObject("Toast").transform; toast.SetParent(safe.transform);
            _eventObject = new GameObject("Events", typeof(EventSystem));
            _root.Configure(_rootObject.GetComponent<Canvas>(), safe.GetComponent<RectTransform>(), page, popup, toast, _eventObject.GetComponent<EventSystem>());
            _prefab = new GameObject("Prefab", typeof(RectTransform), typeof(UiView));
            _prefab.SetActive(false);
            _provider = new Provider { Prefab = _prefab };
            _assets = new AssetService(_provider);
        }
        [TearDown]
        public void TearDown() { ImmediateDestroy(_rootObject); ImmediateDestroy(_prefab); ImmediateDestroy(_eventObject); }

        [Test]
        public void RealFactory_OpenClose_ReleasesAfterActualGameObjectDestruction()
        {
            var factory = new UnityUiViewFactory(_assets, _root, destroy: ImmediateDestroy);
            var service = new UiService(factory, new[] { Definition });
            UiOpenResult opened = service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult();
            Assert.AreEqual(1, _assets.ActiveLeases);
            Assert.AreEqual(1, factory.TrackedCount);
            Assert.AreEqual(1, _root.GetLayer(UiLayer.Page).childCount);
            service.CloseAsync(opened.Handle).GetAwaiter().GetResult();
            Assert.AreEqual(0, _assets.ActiveLeases);
            Assert.AreEqual(0, factory.TrackedCount);
            Assert.AreEqual(1, _provider.Released);
            factory.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void RealFactory_CachedInstanceKeepsLeaseUntilTrimDestroysIt()
        {
            var factory = new UnityUiViewFactory(_assets, _root, destroy: ImmediateDestroy);
            var definition = Definition;
            definition.CacheOnClose = true;
            var service = new UiService(factory, new[] { definition });
            var layer = _root.GetLayer(UiLayer.Page);
            var first = service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult().Handle;
            GameObject instance = layer.GetChild(0).gameObject;
            service.CloseAsync(first).GetAwaiter().GetResult();
            Assert.IsFalse(instance.activeSelf);
            Assert.AreEqual(1, service.CachedCount);
            Assert.AreEqual(1, _assets.ActiveLeases);
            var second = service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult().Handle;
            Assert.AreSame(instance, layer.GetChild(0).gameObject);
            Assert.AreEqual(1, factory.TrackedCount);
            Assert.AreEqual(1, _assets.ActiveLeases);
            service.CloseAsync(second).GetAwaiter().GetResult();
            service.TrimCacheAsync().GetAwaiter().GetResult();
            Assert.IsTrue(instance == null);
            Assert.AreEqual(0, _assets.ActiveLeases);
            Assert.AreEqual(0, factory.TrackedCount);
            Assert.AreEqual(1, _provider.Released);
            service.ShutdownAsync().GetAwaiter().GetResult();
            factory.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Create_ExceedsTrackedLimit_ThrowsAndKeepsLeaseCount()
        {
            // 上限 2：前两个实例不关闭，第三个必须被拒绝，避免清理超时的记录无限累积
            var factory = new UnityUiViewFactory(_assets, _root, destroy: ImmediateDestroy, maxTrackedRecords: 2);
            var service = new UiService(factory, new[] { Definition });

            UiOpenResult first = service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult();

            var multi = new UiDefinition(2, "Multi", AssetKey.From("panel")) { MultiInstance = true };
            var service2 = new UiService(factory, new[] { multi });
            var key2 = new UiKey<EmptyArgs>(2, "Multi");

            service2.OpenAsync(key2, EmptyArgs.Default).GetAwaiter().GetResult();
            Assert.AreEqual(2, factory.TrackedCount);

            Assert.Throws<InvalidOperationException>(() =>
                service2.OpenAsync(key2, EmptyArgs.Default).GetAwaiter().GetResult());

            Assert.AreEqual(2, factory.TrackedCount, "被拒绝的创建不应留下记录");
            Assert.AreEqual(2, _assets.ActiveLeases, "被拒绝时不应额外占用租约");

            service.CloseAsync(first.Handle).GetAwaiter().GetResult();
            factory.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void DestroyTimeout_KeepsLeaseUntilRetryAfterGameObjectGone()
        {
            var factory = new UnityUiViewFactory(_assets, _root, maxDestroyFrames: 0, destroy: go => { });
            UiView view = factory.CreateAsync(Definition, null, default).GetAwaiter().GetResult();
            GameObject instance = view.gameObject;
            factory.Destroy(view);
            Assert.IsFalse(factory.ReleaseAsync(view).GetAwaiter().GetResult());
            Assert.AreEqual(1, _assets.ActiveLeases);
            UnityEngine.Object.DestroyImmediate(view); // 组件伪空不能代表整个实例消失。
            Assert.IsFalse(factory.ReleaseAsync(view).GetAwaiter().GetResult());
            Assert.AreEqual(1, _assets.ActiveLeases);
            ImmediateDestroy(instance);
            Assert.IsTrue(factory.ReleaseAsync(view).GetAwaiter().GetResult());
            Assert.AreEqual(0, _assets.ActiveLeases);
        }

        [Test]
        public void PrefabWithoutView_RollsBackLease()
        {
            UnityEngine.Object.DestroyImmediate(_prefab.GetComponent<UiView>());
            var factory = new UnityUiViewFactory(_assets, _root, destroy: ImmediateDestroy);
            Assert.Throws<InvalidOperationException>(() => factory.CreateAsync(Definition, null, default).GetAwaiter().GetResult());
            Assert.AreEqual(0, _assets.ActiveLeases);
            Assert.AreEqual(0, factory.TrackedCount);
        }

        [Test]
        public void Module_DefersOpenUntilStarted_AndOwnsRootCleanup()
        {
            _rootObject.SetActive(false);
            var module = new UiModule(_assets, _root, new[] { Definition }, destroy: ImmediateDestroy);
            Assert.Throws<InvalidOperationException>(() => module.Service.OpenAsync(Key, EmptyArgs.Default));
            module.InitializeAsync(default).GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => module.Service.OpenAsync(Key, EmptyArgs.Default));
            module.StartAsync(default).GetAwaiter().GetResult();
            module.Service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult();
            module.DisposeAsync().GetAwaiter().GetResult();
            module.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, _assets.ActiveLeases);
            Assert.IsTrue(module.Root == null);
        }

        [Test]
        public void ChildCanvasOriginallyDisabled_IsNotEnabledByShow()
        {
            _prefab.GetComponent<Canvas>().enabled = false;
            _prefab.GetComponent<CanvasGroup>().alpha = 0;
            var child = new GameObject("Disabled", typeof(RectTransform), typeof(Canvas));
            child.transform.SetParent(_prefab.transform, false);
            child.GetComponent<Canvas>().enabled = false;
            var factory = new UnityUiViewFactory(_assets, _root, destroy: ImmediateDestroy);
            var service = new UiService(factory, new[] { Definition });
            UiHandle handle = service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult().Handle;
            Canvas canvas = _root.GetLayer(UiLayer.Page).GetChild(0).Find("Disabled").GetComponent<Canvas>();
            Assert.IsFalse(canvas.enabled);
            Assert.IsTrue(canvas.transform.parent.GetComponent<Canvas>().enabled, "根 Canvas 由服务开启，子 Canvas 则保持原配置");
            Assert.AreEqual(1, canvas.transform.parent.GetComponent<CanvasGroup>().alpha);
            service.HideAsync(handle).GetAwaiter().GetResult(); service.ShowAsync(handle).GetAwaiter().GetResult();
            Assert.IsFalse(canvas.enabled);
            service.ShutdownAsync().GetAwaiter().GetResult(); factory.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Module_UsesOneFullscreenBackdrop_AndMovesItToTopModal()
        {
            _rootObject.SetActive(false);
            var modal = new UiDefinition(2, "Modal", AssetKey.From("panel"))
                { Layer = UiLayer.Popup, Modal = true, MultiInstance = true, CloseOnBackdrop = true };
            var module = new UiModule(_assets, _root, new[] { Definition, modal }, destroy: ImmediateDestroy);
            try
            {
                module.InitializeAsync(default).GetAwaiter().GetResult(); module.StartAsync(default).GetAwaiter().GetResult();
                module.Service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult();
                var key = new UiKey<EmptyArgs>(2, "Modal");
                var first = module.Service.OpenAsync(key, EmptyArgs.Default).GetAwaiter().GetResult();
                int firstOrder = module.Root.BackdropSortingOrder;
                module.Service.OpenAsync(key, EmptyArgs.Default).GetAwaiter().GetResult();
                Assert.IsTrue(module.Root.BackdropVisible); Assert.Greater(module.Root.BackdropSortingOrder, firstOrder);
                var backdrop = module.Root.transform.Find("Shared Modal Backdrop");
                Assert.IsNotNull(backdrop); Assert.AreSame(module.Root.transform, backdrop.parent);
                Assert.AreEqual(Vector2.zero, ((RectTransform)backdrop).anchorMin); Assert.AreEqual(Vector2.one, ((RectTransform)backdrop).anchorMax);
                Assert.AreEqual(2, module.Root.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length);
                backdrop.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.AreEqual(first.Handle, module.Service.TopModal); Assert.AreEqual(firstOrder, module.Root.BackdropSortingOrder);
                module.Service.CloseAsync(first.Handle).GetAwaiter().GetResult(); Assert.IsFalse(module.Root.BackdropVisible);
                Assert.AreEqual(1, _assets.ActiveLeases);
            }
            finally { module.DisposeAsync().GetAwaiter().GetResult(); }
            Assert.AreEqual(0, _assets.ActiveLeases);
        }

        [Test]
        public void InactiveExternalEventSystem_IsRejected()
        {
            _eventObject.SetActive(false);
            Assert.Throws<InvalidOperationException>(() => _root.Validate());
        }
    }
}
