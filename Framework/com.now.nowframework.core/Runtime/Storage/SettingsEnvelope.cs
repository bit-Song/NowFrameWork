using System;
using System.IO;
using System.Security.Cryptography;

namespace NowFrameWork.Core.Storage
{
    // 格式：NWFS + int32 format + int32 schema + int32 payloadLength + payload + SHA256(header+payload)。
    // 整数为 little endian；校验用于检测损坏，不是防篡改或加密。
    internal static class SettingsEnvelope
    {
        internal const int Overhead = 48;

        internal static byte[] Encode(byte[] payload, int version, int maxPayloadBytes)
        {
            if (payload == null) throw new InvalidDataException("Settings codec 返回了 null payload");
            if (payload.Length > maxPayloadBytes) throw new InvalidDataException("Settings payload 超出大小限制");
            var bytes = new byte[payload.Length + Overhead];
            using (var stream = new MemoryStream(bytes))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 78, 87, 70, 83 });
                writer.Write(1);
                writer.Write(version);
                writer.Write(payload.Length);
                writer.Write(payload);
            }
            using (var hash = SHA256.Create())
                Buffer.BlockCopy(hash.ComputeHash(bytes, 0, bytes.Length - 32), 0, bytes, bytes.Length - 32, 32);
            return bytes;
        }

        internal static byte[] Decode(byte[] bytes, int supportedVersion, out int version)
        {
            if (bytes.Length < Overhead) throw new InvalidDataException("Settings 文件被截断");
            using (var hash = SHA256.Create())
            {
                byte[] expected = hash.ComputeHash(bytes, 0, bytes.Length - 32);
                for (int i = 0; i < expected.Length; i++)
                    if (expected[i] != bytes[bytes.Length - 32 + i])
                        throw new InvalidDataException("Settings 文件校验失败");
            }
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != 0x5346574E) throw new InvalidDataException("Settings 文件标识错误");
                int format = reader.ReadInt32();
                version = reader.ReadInt32();
                int length = reader.ReadInt32();
                if (length != bytes.Length - Overhead || version < 1)
                    throw new InvalidDataException("Settings 文件长度/schema 无效");
                if (format != 1 || version > supportedVersion)
                    throw new SettingsCompatibilityException("不支持 Settings 格式/schema：format=" + format + ", schema=" + version);
                return reader.ReadBytes(length);
            }
        }
    }
}
