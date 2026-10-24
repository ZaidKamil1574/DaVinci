using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DataCenter.Editor
{
    /// <summary>
    /// Renders still views of the generated scene to PNG files in Logs/Captures, so layout can be
    /// reviewed from CI or a headless run without opening the Editor.
    /// </summary>
    static class SceneCapture
    {
        const int k_Width = 1600;
        const int k_Height = 900;

        struct View
        {
            public string name;
            public Vector3 position;
            public Vector3 lookAt;
            public float fov;
        }

        static readonly View[] k_Views =
        {
            new View { name = "01_operator", position = new Vector3(-12.2f, 2.05f, 5.15f), lookAt = new Vector3(-13.6f, 1.3f, 3.2f), fov = 55f },
            new View { name = "02_work_rack", position = new Vector3(-13.25f, 1.75f, 4.15f), lookAt = new Vector3(-13.5f, 1.75f, 3.15f), fov = 50f },
            new View { name = "03_section_a", position = new Vector3(-6.5f, 3.6f, -0.5f), lookAt = new Vector3(-12f, 0.8f, 6.5f), fov = 60f },
            new View { name = "04_showcase", position = new Vector3(-4.5f, 2.2f, 1.8f), lookAt = new Vector3(-4.5f, 0.9f, 6.5f), fov = 60f },
            new View { name = "05_hall_overview", position = new Vector3(0f, 26f, -16f), lookAt = new Vector3(0f, 0f, 1f), fov = 60f },
            new View { name = "06_robot_close", position = new Vector3(-14.6f, 1.6f, 5.2f), lookAt = new Vector3(-13.5f, 1.0f, 4.0f), fov = 50f },
        };

        [MenuItem("Tools/Data Center/Capture Views", false, 101)]
        public static void Capture()
        {
            var scene = EditorSceneManager.OpenScene(DataCenterSceneBuilder.ScenePath, OpenSceneMode.Single);
            var folder = Path.Combine(Application.dataPath, "../Logs/Captures");
            Directory.CreateDirectory(folder);

            var cameraObject = new GameObject("Capture Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 120f;

            var target = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            var readback = new Texture2D(k_Width, k_Height, TextureFormat.RGB24, false);

            // The overview looks down through where the ceiling would be.
            var ceiling = GameObject.Find("Environment/Hall/Ceiling");

            foreach (var view in k_Views)
            {
                if (ceiling != null)
                    ceiling.SetActive(!view.name.Contains("overview"));

                camera.fieldOfView = view.fov;
                camera.transform.position = view.position;
                camera.transform.LookAt(view.lookAt);
                camera.Render();

                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0);
                readback.Apply();
                RenderTexture.active = null;

                File.WriteAllBytes(Path.Combine(folder, view.name + ".png"), readback.EncodeToJPG(88));
                Debug.Log($"[Data Center] Captured {view.name}");
            }

            camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(readback);
            EditorSceneManager.CloseScene(scene, false);
        }

        /// <summary>Batch entry: rebuild the scene, then capture. <c>-executeMethod DataCenter.Editor.SceneCapture.BuildAndCaptureBatch</c>.</summary>
        public static void BuildAndCaptureBatch()
        {
            try
            {
                DataCenterSceneBuilder.Build();
                Capture();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
