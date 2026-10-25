using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using DataCenter.Teleop;

namespace DataCenter.Tests
{
    /// <summary>
    /// Renders the scene from fixed operator viewpoints and records the profiler's render counters,
    /// script cost and allocations, with and without occlusion culling. Writes a Markdown report
    /// to Logs/Performance so numbers can be compared between changes.
    /// </summary>
    /// <remarks>
    /// Render counters (triangles, batches, SetPass calls) describe the work submitted for one eye
    /// and are the same on any device. Frame times here are the Editor's and are not a Quest
    /// measurement; on-device numbers come from OVR Metrics Tool or a profiled development build.
    /// </remarks>
    [Category("Performance")]
    public class PerformanceBenchmark
    {
        const string ScenePath = "Assets/DataCenter/Scenes/DataCenter.unity";
        const int k_Warmup = 20;
        const int k_Samples = 60;

        struct View
        {
            public string name;
            public Vector3 position;
            public Vector3 lookAt;
        }

        // Eye height views an operator actually has, plus the worst case overview.
        static readonly View[] k_Views =
        {
            new View { name = "A operator (cold aisle)", position = new Vector3(-12.6f, 1.65f, 5.0f), lookAt = new Vector3(-13.6f, 1.4f, 3.2f) },
            new View { name = "A down the aisle", position = new Vector3(-8.6f, 1.65f, 4.25f), lookAt = new Vector3(-15f, 1.2f, 4.25f) },
            new View { name = "Corridor, looking along hall", position = new Vector3(-19f, 1.65f, 0f), lookAt = new Vector3(19f, 1.2f, 0f) },
            new View { name = "B operator", position = new Vector3(7.4f, 1.65f, 5.0f), lookAt = new Vector3(6.4f, 1.4f, 3.2f) },
            new View { name = "C operator", position = new Vector3(5.0f, 1.65f, -5.4f), lookAt = new Vector3(5.9f, 1.2f, -3.5f) },
            new View { name = "D training bay", position = new Vector3(-7.0f, 1.65f, -6.4f), lookAt = new Vector3(-8.0f, 1.0f, -4.0f) },
            new View { name = "D staging bench close (model LOD0)", position = new Vector3(-12.5f, 1.65f, -4.6f), lookAt = new Vector3(-14.5f, 1.0f, -7.2f) },
            new View { name = "Overview from corner (worst case)", position = new Vector3(19f, 3.8f, -12f), lookAt = new Vector3(-8f, 0f, 6f) },
        };

        struct Result
        {
            public string view;
            public bool occlusion;
            public double triangles, vertices, batches, setPass, drawCalls, shadowCasters;
            public double renderMs, scriptsMs, gcBytes;
        }

        [UnityTest]
        public IEnumerator RenderStatsPerView()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("DataCenter");
#endif
            for (var i = 0; i < 30; i++)
                yield return null;

            // Take the camera away from the orbit rig so it stays where the benchmark puts it.
            var rig = Object.FindAnyObjectByType<DesktopCameraRig>();
            Assert.That(rig, Is.Not.Null);
            var camera = rig.rigCamera;
            rig.enabled = false;
            camera.enabled = false;
            var target = new RenderTexture(1832, 1920, 24) { antiAliasing = 1 };
            camera.targetTexture = target;
            camera.fieldOfView = 90f;
            camera.aspect = 1832f / 1920f;

            using var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            using var vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            using var scripts = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BehaviourUpdate", k_Samples);
            using var lateScripts = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LateBehaviourUpdate", k_Samples);
            using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", k_Samples);

            var results = new List<Result>();
            var watch = new System.Diagnostics.Stopwatch();

            foreach (var view in k_Views)
            {
                foreach (var occlusion in new[] { true, false })
                {
                    camera.transform.position = view.position;
                    camera.transform.LookAt(view.lookAt);
                    camera.useOcclusionCulling = occlusion;

                    for (var i = 0; i < k_Warmup; i++)
                    {
                        camera.Render();
                        yield return null;
                    }

                    var sum = new Result { view = view.name, occlusion = occlusion };
                    double renderTicks = 0;
                    for (var i = 0; i < k_Samples; i++)
                    {
                        watch.Restart();
                        camera.Render();
                        watch.Stop();
                        renderTicks += watch.Elapsed.TotalMilliseconds;
                        yield return null;

                        sum.triangles += triangles.LastValue;
                        sum.vertices += vertices.LastValue;
                        sum.batches += batches.LastValue;
                        sum.setPass += setPass.LastValue;
                        sum.drawCalls += drawCalls.LastValue;
                        sum.shadowCasters += shadowCasters.LastValue;
                        sum.scriptsMs += (scripts.LastValue + lateScripts.LastValue) / 1e6;
                        sum.gcBytes += gc.LastValue;
                    }

                    sum.triangles /= k_Samples;
                    sum.vertices /= k_Samples;
                    sum.batches /= k_Samples;
                    sum.setPass /= k_Samples;
                    sum.drawCalls /= k_Samples;
                    sum.shadowCasters /= k_Samples;
                    sum.scriptsMs /= k_Samples;
                    sum.gcBytes /= k_Samples;
                    sum.renderMs = renderTicks / k_Samples;
                    results.Add(sum);
                }
            }

            camera.targetTexture = null;
            target.Release();

            var report = Format(results);
            Debug.Log(report);
            var folder = Path.Combine(Application.dataPath, "../Logs/Performance");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "render-stats.md"), report);

            Assert.That(results.Any(r => r.triangles > 0), Is.True, "Render counters read zero; the profiler did not record the renders");
        }

        static string Format(List<Result> results)
        {
            var text = new StringBuilder();
            text.AppendLine("# Render statistics per view");
            text.AppendLine();
            text.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                            $"one eye at Quest 3 per-eye resolution (1832x1920, 90° FOV). Averages of {k_Samples} frames after {k_Warmup} warm-up frames.");
            text.AppendLine();
            text.AppendLine("| View | Occlusion | Triangles | Vertices | Batches | SetPass | Draw calls | Shadow casters | Render CPU (ms) | Scripts (ms) | GC / frame (B) |");
            text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in results)
            {
                text.AppendLine($"| {r.view} | {(r.occlusion ? "on" : "off")} | {r.triangles:N0} | {r.vertices:N0} | {r.batches:N0} | {r.setPass:N0} | " +
                                $"{r.drawCalls:N0} | {r.shadowCasters:N0} | {r.renderMs:0.00} | {r.scriptsMs:0.000} | {r.gcBytes:N0} |");
            }

            return text.ToString();
        }
    }
}
