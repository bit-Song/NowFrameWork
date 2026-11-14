using System;

namespace NowFrameWork.Core
{
    /// <summary>注册阶段的错误：null 模块、空 Id、重复 Id、启动后再注册。</summary>
    public sealed class ModuleRegistrationException : Exception
    {
        public ModuleRegistrationException(string message) : base(message) { }
    }

    /// <summary>依赖图错误：缺失依赖、循环依赖。消息中带完整链路。</summary>
    public sealed class ModuleDependencyException : Exception
    {
        public ModuleDependencyException(string message) : base(message) { }
    }
}