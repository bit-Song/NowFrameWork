using System.Collections.Generic;
using System.IO;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    /// <summary>生成"输入 + 相机"演示场景：地面、胶囊角色、Cinemachine 跟随相机。</summary>
    public static class InputCameraSceneMenu
    {
        const string ScenePath = "Assets/Scenes/InputCameraDemo.scene";

        [MenuItem("NowFrameWork/Tests/创建输入与相机演示场景")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 1f, 0f);
            player.AddComponent<Game.CharacterDemo>();

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<CinemachineBrain>();

            GameObject vcamGo = new GameObject("CM Follow");
            CinemachineVirtualCamera vcam = vcamGo.AddComponent<CinemachineVirtualCamera>();
            vcam.Follow = player.transform;
            vcam.LookAt = player.transform;

            // Body：位置跟随
            CinemachineTransposer transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_FollowOffset = new Vector3(0f, 6f, -8f);
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTarget;

            // Aim：转向目标（缺了它相机不会看角色，只会平移）
            CinemachineComposer composer = vcam.AddCinemachineComponent<CinemachineComposer>();
            composer.m_TrackedObjectOffset = new Vector3(0f, 1f, 0f);
            composer.m_HorizontalDamping = 0.5f;
            composer.m_VerticalDamping = 0.5f;

            string dir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("保存场景失败: " + ScenePath);
                return;
            }

            AddToBuildSettings();
            AssetDatabase.Refresh();

            Debug.Log("已创建 " + ScenePath + "，按 Play 即可用 WASD/手柄移动，视角由 Cinemachine 跟随。");
        }

        static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (EditorBuildSettingsScene scene in scenes)
                if (scene.path == ScenePath) return;

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}