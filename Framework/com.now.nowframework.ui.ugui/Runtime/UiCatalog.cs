using System;
using System.Collections.Generic;
using NowFrameWork.Assets;
using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>面板注册表：Inspector 配置面板 ID、地址与层级，运行时转成 UiDefinition。</summary>
    [CreateAssetMenu(menuName = "NowFrameWork/UI/UI Catalog", fileName = "UiCatalog")]
    public sealed class UiCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public int Id;
            public string Name = "Panel";
            public string Address = "";
            public UiLayer Layer = UiLayer.Page;
            public bool MultiInstance;
            public bool Modal;
            public bool CloseOnBackdrop;
            public UiBackBehavior BackBehavior = UiBackBehavior.Close;
            public bool CacheOnClose;
        }

        [SerializeField] List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public bool TryGet(int id, out Entry entry)
        {
            foreach (Entry item in _entries)
            {
                if (item != null && item.Id == id)
                {
                    entry = item;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>转成 UiModule 需要的定义数组；配置非法时抛异常。</summary>
        public UiDefinition[] BuildDefinitions()
        {
            var definitions = new List<UiDefinition>(_entries.Count);
            var ids = new HashSet<int>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (Entry entry in _entries)
            {
                if (entry == null) continue;

                if (entry.Id <= 0) throw new InvalidOperationException("面板 ID 必须大于 0：" + entry.Name);
                if (string.IsNullOrWhiteSpace(entry.Address)) throw new InvalidOperationException("面板缺少 Address：" + entry.Name);
                if (!ids.Add(entry.Id)) throw new InvalidOperationException("面板 ID 重复：" + entry.Id);
                if (!names.Add(entry.Name)) throw new InvalidOperationException("面板名称重复：" + entry.Name);

                definitions.Add(new UiDefinition(entry.Id, entry.Name, AssetKey.From(entry.Address))
                {
                    Layer = entry.Layer,
                    MultiInstance = entry.MultiInstance,
                    Modal = entry.Modal,
                    CloseOnBackdrop = entry.CloseOnBackdrop,
                    BackBehavior = entry.BackBehavior,
                    CacheOnClose = entry.CacheOnClose,
                });
            }

            return definitions.ToArray();
        }
    }
}
