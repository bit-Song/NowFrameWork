using System;
using System.IO;

namespace NowFrameWork.Boot
{
    /// <summary>启动接入样例的数据；不向 Core 内置项目配置，也不自动应用到引擎。</summary>
    [Serializable]
    public sealed class BootPreferences
    {
        public int Schema = 1;
        public string Language = "en-US";

        public static void Validate(BootPreferences value)
        {
            if (value.Schema != 1 || string.IsNullOrWhiteSpace(value.Language))
                throw new InvalidDataException("Boot preferences schema/language 无效");
        }
    }
}
