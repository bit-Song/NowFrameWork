using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// 热更程序集（HotUpdate.asmdef）里的测试入口。
// 注意：这里不能直接引用 AotTest —— AotTest 目前在 Assembly-CSharp 里，
// 热更程序集引用不到它。等给 Assets/Aot 建了 asmdef 并让 HotUpdate 引用后再接回来。
public static class Hello
{
    public static void Run() => RunStep(-1);

    // only >= 0 时只跑那一步，方便定位崩在哪
    public static void RunStep(int only)
    {
        int i = 0;
        Run(i++, only, "基础字符串",              Test_Basic);
        Run(i++, only, "值类型集合",              Test_ValueTypeCollections);
        Run(i++, only, "Unity 结构体集合",        Test_UnityStructCollections);
        Run(i++, only, "LINQ 泛型",               Test_Linq);
        Run(i++, only, "AOT 泛型方法（AotData 泛型实例化）", Test_AotGenericMethod);
        Run(i++, only, "反射构造泛型",            Test_ReflectionGeneric);
        Skip(i++, only, "MakeGenericMethod（未接 AotRuntime）");
        Run(i++, only, "Unity API / GameObject",  Test_UnityApi);
        Run(i++, only, "委托",                    Test_Delegate);
        Run(i++, only, "async/await",             Test_Async);
        Debug.Log("[HELLO] ===== 全部步骤结束（补丁A） =====");
    }

    // 占位步骤：明确打 SKIP，避免日志里把"没测"看成"通过"
    static void Skip(int index, int only, string name)
    {
        if (only >= 0 && index != only) return;
        Debug.Log($"[HELLO {index}] {name}: SKIP");
    }

    static void Run(int index, int only, string name, Action body)
    {
        if (only >= 0 && index != only) return;
        try
        {
            body();
            Debug.Log($"[HELLO {index}] {name}: OK");
        }
        catch (Exception e)
        {
            Debug.LogError($"[HELLO {index}] {name}: FAIL -> {e.GetType().Name}: {e.Message}");
        }
    }

    static void Test_Basic()
    {
        var sb = new StringBuilder();
        sb.Append("中文-补丁A").Append('-').Append(3.14f);
        Debug.Log("[HELLO] " + sb + " | " + string.Format("{0}/{1}", 1, 2));
    }

    // 最容易缺元数据的：List/Dictionary/HashSet 用值类型实例化
    static void Test_ValueTypeCollections()
    {
        var list = new List<int> { 3, 1, 2 };
        list.Sort();
        var dict = new Dictionary<string, int> { ["a"] = 1 };
        var set = new HashSet<long> { 1L, 2L };
        var stack = new Stack<double>();
        stack.Push(1.0);
        string joined = string.Join(",", list);
        int a = dict["a"];
        Debug.Log($"[HELLO] list={joined} dict={a} set={set.Count} stack={stack.Peek()}");
    }

    static void Test_UnityStructCollections()
    {
        var vs = new List<Vector3> { Vector3.zero, Vector3.one };
        var map = new Dictionary<int, GameObject>();
        var key = new Vector2(1, 2);
        Debug.Log($"[HELLO] vs={vs.Count} map={map.Count} key={key}");
    }

    static void Test_Linq()
    {
        var arr = new[] { 1, 2, 3, 4, 5 };
        int sum = arr.Where(x => x % 2 == 1).Select(x => x * 2).Sum();
        var pairs = arr.Select(x => x.ToString()).ToList();
        Debug.Log($"[HELLO] linq sum={sum} pairs={pairs.Count}");
    }

    // 需要热更层能引用 AOT 程序集（Assets/Aot 要有 asmdef），当前先跳过
    static void Test_AotGenericMethod()
    {
        var list = new List<AotData>();
        var dict = new Dictionary<int, AotData>();
        Debug.Log("[HELLO] " + AotTest.Describe(new AotData { A = 1, B = 2 }));
        Debug.Log("[HELLO] MakeList<AotData>=" + AotTest.MakeList<AotData>(3).Count);
    }

    static void Test_ReflectionGeneric()
    {
        var t = typeof(List<int>);
        var inst = (System.Collections.IList)Activator.CreateInstance(t);
        inst.Add(7);
        var dictT = typeof(Dictionary<string, List<int>>);
        var d = Activator.CreateInstance(dictT);
        Debug.Log($"[HELLO] activator list={inst.Count} dict={d != null}");
    }

    static void Test_MakeGenericMethod()
    {
        Debug.Log("[HELLO] MakeGenericMethod 步骤已跳过：热更层还引用不到 AotTest");
    }

    static void Test_UnityApi()
    {
        var go = new GameObject("hello_test_go");
        go.transform.position = new Vector3(1, 2, 3);
        Debug.Log($"[HELLO] unity time={Time.time:F2} pos={go.transform.position}");
        UnityEngine.Object.Destroy(go);
    }

    static void Test_Delegate()
    {
        Action<int> a = x => Debug.Log("[HELLO] action=" + x);
        a(1);
        Func<int, int> f = x => x * x;
        Debug.Log($"[HELLO] func={f(4)}");
    }

    static void Test_Async()
    {
        _ = Delay();
    }

    static async Task Delay()
    {
        await Task.Delay(30);
        Debug.Log("[HELLO] async 完成");
    }
}