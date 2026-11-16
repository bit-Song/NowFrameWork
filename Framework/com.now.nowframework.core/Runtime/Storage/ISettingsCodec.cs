namespace NowFrameWork.Core.Storage
{
    /// <summary>
    /// 由主线程调用；负责类型序列化、语义校验和旧 schema 迁移。
    /// Decode 对无效数据抛 InvalidDataException，不支持的版本抛 SettingsCompatibilityException。
    /// Decode 必须只处理传入数据，不能修改文件或产生外部副作用。
    /// </summary>
    public interface ISettingsCodec<T> where T : class
    {
        int Version { get; }
        byte[] Encode(T value);
        T Decode(byte[] payload, int storedVersion);
    }
}
