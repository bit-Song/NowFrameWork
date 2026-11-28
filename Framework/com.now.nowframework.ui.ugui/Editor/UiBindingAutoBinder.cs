using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NowFrameWork.UI.Editor
{
    /// <summary>编译完成后自动挂载生成脚本并写入引用。</summary>
    public static class UiBindingAutoBinder
    {
        const string Key = "NowFrameWork.UI.Binding.Pending";

        [Serializable]
        public sealed class PendingField
        {
            public string Field;
            public string Path;
            public string TypeName;
        }

        [Serializable]
        public sealed class PendingBinding
        {
            public string PrefabPath;
            public string ScenePath;
            public string RootPath;
            public string ViewType;
            public List<PendingField> Fields = new List<PendingField>();
        }

        [Serializable]
        sealed class PendingList
        {
            public List<PendingBinding> Items = new List<PendingBinding>();
        }

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            EditorApplication.delayCall += Process;
            CompilationPipeline.compilationFinished += _ => { EditorApplication.delayCall += Process; };
        }

        /// <summary>登记一次待绑定请求；域重载后由延迟回调完成。</summary>
        public static void Queue(string prefabPath, GameObject root, string viewType, UiBindingGenerator.Result result)
        {
            if (root == null || result == null) return;

            PendingList list = Load();

            var pending = new PendingBinding
            {
                PrefabPath = prefabPath ?? string.Empty,
                ViewType = viewType,
                RootPath = string.IsNullOrEmpty(prefabPath) ? ScenePathOf(root.transform) : string.Empty,
                ScenePath = string.IsNullOrEmpty(prefabPath) && root.scene.IsValid() ? root.scene.path : string.Empty,
            };

            foreach (UiBindingGenerator.Candidate candidate in result.Fields)
            {
                pending.Fields.Add(new PendingField
                {
                    Field = candidate.FieldName,
                    Path = candidate.Path,
                    TypeName = candidate.TypeName,
                });
            }

            list.Items.Add(pending);
            Save(list);

            Debug.Log("[UI 绑定] 已登记自动绑定：" + viewType + "（编译完成后挂载并写入 " + pending.Fields.Count + " 个引用）。");
        }

        static void Process()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Process;
                return;
            }

            PendingList list = Load();
            if (list.Items.Count == 0) return;

            list.Items.RemoveAll(Apply);
            Save(list);
        }

        static bool Apply(PendingBinding pending)
        {
            try
            {
                Type type = FindViewType(pending.ViewType);

                if (type == null)
                {
                    Debug.LogWarning("[UI 绑定] 找不到生成的类型：" + pending.ViewType + "，请手动执行一次生成。");
                    return true;
                }

                if (!string.IsNullOrEmpty(pending.PrefabPath))
                {
                    ApplyPrefab(pending, type);
                    return true;
                }

                ApplyScene(pending, type);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[UI 绑定] 自动绑定失败：" + error.Message);
                return true;
            }
        }

        static void ApplyPrefab(PendingBinding pending, Type type)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(pending.PrefabPath) == null)
            {
                Debug.LogWarning("[UI 绑定] Prefab 不存在：" + pending.PrefabPath);
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(pending.PrefabPath);

            try
            {
                GameObject root = string.IsNullOrEmpty(pending.RootPath)
                    ? contents
                    : FindChild(contents.transform, pending.RootPath);

                if (root == null) throw new InvalidOperationException("找不到根节点：" + pending.RootPath);

                Bind(root, type, pending.Fields);
                PrefabUtility.SaveAsPrefabAsset(contents, pending.PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Debug.Log("[UI 绑定] 已自动挂载并保存 Prefab：" + pending.PrefabPath);
        }

        static void ApplyScene(PendingBinding pending, Type type)
        {
            GameObject root = null;

            for (int i = 0; i < SceneManager.sceneCount && root == null; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject candidate in scene.GetRootGameObjects())
                {
                    root = FindChild(candidate.transform, pending.RootPath);
                    if (root != null) break;
                }
            }

            if (root == null)
            {
                Debug.LogWarning("[UI 绑定] 场景未打开或找不到节点：" + pending.RootPath + "，请重新执行生成。");
                return;
            }

            Bind(root, type, pending.Fields);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[UI 绑定] 已自动挂载并绑定场景对象：" + root.name);
        }

        static void Bind(GameObject root, Type type, List<PendingField> fields)
        {
            UiView view = root.GetComponent(type) as UiView;

            if (view == null)
            {
                UiView baseView = root.GetComponent<UiView>();
                if (baseView != null) UnityEngine.Object.DestroyImmediate(baseView, true);

                view = root.AddComponent(type) as UiView;
                if (view == null) throw new InvalidOperationException("类型不是 UiView：" + type.FullName);
            }

            Undo.RegisterCompleteObjectUndo(view, "Bind UI Fields");

            var serialized = new SerializedObject(view);

            foreach (PendingField field in fields)
            {
                SerializedProperty property = serialized.FindProperty(field.Field);
                if (property == null) continue;

                Transform target = string.IsNullOrEmpty(field.Path) ? root.transform : root.transform.Find(field.Path);
                if (target == null) continue;

                Component component = FindComponent(target.gameObject, field.TypeName);
                if (component == null) continue;

                property.objectReferenceValue = component;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
        }

        static Component FindComponent(GameObject target, string typeName)
        {
            foreach (Component component in target.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name == typeName) return component;
            }

            return null;
        }

        static Type FindViewType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null && typeof(UiView).IsAssignableFrom(type)) return type;
            }

            return null;
        }

        static GameObject FindChild(Transform root, string path)
        {
            Transform current = string.IsNullOrEmpty(path) ? root : root.Find(path);
            return current != null ? current.gameObject : null;
        }

        static string ScenePathOf(Transform target)
        {
            string path = target.name;

            for (Transform current = target.parent; current != null; current = current.parent)
                path = current.name + "/" + path;

            return path;
        }

        static PendingList Load()
        {
            string json = SessionState.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(json)) return new PendingList();

            try
            {
                return JsonUtility.FromJson<PendingList>(json) ?? new PendingList();
            }
            catch
            {
                return new PendingList();
            }
        }

        static void Save(PendingList list)
        {
            SessionState.SetString(Key, list.Items.Count == 0 ? string.Empty : JsonUtility.ToJson(list));
        }
    }
}
