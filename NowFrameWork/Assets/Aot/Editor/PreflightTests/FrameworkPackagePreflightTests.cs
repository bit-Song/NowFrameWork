using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using AssemblyEntry = NowFrameWork.EditorTools.FrameworkPackagePreflight.AssemblyEntry;

namespace NowFrameWork.EditorTools.Tests
{
    public sealed class FrameworkPackagePreflightTests
    {
        const string Core = "com.now.nowframework.core";
        const string Assets = "com.now.nowframework.assets";
        const string Ui = "com.now.nowframework.ui.ugui";
        const string InputSystem = "com.unity.inputsystem";
        const string GameGuid = "5bc64cb378594262ad279c1ace57eb2b";

        [TestCase("Game")]
        [TestCase("GUID:5BC64CB378594262AD279C1ACE57EB2B")]
        [TestCase("Assembly-CSharp")]
        public void PackageCannotReferenceProjectAssembly(string reference)
        {
            var assembly = Entry("Core", Core, reference);
            var game = new AssemblyEntry("Game", GameGuid, "Assets/Game/Game.asmdef", null);
            var issue = Inspect(new[] { assembly, game }, new Dictionary<string, string[]>()).Single();

            Assert.That(issue.Code, Is.EqualTo("DEP001"));
            Assert.That(issue.Location, Is.EqualTo(assembly.Path));
        }

        [Test]
        public void TransitivePackageDoesNotCountAsAnExplicitDeclaration()
        {
            var widget = Entry("Ui", Ui, "Core", "Assets", "Unity.InputSystem");
            var core = Entry("Core", Core);
            var assets = Entry("Assets", Assets);
            var input = Entry("Unity.InputSystem", InputSystem);
            var deps = new Dictionary<string, string[]> { [Ui] = new[] { Assets }, [Assets] = new[] { Core } };
            var issues = Inspect(new[] { widget, core, assets, input }, deps);

            Assert.That(issues.Select(issue => issue.Code), Is.EquivalentTo(new[] { "DEP002", "DEP002" }));
            Assert.That(issues.Any(issue => issue.Message.Contains(Core)));
            Assert.That(issues.Any(issue => issue.Message.Contains(InputSystem)));
            deps[Ui] = new[] { Assets, Core, InputSystem };
            Assert.That(Inspect(new[] { widget, core, assets, input }, deps), Is.Empty);
        }

        [Test]
        public void SamePackageAndProjectConsumersAreValid()
        {
            var runtime = Entry("Core", Core);
            var editor = Entry("Core.Editor", Core, "Core");
            var game = Entry("Game", null, "Core");

            Assert.That(Inspect(new[] { runtime, editor, game }, new Dictionary<string, string[]>()), Is.Empty);
        }

        [Test]
        public void MissingAndAmbiguousReferencesAreReported()
        {
            var assembly = Entry("Core", Core, "Missing", "Two");
            var first = Entry("Two", Assets);
            var second = Entry("Two", Ui);
            var issues = Inspect(new[] { assembly, first, second }, new Dictionary<string, string[]>());

            Assert.That(issues.Select(issue => issue.Code), Is.EquivalentTo(new[] { "DEP003", "DEP003" }));
        }

        [Test]
        public void TestRunnerExemptionAppliesOnlyToPackageTestsAndOfficialRunner()
        {
            const string runner = "UnityEngine.TestRunner";
            var runtime = Entry("Core", Core, runner);
            var tests = new AssemblyEntry("Core.Tests", "", "Core.Tests.asmdef", Core, new[] { runner }, isTest: true);
            var officialRunner = Entry(runner, "com.unity.test-framework");
            var spoofedRunner = Entry(runner, null);

            Assert.That(Inspect(new[] { runtime, tests, officialRunner }, new Dictionary<string, string[]>())
                .Select(issue => issue.Code), Is.EqualTo(new[] { "DEP002" }));
            Assert.That(Inspect(new[] { tests, spoofedRunner }, new Dictionary<string, string[]>())
                .Select(issue => issue.Code), Is.EqualTo(new[] { "DEP001" }));
        }

        static AssemblyEntry Entry(string name, string package, params string[] references)
            => new AssemblyEntry(name, Guid.NewGuid().ToString("N"),
                (package == null ? "Assets" : "Packages/" + package) + "/" + name + ".asmdef", package, references);

        static FrameworkPreflight.Issue[] Inspect(AssemblyEntry[] assemblies, Dictionary<string, string[]> dependencies)
            => FrameworkPackagePreflight.Inspect(assemblies, dependencies).ToArray();
    }
}
