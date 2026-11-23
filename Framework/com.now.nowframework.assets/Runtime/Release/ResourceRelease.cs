using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace NowFrameWork.Assets
{
    [Serializable]
    public sealed class ReleaseFile
    {
        public string Path;
        public long Length;
        public string Sha256;
    }

    [Serializable]
    public sealed class ResourceRelease
    {
        public string Id;
        public string Platform;
        public string Architecture;
        public string ClientBuildId;
        public string FrameworkContractVersion;
        public string ResourceCatalogId;
        public string CodeSetId;
        public int SaveSchemaReadMin;
        public int SaveSchemaReadMax;
        public int SaveSchemaWriteVersion;
        public string CatalogPath;
        public ReleaseFile[] Files;

        internal void Validate()
        {
            ValidateSegment(Id);
            if (string.IsNullOrEmpty(Platform) || string.IsNullOrEmpty(Architecture) ||
                string.IsNullOrEmpty(ClientBuildId) || string.IsNullOrEmpty(FrameworkContractVersion) ||
                string.IsNullOrEmpty(ResourceCatalogId) ||
                string.IsNullOrEmpty(CodeSetId) || SaveSchemaReadMin < 1 ||
                SaveSchemaReadMax < SaveSchemaReadMin || SaveSchemaWriteVersion < SaveSchemaReadMin ||
                SaveSchemaWriteVersion > SaveSchemaReadMax || Files == null || Files.Length == 0)
                throw new InvalidDataException("发布清单字段不完整或版本范围无效");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReleaseFile file in Files)
            {
                if (file == null) throw new InvalidDataException("发布文件不存在");
                ValidatePath(file.Path);
                if (!paths.Add(file.Path) || file.Length < 0 || file.Sha256 == null ||
                    !Regex.IsMatch(file.Sha256, "^[0-9a-fA-F]{64}$"))
                    throw new InvalidDataException("发布文件描述无效: " + file.Path);
            }
            if (string.IsNullOrEmpty(CatalogPath) || !paths.Contains(CatalogPath) ||
                !CatalogPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("清单必须包含 JSON Catalog");
        }

        internal static void ValidateSegment(string value)
        {
            if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"\A[A-Za-z0-9_-][A-Za-z0-9._-]{0,127}\z") ||
                value.EndsWith(".", StringComparison.Ordinal) ||
                Regex.IsMatch(value, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("发布路径片段无效: " + value);
        }

        internal static void ValidatePath(string path)
        {
            if (path == null) throw new InvalidDataException("发布文件路径不能为空");
            foreach (string segment in path.Split('/')) ValidateSegment(segment);
        }
    }

    public sealed class ReleaseCompatibility
    {
        readonly string _platform, _architecture, _client, _contract, _code;
        readonly int _minSchema, _maxSchema, _writeSchema;

        public ReleaseCompatibility(string platform, string architecture, string clientBuildId, string contractVersion,
            string codeSetId, int minSchema, int maxSchema, int writeSchema)
        {
            _platform = platform; _architecture = architecture; _client = clientBuildId;
            _contract = contractVersion; _code = codeSetId;
            if (minSchema < 1 || maxSchema < minSchema || writeSchema < minSchema || writeSchema > maxSchema)
                throw new ArgumentOutOfRangeException(nameof(writeSchema));
            _minSchema = minSchema; _maxSchema = maxSchema; _writeSchema = writeSchema;
        }

        internal void Check(ResourceRelease release)
        {
            release.Validate();
            if (release.Platform != _platform || release.Architecture != _architecture ||
                release.ClientBuildId != _client || release.FrameworkContractVersion != _contract ||
                release.CodeSetId != _code || release.SaveSchemaReadMin > _minSchema ||
                release.SaveSchemaReadMax < _maxSchema || release.SaveSchemaWriteVersion != _writeSchema)
                throw new InvalidDataException("发布集与本客户端、随包代码或读写存档范围不兼容: " + release.Id);
        }
    }
}
