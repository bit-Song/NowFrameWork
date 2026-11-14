using System;
using System.Reflection;
using HybridCLR;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class LoadDllScript : MonoBehaviour
{
    // 当前 Addressables 里就是用路径登记的地址；将来换成稳定地址再改这里
    const string DllAddress = "Assets/HotUpdateDlls/HotUpdate.dll.bytes";

    [Header("测试用：-1 跑全部步骤，>=0 只跑那一步")]
    public int onlyStep = -1;

    [Header("H03 第二轮开关：加载 AOT 补充元数据后再加载热更 dll")]
    public bool loadAotMetadata = false;

    // 必须在热更 dll 之前加载；路径即地址（拖进 Addressables 后默认就是路径）
    static readonly string[] AotMetaAddresses =
    {
        "Assets/AOTMeta/mscorlib.dll.bytes",
        "Assets/AOTMeta/System.dll.bytes",
        "Assets/AOTMeta/System.Core.dll.bytes",
        "Assets/AOTMeta/UnityEngine.CoreModule.dll.bytes",
        "Assets/AOTMeta/AotRuntime.dll.bytes",
    };

    async void Start()
    {
        try
        {
            // 0) 先补元数据，顺序不能和加载 dll 调换。
            //    勾选框或命令行 -aotmeta 任一为真即启用，方便同一个 exe 做 A/B 对照
            bool loadMeta = loadAotMetadata || HasArg("-aotmeta");
            Debug.Log("[HotUpdate] loadAotMetadata=" + loadMeta);
            if (loadMeta) await LoadAotMetadataAsync();

            // 1) 本地资源时返回 0，自动跳过下载
            long needBytes = 0;
            var sizeHandle = Addressables.GetDownloadSizeAsync(DllAddress);
            try
            {
                needBytes = await sizeHandle.Task;
                if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
                    Debug.LogWarning("[HotUpdate] 取下载大小失败: " + sizeHandle.OperationException);
            }
            finally
            {
                if (sizeHandle.IsValid()) Addressables.Release(sizeHandle);
            }

            if (needBytes > 0)
            {
                Debug.Log($"[HotUpdate] 需下载 {needBytes / 1024f:F1} KB");
                var dlHandle = Addressables.DownloadDependenciesAsync(DllAddress);
                try
                {
                    await dlHandle.Task;
                    if (dlHandle.Status != AsyncOperationStatus.Succeeded)
                        throw new InvalidOperationException("[HotUpdate] 下载 dll 失败", dlHandle.OperationException);
                }
                finally
                {
                    if (dlHandle.IsValid()) Addressables.Release(dlHandle);
                }
            }

            // 2) 加载 dll 字节并执行热更入口
            var textAssetHandle = Addressables.LoadAssetAsync<TextAsset>(DllAddress);
            try
            {
                var textAsset = await textAssetHandle.Task;
                if (textAsset == null)
                    throw new InvalidOperationException("[HotUpdate] 资源为空: " + DllAddress);

                Assembly asm = Assembly.Load(textAsset.bytes);   // bytes 是托管副本，Load 后可释放
                Type helloType = asm.GetType("Hello");
                if (helloType == null)
                    throw new MissingMemberException("[HotUpdate] 热更 dll 里找不到类型: Hello");

                MethodInfo runStep = helloType.GetMethod("RunStep", BindingFlags.Public | BindingFlags.Static);
                if (runStep == null)
                    throw new MissingMethodException("[HotUpdate] 找不到静态方法: Hello.RunStep(int)");

                runStep.Invoke(null, new object[] { onlyStep });
                Debug.Log("[HotUpdate] 热更 dll 执行完毕 " + asm.GetName().Name + " " + asm.GetName().Version);
            }
            finally
            {
                if (textAssetHandle.IsValid()) Addressables.Release(textAssetHandle);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[HotUpdate] 失败: " + e);
        }
    }

    static bool HasArg(string name)
    {
        foreach (var a in Environment.GetCommandLineArgs())
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // H03 第二轮：给解释器补充 AOT 元数据，让它能运行 AOT 里没有实例化的泛型
    async System.Threading.Tasks.Task LoadAotMetadataAsync()
    {
        Debug.Log("[HotUpdate] 开始加载 AOT 补充元数据");
        foreach (var address in AotMetaAddresses)
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(address);
            try
            {
                var ta = await handle.Task;
                if (ta == null)
                {
                    Debug.LogError("[HotUpdate] 缺少元数据: " + address + "（是否已加进 Addressables 并 Build？）");
                    continue;
                }
                var err = RuntimeApi.LoadMetadataForAOTAssembly(ta.bytes, HomologousImageMode.Consistent);
                Debug.Log("[HotUpdate] meta " + System.IO.Path.GetFileName(address) + " -> " + err);
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }
    }
}