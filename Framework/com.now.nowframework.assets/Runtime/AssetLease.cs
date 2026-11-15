using System;

namespace NowFrameWork.Assets
{
    /// <summary>租约类别：业务引用 与 缓存保留（池/常驻）。</summary>
    public enum AssetLeaseKind
    {
        Business = 0,
        Cache = 1,
    }

    /// <summary>内部：租约归还回调的宿主（由 AssetService 实现），避免每个租约分配一个闭包。</summary>
    internal interface ILeaseOwner
    {
        void ReleaseLease(string canonicalKey);
    }

    /// <summary>
    /// 一次资源租约。class 而非 struct：复制句柄会让"谁能释放"失去唯一性，最终变成重复归还。
    ///
    /// 释放幂等；释放后再取 `Asset`：Editor/Development 抛异常，Release 返回 null。
    /// </summary>
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        readonly ILeaseOwner _owner;
        readonly T _asset;

        internal AssetLease(ILeaseOwner owner, T asset, AssetKey key, string canonicalKey, long version, AssetLeaseKind kind)
        {
            _owner = owner;
            _asset = asset;
            Key = key;
            CanonicalKey = canonicalKey;
            Version = version;
            Kind = kind;
        }

        public T Asset
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (IsReleased) throw new InvalidOperationException("租约已释放，不能再使用 Asset: " + CanonicalKey);
#endif
                return IsReleased ? null : _asset;
            }
        }

        public AssetKey Key { get; }

        /// <summary>Provider 规范化后的键（地址与 GUID 指向同一资源时相同）。</summary>
        public string CanonicalKey { get; }

        public long Version { get; }

        public AssetLeaseKind Kind { get; }

        public bool IsReleased { get; private set; }

        public void Dispose()
        {
            if (IsReleased) return;

            IsReleased = true;
            _owner?.ReleaseLease(CanonicalKey);
        }

        public override string ToString()
            => "Lease(" + CanonicalKey + "#" + Version + (IsReleased ? ", released" : "") + ")";
    }
}