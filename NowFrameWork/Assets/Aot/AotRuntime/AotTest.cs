using System;
using System.Collections.Generic;
using UnityEngine;




public struct AotData
{
    public int A;
    public float B;
}
public static class AotTest
{
    // 泛型方法：热更侧用值类型实例化时，如果没有补充元数据就会炸
    public static string Describe<T>(T value)
        => typeof(T).Name + ":" + value;

    public static T MakeDefault<T>() => default(T);

    public static List<T> MakeList<T>(int n)
    {
        var list = new List<T>(n);
        for (int i = 0; i < n; i++) list.Add(default);
        return list;
    }

    // 一个普通的 AOT 方法，给反射用
    public static string Ping(string s) => "aot-ping:" + s;
}