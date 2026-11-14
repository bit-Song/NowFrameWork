#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NowFrameWork.Probe
{
    /// <summary>
    /// 实验：AssetBundle.Unload(false) 与 Unload(true) 之后，资源什么时候变成"不可用"。
    ///
    /// 背景：Addressables 1.22.4-t1 在 AssetBundleProvider.Unload() 里调的是 Unload(true)，
    /// 需要确认这个调用是否会立即让资源失效——这决定了「销毁实例后能否同帧释放租约」。
    ///
    /// 用法：-executeMethod NowFrameWork.Probe.BundleUnloadProbe.Run
    /// </summary>
    public static class BundleUnloadProbe
    {
        const string ProbeDir = "Assets/__Probe";
        const string ProbeFile = ProbeDir + "/probe.txt";
        const string BundleDir = "Temp/__probe_bundles";
        const string ReportPath = "E:/Unity/TuanJieProject/NowFrameWork/EXE/_evidence/ab-unload-probe.txt";

        [MenuItem("NowFrameWork/实验/AssetBundle 卸载语义")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AssetBundle 卸载语义探针");
            sb.AppendLine("unity: " + Application.unityVersion);
            sb.AppendLine("utc: " + System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            try
            {
                Prepare();
                string bundlePath = BuildBundle();

                CaseUnloadFalse(sb, bundlePath);
                CaseUnloadTrue(sb, bundlePath);
            }
            catch (System.Exception e)
            {
                sb.AppendLine("EXCEPTION: " + e);
            }
            finally
            {
                Cleanup();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            Debug.Log("[Probe] 报告已写入 " + ReportPath);
            Debug.Log(sb.ToString());
        }

        static void Prepare()
        {
            Directory.CreateDirectory(ProbeDir);
            File.WriteAllText(ProbeFile, "hello-probe");
            AssetDatabase.ImportAsset(ProbeFile, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(ProbeFile);
            importer.assetBundleName = "probe";
            AssetDatabase.Refresh();
        }

        static string BuildBundle()
        {
            Directory.CreateDirectory(BundleDir);

            BuildPipeline.BuildAssetBundles(BundleDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);

            return Path.Combine(BundleDir, "probe");
        }

        static void CaseUnloadFalse(StringBuilder sb, string bundlePath)
        {
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            var asset = bundle.LoadAsset<TextAsset>("probe.txt");
            int id = asset.GetInstanceID();

            sb.AppendLine("## A. Unload(false)");
            sb.AppendLine("  加载后可用: " + (Resources.InstanceIDToObject(id) != null));

            bundle.Unload(false);

            sb.AppendLine("  Unload(false) 后立即可用: " + (Resources.InstanceIDToObject(id) != null));

            Resources.UnloadUnusedAssets();
            sb.AppendLine("  UnloadUnusedAssets 后可用: " + (Resources.InstanceIDToObject(id) != null));
            sb.AppendLine();
        }

        static void CaseUnloadTrue(StringBuilder sb, string bundlePath)
        {
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            var asset = bundle.LoadAsset<TextAsset>("probe.txt");
            int id = asset.GetInstanceID();

            sb.AppendLine("## B. Unload(true)");
            sb.AppendLine("  加载后可用: " + (Resources.InstanceIDToObject(id) != null));

            bundle.Unload(true);

            sb.AppendLine("  Unload(true) 后立即可用: " + (Resources.InstanceIDToObject(id) != null));
            sb.AppendLine("  托管引用是否为 null: " + (asset == null));
            sb.AppendLine();
        }

        static void Cleanup()
        {
            if (Directory.Exists(ProbeDir)) AssetDatabase.DeleteAsset(ProbeDir);
            if (Directory.Exists(BundleDir)) Directory.Delete(BundleDir, true);
        }
    }
}
#endif