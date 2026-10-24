using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using DataCenter.Robotics;
using DataCenter.Tasks;
using DataCenter.Teleop;
using DataCenter.UI;
using static DataCenter.Editor.RobotBuilder;

namespace DataCenter.Editor
{
    /// <summary>
    /// Generates the data center teleoperation scene from code, so it is reproducible and diffable
    /// instead of living only in hand-edited scene YAML.
    /// </summary>
    /// <remarks>
    /// Run from <b>Tools &gt; Data Center &gt; Build Scene</b>, or headless with
    /// <c>-executeMethod DataCenter.Editor.DataCenterSceneBuilder.BuildBatch</c>. Rebuilding
    /// overwrites the scene and everything under Assets/DataCenter/Generated and /Prefabs; the Da
    /// Vinci scene and other project assets are never touched.
    /// </remarks>
    public static class DataCenterSceneBuilder
    {
        public const string ScenePath = BuilderAssets.ScenesFolder + "/DataCenter.unity";
        const string XROriginPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        /// <summary>Where the Section A robot parks: in the cold aisle, facing rack A-R03.</summary>
        static readonly Vector3 k_RobotASpawn = new Vector3(-13.5f, 0f, 4.0f);

        [MenuItem("Tools/Data Center/Build Scene", false, 0)]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>Batch-mode entry point. Exits with 1 if anything throws.</summary>
        public static void BuildBatch()
        {
            try
            {
                SourceModelProbe.Report();
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            var started = System.DateTime.Now;
            BuilderAssets.ClearCache();
            BuilderAssets.EnsureFolders();
            BuilderAssets.EnsureLayers();
            EnvironmentBuilder.CreateMaterials();

            // ---------- Data
            var rj45 = BuilderAssets.PortTypeAsset("RJ45", "RJ45 Copper", new Color(0.12f, 0.42f, 0.95f), 0.0065f, 0.02f, 25f);
            var lc = BuilderAssets.PortTypeAsset("LC", "LC Duplex Fiber", new Color(1f, 0.82f, 0.05f), 0.005f, 0.02f, 22f);
            var c13 = BuilderAssets.PortTypeAsset("C13", "IEC C13/C14 Power", new Color(0.06f, 0.06f, 0.06f), 0.011f, 0.03f, 25f);
            var spec = BuilderAssets.RobotSpecAsset();
            var ordersA = BuilderAssets.WorkOrderSetAsset("A");

            // ---------- Prefabs
            var robotPrefab = RobotBuilder.Build(spec);
            var rackPrefabs = new GameObject[4];
            for (var i = 0; i < rackPrefabs.Length; i++)
                rackPrefabs[i] = EnvironmentBuilder.BuildRackPrefab(i);
            var connectorRj45 = EnvironmentBuilder.BuildConnectorPrefab(rj45);
            var connectorLc = EnvironmentBuilder.BuildConnectorPrefab(lc);
            EnvironmentBuilder.BuildConnectorPrefab(c13);

            // ---------- Scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("Systems").transform;
            var link = systems.gameObject.AddComponent<TeleopLink>();
            var workOrders = systems.gameObject.AddComponent<WorkOrderManager>();
            workOrders.Configure(new List<WorkOrderSet> { ordersA });
            systems.gameObject.AddComponent<FeedbackAudio>();

            var environment = new GameObject("Environment").transform;
            EnvironmentBuilder.BuildHall(environment);
            EnvironmentBuilder.BuildSectionA(environment, rackPrefabs, rj45);
            var showcase = EnvironmentBuilder.PlaceSourceModel(environment, new Vector3(-4.5f, 0f, 6.5f), 0f);

            var cart = EnvironmentBuilder.BuildCart(environment, "Cable Cart A", new Vector3(-12.7f, 0f, 4.0f), Quaternion.identity);

            // ---------- Robots
            var robots = new GameObject("Robots").transform;
            var robotA = PlaceRobot(robots, robotPrefab, link, "R-A1", "Atlas", "A", k_RobotASpawn, 180f);

            // ---------- Cables, lying on the cart with their plugs pointing at the rack.
            var cables = new GameObject("Cables").transform;
            var plugRotation = Quaternion.Euler(0f, 180f, 0f);
            var top = EnvironmentBuilder.CartTop + 0.008f;
            // Both leads sit on the cart edge nearest the robot, within the arm's 0.84 m reach.
            EnvironmentBuilder.PlaceCable(cables, "A-C01", connectorRj45, rj45,
                new Pose(new Vector3(-12.86f, top, 3.80f), plugRotation),
                new Pose(new Vector3(-12.86f, top, 3.92f), plugRotation), 2.4f);

            // A fiber patch lead on the same cart: the wrong connector for a copper panel, there to
            // show the type check rejecting it.
            EnvironmentBuilder.PlaceCable(cables, "A-F01", connectorLc, lc,
                new Pose(new Vector3(-12.86f, top, 4.04f), plugRotation),
                new Pose(new Vector3(-12.86f, top, 4.16f), plugRotation), 1.6f);

            // ---------- Operator: handle, inputs, rigs, UI
            var handle = BuildHandle();
            var desktopInput = systems.gameObject.AddComponent<DesktopTeleopInput>();
            desktopInput.Configure(BuilderAssets.Mask(BuilderAssets.HandleLayer), BuilderAssets.Mask("Default", BuilderAssets.EquipmentLayer));
            var xrInput = systems.gameObject.AddComponent<XRTeleopInput>();

            var teleopOperator = systems.gameObject.AddComponent<TeleopOperator>();
            teleopOperator.Configure(link, new List<MonoBehaviour> { desktopInput, xrInput }, handle, robotA.robotId);

            var desktopRig = BuildDesktopRig(desktopInput, out var desktopCamera);
            var xrOrigin = BuildXROrigin(out var xrCamera);

            var rigSelector = systems.gameObject.AddComponent<RigSelector>();
            rigSelector.Configure(teleopOperator, desktopRig, desktopCamera, xrOrigin != null ? xrOrigin.transform : null, xrCamera);
            desktopCamera.Focus(robotA.transform, true);

            BuildHud(teleopOperator, workOrders);
            BuildVRPanel(teleopOperator, workOrders);

            // ---------- Lighting and atmosphere
            ConfigureLighting();

            // ---------- Static flags, then navigation
            MarkStatic(environment.gameObject);
            BakeNavMesh(environment);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            var triangles = CountTriangles(environment) + CountTriangles(robots) + CountTriangles(cables);
            Debug.Log($"[Data Center] Built {ScenePath} in {(System.DateTime.Now - started).TotalSeconds:0.0}s. " +
                      $"Scene triangles (all LOD0, before culling): {triangles:N0}. Showcase bay: {(showcase != null ? "placed" : "missing")}. Cart: {cart.name}.");
        }

        static TeleopRobot PlaceRobot(Transform parent, GameObject prefab, TeleopLink link, string id, string displayName, string section, Vector3 position, float yaw)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = $"{id} {displayName}";
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var robot = instance.GetComponent<TeleopRobot>();
            robot.SetLink(link);
            robot.SetIdentity(id, displayName, section);
            PrefabUtility.RecordPrefabInstancePropertyModifications(robot);
            return robot;
        }

        static TargetHandle BuildHandle()
        {
            var root = new GameObject("Target Handle");
            root.layer = BuilderAssets.Layer(BuilderAssets.HandleLayer);

            var material = BuilderAssets.UnlitMaterial("Handle", new Color(0.1f, 0.85f, 1f));
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.SetParent(root.transform, false);
            ball.transform.localScale = Vector3.one * 0.045f;
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.GetComponent<MeshRenderer>().sharedMaterial = material;
            ball.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // A short pointer along the approach axis, so the operator can see which way the plug will go in.
            var pointer = Cylinder("Approach Axis", root.transform, new Vector3(0f, 0f, 0.05f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.008f, 0.04f, 0.008f), material);
            pointer.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var up = Cylinder("Up Axis", root.transform, new Vector3(0f, 0.03f, 0f), Quaternion.identity, new Vector3(0.005f, 0.02f, 0.005f), material);
            up.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var collider = root.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.06f;

            SetLayerRecursively(root, BuilderAssets.Layer(BuilderAssets.HandleLayer));

            var handle = root.AddComponent<TargetHandle>();
            handle.Configure(new Renderer[] { ball.GetComponent<Renderer>(), pointer.GetComponent<Renderer>(), up.GetComponent<Renderer>() });
            return handle;
        }

        static GameObject BuildDesktopRig(DesktopTeleopInput input, out DesktopCameraRig rig)
        {
            var root = new GameObject("Desktop Rig");
            var cameraObject = new GameObject("Desktop Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 70f;
            camera.fieldOfView = 55f;
            camera.allowHDR = false;
            cameraObject.AddComponent<AudioListener>();

            rig = root.AddComponent<DesktopCameraRig>();
            rig.Configure(camera, input);
            return root;
        }

        static XROrigin BuildXROrigin(out Camera xrCamera)
        {
            xrCamera = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XROriginPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Data Center] XR Origin prefab not found at {XROriginPrefabPath}. Import the XRI Starter Assets sample.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "XR Origin (XR Rig)";
            var origin = instance.GetComponent<XROrigin>();
            xrCamera = origin != null ? origin.Camera : instance.GetComponentInChildren<Camera>(true);

            if (xrCamera != null)
            {
                xrCamera.clearFlags = CameraClearFlags.SolidColor;
                xrCamera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
                xrCamera.farClipPlane = 70f;
                xrCamera.enabled = false;

                // One AudioListener in the scene: the desktop camera has it. Components cannot be
                // removed from a prefab instance, so disable instead.
                var listener = xrCamera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(listener);
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(xrCamera);
            }

            // The operator stands still while the robot moves. Locomotion and teleport would steal the
            // thumbsticks that drive the base and the wrist.
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Locomotion" || t.name.Contains("Teleport Interactor") || t.name == "Teleport Stabilized Origin")
                {
                    t.gameObject.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                }
            }

            return origin;
        }

        static void BuildHud(TeleopOperator teleopOperator, WorkOrderManager workOrders)
        {
            var panelSettingsPath = BuilderAssets.GeneratedRoot + "/UI/HudPanelSettings.asset";
            var panelSettings = BuilderAssets.LoadOrCreate<PanelSettings>(panelSettingsPath);
            panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(BuilderAssets.UIFolder + "/DataCenterTheme.tss");
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1600, 900);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 10;
            EditorUtility.SetDirty(panelSettings);

            var hud = new GameObject("HUD");
            var document = hud.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BuilderAssets.UIFolder + "/Hud.uxml");
            var controller = hud.AddComponent<HudController>();
            controller.Configure(teleopOperator, workOrders);
        }

        static void BuildVRPanel(TeleopOperator teleopOperator, WorkOrderManager workOrders)
        {
            var root = new GameObject("VR Status Panel");
            var visual = Child("Visual", root.transform, Vector3.zero);
            var back = Box("Backing", visual, new Vector3(0f, 0f, 0.004f), new Vector3(0.64f, 0.3f, 0.006f), BuilderAssets.UnlitMaterial("VR_Panel", new Color(0.055f, 0.075f, 0.105f)));
            back.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var accent = Box("Accent", visual, new Vector3(0f, 0.148f, 0f), new Vector3(0.64f, 0.006f, 0.004f), BuilderAssets.UnlitMaterial("VR_Accent", new Color(0.1f, 0.83f, 1f)));
            accent.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var text = EnvironmentBuilder.Text("Status", visual, Vector3.zero, "", 0.95f, new Color(0.9f, 0.93f, 0.95f), new Vector2(0.6f, 0.27f));
            text.alignment = TextAlignmentOptions.TopLeft;
            text.fontStyle = FontStyles.Normal;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;

            var panel = root.AddComponent<VRStatusPanel>();
            panel.Configure(teleopOperator, workOrders, text, visual.gameObject);
            visual.gameObject.SetActive(false);
        }

        static void ConfigureLighting()
        {
            // One realtime directional light standing in for the overhead fixtures; the ceiling panels
            // are unlit emissive quads, so they read as lights without costing any.
            var lightObject = new GameObject("Key Light");
            lightObject.transform.rotation = Quaternion.Euler(62f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.65f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.6f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.21f, 0.23f);
            RenderSettings.skybox = null;
            RenderSettings.sun = light;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.1f, 0.11f, 0.13f);
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 60f;
        }

        static void MarkStatic(GameObject root)
        {
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                        | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                // Port LEDs change colour through property blocks and must stay dynamic; text too.
                if (t.GetComponentInParent<PortSocket>() != null || t.GetComponent<TMP_Text>() != null)
                    continue;

                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
        }

        static void BakeNavMesh(Transform environment)
        {
            var surface = environment.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = BuilderAssets.Mask("Default", BuilderAssets.EquipmentLayer);
            surface.BuildNavMesh();

            var path = BuilderAssets.GeneratedRoot + "/NavMesh/DataCenter_NavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            EditorUtility.SetDirty(surface);
        }

        static long CountTriangles(Transform root)
        {
            long total = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;

                // Count only the highest LOD of each group, the worst case when close.
                var group = filter.GetComponentInParent<LODGroup>();
                if (group != null)
                {
                    var lods = group.GetLODs();
                    var renderer = filter.GetComponent<Renderer>();
                    if (lods.Length > 0 && System.Array.IndexOf(lods[0].renderers, renderer) < 0)
                        continue;
                }

                for (var i = 0; i < mesh.subMeshCount; i++)
                    total += mesh.GetIndexCount(i) / 3;
            }

            return total;
        }
    }
}
