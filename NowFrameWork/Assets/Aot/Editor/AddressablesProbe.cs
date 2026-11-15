using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace NowFrameWork.EditorTools
{
    /// <summary>
    /// 资源实验：验证 Provider 三个关键行为（EditMode 可跑，用 WaitForCompletion 同步等待）。
    /// 1) 地址能否解析出 PrimaryKey；2) 能否加载到资源；3) Release 后资源是否立即失效（决定 R-1）。
    /// </summary>
    public static class AddressablesProbe
    {
        const string Address = "Assets/HotUpdateDlls/HotUpdate.dll.bytes";

        [MenuItem("NowFrameWork/Tests/资源实验：加载 / 释放 / 键")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Addressables Provider 探针");
            sb.AppendLine("unity: " + Application.unityVersion);
            sb.AppendLine("address: " + Address);
            sb.AppendLine();

            try
            {
                Addressables.InitializeAsync().WaitForCompletion();
                sb.AppendLine("## 1. 初始化: OK");

                AsyncOperationHandle<IList<IResourceLocation>> locations =
                    Addressables.LoadResourceLocationsAsync(Address, typeof(UnityEngine.Object));
                IList<IResourceLocation> list = locations.WaitForCompletion();

                string primary = (list != null && list.Count > 0) ? list[0].PrimaryKey : null;
                sb.AppendLine("## 2. locations: " + (list?.Count ?? 0) + "  PrimaryKey: " + (primary ?? "(null)"));
                Addressables.Release(locations);

                AsyncOperationHandle<UnityEngine.Object> load =
                    Addressables.LoadAssetAsync<UnityEngine.Object>(Address);
                UnityEngine.Object asset = load.WaitForCompletion();
                int id = asset != null ? asset.GetInstanceID() : 0;

                sb.AppendLine("## 3. 加载: " + (asset != null ? "OK (" + asset.GetType().Name + ")" : "失败"));

                Addressables.Release(load);

                bool managedNull = asset == null;
                bool idAlive = id != 0 && Resources.InstanceIDToObject(id) != null;

                sb.AppendLine("## 4. Release 之后:");
                sb.AppendLine("   托管引用 == null: " + managedNull);
                sb.AppendLine("   InstanceID 仍可用: " + idAlive);
                sb.AppendLine("   结论: " + ((!idAlive) ? "资源立即失效 → R-1 必须保留" : "资源仍存活"));
            }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION: " + e);
            }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            string path = Path.Combine(root, "EXE", "_evidence", "addressables-probe.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            Debug.Log("[Probe] 报告: " + path + "\n" + sb);
        }
    }
}