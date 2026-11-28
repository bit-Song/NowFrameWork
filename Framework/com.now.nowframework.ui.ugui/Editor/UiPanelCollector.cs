using System;
using System.Collections.Generic;
using System.IO;
using NowFrameWork.UI;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace NowFrameWork.UI.Editor
{
    /// <summary>扫描带 UiPanelDefinition 的 Prefab，校验后生成/更新 UiCatalog。</summary>
    public static class UiPanelCollector
    {
        const string CatalogPath = "Assets/NowFrameWork/UI/UiCatalog.asset";

        [MenuItem("NowFrameWork/UI/收集面板定义", priority = 2)]
        public static void Collect()
        {
            if (!Build(out List<UiCatalog.Entry> entries, out List<string> errors))
            {
                foreach (string error in errors) Debug.LogError("[UI 定义] " + error);
                EditorUtility.DisplayDialog("收集面板定义失败", string.Join("\n", errors), "好");
                return;
            }

            UiCatalog catalog = AssetDatabase.LoadAssetAtPath<UiCatalog>(CatalogPath);

            if (catalog == null)
            {
                string folder = Path.GetDirectoryName(CatalogPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                catalog = ScriptableObject.CreateInstance<UiCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("_entries");
            list.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                UiCatalog.Entry entry = entries[i];

                item.FindPropertyRelative("Id").intValue = entry.Id;
                item.FindPropertyRelative("Name").stringValue = entry.Name;
                item.FindPropertyRelative("Address").stringValue = entry.Address;
                item.FindPropertyRelative("Layer").enumValueIndex = (int)entry.Layer;
                item.FindPropertyRelative("MultiInstance").boolValue = entry.MultiInstance;
                item.FindPropertyRelative("Modal").boolValue = entry.Modal;
                item.FindPropertyRelative("CloseOnBackdrop").boolValue = entry.CloseOnBackdrop;
                item.FindPropertyRelative("BackBehavior").enumValueIndex = (int)entry.BackBehavior;
                item.FindPropertyRelative("CacheOnClose").boolValue = entry.CacheOnClose;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            Debug.Log("[UI 定义] 已收集 " + entries.Count + " 个面板到 " + CatalogPath, catalog);
            Selection.activeObject = catalog;
        }

        [MenuItem("NowFrameWork/UI/校验面板定义", priority = 3)]
        public static void Validate()
        {
            bool ok = Build(out List<UiCatalog.Entry> entries, out List<string> errors);

            foreach (string error in errors) Debug.LogError("[UI 定义] " + error);

            EditorUtility.DisplayDialog("校验面板定义",
                ok ? "通过，共 " + entries.Count + " 个面板。" : "发现 " + errors.Count + " 个问题，详见 Console。", "好");
        }

        static bool Build(out List<UiCatalog.Entry> entries, out List<string> errors)
        {
            entries = new List<UiCatalog.Entry>();
            errors = new List<string>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                errors.Add("未找到 Addressables 配置。");
                return false;
            }

            var byId = new Dictionary<int, string>();
            var byName = new Dictionary<string, string>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab == null) continue;

                UiPanelDefinition definition = prefab.GetComponent<UiPanelDefinition>();
                if (definition == null) continue;

                if (definition.PanelId <= 0)
                {
                    errors.Add(path + "：未填写 PanelId。");
                    continue;
                }

                if (byId.TryGetValue(definition.PanelId, out string previous))
                {
                    errors.Add("PanelId 重复 " + definition.PanelId + "：" + previous + " 与 " + path);
                    continue;
                }

                byId.Add(definition.PanelId, path);

                if (byName.TryGetValue(prefab.name, out string previousName))
                    errors.Add("面板名重复 " + prefab.name + "：" + previousName + " 与 " + path);
                else
                    byName.Add(prefab.name, path);

                if (prefab.GetComponent<UiView>() == null)
                {
                    errors.Add(path + "：根节点没有 UiView 派生组件。");
                    continue;
                }

                AddressableAssetEntry addressable = settings.FindAssetEntry(guid);

                if (addressable == null || string.IsNullOrEmpty(addressable.address))
                {
                    errors.Add(path + "：未登记为 Addressable（或地址为空）。");
                    continue;
                }

                if (definition.Modal && definition.Layer != UiLayer.Popup)
                    errors.Add(path + "：Modal 只允许放在 Popup 层。");

                entries.Add(new UiCatalog.Entry
                {
                    Id = definition.PanelId,
                    Name = prefab.name,
                    Address = addressable.address,
                    Layer = definition.Layer,
                    MultiInstance = definition.MultiInstance,
                    Modal = definition.Modal,
                    CloseOnBackdrop = definition.CloseOnBackdrop,
                    BackBehavior = definition.BackBehavior,
                    CacheOnClose = definition.CacheOnClose,
                });
            }

            if (errors.Count > 0) return false;
            if (entries.Count == 0) errors.Add("没有找到任何带 UiPanelDefinition 的 Prefab。");

            return errors.Count == 0;
        }
    }
}
