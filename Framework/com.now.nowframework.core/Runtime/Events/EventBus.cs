using System;
using System.Collections.Generic;

using NowFrameWork.Core;
using NowFrameWork.Core.Logging;

namespace NowFrameWork.Core.Events
{
    /// <summary>
    /// C04 事件总线实现。
    ///
    /// GC 设计：
    /// - 每种事件类型一条 Channel，订阅时分配一次；派发只遍历**快照数组**，不分配；
    /// - 快照在订阅/退订后置脏，下一次派发前重建（重建才分配）；
    /// - 延迟派发按类型排队（List&lt;T&gt;），不装箱、不产生闭包；
    /// - 异常路径才拼字符串，且先判 IsEnabled。
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        /// <summary>单次 Flush 的安全上限，防止 handler 无限投递延迟事件导致死循环。</summary>
        const int MaxDeferredPerFlush = 4096;

        /// <summary>
        /// 派发递归深度上限。handler 内部再次 Publish 同一事件会层层嵌套，
        /// 超过这个深度直接丢弃并记 Error —— 表现为「派发被截断 + 一条错误日志」，
        /// 而不是栈溢出把整个进程带走。
        /// </summary>
        public const int MaxPublishDepth = 16;

        int _publishDepth;

        readonly Dictionary<Type, IChannel> _channels = new Dictionary<Type, IChannel>(16);
        readonly ILog _log;

        /// <summary>FlushDeferred 的键快照缓冲，复用以免每帧分配（R1）。</summary>
        readonly List<Type> _flushKeys = new List<Type>(16);

        const string C04Tag = "C04";

        public EventBus(ILog log = null)
        {
            _log = log ?? Logs.Null;
        }

        public IEventSubscription Subscribe<T>(EventHandler<T> handler) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            Channel<T> channel = GetOrCreateChannel<T>();
            channel.AddDelegate(handler);
            return new Subscription(this, typeof(T), handler);
        }

        public IEventSubscription Subscribe<T>(IEventHandler<T> handler) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            Channel<T> channel = GetOrCreateChannel<T>();
            channel.AddHandler(handler);
            return new Subscription(this, typeof(T), handler);
        }

        public void Publish<T>(in T evt) where T : struct
        {
            if (_publishDepth >= MaxPublishDepth)
            {
                // 递归保护：丢弃这一层，避免无限嵌套直到栈溢出
                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, C04Tag,
                        "事件派发递归超过 " + MaxPublishDepth + " 层，已丢弃: " + typeof(T).Name);
                return;
            }

            _publishDepth++;
            try
            {
                if (_channels.TryGetValue(typeof(T), out IChannel channel))
                    ((Channel<T>)channel).Publish(in evt, _log);
            }
            finally
            {
                _publishDepth--;
            }
        }

        public void PublishDeferred<T>(in T evt) where T : struct
        {
            GetOrCreateChannel<T>().Enqueue(in evt);
        }

        public void FlushDeferred()
        {
            if (_channels.Count == 0) return;

            // 遍历字典期间 handler 可能订阅新类型 → 先快照键；缓冲复用，避免每帧分配
            _flushKeys.Clear();
            foreach (KeyValuePair<Type, IChannel> pair in _channels) _flushKeys.Add(pair.Key);

            for (int i = 0; i < _flushKeys.Count; i++)
                if (_channels.TryGetValue(_flushKeys[i], out IChannel channel)) channel.FlushDeferred(_log);
        }

        public void Clear()
        {
            foreach (KeyValuePair<Type, IChannel> pair in _channels) pair.Value.Clear();
            _channels.Clear();
        }

        Channel<T> GetOrCreateChannel<T>() where T : struct
        {
            if (_channels.TryGetValue(typeof(T), out IChannel existing))
                return (Channel<T>)existing;

            var created = new Channel<T>();
            _channels.Add(typeof(T), created);
            return created;
        }

        void Unsubscribe(Subscription subscription)
        {
            if (!_channels.TryGetValue(subscription.EventType, out IChannel channel)) return;
            channel.Remove(subscription.Handler);
        }

        interface IChannel
        {
            void Clear();
            void Remove(object handler);
            void FlushDeferred(ILog log);
        }

        /// <summary>单一事件类型的订阅与队列。</summary>
        sealed class Channel<T> : IChannel where T : struct
        {
            readonly List<EventHandler<T>> _delegates = new List<EventHandler<T>>(4);
            readonly List<IEventHandler<T>> _handlers = new List<IEventHandler<T>>(4);
            readonly List<T> _deferred = new List<T>(4);

            EventHandler<T>[] _delegateSnapshot = Array.Empty<EventHandler<T>>();
            IEventHandler<T>[] _handlerSnapshot = Array.Empty<IEventHandler<T>>();

            bool _dirty;

            public void AddDelegate(EventHandler<T> handler)
            {
                _delegates.Add(handler);
                _dirty = true;
            }

            public void AddHandler(IEventHandler<T> handler)
            {
                _handlers.Add(handler);
                _dirty = true;
            }

            public void Remove(object handler)
            {
                if (handler is EventHandler<T> d) _delegates.Remove(d);
                else if (handler is IEventHandler<T> h) _handlers.Remove(h);
                else return;

                _dirty = true;
            }

            public void Enqueue(in T evt) => _deferred.Add(evt);

            public void Publish(in T evt, ILog log)
            {
                EnsureSnapshot();

                EventHandler<T>[] delegates = _delegateSnapshot;
                for (int i = 0; i < delegates.Length; i++)
                {
                    try { delegates[i](in evt); }
                    catch (Exception e) { Report(log, e); }
                }

                IEventHandler<T>[] handlers = _handlerSnapshot;
                for (int i = 0; i < handlers.Length; i++)
                {
                    try { handlers[i].Handle(in evt); }
                    catch (Exception e) { Report(log, e); }
                }
            }

            public void FlushDeferred(ILog log)
            {
                if (_deferred.Count == 0) return;

                int guard = 0;
                // 索引遍历：handler 里继续投递的延迟事件会在本轮内被处理（有上限保护）
                for (int i = 0; i < _deferred.Count; i++)
                {
                    if (++guard > MaxDeferredPerFlush)
                    {
                        if (log.IsEnabled(LogLevel.Error))
                            log.Write(LogLevel.Error, "C04", "延迟派发超过上限 " + MaxDeferredPerFlush + "，已丢弃剩余事件: " + typeof(T).Name);
                        break;
                    }

                    Publish(_deferred[i], log);
                }

                _deferred.Clear();
            }

            public void Clear()
            {
                _delegates.Clear();
                _handlers.Clear();
                _deferred.Clear();
                _delegateSnapshot = Array.Empty<EventHandler<T>>();
                _handlerSnapshot = Array.Empty<IEventHandler<T>>();
                _dirty = false;
            }

            void EnsureSnapshot()
            {
                if (!_dirty) return;
                _dirty = false;

                if (_delegates.Count == 0) _delegateSnapshot = Array.Empty<EventHandler<T>>();
                else
                {
                    if (_delegateSnapshot.Length != _delegates.Count) _delegateSnapshot = new EventHandler<T>[_delegates.Count];
                    _delegates.CopyTo(_delegateSnapshot);
                }

                if (_handlers.Count == 0) _handlerSnapshot = Array.Empty<IEventHandler<T>>();
                else
                {
                    if (_handlerSnapshot.Length != _handlers.Count) _handlerSnapshot = new IEventHandler<T>[_handlers.Count];
                    _handlers.CopyTo(_handlerSnapshot);
                }
            }

            static void Report(ILog log, Exception e)
            {
                if (!log.IsEnabled(LogLevel.Error)) return;
                log.Write(LogLevel.Error, "C04",
                    "事件处理失败: " + typeof(T).Name + " -> " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>订阅令牌实现。Dispose 幂等；释放后不再从总线移除任何东西。</summary>
        sealed class Subscription : IEventSubscription
        {
            readonly EventBus _bus;

            public Subscription(EventBus bus, Type eventType, object handler)
            {
                _bus = bus;
                EventType = eventType;
                Handler = handler;
            }

            public Type EventType { get; }
            public object Handler { get; }
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                _bus.Unsubscribe(this);
            }
        }
    }
}
