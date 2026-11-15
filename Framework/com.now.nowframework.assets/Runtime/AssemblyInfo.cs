using System.Runtime.CompilerServices;

// 测试需要访问 internal 的计数不变量自检
[assembly: InternalsVisibleTo("NowFrameWork.Assets.Tests")]

// Addressables 适配层需要构造 SceneLease（构造函数保持 internal，不暴露给业务）
[assembly: InternalsVisibleTo("NowFrameWork.Assets.Addressables")]