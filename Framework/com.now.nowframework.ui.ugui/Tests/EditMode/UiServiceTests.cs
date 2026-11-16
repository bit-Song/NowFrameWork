using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Assets;
using UnityEngine;

namespace NowFrameWork.UI.Tests
{
    /// <summary>UI-1：打开/关闭/显隐状态机（假工厂，不碰 Prefab 与布局）。</summary>
    public class UiServiceTests
    {
        sealed class Args
        {
            public int Value;
        }

        sealed class FakeFactory : IUiViewFactory
        {
            public int Created;
            public int Released;
            public Func<UniTask<UiView>> OnCreate;

            public FakeFactory()
            {
                OnCreate = () =>
                {
                    var go = new GameObject("UiView");
                    return UniTask.FromResult(go.AddComponent<UiView>());
                };
            }

            public async UniTask<UiView> CreateAsync(UiDefinition definition, Transform parent, CancellationToken ct)
            {
                Created++;
                return await OnCreate();
            }

            public void Destroy(UiView view)
            {
                if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
            }

            public UniTask<bool> ReleaseAsync(UiView view)
            {
                Released++;
                Destroy(view);
                return UniTask.FromResult(true);
            }
        }

        sealed class FakeTransition : IUiTransition
        {
            public int Shows;
            public int Hides;

            public UniTask ShowAsync(UiView view, CancellationToken ct)
            {
                Shows++;
                return UniTask.CompletedTask;
            }

            public UniTask HideAsync(UiView view, CancellationToken ct)
            {
                Hides++;
                return UniTask.CompletedTask;
            }
        }

        static readonly UiKey<Args> SettingsKey = new UiKey<Args>(1001, "Settings");

        static (UiService service, FakeFactory factory, FakeTransition transition) Create(bool multi = false)
        {
            var factory = new FakeFactory();
            var transition = new FakeTransition();
            var definitions = new List<UiDefinition>
            {
                new UiDefinition(SettingsKey.Id, "Settings", AssetKey.From("Assets/UI/Settings.prefab"))
                {
                    Layer = UiLayer.Page,
                    MultiInstance = multi,
                },
            };

            return (new UiService(factory, definitions, transition), factory, transition);
        }

        [Test]
        public void Open_CreatesView_AndReturnsHandle()
        {
            var (service, factory, transition) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();

            Assert.AreEqual(UiOpenStatus.Ok, result.Status);
            Assert.IsTrue(result.Handle.IsValid);
            Assert.AreEqual(1, factory.Created);
            Assert.AreEqual(1, service.VisibleCount);
            Assert.AreEqual(1, service.SessionCount);
            Assert.AreEqual(1, transition.Shows, "打开后应播放一次显示过渡");

            service.CloseAsync(result.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void Open_SingleInstance_ReturnsAlreadyOpen()
        {
            var (service, factory, _) = Create();

            UiOpenResult first = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            UiOpenResult second = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();

            Assert.AreEqual(UiOpenStatus.AlreadyOpen, second.Status);
            Assert.AreEqual(first.Handle, second.Handle, "单实例应返回同一会话句柄");
            Assert.AreEqual(1, factory.Created, "不应重复创建");

            service.CloseAsync(first.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void Open_WhileOpening_ReturnsBusy()
        {
            var (service, factory, _) = Create();
            var pending = new UniTaskCompletionSource<UiView>();
            factory.OnCreate = () => pending.Task;

            UniTask<UiOpenResult> first = service.OpenAsync(SettingsKey, new Args());
            UiOpenResult second = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();

            Assert.AreEqual(UiOpenStatus.Busy, second.Status, "加载中再次打开应返回 Busy");
            Assert.AreEqual(1, service.OpeningCount);

            var go = new GameObject("UiView");
            pending.TrySetResult(go.AddComponent<UiView>());

            UiOpenResult opened = first.GetAwaiter().GetResult();
            Assert.AreEqual(UiOpenStatus.Ok, opened.Status);
            Assert.AreEqual(0, service.OpeningCount);

            service.CloseAsync(opened.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void Open_UnknownKey_Throws()
        {
            var (service, _, _) = Create();
            var unknown = new UiKey<Args>(9999, "Unknown");

            Assert.Throws<InvalidOperationException>(() =>
                service.OpenAsync(unknown, new Args()).GetAwaiter().GetResult());
        }

        [Test]
        public void Open_Cancelled_CleansUpSession()
        {
            var (service, factory, _) = Create();
            var pending = new UniTaskCompletionSource<UiView>();
            factory.OnCreate = () => pending.Task;

            var cts = new CancellationTokenSource();
            UniTask<UiOpenResult> opening = service.OpenAsync(SettingsKey, new Args(), cts.Token);

            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => opening.GetAwaiter().GetResult());
            Assert.AreEqual(0, service.SessionCount, "取消失败的会话必须清理");
            Assert.AreEqual(0, service.VisibleCount);
        }

        [Test]
        public void Close_ReleasesView_AndClearsCounters()
        {
            var (service, factory, _) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            service.CloseAsync(result.Handle).GetAwaiter().GetResult();

            Assert.AreEqual(1, factory.Released);
            Assert.AreEqual(0, service.VisibleCount);
            Assert.AreEqual(0, service.SessionCount);
        }

        [Test]
        public void ShowHide_DelegateToTransition()
        {
            var (service, _, transition) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            service.HideAsync(result.Handle).GetAwaiter().GetResult();
            service.ShowAsync(result.Handle).GetAwaiter().GetResult();

            Assert.AreEqual(1, transition.Hides);
            Assert.AreEqual(2, transition.Shows, "打开一次 + 显式 Show 一次");

            service.CloseAsync(result.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void StaleHandle_IsIgnored()
        {
            var (service, factory, _) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            service.CloseAsync(result.Handle).GetAwaiter().GetResult();

            // 旧句柄再次关闭：应被忽略，不产生第二次释放
            service.CloseAsync(result.Handle).GetAwaiter().GetResult();

            Assert.AreEqual(1, factory.Released);
        }
    }
}