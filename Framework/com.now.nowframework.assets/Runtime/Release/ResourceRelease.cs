using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace NowFrameWork.Assets
{
    [Serializable]
    public sealed class ReleaseFile
    {
        /// <summary>相对发布目录的文件路径。</summary>
        public string Path;
        /// <summary>文件字节数。</summary>
        public long Length;
        /// <summary>文件 SHA-256，用于完整性校验。</summary>
        public string Sha256;
    }

    [Serializable]
    public sealed class ResourceRelease
    {
        /// <summary>发布集 ID。</summary>
        public string Id;
        /// <summary>目标平台。</summary>
        public string Platform;
        /// <summary>目标架构。</summary>
        public string Architecture;
        /// <summary>客户端构建标识，必须匹配。</summary>
        public string ClientBuildId;
        /// <summary>框架契约版本。</summary>
        public string FrameworkContractVersion;
        /// <summary>Catalog 标识。</summary>
        public string ResourceCatalogId;
        /// <summary>代码集标识（普通 AOT 为 builtin）。</summary>
        public string CodeSetId;
        /// <summary>可读存档 schema 最小值。</summary>
        public int SaveSchemaReadMin;
        /// <summary>可读存档 schema 最大值。</summary>
        public int SaveSchemaReadMax;
        /// <summary>写存档使用的 schema 版本。</summary>
        public int SaveSchemaWriteVersion;
        /// <summary>Catalog 相对路径。</summary>
        public string CatalogPath;
        /// <summary>发布集包含的文件清单。</summary>
        public ReleaseFile[] Files;

        /// <summary>校验清单字段与文件项。</summary>
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

        /// <summary>校验平台/架构等单段字段。</summary>
        internal static void ValidateSegment(string value)
        {
            if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"\A[A-Za-z0-9_-][A-Za-z0-9._-]{0,127}\z") ||
                value.EndsWith(".", StringComparison.Ordinal) ||
                Regex.IsMatch(value, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("发布路径片段无效: " + value);
        }

        /// <summary>校验相对路径，拒绝绝对路径与 ..。</summary>
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

        /// <summary>发布兼容性约束（平台/架构/契约/存档版本）。</summary>
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
