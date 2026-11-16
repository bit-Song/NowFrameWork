using System;

namespace NowFrameWork.Boot
{
    /// <summary>一次装配使用不可变策略；更新键应覆盖本次配置、场景、DLL 及元数据的依赖集合。</summary>
    public sealed class BootOptions
    {
        public string ConfigAddress { get; }
        public string SceneAddress { get; }
        public bool CheckResourceUpdates { get; }
        public string UpdateKey { get; }
        public bool LoadCode { get; }

        public BootOptions(string configAddress = "", string sceneAddress = "", bool checkResourceUpdates = false,
            string updateKey = "", bool loadCode = false)
        {
            if (checkResourceUpdates && string.IsNullOrWhiteSpace(updateKey))
                throw new ArgumentException("启用资源更新必须提供覆盖本次资源的更新键/标签", nameof(updateKey));
            ConfigAddress = configAddress;
            SceneAddress = sceneAddress;
            CheckResourceUpdates = checkResourceUpdates;
            UpdateKey = updateKey;
            LoadCode = loadCode;
        }
    }
}
