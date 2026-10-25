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
        public static void BuildWebGL() => BuildIsolated(BuildTarget.WebGL, WebGLOutput, ConfigureWebXR);

        /// <summary>
        /// Builds WebGL for a static host that cannot be configured, such as Wix's: one that sends
        /// no Content-Encoding header for Brotli files and only serves known file extensions.
        /// </summary>
        /// <remarks>
        /// Turns on Unity's decompression fallback for this build only, so the loader decompresses
        /// the Brotli files itself, then renames them to .bin, which such hosts serve byte for byte.
        /// The fallback costs a few seconds of in-browser decompression on first load; the regular
        /// WebGL build, for a host that sends the header, is unchanged.
        /// </remarks>
        [MenuItem("Tools/Data Center/Platforms/Build WebGL for Static Host (Wix)", false, 213)]
        public static void BuildWebGLStaticHost()
        {
            var fallback = PlayerSettings.WebGL.decompressionFallback;
            var ok = false;
            try
            {
                ok = BuildIsolated(BuildTarget.WebGL, StaticHostOutput, () =>
                {
                    ConfigureWebXR();
                    PlayerSettings.WebGL.decompressionFallback = true;
                });
            }
            finally
            {
                PlayerSettings.WebGL.decompressionFallback = fallback;
                AssetDatabase.SaveAssets();
            }

            if (ok)
                PrepareForStaticHost(StaticHostOutput);
        }

        const string StaticHostOutput = "Builds/WebGL-StaticHost";
        const long StaticHostSizeLimit = 20L * 1024 * 1024;

        /// <summary>Renames the compressed build files to .bin and points the page at them.</summary>
        static void PrepareForStaticHost(string output)
        {
            var page = Path.Combine(output, "index.html");
            var html = File.ReadAllText(page);
            foreach (var file in Directory.GetFiles(Path.Combine(output, "Build"), "*.unityweb"))
            {
                var name = Path.GetFileName(file);
                var renamed = Path.ChangeExtension(name, ".bin");
                File.Move(file, Path.Combine(Path.GetDirectoryName(file), renamed));
                html = html.Replace("/" + name + "\"", "/" + renamed + "\"");
            }

            html = html.Replace("<title>Unity WebGL Player | " + ProductName + "</title>", "<title>Data Center Teleop Simulator</title>");
            File.WriteAllText(page, html);

            long bytes = 0;
            foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(file).Length;
            var size = $"{bytes / (1024f * 1024f):0.0} MB";
            if (bytes > StaticHostSizeLimit)
                Debug.LogWarning($"[Data Center] Static host build is {size}, over Wix's 20 MB site limit.");
            else
                Debug.Log($"[Data Center] Static host build ready: {size} -> {output}");
        }

        /// <summary>Batch entry: build WebGL for a static host. See <see cref="BuildWebGLStaticHost"/>.</summary>
        public static void BuildWebGLStaticHostBatch()
        {
            BuildWebGLStaticHost();
            EditorApplication.Exit(File.Exists(Path.Combine(StaticHostOutput, "index.html")) ? 0 : 1);
        }

        [MenuItem("Tools/Data Center/Platforms/Build Quest APK", false, 211)]
        public static void BuildQuest() => BuildIsolated(BuildTarget.Android, QuestOutput, LogQuestXRSetup, BuildOptions.CleanBuildCache);

        // Quest builds always clean the build cache. Android builds are incremental through a
        // Gradle project in Library/Bee, and a build of another scene (the Da Vinci scene from the
        // Build Profiles window) can leave its data there to be packaged into this APK as well.

        /// <summary>
        /// Builds the data center scene for Quest and installs and launches it on the headset
        /// connected over USB. Unlike the Build Profiles window's Build And Run, this builds the
        /// data center scene rather than the project's scene list, under its own app id.
        /// </summary>
        [MenuItem("Tools/Data Center/Platforms/Build And Run on Quest", false, 212)]
        public static void BuildAndRunQuest()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("Build And Run on Quest", "Switch the active platform to Android first (File > Build Profiles > Android > Switch Platform).", "OK");
                return;
            }

            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                BuildIsolated(BuildTarget.Android, QuestOutput, LogQuestXRSetup, BuildOptions.AutoRunPlayer | BuildOptions.CleanBuildCache);
        }

        static bool Build(BuildTarget target, string output, BuildOptions extra = BuildOptions.None)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DataCenterSceneBuilder.ScenePath },
                locationPathName = output,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = extra,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[Data Center] {target} build {summary.result}: {summary.totalSize / (1024f * 1024f):0.0} MB uncompressed report size, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime.TotalSeconds:0}s -> {output}");

            if (target == BuildTarget.Android && File.Exists(output))
                Debug.Log($"[Data Center] APK size: {new FileInfo(output).Length / (1024f * 1024f):0.0} MB");

            if (target == BuildTarget.WebGL && Directory.Exists(output))
            {
                long bytes = 0;
                foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
                    bytes += new FileInfo(file).Length;
                Debug.Log($"[Data Center] WebGL download size on disk: {bytes / (1024f * 1024f):0.0} MB");
            }

            return summary.result == BuildResult.Succeeded;
        }

        /// <summary>Batch entry: configure WebXR and build WebGL. See <see cref="BuildIsolated"/>.</summary>
        public static void BuildWebGLBatch() =>
            EditorApplication.Exit(BuildIsolated(BuildTarget.WebGL, WebGLOutput, ConfigureWebXR) ? 0 : 1);

        /// <summary>Batch entry: build the Quest APK. See <see cref="BuildIsolated"/>.</summary>
        public static void BuildQuestBatch() =>
            EditorApplication.Exit(BuildIsolated(BuildTarget.Android, QuestOutput, LogQuestXRSetup, BuildOptions.CleanBuildCache) ? 0 : 1);

        /// <summary>
        /// Builds the data center scene without leaving any trace on the project the Da Vinci app
        /// ships from. For the duration of the build only, it gives the player its own name and
        /// application id, so installing it on a Quest does not replace the Da Vinci app; afterwards
        /// it restores those, the preloaded assets that XR build processors add and remove, and the
        /// active build target.
        /// </summary>
        static bool BuildIsolated(BuildTarget target, string output, System.Action configure, BuildOptions extra = BuildOptions.None)
        {
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var named = NamedBuildTarget.FromBuildTargetGroup(group);
            var originalTarget = EditorUserBuildSettings.activeBuildTarget;
            var originalGroup = BuildPipeline.GetBuildTargetGroup(originalTarget);

            // Clear what an earlier interrupted build left behind (XR, OpenXR and WebXR settings).
            PlayerSettings.SetPreloadedAssets(System.Array.FindAll(PlayerSettings.GetPreloadedAssets(),
                asset => asset != null && !asset.GetType().FullName.Contains("XR")));
            var preloaded = PlayerSettings.GetPreloadedAssets();
            var productName = PlayerSettings.productName;
            var applicationId = PlayerSettings.GetApplicationIdentifier(named);
            Debug.Log($"[Data Center] Building {target}. Active target before: {originalTarget}.");

            var ok = false;
            try
            {
                PlayerSettings.productName = ProductName;
                PlayerSettings.SetApplicationIdentifier(named, ApplicationId);
                configure?.Invoke();
                ok = Build(target, output, extra);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                PlayerSettings.productName = productName;
                PlayerSettings.SetApplicationIdentifier(named, applicationId);
                PlayerSettings.SetPreloadedAssets(preloaded);
                if (EditorUserBuildSettings.activeBuildTarget != originalTarget)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalTarget);
                AssetDatabase.SaveAssets();
            }

            return ok;
        }

        const string ProductName = "DC Teleop";
        const string ApplicationId = "com.zrmap.dcteleop";

        /// <summary>Logs the Android XR configuration a Quest build will use, for the build record.</summary>
        static void LogQuestXRSetup()
        {
            var perTarget = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
                .Select(guid => AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault();
            var android = perTarget != null ? perTarget.SettingsForBuildTarget(BuildTargetGroup.Android) : null;
            var loaders = android != null && android.Manager != null ? string.Join(", ", android.Manager.activeLoaders.Select(l => l.name)) : "none";
            Debug.Log($"[Data Center] Quest build: loaders [{loaders}], scripting backend {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}, " +
                      $"architectures {PlayerSettings.Android.targetArchitectures}, graphics {string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}, " +
                      $"min SDK {PlayerSettings.Android.minSdkVersion}.");
        }
    }
}
