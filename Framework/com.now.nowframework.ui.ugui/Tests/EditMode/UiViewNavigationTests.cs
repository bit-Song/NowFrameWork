using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NowFrameWork.UI.Tests
{
    /// <summary>方向导航：CanvasGroup 过滤与当前控件失效后的焦点迁移决策。</summary>
    public class UiViewNavigationTests
    {
        GameObject _viewObject;
        UiView _view;

        [SetUp]
        public void SetUp()
        {
            _viewObject = new GameObject("View", typeof(Canvas), typeof(CanvasGroup));
            _view = _viewObject.AddComponent<UiView>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewObject != null) Object.DestroyImmediate(_viewObject);
        }

        static Button CreateButton(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            return go.GetComponent<Button>();
        }

        [Test]
        public void ConfigureNavigation_SkipsControlsUnderDisabledCanvasGroup()
        {
            Button first = CreateButton(_view.transform, "First");

            var holder = new GameObject("Holder", typeof(RectTransform), typeof(CanvasGroup));
            holder.transform.SetParent(_view.transform, false);
            holder.GetComponent<CanvasGroup>().interactable = false;

            Button second = CreateButton(holder.transform, "Second");

            _view.ConfigureVerticalNavigation(first, second);

            Assert.AreSame(first, _view.DefaultFocus, "默认焦点应跳过禁用 CanvasGroup 下的控件");
            Assert.IsNull(first.navigation.selectOnDown, "禁用组下的控件不应被连入导航");
        }

        [Test]
        public void ConfigureNavigation_KeepsButtonsInGroupThatIgnoresRaycasts()
        {
            var holder = new GameObject("No pointer input", typeof(RectTransform), typeof(CanvasGroup));
            holder.transform.SetParent(_view.transform, false);
            holder.GetComponent<CanvasGroup>().blocksRaycasts = false;
            var first = CreateButton(_view.transform, "First");
            var second = CreateButton(holder.transform, "Second");

            _view.ConfigureVerticalNavigation(first, second);

            Assert.AreSame(second, first.navigation.selectOnDown);
        }

        [Test]
        public void ConfigureNavigation_FrontViewRecoversWhenUnityClearsSelection()
        {
            var systemObject = new GameObject("Events", typeof(EventSystem));
            var system = systemObject.GetComponent<EventSystem>();
            var previousSystem = EventSystem.current;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            // EditMode 不会自动登记普通 EventSystem，显式驱动其 Unity 生命周期。
            typeof(EventSystem).GetMethod("OnEnable", flags).Invoke(system, null);
            EventSystem.current = system;
            try
            {
                var first = CreateButton(_view.transform, "First");
                var second = CreateButton(_view.transform, "Second");
                _view.BeginVisibility(true);
                _view.CommitVisible();
                _view.OwnsNavigationFocus = true;
                _view.ConfigureVerticalNavigation(first, second);
                Assert.AreSame(first.gameObject, system.currentSelectedGameObject);

                first.interactable = false;
                Assert.IsNull(system.currentSelectedGameObject, "UGUI 应先清空选中项。");
                _view.OwnsNavigationFocus = false;
                _view.ConfigureVerticalNavigation(first, second);
                Assert.IsNull(system.currentSelectedGameObject, "后台面板不能在焦点为空时抢占。");
                _view.OwnsNavigationFocus = true;
                _view.ConfigureVerticalNavigation(first, second);
                Assert.AreSame(second.gameObject, system.currentSelectedGameObject);
            }
            finally
            {
                _view.CommitHidden();
                typeof(EventSystem).GetMethod("OnDisable", flags).Invoke(system, null);
                Object.DestroyImmediate(systemObject);
                if (previousSystem != null) EventSystem.current = previousSystem;
            }
        }

        [Test]
        public void FindNearestNavigable_SkipsDisabledControlAndWraps()
        {
            Button first = CreateButton(_view.transform, "First");
            Button second = CreateButton(_view.transform, "Second");

            _view.ConfigureVerticalNavigation(first, second);

            // 第二个按钮失效：从索引 1 出发应回绕到可用的第一个，而不是返回 null（丢焦点）
            second.interactable = false;
            _view.ConfigureVerticalNavigation(first, second);

            var navigable = new List<Selectable> { first };
            Selectable replacement = _view.FindNearestNavigable(new Selectable[] { first, second }, navigable, 1);

            Assert.AreSame(first, replacement, "应在可用控件中就近选择，而不是放弃焦点");
        }
    }
}
