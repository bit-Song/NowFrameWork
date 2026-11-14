using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

    // {{ AOT assemblies
    public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
    {
        "AotRuntime.dll",
        "System.Core.dll",
        "mscorlib.dll",
    };
    // }}

    // {{ constraint implement type
    // }} 

    // {{ AOT generic types
    // System.Action<AotData>
    // System.Action<UnityEngine.Vector3>
    // System.Action<int>
    // System.Action<object>
    // System.Collections.Generic.ArraySortHelper<AotData>
    // System.Collections.Generic.ArraySortHelper<UnityEngine.Vector3>
    // System.Collections.Generic.ArraySortHelper<int>
    // System.Collections.Generic.ArraySortHelper<object>
    // System.Collections.Generic.Comparer<AotData>
    // System.Collections.Generic.Comparer<UnityEngine.Vector3>
    // System.Collections.Generic.Comparer<int>
    // System.Collections.Generic.Comparer<object>
    // System.Collections.Generic.Dictionary.Enumerator<int,AotData>
    // System.Collections.Generic.Dictionary.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,AotData>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.KeyCollection<int,AotData>
    // System.Collections.Generic.Dictionary.KeyCollection<int,object>
    // System.Collections.Generic.Dictionary.KeyCollection<object,int>
    // System.Collections.Generic.Dictionary.KeyCollection<object,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,AotData>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.ValueCollection<int,AotData>
    // System.Collections.Generic.Dictionary.ValueCollection<int,object>
    // System.Collections.Generic.Dictionary.ValueCollection<object,int>
    // System.Collections.Generic.Dictionary.ValueCollection<object,object>
    // System.Collections.Generic.Dictionary<int,AotData>
    // System.Collections.Generic.Dictionary<int,object>
    // System.Collections.Generic.Dictionary<object,int>
    // System.Collections.Generic.Dictionary<object,object>
    // System.Collections.Generic.EqualityComparer<AotData>
    // System.Collections.Generic.EqualityComparer<int>
    // System.Collections.Generic.EqualityComparer<long>
    // System.Collections.Generic.EqualityComparer<object>
    // System.Collections.Generic.HashSet.Enumerator<long>
    // System.Collections.Generic.HashSet<long>
    // System.Collections.Generic.HashSetEqualityComparer<long>
    // System.Collections.Generic.ICollection<AotData>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,AotData>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.ICollection<UnityEngine.Vector3>
    // System.Collections.Generic.ICollection<int>
    // System.Collections.Generic.ICollection<long>
    // System.Collections.Generic.ICollection<object>
    // System.Collections.Generic.IComparer<AotData>
    // System.Collections.Generic.IComparer<UnityEngine.Vector3>
    // System.Collections.Generic.IComparer<int>
    // System.Collections.Generic.IComparer<object>
    // System.Collections.Generic.IEnumerable<AotData>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,AotData>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.IEnumerable<UnityEngine.Vector3>
    // System.Collections.Generic.IEnumerable<int>
    // System.Collections.Generic.IEnumerable<long>
    // System.Collections.Generic.IEnumerable<object>
    // System.Collections.Generic.IEnumerator<AotData>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,AotData>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.IEnumerator<UnityEngine.Vector3>
    // System.Collections.Generic.IEnumerator<int>
    // System.Collections.Generic.IEnumerator<long>
    // System.Collections.Generic.IEnumerator<object>
    // System.Collections.Generic.IEqualityComparer<int>
    // System.Collections.Generic.IEqualityComparer<long>
    // System.Collections.Generic.IEqualityComparer<object>
    // System.Collections.Generic.IList<AotData>
    // System.Collections.Generic.IList<UnityEngine.Vector3>
    // System.Collections.Generic.IList<int>
    // System.Collections.Generic.IList<object>
    // System.Collections.Generic.KeyValuePair<int,AotData>
    // System.Collections.Generic.KeyValuePair<int,object>
    // System.Collections.Generic.KeyValuePair<object,int>
    // System.Collections.Generic.KeyValuePair<object,object>
    // System.Collections.Generic.List.Enumerator<AotData>
    // System.Collections.Generic.List.Enumerator<UnityEngine.Vector3>
    // System.Collections.Generic.List.Enumerator<int>
    // System.Collections.Generic.List.Enumerator<object>
    // System.Collections.Generic.List<AotData>
    // System.Collections.Generic.List<UnityEngine.Vector3>
    // System.Collections.Generic.List<int>
    // System.Collections.Generic.List<object>
    // System.Collections.Generic.ObjectComparer<AotData>
    // System.Collections.Generic.ObjectComparer<UnityEngine.Vector3>
    // System.Collections.Generic.ObjectComparer<int>
    // System.Collections.Generic.ObjectComparer<object>
    // System.Collections.Generic.ObjectEqualityComparer<AotData>
    // System.Collections.Generic.ObjectEqualityComparer<int>
    // System.Collections.Generic.ObjectEqualityComparer<long>
    // System.Collections.Generic.ObjectEqualityComparer<object>
    // System.Collections.Generic.Stack.Enumerator<double>
    // System.Collections.Generic.Stack<double>
    // System.Collections.ObjectModel.ReadOnlyCollection<AotData>
    // System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.Vector3>
    // System.Collections.ObjectModel.ReadOnlyCollection<int>
    // System.Collections.ObjectModel.ReadOnlyCollection<object>
    // System.Comparison<AotData>
    // System.Comparison<UnityEngine.Vector3>
    // System.Comparison<int>
    // System.Comparison<object>
    // System.Func<System.Threading.Tasks.VoidTaskResult>
    // System.Func<int,byte>
    // System.Func<int,int>
    // System.Func<int,object>
    // System.Func<object,System.Threading.Tasks.VoidTaskResult>
    // System.Func<object,byte>
    // System.Linq.Enumerable.Iterator<int>
    // System.Linq.Enumerable.Iterator<object>
    // System.Linq.Enumerable.WhereArrayIterator<int>
    // System.Linq.Enumerable.WhereEnumerableIterator<int>
    // System.Linq.Enumerable.WhereEnumerableIterator<object>
    // System.Linq.Enumerable.WhereListIterator<int>
    // System.Linq.Enumerable.WhereSelectArrayIterator<int,int>
    // System.Linq.Enumerable.WhereSelectArrayIterator<int,object>
    // System.Linq.Enumerable.WhereSelectEnumerableIterator<int,int>
    // System.Linq.Enumerable.WhereSelectEnumerableIterator<int,object>
    // System.Linq.Enumerable.WhereSelectListIterator<int,int>
    // System.Linq.Enumerable.WhereSelectListIterator<int,object>
    // System.Predicate<AotData>
    // System.Predicate<UnityEngine.Vector3>
    // System.Predicate<int>
    // System.Predicate<long>
    // System.Predicate<object>
    // System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<System.Threading.Tasks.VoidTaskResult>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable<System.Threading.Tasks.VoidTaskResult>
    // System.Runtime.CompilerServices.TaskAwaiter<System.Threading.Tasks.VoidTaskResult>
    // System.Threading.Tasks.Task<System.Threading.Tasks.VoidTaskResult>
    // }}

    public void RefMethods()
    {
        // string AotTest.Describe<AotData>(AotData)
        // System.Collections.Generic.List<AotData> AotTest.MakeList<AotData>(int)
        // System.Collections.Generic.IEnumerable<int> System.Linq.Enumerable.Select<int,int>(System.Collections.Generic.IEnumerable<int>,System.Func<int,int>)
        // System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Select<int,object>(System.Collections.Generic.IEnumerable<int>,System.Func<int,object>)
        // System.Collections.Generic.List<object> System.Linq.Enumerable.ToList<object>(System.Collections.Generic.IEnumerable<object>)
        // System.Collections.Generic.IEnumerable<int> System.Linq.Enumerable.Where<int>(System.Collections.Generic.IEnumerable<int>,System.Func<int,bool>)
        // System.Collections.Generic.IEnumerable<int> System.Linq.Enumerable.Iterator<int>.Select<int>(System.Func<int,int>)
        // System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Iterator<int>.Select<object>(System.Func<int,object>)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Hello.<Delay>d__14>(System.Runtime.CompilerServices.TaskAwaiter&,Hello.<Delay>d__14&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Hello.<Delay>d__14>(System.Runtime.CompilerServices.TaskAwaiter&,Hello.<Delay>d__14&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Hello.<Delay>d__14>(Hello.<Delay>d__14&)
        // string string.Join<int>(string,System.Collections.Generic.IEnumerable<int>)
        // string string.JoinCore<int>(System.Char*,int,System.Collections.Generic.IEnumerable<int>)
    }
}
