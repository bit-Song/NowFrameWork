using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NowFrameWork.Core;
using UnityEngine;

// UnityEngine 里也有 LogEntry / Logger，显式指定用框架的
using LogEntry = NowFrameWork.Core.LogEntry;
using Logger = NowFrameWork.Core.Logger;

namespace NowFrameWork.Unity
{
    /// <summary>
    /// 崩溃钩子：把日志环形缓冲落盘，供玩家反馈时取回。
    ///
    /// 触发时机：
    /// 1. Unity 记录到 Exception/Error 日志时（只落盘第一次，避免刷屏刷出几百个文件）；
    /// 2. 应用退出（Application.quitting）；
    /// 3. 业务主动调用 <see cref="Dump"/>。
    ///
    /// 输出目录由调用方指定，建议 Application.persistentDataPath 下的子目录。
    /// 落盘失败只记 Error，不抛异常——崩溃钩子自己崩溃是最糟的情况。
    /// </summary>
    public sealed class CrashDump : IDisposable
    {
        readonly Logger _log;
        readonly string _directory;
        readonly List<LogEntry> _buffer = new List<LogEntry>(64);

        bool _installed;
        bool _dumpedOnError;

        public CrashDump(Logger log, string directory)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _directory = directory;
        }

        public void Install()
        {
            if (_installed) return;

            Application.logMessageReceived += OnLogMessage;
            Application.quitting += OnQuitting;
            _installed = true;
        }

        public void Dispose()
        {
            if (!_installed) return;

            Application.logMessageReceived -= OnLogMessage;
            Application.quitting -= OnQuitting;
            _installed = false;
        }

        /// <summary>把最近的日志写到文件，返回路径；失败返回 null。</summary>
        public string Dump(string reason)
        {
            try
            {
                if (string.IsNullOrEmpty(_directory)) return null;
                if (!Directory.Exists(_directory)) Directory.CreateDirectory(_directory);

                _log.DumpRecent(_buffer);

                var builder = new StringBuilder(1024);
                builder.AppendLine("# NowFrameWork crash dump");
                builder.AppendLine("reason: " + reason);
                builder.AppendLine("utc: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine("entries: " + _buffer.Count);
                builder.AppendLine();

                for (int i = 0; i < _buffer.Count; i++)
                {
                    LogEntry entry = _buffer[i];
                    builder.Append("[").Append(entry.Level).Append("][").Append(entry.Tag).Append("] ")
                           .AppendLine(entry.Message);
                }

                string path = Path.Combine(_directory, "crash-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError("CrashDump 写入失败: " + e.Message);
                return null;
            }
        }

        void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (_dumpedOnError) return;
            if (type != LogType.Exception && type != LogType.Error) return;

            _dumpedOnError = true;
            Dump("unity:" + type);
        }

        void OnQuitting() => Dump("application-quitting");
    }
}