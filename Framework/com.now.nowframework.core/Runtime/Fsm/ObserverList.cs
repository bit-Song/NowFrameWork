using System;

namespace NowFrameWork.Core.Fsm
{
    /// <summary>订阅变更时复制数组，通知时读取不可变快照，避免每次 GetInvocationList 分配。</summary>
    internal sealed class ObserverList<T>
    {
        private Action<T>[] _snapshot = Array.Empty<Action<T>>(); // 当前订阅快照，通知期间不原地修改。

        /// <summary>订阅一个或多个回调；分配只发生在订阅变更时。</summary>
        public void Add(Action<T> observer)
        {
            if (observer == null) return;
            Delegate[] additions = observer.GetInvocationList(); // 仅订阅时拆解多播委托。
            var updated = new Action<T>[_snapshot.Length + additions.Length];
            Array.Copy(_snapshot, updated, _snapshot.Length);
            for (int i = 0; i < additions.Length; i++) updated[_snapshot.Length + i] = (Action<T>)additions[i];
            _snapshot = updated;
        }

        /// <summary>按 C# 事件语义移除最后一段匹配的订阅序列。</summary>
        public void Remove(Action<T> observer)
        {
            if (observer == null) return;
            Delegate[] removals = observer.GetInvocationList(); // 仅退订时分配。
            for (int start = _snapshot.Length - removals.Length; start >= 0; start--)
            {
                bool matches = true;
                for (int i = 0; i < removals.Length; i++)
                    if (!_snapshot[start + i].Equals(removals[i])) { matches = false; break; }
                if (!matches) continue;
                var updated = new Action<T>[_snapshot.Length - removals.Length];
                Array.Copy(_snapshot, 0, updated, 0, start);
                Array.Copy(_snapshot, start + removals.Length, updated, start, updated.Length - start);
                _snapshot = updated;
                return;
            }
        }

        /// <summary>逐个通知，隔离观察者异常并返回最近的异常；正常通知不分配。</summary>
        public Exception Invoke(T value)
        {
            Action<T>[] snapshot = _snapshot; // 本轮订阅或退订只影响下一轮通知。
            Exception lastError = null;
            for (int i = 0; i < snapshot.Length; i++)
                try { snapshot[i](value); }
                catch (Exception exception) { lastError = exception; }
            return lastError;
        }
    }
}
