using System;
using System.Collections;
using System.Collections.Generic;
using NowFrameWork.Core.Threading;
using NowFrameWork.Core.Fsm;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace NowFrameWork.Framework.PlayMode.Tests
{
    /// <summary>每项在独立运行时场景中验证真实 Unity 生命周期。</summary>
    public sealed class HfsmSceneTests
    {
        private Scene _scene; // 本次测试专用场景。
        private Scene _previousScene; // 测试前的活动场景。

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            MainThreadGuard.BindCurrentThread();
            _previousScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("HFSM_Test_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(_scene);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_previousScene.IsValid() && _previousScene.isLoaded) SceneManager.SetActiveScene(_previousScene);
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        /// <summary>真实启用、禁用、重新启用和销毁都应正确进入或清理。</summary>
        [UnityTest]
        public IEnumerator IdleMove_DisableEnableDestroy()
        {
            HfsmLifecycleHost host = CreateHost(HfsmLifecycleHost.Scenario.IdleMove);
            Assert.AreEqual("Idle", host.Fsm.CurrentStateId);
            Vector3 start = host.transform.position;
            host.MoveRequested = true;
            yield return null;
            yield return null;
            Assert.AreEqual("Move", host.Fsm.CurrentStateId);
            Assert.Greater(host.transform.position.x, start.x);
            host.enabled = false;
            int ticks = host.Counts.Ticks;
            int fixedTicks = host.Counts.FixedTicks;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.AreEqual(ticks, host.Counts.Ticks);
            Assert.AreEqual(fixedTicks, host.Counts.FixedTicks);
            Assert.IsFalse(host.Fsm.IsRunning);
            host.MoveRequested = false;
            host.enabled = true;
            Assert.AreEqual("Idle", host.Fsm.CurrentStateId);
            StateMachine<string> fsm = host.Fsm;
            HfsmLifecycleHost.Counters counts = host.Counts;
            int exits = counts.Exits;
            Object.Destroy(host.gameObject);
            yield return null;
            Assert.AreEqual(exits + 1, counts.Exits);
            Assert.IsFalse(fsm.IsRunning);
            var trace = new List<FsmTraceRecord>();
            fsm.CopyTraceTo(trace, 0);
            Assert.IsTrue(trace.Exists(record => record.Kind == FsmTraceKind.Stopped));
            Assert.IsTrue(trace.Exists(record => record.Kind == FsmTraceKind.Enter && record.Target == "Move"));
        }

        /// <summary>物理更新来自真实 FixedUpdate，刚体同时参与场景模拟。</summary>
        [UnityTest]
        public IEnumerator FixedUpdate_TracksPhysicsLifecycle()
        {
            HfsmLifecycleHost host = CreateHost(HfsmLifecycleHost.Scenario.IdleMove);
            Rigidbody body = host.gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = Vector3.right;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.Greater(host.Counts.FixedTicks, 0);
            Assert.Greater(body.position.x, 0);
            host.gameObject.SetActive(false);
            int fixedTicks = host.Counts.FixedTicks;
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(fixedTicks, host.Counts.FixedTicks);
        }

        /// <summary>收招由 Unity 帧时间推进；死亡事件可强制覆盖等待。</summary>
        [UnityTest]
        public IEnumerator Attack_WaitsAndCanBeForceInterrupted()
        {
            HfsmLifecycleHost host = CreateHost(HfsmLifecycleHost.Scenario.Attack);
            host.Fsm.ChangeState("Idle");
            Assert.AreEqual("Attack", host.Fsm.CurrentStateId);
            Assert.AreEqual("Idle", host.Fsm.PendingTarget);
            Assert.AreEqual("等待攻击收招", host.Fsm.ExitWaitReason);
            yield return WaitUntil(() => host.Fsm.CurrentStateId == "Idle");
            Assert.AreEqual(1, host.Counts.ExitRequests);
            host.Fsm.Restart();
            host.Fsm.ChangeState("Idle");
            host.Fsm.SendEvent("KILLED");
            Assert.AreEqual("Dead", host.Fsm.CurrentStateId);
            Assert.IsFalse(host.Fsm.HasPendingTransition);
            Assert.AreEqual("强制死亡", host.Fsm.History[host.Fsm.History.Count - 1].RuleName);
        }

        /// <summary>真实 Update 中的子状态异常会停止父链，修复后显式重启。</summary>
        [UnityTest]
        public IEnumerator ChildFault_StopsChainAndExplicitRestartRecovers()
        {
            HfsmLifecycleHost host = CreateHost(HfsmLifecycleHost.Scenario.Nested);
            host.ThrowOnTick = true;
            yield return WaitUntil(() => host.CaughtException != null);
            Assert.IsTrue(host.Fsm.IsFaulted);
            Assert.IsTrue(host.Child.IsFaulted);
            Assert.IsFalse(host.Fsm.IsRunning);
            Assert.IsFalse(host.Child.IsRunning);
            Assert.AreSame(host.CaughtException, host.Fsm.LastException);
            Assert.AreEqual("SceneRoot/Alive/Work", host.Fsm.FaultPath);
            Assert.AreEqual(1, host.Counts.Exits);
            Assert.AreEqual(1, host.Counts.Faults);
            host.ThrowOnTick = false;
            host.Fsm.Restart();
            int before = host.Counts.Ticks;
            yield return null;
            yield return null;
            Assert.IsTrue(host.Fsm.IsRunning);
            Assert.IsNull(host.Fsm.LastException);
            Assert.Greater(host.Counts.Ticks, before);
        }

        /// <summary>真实动画事件发送 FSM 事件，验证动画回调与状态切换的连接。</summary>
        [UnityTest]
        public IEnumerator AnimationEvent_TriggersLocalTransition()
        {
            HfsmLifecycleHost host = CreateHost(HfsmLifecycleHost.Scenario.IdleMove);
            host.MoveRequested = true;
            host.Fsm.ChangeState("Move");
            Animation animation = host.gameObject.AddComponent<Animation>();
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            var clip = new AnimationClip { legacy = true, name = "HFSM_TestAnimation" };
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0, 0, 0.15f, 1));
            clip.AddEvent(new AnimationEvent { time = 0.05f, functionName = nameof(HfsmLifecycleHost.OnAnimationFinished) });
            animation.AddClip(clip, clip.name);
            Assert.IsTrue(animation.Play(clip.name));
            yield return WaitUntil(() => host.Counts.AnimationEvents > 0);
            Assert.AreEqual("Idle", host.Fsm.CurrentStateId);
            Assert.AreEqual(StateChangeReason.LocalEvent, host.Fsm.History[host.Fsm.History.Count - 1].Reason);
            Object.Destroy(clip);
        }

        /// <summary>在激活前配置场景模式，再让真实 Awake/OnEnable 初始化。</summary>
        private static HfsmLifecycleHost CreateHost(HfsmLifecycleHost.Scenario mode)
        {
            var go = new GameObject("HFSM Lifecycle Host");
            go.SetActive(false);
            HfsmLifecycleHost host = go.AddComponent<HfsmLifecycleHost>();
            host.Mode = mode;
            go.SetActive(true);
            return host;
        }

        /// <summary>给异步行为设置明确超时，避免测试无限等待。</summary>
        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), "Unity 场景行为未在三秒内完成。");
        }
    }
}
