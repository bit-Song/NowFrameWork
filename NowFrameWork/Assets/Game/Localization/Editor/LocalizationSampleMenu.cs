using System;
using System.Linq;
using Game;
using Game.Localization;
using NowFrameWork.Boot;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NowFrameWork.EditorTools
{
    public static class LocalizationSampleMenu
    {
        const string Folder = "Assets/Tests/Localization";
        const string Panel = Folder + "/Panel.prefab";
        const string ScenePath = "Assets/Scenes/UILocalization.scene";
        const string PanelAddress = "UiProbe/LocalizedPanel";

        [MenuItem("NowFrameWork/Tests/UI/创建本地化样例")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Tests", "Localization");

            if (LocalizationEditorSettings.ActiveLocalizationSettings == null)
            {
                var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                AssetDatabase.CreateAsset(settings, Folder + "/LocalizationSettings.asset");
                settings.GetStartupLocaleSelectors().Add(new SystemLocaleSelector());
                settings.GetStartupLocaleSelectors().Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier(LanguageSelection.English) });
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
                EditorUtility.SetDirty(settings);
            }

            Locale english = GetLocale(LanguageSelection.English);
            Locale chinese = GetLocale(LanguageSelection.Chinese);
            if (!chinese.GetFallbacks().Contains(english))
            {
                chinese.Metadata.AddMetadata(new FallbackLocale(english));
                EditorUtility.SetDirty(chinese);
            }

            var collection = LocalizationEditorSettings.GetStringTableCollection(LanguageSelection.Table)
                ?? LocalizationEditorSettings.CreateStringTableCollection(LanguageSelection.Table, Folder, new[] { english, chinese });
            var en = (StringTable)(collection.GetTable(english.Identifier) ?? collection.AddNewTable(english.Identifier));
            var zh = (StringTable)(collection.GetTable(chinese.Identifier) ?? collection.AddNewTable(chinese.Identifier));
            AddEntry(en, "panel.heading", "Localized UI ({0} items)");
            AddEntry(en, "panel.fallback", "Fallback: English text");
            AddEntry(zh, "panel.heading", "本地化界面（{0} 项）");
            EditorUtility.SetDirty(collection.SharedData);
            ConfigurePanel();
            ConfigureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("本地化样例已就绪：" + ScenePath);
        }

        static Locale GetLocale(string code)
        {
            Locale locale = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(code));
            if (locale != null) return locale;
            locale = Locale.CreateLocale(code);
            AssetDatabase.CreateAsset(locale, Folder + "/" + code + ".asset");
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        static void AddEntry(StringTable table, string key, string text)
        {
            if (table.GetEntry(key) != null) return;
            table.AddEntry(key, text);
            EditorUtility.SetDirty(table);
        }

        static void ConfigurePanel()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Panel) == null)
            {
                if (!AssetDatabase.CopyAsset("Assets/Tests/UIIntegration/Panel.prefab", Panel))
                    throw new InvalidOperationException("复制 UI 样例面板失败");
                var prefab = PrefabUtility.LoadPrefabContents(Panel);
                try
                {
                    prefab.GetComponentInChildren<Text>(true).gameObject.AddComponent<LocalizedSampleText>();
                    PrefabUtility.SaveAsPrefabAsset(prefab, Panel);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            string guid = AssetDatabase.AssetPathToGUID(Panel);
            var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            if (entry.address != PanelAddress) entry.SetAddress(PanelAddress);
        }

        static void ConfigureScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null &&
                !AssetDatabase.CopyAsset("Assets/Scenes/UIIntegration.scene", ScenePath))
                throw new InvalidOperationException("复制 UI 样例场景失败");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var ui = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<UiFlowSample>(true)).Single();
                var localized = ui.GetComponent<LocalizedUiEntry>() ?? ui.gameObject.AddComponent<LocalizedUiEntry>();
                Set(localized, "_ui", ui);
                var so = new SerializedObject(ui);
                so.FindProperty("_panelAddress").stringValue = PanelAddress;
                so.ApplyModifiedPropertiesWithoutUndo();
                Set(ui.GetComponent<BootFlow>(), "_projectEntry", localized);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("保存本地化样例场景失败");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        static void Set(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
