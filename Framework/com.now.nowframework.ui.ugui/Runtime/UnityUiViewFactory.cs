using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>仅主线程。每个实例独占一份 Prefab 租约，GameObject 真正销毁后归还；失败记录保留到重试。</summary>
    public sealed class UnityUiViewFactory : IUiViewFactory
    {
        sealed class Record
        {
            public GameObject Instance;
            public UiView View;
            public AssetLease<GameObject> Lease;
            public bool DestroyRequested;
        }

        readonly IAssetService _assets;
        readonly int _maxFrames;
        readonly Func<UniTask> _frameWaiter;
        readonly Action<GameObject> _destroy;
        readonly List<Record> _records = new List<Record>();
        UiRoot _root;
        GameObject _staging;
        bool _closing;
        UniTaskCompletionSource _disposing;

        public UnityUiViewFactory(IAssetService assets, UiRoot root = null, int maxDestroyFrames = 60,
            Func<UniTask> frameWaiter = null, Action<GameObject> destroy = null)
        {
            MainThreadGuard.Assert("UnityUiViewFactory.ctor");
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            if (maxDestroyFrames < 0) throw new ArgumentOutOfRangeException(nameof(maxDestroyFrames));
            _root = root; _maxFrames = maxDestroyFrames; _frameWaiter = frameWaiter;
            _destroy = destroy ?? (go => UnityEngine.Object.Destroy(go));
        }

        public int TrackedCount => _records.Count;
        internal void AttachRoot(UiRoot root) => _root = root;

        public async UniTask<UiView> CreateAsync(UiDefinition definition, Transform parent, CancellationToken ct)
        {
            MainThreadGuard.Assert("UnityUiViewFactory.CreateAsync");
            ct.ThrowIfCancellationRequested();
            if (_closing) throw new ObjectDisposedException(nameof(UnityUiViewFactory));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_root == null) throw new InvalidOperationException("UI 根尚未初始化");
            Transform layer = parent != null ? parent : _root.GetLayer(definition.Layer);
            AssetLease<GameObject> lease = await _assets.AcquireAsync<GameObject>(definition.Prefab, ct);
            var record = new Record { Lease = lease };
            _records.Add(record); // 先接管租约，部分创建失败也可由 DisposeAsync 重试收尾。
            try
            {
                ct.ThrowIfCancellationRequested();
                if (_closing || _root == null) throw new InvalidOperationException("UI 工厂已关闭或根已失效");
                if (lease.Asset.GetComponent<UiView>() == null) throw new InvalidOperationException("UI Prefab 根缺少 UiView: " + definition);
                if (_staging == null)
                {
                    _staging = new GameObject("UI Inactive Staging");
                    _staging.SetActive(false);
                    _staging.transform.SetParent(_root.transform, false);
                }
                record.Instance = UnityEngine.Object.Instantiate(lease.Asset, _staging.transform, false);
                record.View = record.Instance.GetComponent<UiView>();
                record.View.PrepareHidden();
                record.Instance.SetActive(false);
                record.Instance.transform.SetParent(layer, false);
                return record.View;
            }
            catch (Exception error)
            {
                try
                {
                    DestroyRecord(record);
                    if (!await ReleaseRecordAsync(record)) throw new TimeoutException("UI 部分创建失败，销毁等待超时，保留租约");
                }
                catch (Exception cleanupError) { throw new AggregateException("UI 创建与回滚同时失败: " + definition, error, cleanupError); }
                throw;
            }
        }

        public void Destroy(UiView view)
        {
            MainThreadGuard.Assert("UnityUiViewFactory.Destroy");
            DestroyRecord(Find(view));
        }

        public UniTask<bool> ReleaseAsync(UiView view)
        {
            MainThreadGuard.Assert("UnityUiViewFactory.ReleaseAsync");
            Record record = Find(view);
            if (!record.DestroyRequested) throw new InvalidOperationException("归还 UI 租约前必须先发起销毁");
            return ReleaseRecordAsync(record);
        }

        Record Find(UiView view)
        {
            foreach (Record record in _records) if (ReferenceEquals(record.View, view)) return record;
            throw new InvalidOperationException("视图不属于此 UI 工厂或已经释放");
        }

        void DestroyRecord(Record record)
        {
            if (record.DestroyRequested) return;
            if (record.Instance != null) _destroy(record.Instance);
            record.DestroyRequested = true;
        }

        async UniTask<bool> ReleaseRecordAsync(Record record)
        {
            if (!await UnityObjectLifecycle.WaitDestroyedAsync(record.Instance, _maxFrames, _frameWaiter)) return false;
            record.Lease.Dispose();
            record.Lease = null;
            _records.Remove(record);
            return true;
        }

        /// <summary>仅在服务已停止并完成回调收尾后调用，也会重试部分创建失败留下的记录。</summary>
        public UniTask DisposeAsync()
        {
            MainThreadGuard.Assert("UnityUiViewFactory.DisposeAsync");
            _closing = true;
            if (_disposing != null) return _disposing.Task;
            var completion = new UniTaskCompletionSource(); _disposing = completion;
            DisposeCoreAsync(completion).Forget();
            return completion.Task;
        }

        async UniTask DisposeCoreAsync(UniTaskCompletionSource completion)
        {
            var errors = new List<Exception>();
            foreach (Record record in new List<Record>(_records))
            {
                try
                {
                    DestroyRecord(record);
                    if (!await ReleaseRecordAsync(record)) throw new TimeoutException("UI 实例尚未销毁，保留租约");
                }
                catch (Exception error) { errors.Add(error); }
            }
            try
            {
                if (errors.Count != 0) throw new AggregateException(errors);
                if (_staging != null) _destroy(_staging);
                if (!await UnityObjectLifecycle.WaitDestroyedAsync(_staging, _maxFrames, _frameWaiter)) throw new TimeoutException("UI staging 未销毁");
                _staging = null;
                completion.TrySetResult();
            }
            catch (Exception error) { _disposing = null; completion.TrySetException(error); }
        }
    }
}
