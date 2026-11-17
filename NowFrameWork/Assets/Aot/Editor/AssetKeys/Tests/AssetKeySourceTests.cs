using System;
using NUnit.Framework;

namespace NowFrameWork.EditorTools.AssetKeys
{
    public sealed class AssetKeySourceTests
    {
        static AssetKeySource.Entry E(string path, string address = null) =>
            new AssetKeySource.Entry { Path = path, Address = address ?? path, Group = "Test" };
        static AssetKeySource.Plan Build(params AssetKeySource.Entry[] entries) => AssetKeySource.Build(entries, "Assets/GameRes", "Game.Generated");

        [Test]
        public void SameFileNames_InDifferentDirectories_HaveDistinctSymbols()
        {
            var result = Build(E("Assets/GameRes/Battle/Enemy/Goblin.prefab", "Enemy/Goblin"),
                E("Assets/GameRes/Collection/Preview/Goblin.prefab", "Preview/Goblin"));
            Assert.IsEmpty(result.Errors);
            Assert.AreEqual("AssetKeys.Battle.Enemy.Goblin_prefab", result.Entries[0].Symbol);
            StringAssert.Contains("AssetKey.From(\"Enemy/Goblin\")", result.Source);
        }

        [Test]
        public void DifferentExtensions_CreateDistinctFields()
        {
            var result = Build(E("Assets/GameRes/UI/Icon.png"), E("Assets/GameRes/UI/Icon.prefab"));
            Assert.IsEmpty(result.Errors);
            StringAssert.Contains("Icon_png", result.Source);
            StringAssert.Contains("Icon_prefab", result.Source);
        }

        [Test]
        public void SanitizedFileCollision_ReportsBothPaths_AndProducesNoSource()
        {
            var result = Build(E("Assets/GameRes/UI/icon-large.png"), E("Assets/GameRes/UI/icon_large.png"));
            Assert.IsNotEmpty(result.Errors);
            StringAssert.Contains("icon-large.png", string.Join("\n", result.Errors));
            StringAssert.Contains("icon_large.png", string.Join("\n", result.Errors));
            Assert.IsNull(result.Source);
        }

        [Test]
        public void SanitizedDirectoryCollision_IsNotSilentlyMerged()
        {
            var result = Build(E("Assets/GameRes/A-B/One.png"), E("Assets/GameRes/A_B/Two.png"));
            Assert.IsNotEmpty(result.Errors);
            Assert.IsNull(result.Source);
        }

        [Test]
        public void DirectoryAndFieldCollision_IsRejected()
        {
            var result = Build(E("Assets/GameRes/UI/Icon.png"), E("Assets/GameRes/UI/Icon_png/Child.png"));
            Assert.IsNotEmpty(result.Errors);
            Assert.IsNull(result.Source);
        }

        [Test]
        public void DuplicateAddressAcrossGroups_IsRejected()
        {
            var a = E("Assets/GameRes/One/A.png", "same");
            var b = E("Assets/GameRes/Two/B.png", "same"); b.Group = "Other";
            var result = Build(a, b);
            StringAssert.Contains("Address 重复", string.Join("\n", result.Errors));
            StringAssert.Contains("Other", string.Join("\n", result.Errors));
            Assert.IsNull(result.Source);
        }

        [Test]
        public void KeywordsAndLeadingDigits_AreValidIdentifiers()
        {
            var result = Build(E("Assets/GameRes/class/123.prefab"));
            Assert.IsEmpty(result.Errors);
            StringAssert.Contains("static class _class", result.Source);
            StringAssert.Contains("_123_prefab", result.Source);
        }

        [Test]
        public void AddressWithQuotesAndControls_IsEscaped()
        {
            var result = Build(E("Assets/GameRes/A.prefab", "a\"b\\c\n"));
            Assert.IsEmpty(result.Errors);
            StringAssert.Contains("a\\\"b\\\\c\\u000a", result.Source);
        }

        [Test]
        public void OutputIsDeterministic_RegardlessOfEntryOrder()
        {
            var a = E("Assets/GameRes/A/A.png"); var b = E("Assets/GameRes/B/B.png");
            Assert.AreEqual(Build(a, b).Source, Build(b, a).Source);
        }

        [Test]
        public void RootFilter_UsesDirectoryBoundary()
        {
            var result = Build(E("Assets/GameResOther/A.png"), E("Assets/GameRes/A.png"));
            Assert.IsEmpty(result.Errors);
            Assert.AreEqual(1, result.Entries.Count);
        }

        [Test]
        public void InvalidNamespaceOrRoot_ProducesNoSource()
        {
            var entries = new[] { E("Assets/GameRes/A.png") };
            Assert.IsNull(AssetKeySource.Build(entries, "Assets/GameRes", "Game.class").Source);
            Assert.IsNull(AssetKeySource.Build(entries, "../Assets", "Game").Source);
        }

        [Test]
        public void MissingExtensionOrEmptyInput_IsRejected()
        {
            Assert.IsNull(Build(E("Assets/GameRes/A")).Source);
            Assert.IsNull(Build().Source);
        }

        [Test]
        public void NestedTypeWithSameNameAsParent_IsRejected()
        {
            Assert.IsNull(Build(E("Assets/GameRes/UI/UI/A.png")).Source);
        }
    }
}
