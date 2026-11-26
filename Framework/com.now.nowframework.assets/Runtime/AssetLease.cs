using NowFrameWork.Core.Threading;
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
        /// <summary>归还租约。version 用于识别"条目已被重建"的过期租约。</summary>
        void ReleaseLease(string canonicalKey, long version);
    }

    /// <summary>
    /// 一次资源租约。class 而非 struct：复制句柄会让"谁能释放"失去唯一性，最终变成重复归还。
    ///
    /// Dispose 失败时可重试。开始释放后再取 `Asset`：Editor/Development 抛异常，Release 返回 null。
    /// </summary>
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        readonly ILeaseOwner _owner;
        readonly T _asset;
        bool _releaseStarted, _releasing;

        internal AssetLease(ILeaseOwner owner, T asset, AssetKey key, string canonicalKey, long version, AssetLeaseKind kind)
        {
            _owner = owner;
            _asset = asset;
            Key = key;
            CanonicalKey = canonicalKey;
            Version = version;
            Kind = kind;
        }

        /// <summary>租约持有的资源。</summary>
        public T Asset
        {
            get
            {
                MainThreadGuard.Assert("AssetLease.Asset");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_releaseStarted) throw new InvalidOperationException("租约已开始释放，不能再使用 Asset: " + CanonicalKey);
#endif
                return _releaseStarted ? null : _asset;
            }
        }

        /// <summary>申请时的资源键。</summary>
        public AssetKey Key { get; }

        /// <summary>Provider 规范化后的键（地址与 GUID 指向同一资源时相同）。</summary>
        public string CanonicalKey { get; }

        /// <summary>资源版本。</summary>
        public long Version { get; }

        /// <summary>租约类型（普通/常驻）。</summary>
        public AssetLeaseKind Kind { get; }

        /// <summary>是否已释放。</summary>
        public bool IsReleased { get; private set; }

        /// <summary>归还租约；重复释放无副作用。</summary>
        public void Dispose()
        {
            MainThreadGuard.Assert("AssetLease.Dispose");
            if (IsReleased || _releasing) return;

            _releaseStarted = _releasing = true;
            try
            {
                _owner?.ReleaseLease(CanonicalKey, Version);
                IsReleased = true;
            }
            finally { _releasing = false; }
        }

        /// <summary>诊断用的租约描述。</summary>
        public override string ToString()
            => "Lease(" + CanonicalKey + "#" + Version + (IsReleased ? ", released" : "") + ")";
    }
}
