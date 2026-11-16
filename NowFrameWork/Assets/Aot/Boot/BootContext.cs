using System;
using System.Reflection;
using NowFrameWork.Assets;
using NowFrameWork.Core.Storage;
using NowFrameWork.Unity;

namespace NowFrameWork.Boot
{
    /// <summary>仅主线程；由一次 BootSession 尝试独占。成功后保留，清理完成后丢弃，不复用。</summary>
    public sealed class BootContext
    {
        public App App { get; }
        public AssetService Assets { get; }
        public ConfigService Configs { get; }
        public FileSettingsStore<BootPreferences> Settings { get; }
        public ISceneService Scenes { get; }
        public SettingsLoadResult SettingsResult { get; internal set; }
        public UnityEngine.Object Configuration { get; internal set; }
        public SceneLease Scene { get; internal set; }
        public Assembly CodeAssembly { get; internal set; }
        internal bool Consumed;

        public BootContext(App app, AssetService assets, FileSettingsStore<BootPreferences> settings, ISceneService scenes = null)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            Assets = assets ?? throw new ArgumentNullException(nameof(assets));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Scenes = scenes;
            Configs = new ConfigService(assets);
            App.Hub.Register(new AssetsModule(assets, App.Log));
        }
    }
}
