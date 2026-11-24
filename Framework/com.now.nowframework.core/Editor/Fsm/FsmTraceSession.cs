#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NowFrameWork.Core.Fsm.Editor
{
    /// <summary>保存最近一次运行的纯数据轨迹，停止 Play 或域重载后仍可查看。</summary>
    [FilePath("Library/NowFrameWork.Core.Fsm/TraceSession.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FsmTraceSession : ScriptableSingleton<FsmTraceSession>
    {
        private const int Capacity = 4096; // 编辑器会话记录上限。
        [SerializeField] private List<FsmTraceRecord> _records = new(); // 不包含 Unity 对象的快照。
        [SerializeField] private string _label; // 当前会话开始时间（UTC）。
        [SerializeField] private bool _isLive; // 本次运行是否尚未结束。
        [SerializeField] private int _generation; // 域重载后的运行代次，避免实例 ID 冲突。
        [SerializeField] private int _version; // 内容更新版本，用于刷新显示。
        [SerializeField] private long _trimmed; // 编辑器会话已丢弃的最早记录数量。
        [SerializeField] private double _timeOrigin; // 会话第一条记录的时刻，不随旧记录淘汰而漂移。
        [SerializeField] private bool _hasTimeOrigin;

        public IReadOnlyList<FsmTraceRecord> Records => _records;
        public string Label => _label;
        public bool IsLive => _isLive;
        public int Generation => _generation;
        public int Version => _version;
        public long Trimmed => _trimmed;
        public double TimeOrigin => _timeOrigin;

        /// <summary>新一轮 Play 开始时清空旧会话。</summary>
        public void Begin()
        {
            _records.Clear(); _trimmed = 0; _generation = 0; _hasTimeOrigin = false; _timeOrigin = 0;
            _label = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            _isLive = true; _version++; Save(true);
        }

        /// <summary>源运行域变化时使用新代次，保留此前记录。</summary>
        public void AdvanceGeneration() { _generation++; }

        /// <summary>追加已按发生顺序排列的快照，容量满后丢弃最早记录。</summary>
        public void Append(List<FsmTraceRecord> records)
        {
            if (records.Count == 0) return;
            if (!_hasTimeOrigin) { _timeOrigin = records[0].Time; _hasTimeOrigin = true; }
            _records.AddRange(records);
            int excess = _records.Count - Capacity;
            if (excess > 0) { _records.RemoveRange(0, excess); _trimmed += excess; }
            _version++;
        }

        /// <summary>只清空显示记录，不改变状态机或重新播放源缓冲。</summary>
        public void Clear() { _records.Clear(); _trimmed = 0; _hasTimeOrigin = false; _timeOrigin = 0; _version++; Save(true); }
        /// <summary>保存当前快照，域重载前调用。</summary>
        public void Persist() { Save(true); }
        /// <summary>结束会话，保留最后的停止和退出记录。</summary>
        public void Finish() { _isLive = false; _version++; Save(true); }
    }

    /// <summary>独立于窗口采集，不因关闭窗口或暂停显示而丢失瞬时切换。</summary>
    [InitializeOnLoad]
    internal static class FsmTraceRecorder
    {
        private static readonly Dictionary<long, long> Cursors = new(); // 本运行域每台状态机已读取的版本。
        private static readonly List<FsmTraceRecord> Batch = new(); // 本次增量快照。
        private static readonly HashSet<long> LiveIds = new(); // 本轮仍存活的实例 ID。
        private static readonly List<long> RetiredIds = new(); // 等待清理的采集游标。
        private static double _nextCapture; // 下次常规采集时间。
        private static bool _generationInitialized; // 本域是否已经分配会话代次。

        static FsmTraceRecorder()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += SaveBeforeReload;
            EditorApplication.quitting += SaveBeforeQuit;
        }

        /// <summary>将运行域 ID 转成会话内唯一 ID。</summary>
        public static long GetSessionId(long runtimeId)
        {
            return runtimeId == 0 ? 0 : ((long)FsmTraceSession.instance.Generation << 32) | (uint)runtimeId;
        }

        /// <summary>运行中每秒采集十次，不逐帧创建轨迹。</summary>
        private static void Update()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < _nextCapture) return;
            if (!FsmTraceSession.instance.IsLive)
            {
                FsmTraceSession.instance.Begin();
                Cursors.Clear(); _generationInitialized = false;
            }
            _nextCapture = EditorApplication.timeSinceStartup + 0.1;
            Capture();
        }

        /// <summary>只复制源缓冲中尚未读取的记录，并合并不同层级的发生顺序。</summary>
        public static void Capture()
        {
            FsmTraceSession session = FsmTraceSession.instance;
            if (!session.IsLive) return;
            if (!_generationInitialized) { session.AdvanceGeneration(); _generationInitialized = true; }
            Batch.Clear();
            LiveIds.Clear();
            foreach (IStateMachineInfo machine in FsmDiagnostics.GetMachines())
            {
                LiveIds.Add(machine.DebugId);
                Cursors.TryGetValue(machine.DebugId, out long cursor);
                if (cursor == machine.TraceVersion) continue;
                int first = Batch.Count;
                Cursors[machine.DebugId] = machine.CopyTraceTo(Batch, cursor);
                for (int i = first; i < Batch.Count; i++)
                {
                    FsmTraceRecord record = Batch[i];
                    record.MachineId = GetSessionId(record.MachineId);
                    record.ParentId = GetSessionId(record.ParentId);
                    record.RootId = GetSessionId(record.RootId);
                    Batch[i] = record;
                }
            }
            RetiredIds.Clear();
            foreach (long id in Cursors.Keys)
                if (!LiveIds.Contains(id)) RetiredIds.Add(id);
            foreach (long id in RetiredIds) Cursors.Remove(id);
            Batch.Sort(CompareRecords);
            session.Append(Batch);
        }

        /// <summary>全局序号排序，缺失提示放在它对应的第一条保留记录之前。</summary>
        private static int CompareRecords(FsmTraceRecord a, FsmTraceRecord b)
        {
            int order = a.Sequence.CompareTo(b.Sequence);
            if (order != 0) return order;
            if (a.Kind == FsmTraceKind.Gap && b.Kind != FsmTraceKind.Gap) return -1;
            if (b.Kind == FsmTraceKind.Gap && a.Kind != FsmTraceKind.Gap) return 1;
            return 0;
        }

        /// <summary>处理开始、结束和禁用 Domain Reload 的运行边界。</summary>
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                FsmTraceSession.instance.Begin();
                Cursors.Clear(); _generationInitialized = false;
                // 不重载运行域时，不将上一次运行残留的缓冲重新导入新会话。
                foreach (IStateMachineInfo machine in FsmDiagnostics.GetMachines())
                    Cursors[machine.DebugId] = machine.TraceVersion;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                Capture(); FsmTraceSession.instance.Persist();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                Capture(); FsmTraceSession.instance.Finish();
            }
        }

        /// <summary>在对象和静态字段销毁前保存，确保停止 Play 后仍可回看。</summary>
        private static void SaveBeforeReload() { Capture(); FsmTraceSession.instance.Persist(); }

        /// <summary>直接关闭编辑器时也结束会话，避免下次打开仍标记为运行中。</summary>
        private static void SaveBeforeQuit() { Capture(); FsmTraceSession.instance.Finish(); }
    }
}
#endif
