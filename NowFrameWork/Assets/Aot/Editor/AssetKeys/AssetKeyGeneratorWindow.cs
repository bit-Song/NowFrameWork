using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace NowFrameWork.EditorTools.AssetKeys
{
    internal sealed class AssetKeyGeneratorWindow : EditorWindow
    {
        const string OutputDirectory = "Assets/Generated/AssetKeys";
        const string OutputFile = OutputDirectory + "/AssetKeys.g.cs";
        const string AssemblyFile = OutputDirectory + "/NowFrameWork.Generated.AssetKeys.asmdef";
        const string AssemblyContent = "{\n  \"name\": \"NowFrameWork.Generated.AssetKeys\",\n  \"rootNamespace\": \"NowFrameWork.Generated\",\n  \"references\": [\"NowFrameWork.Assets\"],\n  \"autoReferenced\": false,\n  \"noEngineReferences\": true\n}\n";
        [SerializeField] string _root = "Assets";
        [SerializeField] string _namespace = "NowFrameWork.Generated";
        Vector2 _scroll;
        AssetKeySource.Plan _plan;
        string _status;

        [MenuItem("NowFrameWork/Assets/资源键生成器")]
        static void Open() => GetWindow<AssetKeyGeneratorWindow>("资源键生成器");

        void OnGUI()
        {
            EditorGUILayout.HelpBox("读取参与构建的 Addressables Group，按相对文件夹生成层级，末级为 文件名_扩展名。保留原 Address。", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            _root = EditorGUILayout.TextField("资源根目录", _root);
            _namespace = EditorGUILayout.TextField("代码命名空间", _namespace);
            if (EditorGUI.EndChangeCheck()) { _plan = null; _status = null; }
            EditorGUILayout.LabelField("输出", OutputFile);
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("1. 扫描资源与冲突（不写文件）")) Scan();
                using (new EditorGUI.DisabledScope(_plan == null || _plan.Errors.Count != 0))
                    if (GUILayout.Button("2. 预览后确认生成…")) ConfirmGenerate();
            }
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            if (_plan == null) return;
            EditorGUILayout.LabelField("资源 " + _plan.Entries.Count + " / 问题 " + _plan.Errors.Count);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (string error in _plan.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (AssetKeySource.Entry entry in _plan.Entries)
            {
                EditorGUILayout.SelectableLabel(entry.Symbol ?? "（未生成）", GUILayout.Height(18));
                EditorGUILayout.SelectableLabel(entry.Path + "  [" + entry.Group + "]", GUILayout.Height(18));
                EditorGUILayout.SelectableLabel("Address: " + entry.Address, GUILayout.Height(18));
                if (GUILayout.Button("定位资源", GUILayout.Width(90)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(entry.Path));
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();
        }

        void Scan()
        {
            try
            {
                _plan = ScanCatalog(_root, _namespace);
                _status = _plan.Errors.Count != 0 ? "请修正上方问题后重新扫描；现有生成文件保持不变。" :
                    File.Exists(OutputFile) && File.ReadAllText(OutputFile).Replace("\r\n", "\n") == _plan.Source
                    ? "扫描通过，已有代码与当前资源一致。" : "扫描通过；确认前不会创建或修改文件。";
            }
            catch (Exception error) { _plan = null; _status = "扫描失败: " + error.Message; Debug.LogException(error); }
        }

        internal static AssetKeySource.Plan ScanCatalog(string root, string nameSpace)
        {
            var entries = new List<AssetKeySource.Entry>();
            var allAddresses = new Dictionary<string, AssetKeySource.Entry>(StringComparer.Ordinal);
            var errors = new List<string>();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("未找到 Addressables 配置");
            root = root.Replace('\\', '/').TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root)) throw new ArgumentException("资源根目录不存在: " + root);
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null) continue;
                BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema == null || !schema.IncludeInBuild) continue;
                var gathered = new List<AddressableAssetEntry>();
                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (string.IsNullOrEmpty(entry.AssetPath))
                    {
                        if (root == "Assets") errors.Add("资源丢失 [Group: " + group.Name + ", Address: " + entry.address + ", GUID: " + entry.guid + "]");
                        continue;
                    }
                    entry.GatherAllAssets(gathered, true, true, false);
                }
                foreach (AddressableAssetEntry entry in gathered)
                {
                    string path = entry.AssetPath;
                    var item = new AssetKeySource.Entry { Path = path, Address = entry.address, Group = group.Name };
                    if (schema.IncludeAddressInCatalog && !string.IsNullOrEmpty(entry.address))
                    {
                        if (allAddresses.TryGetValue(entry.address, out AssetKeySource.Entry previous))
                        {
                            if (path.StartsWith(root + "/", StringComparison.Ordinal) || previous.Path.StartsWith(root + "/", StringComparison.Ordinal))
                                errors.Add("Address 在参与构建的 Group 中重复 '" + entry.address + "':\n"
                                    + previous.Path + " [" + previous.Group + "]\n" + path + " [" + group.Name + "]");
                        }
                        else allAddresses.Add(entry.address, item);
                    }
                    if (!path.StartsWith(root + "/", StringComparison.Ordinal)) continue;
                    if (!schema.IncludeAddressInCatalog) errors.Add("Group 未启用 Include Addresses in Catalog: " + group.Name + " / " + path);
                    entries.Add(item);
                }
            }
            AssetKeySource.Plan plan = AssetKeySource.Build(entries, root, nameSpace);
            plan.Errors.AddRange(errors);
            if (errors.Count != 0) plan.Source = null;
            return plan;
        }

        void ConfirmGenerate()
        {
            string preview = _plan.Source;
            Scan(); // 再次检查资源变动，不能写入过期的预览。
            if (_plan == null || _plan.Errors.Count != 0) return;
            if (_plan.Source != preview) { _status = "资源已变化，预览已刷新。请检查后再次点击生成。"; return; }
            if (EditorUtility.DisplayDialog("确认生成资源键", "扫描通过，共 " + _plan.Entries.Count + " 个资源。\n输出: " + OutputFile
                + "\n将创建或更新专用程序集 NowFrameWork.Generated.AssetKeys。\n目录/文件改名可能改变代码引用，请检查预览。", "确认生成", "取消"))
            {
                try
                {
                    AssetKeySource.Plan latest = ScanCatalog(_root, _namespace);
                    if (latest.Errors.Count != 0 || latest.Source != preview) throw new InvalidOperationException("确认期间资源已变化，请重新扫描");
                    WriteGenerated(latest.Source);
                    _status = "生成完成。业务 asmdef 请显式引用 NowFrameWork.Generated.AssetKeys。";
                }
                catch (Exception error) { _status = "生成失败: " + error.Message; Debug.LogException(error); }
            }
        }

        static void WriteGenerated(string source)
        {
            if (File.Exists(OutputFile) && !File.ReadAllText(OutputFile).StartsWith(AssetKeySource.Marker, StringComparison.Ordinal))
                throw new IOException("输出位置已有非生成代码，拒绝覆盖: " + OutputFile);
            if (File.Exists(AssemblyFile) && File.ReadAllText(AssemblyFile).Replace("\r\n", "\n") != AssemblyContent)
                throw new IOException("输出位置已有不同程序集配置，拒绝覆盖: " + AssemblyFile);
            Directory.CreateDirectory(OutputDirectory);
            if (!File.Exists(AssemblyFile)) AtomicWrite(AssemblyFile, AssemblyContent);
            AtomicWrite(OutputFile, source);
            AssetDatabase.Refresh();
        }

        static void AtomicWrite(string path, string content)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
