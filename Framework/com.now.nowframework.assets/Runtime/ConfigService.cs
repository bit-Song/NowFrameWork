using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Utility;

namespace NowFrameWork.Assets
{
    /// <summary>
    /// 配置服务：把配置资源**常驻**在内存里，按 key 缓存、按类型强校验。
    ///
    /// 为什么用 Cache 租约而不是每次 Acquire：
    /// 配置是全局只读数据，生命周期等于「运行时」而不是「某次使用」；
    /// 用池级保活租约可以避免业务忘记释放，也避免频繁加载/卸载。
    ///
    /// 释放：调用 <see cref="DisposeAsync"/> 归还全部租约（通常在 Scope 关闭时）。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class ConfigService : IDisposable
    {
        readonly IAssetService _assets;
        readonly Dictionary<string, AssetLease<UnityEngine.Object>> _leases =
            new Dictionary<string, AssetLease<UnityEngine.Object>>(StringComparer.Ordinal);
        readonly Dictionary<AssetKey, UnityEngine.Object> _values =
            new Dictionary<AssetKey, UnityEngine.Object>();

        bool _disposed;

        public ConfigService(IAssetService assets)
        {
            _assets = Guard.NotNull(assets, nameof(assets));
        }

        public int Count => _values.Count;

        /// <summary>加载配置；同一 key 只会真正加载一次。类型不匹配会抛 InvalidCastException。</summary>
        public async UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default) where T : UnityEngine.Object
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ConfigService));
            ct.ThrowIfCancellationRequested();

            AssetKey cacheKey = KeyOf(key);

            if (_values.TryGetValue(cacheKey, out UnityEngine.Object cached))
            {
                if (cached is T typed) return typed;

                throw new InvalidCastException(
                    "配置类型不匹配: " + cacheKey + " 已加载为 " + cached.GetType().Name + "，请求 " + typeof(T).Name);
            }

            AssetLease<UnityEngine.Object> lease = await _assets.AcquireCacheAsync<UnityEngine.Object>(key, ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                if (_disposed) throw new ObjectDisposedException(nameof(ConfigService));
                UnityEngine.Object asset = lease.Asset;
                if (asset == null) throw new InvalidOperationException("配置加载结果为空: " + cacheKey);
                if (!(asset is T value))
                    throw new InvalidCastException("配置类型不匹配: " + cacheKey + " 实际是 " + asset.GetType().Name + "，请求 " + typeof(T).Name);

                // 等待期间其他请求可能已经入库；地址别名也按 canonical 合并保活租约。
                if (!_leases.ContainsKey(lease.CanonicalKey))
                {
                    _leases.Add(lease.CanonicalKey, lease);
                    lease = null;
                }
                _values[cacheKey] = value;
                return value;
            }
            finally
            {
                lease?.Dispose();
            }
        }

        /// <summary>归还全部配置租约；幂等。</summary>
        public UniTask DisposeAsync()
        {
            if (_disposed) return UniTask.CompletedTask;
            _disposed = true;

            foreach (KeyValuePair<string, AssetLease<UnityEngine.Object>> pair in _leases)
                pair.Value.Dispose();

            _leases.Clear();
            _values.Clear();

            return UniTask.CompletedTask;
        }

        public void Dispose() => DisposeAsync().Forget();

        static AssetKey KeyOf(object key)
        {
            switch (key)
            {
                case AssetKey assetKey: return assetKey;
                case string address: return AssetKey.From(address);
                case null: throw new ArgumentNullException(nameof(key));
                default: throw new ArgumentException("不支持的配置键类型: " + key.GetType().Name);
            }
        }
    }
}
