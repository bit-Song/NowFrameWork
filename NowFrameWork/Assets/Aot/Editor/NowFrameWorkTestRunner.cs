using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    /// <summary>
    /// 在**已打开的编辑器**里跑 EditMode 全量测试，并把结果写成 NUnit XML。
    ///
    /// 为什么需要它：Unity 一个工程同时只允许一个实例，batchmode 跑测试必须关掉编辑器。
    /// 这个菜单让"跑测试"不再需要关编辑器。
    /// 
    /// 结果路径：<工程>/../../EXE/_evidence/editmode-results.xml
    /// </summary>
    public static class NowFrameWorkTestRunner
    {
        const string MenuPath = "NowFrameWork/Tests/运行全部 EditMode 测试";

        static TestRunnerApi _api;

        [MenuItem(MenuPath)]
        public static void RunEditMode()
        {
            if (_api != null)
            {
                Debug.LogWarning("[Tests] 已有一轮测试在执行，忽略本次请求");
                return;
            }

            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new Callbacks(() => _api = null));

            var filter = new Filter { testMode = TestMode.EditMode };
            var settings = new ExecutionSettings(filter);

            Debug.Log("[Tests] 开始执行 EditMode 测试…");
            _api.Execute(settings);
        }

        [MenuItem("NowFrameWork/Tests/打开结果目录")]
        public static void OpenResultFolder()
        {
            string dir = Path.GetDirectoryName(ResultPath());
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        static string ResultPath()
        {
            // Assets → 工程根 → ../../EXE/_evidence
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            return Path.Combine(root, "EXE", "_evidence", "editmode-results.xml");
        }

        sealed class Callbacks : ICallbacks
        {
            readonly System.Action _onFinished;

            public Callbacks(System.Action onFinished) => _onFinished = onFinished;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                try
                {
                    string path = ResultPath();
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    File.WriteAllText(path, result.ToXml().OuterXml);

                    Debug.Log("[Tests] 完成：共 " + result.PassCount + " 通过 / " + result.FailCount + " 失败；结果已写入 " + path);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Tests] 写结果失败: " + e.Message);
                }
                finally
                {
                    _onFinished?.Invoke();
                }
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}