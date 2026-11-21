#if UNITY_EDITOR
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NowFrameWork.Tests
{
    public sealed class UiScenePlayTests
    {
        [UnityTest]
        public IEnumerator IntegrationOpensAndReleases() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(System.TimeSpan.FromSeconds(90));
            await OpenSceneAsync("UIIntegration", timeout.Token);
            var sample = Object.FindObjectOfType<UiFlowSample>();
            Assert.That(sample, Is.Not.Null);
            await VerifyAsync(sample.GetComponent<BootFlow>(), sample.Run100Async, timeout.Token);
        });

        [UnityTest]
        public IEnumerator TweenNavigatesAndReleases() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource();
            using var timer = timeout.CancelAfterSlim(System.TimeSpan.FromSeconds(60));
            await OpenSceneAsync("UITween", timeout.Token);
            var sample = Object.FindObjectOfType<UiWindowsSample>();
            Assert.That(sample, Is.Not.Null);
            await VerifyAsync(sample.GetComponent<BootFlow>(), sample.VerifyFlowAsync, timeout.Token);
        });

        static async UniTask OpenSceneAsync(string name, CancellationToken ct)
        {
            await EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/" + name + ".scene", new LoadSceneParameters(LoadSceneMode.Single))
                .ToUniTask(cancellationToken: ct);
        }

        static async UniTask VerifyAsync(BootFlow flow, System.Func<CancellationToken, UniTask> exercise,
            CancellationToken ct)
        {
            Assert.That(flow, Is.Not.Null);
            try
            {
                var sessionField = typeof(BootFlow).GetField("_session",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                BootSession session = null;
                await UniTask.WaitUntil(() =>
                {
                    session = (BootSession)sessionField.GetValue(flow);
                    return session?.State == BootState.Running;
                }, cancellationToken: ct);
                Assert.That(GameEntry.HasUI, Is.True);
                IAssetService assets = GameEntry.Assets;
                await exercise(ct);
                await flow.Stop();
                Assert.That(session.State, Is.EqualTo(BootState.Stopped));
                Assert.That(GameEntry.IsReady, Is.False);
                Assert.That(assets.PendingRequests, Is.Zero);
                Assert.That(assets.ActiveLeases, Is.Zero);
            }
            finally
            {
                if (flow != null) await flow.Stop();
                if (flow != null) Object.Destroy(flow.gameObject);
                await UniTask.NextFrame();
            }
        }
    }
}
#endif
