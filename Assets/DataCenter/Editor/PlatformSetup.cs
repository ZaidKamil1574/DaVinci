using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

namespace DataCenter.Editor
{
    /// <summary>
    /// Platform configuration and builds for the data center scene: WebGL with WebXR, and Quest.
    /// </summary>
    /// <remarks>
    /// Builds pass their scene list explicitly to <see cref="BuildPipeline.BuildPlayer(BuildPlayerOptions)"/>,
    /// so the project's own build settings, which ship the Da Vinci scene, are never edited.
    /// Settings changed here are scoped to the WebGL or Android target.
    /// </remarks>
    public static class PlatformSetup
    {
        const string WebXRPackage = "Packages/com.de-panther.webxr";
        const string WebGLTemplate = "PROJECT:WebXRFullView2020";
        const string WebGLOutput = "Builds/WebGL";
        const string QuestOutput = "Builds/Quest/DataCenterTeleop.apk";

        [MenuItem("Tools/Data Center/Platforms/Configure WebXR (WebGL)", false, 200)]
        public static void ConfigureWebXR()
        {
            CopyWebXRTemplates();

            PlayerSettings.WebGL.template = WebGLTemplate;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);

            AssignLoader(BuildTargetGroup.WebGL, "WebXR.WebXRLoader");
            AssetDatabase.SaveAssets();
            Debug.Log("[Data Center] WebGL configured for WebXR.");
        }

        /// <summary>
        /// Copies WebXR Export's page templates into Assets/WebGLTemplates. Does what the package's
        /// own menu item does, without its confirmation dialog, so it also runs headless.
        /// </summary>
        static void CopyWebXRTemplates()
        {
            // Registry packages live in Library/PackageCache, so resolve the real folder.
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(WebXRPackage + "/package.json");
            var root = package != null ? package.resolvedPath : Path.GetFullPath(WebXRPackage);
            var source = Path.Combine(root, "Hidden~", "WebGLTemplates");
            var destination = Path.Combine(Application.dataPath, "WebGLTemplates");
            if (!Directory.Exists(source))
            {
                Debug.LogWarning($"[Data Center] WebXR templates not found at {source}.");
                return;
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta"))
                    continue;

                var relative = file.Substring(source.Length + 1);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }

            AssetDatabase.Refresh();
        }

        static void AssignLoader(BuildTargetGroup group, string loaderType)
        {
            var perTarget = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
                .Select(guid => AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault();
            if (perTarget == null)
            {
                Debug.LogWarning("[Data Center] No XR General Settings asset; skipping loader assignment.");
                return;
            }

            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            var settings = perTarget.SettingsForBuildTarget(group);
            settings.InitManagerOnStart = true;
            var assigned = XRPackageMetadataStore.AssignLoader(settings.Manager, loaderType, group);
            EditorUtility.SetDirty(perTarget);
            Debug.Log($"[Data Center] {(assigned ? "Assigned" : "Already assigned or unavailable")}: {loaderType} for {group}.");
        }

        [MenuItem("Tools/Data Center/Platforms/Build WebGL (WebXR)", false, 210)]
        public static void BuildWebGL() => Build(BuildTarget.WebGL, WebGLOutput);

        [MenuItem("Tools/Data Center/Platforms/Build Quest APK", false, 211)]
        public static void BuildQuest() => Build(BuildTarget.Android, QuestOutput);

        static bool Build(BuildTarget target, string output)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DataCenterSceneBuilder.ScenePath },
                locationPathName = output,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[Data Center] {target} build {summary.result}: {summary.totalSize / (1024f * 1024f):0.0} MB uncompressed report size, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime.TotalSeconds:0}s -> {output}");

            if (target == BuildTarget.WebGL && Directory.Exists(output))
            {
                long bytes = 0;
                foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
                    bytes += new FileInfo(file).Length;
                Debug.Log($"[Data Center] WebGL download size on disk: {bytes / (1024f * 1024f):0.0} MB");
            }

            return summary.result == BuildResult.Succeeded;
        }

        /// <summary>
        /// Batch entry: configure WebXR and build WebGL, then switch back to whatever target was
        /// active so the next person to open the project is not surprised by a platform switch.
        /// </summary>
        public static void BuildWebGLBatch()
        {
            // Clear what an earlier interrupted build left behind (XR, OpenXR and WebXR settings).
            PlayerSettings.SetPreloadedAssets(System.Array.FindAll(PlayerSettings.GetPreloadedAssets(),
                asset => asset != null && !asset.GetType().FullName.Contains("XR")));

            var original = EditorUserBuildSettings.activeBuildTarget;
            var originalGroup = BuildPipeline.GetBuildTargetGroup(original);

            // XR and WebXR build processors add their settings to the preloaded assets for the
            // duration of a build and remove them after. Snapshot the list so an interrupted build
            // cannot leave them behind in the project.
            var preloaded = PlayerSettings.GetPreloadedAssets();
            Debug.Log($"[Data Center] Active build target before WebGL build: {original}");

            var ok = false;
            try
            {
                ConfigureWebXR();
                ok = Build(BuildTarget.WebGL, WebGLOutput);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(preloaded);
                if (EditorUserBuildSettings.activeBuildTarget != original)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, original);
                AssetDatabase.SaveAssets();
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
