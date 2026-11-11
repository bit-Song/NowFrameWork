using System;
using System.Reflection;
using UnityEngine;

public class LoadDllScript : MonoBehaviour
{
    public TextAsset dllText;
    void Start()
    {
        Assembly hotUpdateAss = Assembly.Load(dllText.bytes);
        Type helloType = hotUpdateAss.GetType("Hello");
        MethodInfo helloMethod = helloType.GetMethod("Run");
        helloMethod.Invoke(null, null);
    }
}
