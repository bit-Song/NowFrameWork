using System;

namespace NowFrameWork.Core
{
    /// <summary>注册阶段的错误：null 模块、空 Id、重复 Id、启动后再注册。</summary>
    public sealed class ModuleRegistrationException : Exception
    {
        /// <summary>模块注册失败（空、重复或启动后注册）。</summary>
        public ModuleRegistrationException(string message) : base(message) { }
    }

    /// <summary>依赖图错误的分类。</summary>
    public enum ModuleDependencyKind
    {
        /// <summary>DependsOn 里写了未注册的模块 Id。</summary>
        Missing = 0,

        /// <summary>依赖关系成环。</summary>
        Cycle = 1,
    }

    /// <summary>依赖图错误：缺失依赖或循环依赖，消息中带完整链路。</summary>
    public sealed class ModuleDependencyException : Exception
    {
        /// <summary>模块依赖缺失或成环。</summary>
        public ModuleDependencyException(ModuleDependencyKind kind, string message) : base(message)
        {
            Kind = kind;
        }

        /// <summary>调用方可以据此区分处理，不必解析消息文本。</summary>
        public ModuleDependencyKind Kind { get; }
    }
}