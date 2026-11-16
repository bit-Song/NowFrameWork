using System;
using System.IO;
using System.Text;
using NowFrameWork.Core.Storage;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.Unity.Storage
{
    /// <summary>
    /// 仅主线程。使用 JsonUtility 序列化普通 Serializable DTO 的字段，不支持 Unity 对象引用。
    /// migrate 处理旧 schema；validate 必须对缺少字段、越界值等无效数据抛 InvalidDataException。
    /// </summary>
    public sealed class JsonSettingsCodec<T> : ISettingsCodec<T> where T : class
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        readonly Func<string, int, T> _migrate;
        readonly Action<T> _validate;
        public int Version { get; }

        public JsonSettingsCodec(int version = 1, Func<string, int, T> migrate = null, Action<T> validate = null)
        {
            MainThreadGuard.Assert("JsonSettingsCodec.ctor");
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            Type type = typeof(T);
            if (!type.IsSerializable || type.IsAbstract || typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || typeof(UnityEngine.Object).IsAssignableFrom(type))
                throw new ArgumentException("Json Settings 需要普通 Serializable DTO，不能使用 Unity 对象、抽象类型或集合/字符串作为根");
            Version = version;
            _migrate = migrate;
            _validate = validate;
        }

        public byte[] Encode(T value)
        {
            MainThreadGuard.Assert("JsonSettingsCodec.Encode");
            if (value == null) throw new ArgumentNullException(nameof(value));
            _validate?.Invoke(value);
            return Utf8.GetBytes(JsonUtility.ToJson(value));
        }

        public T Decode(byte[] payload, int storedVersion)
        {
            MainThreadGuard.Assert("JsonSettingsCodec.Decode");
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (storedVersion < 1 || storedVersion > Version || (storedVersion != Version && _migrate == null))
                throw new SettingsCompatibilityException("Json Settings 没有 schema 迁移路径：" + storedVersion + " -> " + Version);
            string json;
            try { json = Utf8.GetString(payload); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Json Settings UTF-8 无效", error); }
            T value;
            if (storedVersion != Version) value = _migrate(json, storedVersion);
            else
            {
                try { value = JsonUtility.FromJson<T>(json); }
                catch (ArgumentException error) { throw new InvalidDataException("Json Settings JSON 无效", error); }
            }
            if (value == null) throw new InvalidDataException("Json Settings 解码/迁移返回了 null");
            _validate?.Invoke(value);
            return value;
        }
    }
}
