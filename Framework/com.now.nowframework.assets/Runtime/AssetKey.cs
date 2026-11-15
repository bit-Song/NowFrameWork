using System;
using NowFrameWork.Core.Utility;

namespace NowFrameWork.Assets
{
    /// <summary>
    /// 资源加载键。
    ///
    /// 相等性只看 Address + SubObject：Variant 是**池维度**，同一资源的两套池共享一份加载。
    /// 哈希用 FNV-1a（跨进程稳定），便于把键写进日志或存档后复现。
    /// </summary>
    public readonly struct AssetKey : IEquatable<AssetKey>
    {
        public AssetKey(string address, string subObject = null, string variant = null)
        {
            Address = address;
            SubObject = subObject;
            Variant = variant;
        }

        /// <summary>地址或 GUID 字符串。</summary>
        public string Address { get; }

        /// <summary>子资源名（图集里的 sprite 等），可空。</summary>
        public string SubObject { get; }

        /// <summary>池变体维度（不同父节点/初始状态），可空。不参与相等性。</summary>
        public string Variant { get; }

        public bool IsValid => !string.IsNullOrEmpty(Address);

        public static AssetKey From(string address, string subObject = null) => new AssetKey(address, subObject);

        public static implicit operator AssetKey(string address) => new AssetKey(address);

        public AssetKey WithVariant(string variant) => new AssetKey(Address, SubObject, variant);

        public bool Equals(AssetKey other)
            => string.Equals(Address, other.Address, StringComparison.Ordinal)
            && string.Equals(SubObject ?? string.Empty, other.SubObject ?? string.Empty, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is AssetKey other && Equals(other);

        public override int GetHashCode()=> Hash.Combine(Hash.Of(Address), Hash.Of(SubObject));

        public override string ToString()
        {
            string result = Address ?? string.Empty;
            if (!string.IsNullOrEmpty(SubObject)) result += "/" + SubObject;
            if (!string.IsNullOrEmpty(Variant)) result += "#" + Variant;
            return result;
        }
    }
}