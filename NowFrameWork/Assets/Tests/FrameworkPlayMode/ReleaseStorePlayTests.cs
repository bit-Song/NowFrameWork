using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using NowFrameWork.Unity.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace NowFrameWork.Tests
{
    public sealed class ReleaseStorePlayTests
    {
        sealed class Fixture : IDisposable
        {
            public readonly string Root = Path.Combine(Path.GetTempPath(), "NowFrameWork-Release-" + Guid.NewGuid().ToString("N"));
            static readonly byte[] Catalog = Encoding.UTF8.GetBytes("{}");
            static readonly byte[] Bundle = Encoding.UTF8.GetBytes("verified bundle bytes");

            public ReleaseStore Open() => new ReleaseStore(Root,
                new ReleaseCompatibility("Windows", "x64", "client-v1", "1", "builtin", 1, 2, 2),
                new JsonSettingsCodec<ReleaseState>());

            public static ResourceRelease Manifest(string id) => new ResourceRelease
            {
                Id = id,
                Platform = "Windows",
                Architecture = "x64",
                ClientBuildId = "client-v1",
                FrameworkContractVersion = "1",
                ResourceCatalogId = id + "-catalog",
                CodeSetId = "builtin",
                SaveSchemaReadMin = 1,
                SaveSchemaReadMax = 2,
                SaveSchemaWriteVersion = 2,
                CatalogPath = "catalog.json",
                Files = new[] { Describe("catalog.json", Catalog), Describe("data.bundle", Bundle) }
            };

            public static ReleaseFile Describe(string path, byte[] bytes)
            {
                using var sha = SHA256.Create();
                return new ReleaseFile { Path = path, Length = bytes.Length,
                    Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "") };
            }

            public static UniTask Copy(string path, Stream target, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bytes = path == "catalog.json" ? Catalog : Bundle;
                target.Write(bytes, 0, bytes.Length);
                return UniTask.CompletedTask;
            }

            public async UniTask ConfirmBaseAsync()
            {
                var store = Open();
                await store.StageAsync(Manifest("v1"), Copy);
                var boot = Open();
                var selection = await boot.SelectForBootAsync();
                Assert.That(selection.IsTrial, Is.True);
                boot.ConfirmRunning(selection);
            }

            public string File(string id, string file) => Path.Combine(Root, "releases", id, file);

            public void Dispose()
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator NormalUpgradeAndInterruptedTrialHaveDifferentOutcomes() => UniTask.ToCoroutine(async () =>
        {
            using var fixture = new Fixture();
            Assert.That(await fixture.Open().SelectForBootAsync(), Is.Null);
            await fixture.ConfirmBaseAsync();
            await fixture.Open().StageAsync(Fixture.Manifest("v2"), Fixture.Copy);
            var trialBoot = fixture.Open();
            var trial = await trialBoot.SelectForBootAsync();
            Assert.That(trial.Id, Is.EqualTo("v2"));
            Assert.That(trial.IsTrial, Is.True);
            // Simulate a new process reading the persisted marker before boot confirmation.
            var rollback = await fixture.Open().SelectForBootAsync();
            Assert.That(rollback.Id, Is.EqualTo("v1"));
            Assert.That(rollback.IsTrial, Is.False);

            await fixture.Open().StageAsync(Fixture.Manifest("v2"), Fixture.Copy);
            var successfulBoot = fixture.Open();
            successfulBoot.ConfirmRunning(await successfulBoot.SelectForBootAsync());
            var reopened = await fixture.Open().SelectForBootAsync();
            Assert.That(reopened.Id, Is.EqualTo("v2"));
            Assert.That(reopened.IsTrial, Is.False);
        });

        [UnityTest]
        public IEnumerator WindowsCatalogLoadsVerifiedLocalBundleForSelectedVersion() => UniTask.ToCoroutine(async () =>
        {
            string fixtures = Environment.GetEnvironmentVariable("NWF_WINDOWS_RELEASE_FIXTURES");
            if (string.IsNullOrEmpty(fixtures)) Assert.Ignore("Generate the Windows release fixtures first.");
            using var fixture = new Fixture();
            await StageFixtureAsync(fixture.Open(), fixtures, "v1");
            var firstBoot = fixture.Open();
            var first = await firstBoot.SelectForBootAsync();
            Assert.That(await ReadConfigAsync(first), Is.EqualTo("v1"));
            firstBoot.ConfirmRunning(first);

            await StageFixtureAsync(fixture.Open(), fixtures, "v2");
            var secondBoot = fixture.Open();
            var trial = await secondBoot.SelectForBootAsync();
            Assert.That(await ReadConfigAsync(trial), Is.EqualTo("v2"));
            // A restart before the Running checkpoint selects the complete v1 catalog and bundle.
            Assert.That(await ReadConfigAsync(await fixture.Open().SelectForBootAsync()), Is.EqualTo("v1"));
            await StageFixtureAsync(fixture.Open(), fixtures, "v2");
            var verifiedBoot = fixture.Open();
            var verified = await verifiedBoot.SelectForBootAsync();
            Assert.That(await ReadConfigAsync(verified), Is.EqualTo("v2"));
            verifiedBoot.ConfirmRunning(verified);
            Assert.That(await ReadConfigAsync(await fixture.Open().SelectForBootAsync()), Is.EqualTo("v2"));
        });

        static UniTask StageFixtureAsync(ReleaseStore store, string root, string id)
        {
            string directory = Path.Combine(root, id);
            string bundle = "release-" + id + ".bundle";
            ResourceRelease release = Fixture.Manifest(id);
            release.Files = new[]
            {
                Fixture.Describe("catalog.json", File.ReadAllBytes(Path.Combine(directory, "catalog.json"))),
                Fixture.Describe(bundle, File.ReadAllBytes(Path.Combine(directory, bundle)))
            };
            return store.StageAsync(release, async (path, output, ct) =>
            {
                using var input = File.OpenRead(Path.Combine(directory, path));
                await input.CopyToAsync(output, 65536, ct);
            });
        }

        static async UniTask<string> ReadConfigAsync(ReleaseSelection selection)
        {
            Assert.That(selection, Is.Not.Null);
            var provider = new AddressablesProvider(selection, TimeSpan.FromSeconds(10));
            var assets = new AssetService(provider);
            try
            {
                await assets.InitializeAsync(default);
                using var lease = await assets.AcquireAsync<TextAsset>("ReleaseProbe/Config", default);
                return lease.Asset.text;
            }
            finally { provider.RemoveReleaseCatalog(); }
        }

        [UnityTest]
        public IEnumerator RejectionCorruptionAndInterruptedDownloadKeepConfirmedRelease() => UniTask.ToCoroutine(async () =>
        {
            using var fixture = new Fixture();
            await fixture.ConfirmBaseAsync();

            var bad = Fixture.Manifest("v2");
            bad.ClientBuildId = "another-client";
            await ExpectAsync<InvalidDataException>(() => fixture.Open().StageAsync(bad,
                (_, __, ___) => throw new InvalidOperationException("Incompatible content reached transport.")));

            var traversal = Fixture.Manifest("v2");
            traversal.Files[1].Path = "../escape";
            await ExpectAsync<InvalidDataException>(() => fixture.Open().StageAsync(traversal,
                (_, __, ___) => throw new InvalidOperationException("Invalid path reached transport.")));

            await ExpectAsync<InvalidDataException>(() => fixture.Open().StageAsync(Fixture.Manifest("v2"),
                (path, output, ct) =>
                {
                    if (path == "catalog.json") return Fixture.Copy(path, output, ct);
                    output.WriteByte(123);
                    return UniTask.CompletedTask;
                }));

            using var canceled = new CancellationTokenSource();
            await ExpectAsync<OperationCanceledException>(() => fixture.Open().StageAsync(Fixture.Manifest("v2"),
                (path, output, ct) =>
                {
                    if (path == "catalog.json") return Fixture.Copy(path, output, ct);
                    output.WriteByte(123);
                    canceled.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return UniTask.CompletedTask;
                }, canceled.Token));

            var knownGood = await fixture.Open().SelectForBootAsync();
            Assert.That(knownGood.Id, Is.EqualTo("v1"));
            await fixture.Open().StageAsync(Fixture.Manifest("v2"), Fixture.Copy);
            File.WriteAllText(fixture.File("v2", "data.bundle"), "damaged");
            Assert.That((await fixture.Open().SelectForBootAsync()).Id, Is.EqualTo("v1"));
            await fixture.Open().StageAsync(Fixture.Manifest("v2"), Fixture.Copy);
            Assert.That((await fixture.Open().SelectForBootAsync()).Id, Is.EqualTo("v2"));

            File.WriteAllText(fixture.File("v1", "data.bundle"), "damaged");
            Assert.That(await fixture.Open().SelectForBootAsync(), Is.Null);
        });

        static async UniTask ExpectAsync<T>(Func<UniTask> work) where T : Exception
        {
            try { await work(); }
            catch (T) { return; }
            Assert.Fail("Expected " + typeof(T).Name);
        }
    }
}
