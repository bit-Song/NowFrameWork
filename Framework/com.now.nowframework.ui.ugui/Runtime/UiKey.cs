using System;

namespace NowFrameWork.UI
{
    /// <summary>
    /// 类型化界面标识：`Id` 用于 AOT 友好的查表，`TArgs` 只提供编译期参数检查。
    ///
    /// 约束 `class` 的原因：结构体参数在 AOT/热更下需要逐类型补元数据，
    /// 引用类型共享泛型实现，不需要。
    /// </summary>
    public sealed class UiKey<TArgs> where TArgs : class
    {
        public UiKey(int id, string name)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "UiKey.Id 必须大于 0");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("UiKey.Name 不能为空", nameof(name));

            Id = id;
            Name = name;
        }

        /// <summary>服务内唯一标识；与泛型参数无关，便于非泛型注册表按 int 查表。</summary>
        public int Id { get; }

        public string Name { get; }

        public override string ToString() => Name + "#" + Id;
    }
}