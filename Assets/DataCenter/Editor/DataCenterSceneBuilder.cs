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
            var orderSets = new List<WorkOrderSet>();
            foreach (var section in EnvironmentBuilder.Sections)
                orderSets.Add(BuilderAssets.WorkOrderSetAsset(section.id));

            // ---------- Prefabs
            var robotPrefab = RobotBuilder.Build(spec);
            var rackPrefabs = new GameObject[4];
            for (var i = 0; i < rackPrefabs.Length; i++)
                rackPrefabs[i] = EnvironmentBuilder.BuildRackPrefab(i);
            var connectorRj45 = EnvironmentBuilder.BuildConnectorPrefab(rj45);
            var connectorLc = EnvironmentBuilder.BuildConnectorPrefab(lc);
            var connectorC13 = EnvironmentBuilder.BuildConnectorPrefab(c13);

            // ---------- Scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("Systems").transform;
            var link = systems.gameObject.AddComponent<TeleopLink>();
            var workOrders = systems.gameObject.AddComponent<WorkOrderManager>();
            workOrders.Configure(orderSets);
            systems.gameObject.AddComponent<FeedbackAudio>();

            // ---------- Environment
            var environment = new GameObject("Environment").transform;
            var footprints = new List<Rect>();
            EnvironmentBuilder.BuildHall(environment);
            EnvironmentBuilder.BuildSectionDividers(environment);
            EnvironmentBuilder.BuildSectionA(environment, rackPrefabs, rj45, footprints);
            EnvironmentBuilder.BuildSectionB(environment, rackPrefabs, lc, footprints);
            EnvironmentBuilder.BuildSectionC(environment, rackPrefabs, c13, footprints);
            EnvironmentBuilder.BuildSectionD(environment, rj45, footprints);

            // The supplied model is an open rack with servers and loose cabling, which reads as
            // equipment being integrated before deployment, so it lives in Staging.
            var showcase = EnvironmentBuilder.PlaceSourceModel(environment, new Vector3(-14.5f, 0f, -7.2f), 0f);
            if (showcase != null)
                footprints.Add(Rect.MinMaxRect(-15.3f, -7.6f, -13.7f, -6.8f));

            // ---------- Robots, one per section, each with a cart of cables beside it
            var robots = new GameObject("Robots").transform;
            var cables = new GameObject("Cables").transform;
            var stations = new[] { EnvironmentBuilder.StationA, EnvironmentBuilder.StationB, EnvironmentBuilder.StationC, EnvironmentBuilder.StationD };
            var placed = new List<TeleopRobot>();
            foreach (var station in stations)
                placed.Add(PlaceRobot(robots, robotPrefab, link, station.robotId, station.robotName, station.sectionId, station.spawn, station.yaw));
            var robotA = placed[0];

            EnvironmentBuilder.BuildStationCart(environment, cables, EnvironmentBuilder.StationA,
                ("A-C01", connectorRj45, rj45, 2.4f), ("A-C02", connectorRj45, rj45, 2.4f),
                // A fiber lead on a copper cart: the wrong connector, there to show the type check.
                ("A-F01", connectorLc, lc, 1.6f));
            EnvironmentBuilder.BuildStationCart(environment, cables, EnvironmentBuilder.StationB,
                ("B-F01", connectorLc, lc, 2.0f), ("B-C01", connectorRj45, rj45, 2.0f));
            EnvironmentBuilder.BuildStationCart(environment, cables, EnvironmentBuilder.StationC,
                ("C-P01", connectorC13, c13, 1.8f));
            EnvironmentBuilder.BuildStationCart(environment, cables, EnvironmentBuilder.StationD,
                ("D-C01", connectorRj45, rj45, 1.6f));
            foreach (var station in stations)
                footprints.Add(new Rect(EnvironmentBuilder.StationPoint(station, -0.82f, 0f, 0f).x - 0.25f, EnvironmentBuilder.StationPoint(station, -0.82f, 0f, 0f).z - 0.3f, 0.5f, 0.6f));

            // ---------- Floor plan for the minimap
            var map = systems.gameObject.AddComponent<FacilityMap>();
            var zones = new List<MapZone>();
            foreach (var section in EnvironmentBuilder.Sections)
                zones.Add(new MapZone { id = section.id, title = section.title, area = section.area, accent = section.accent });
            map.Configure(Rect.MinMaxRect(-EnvironmentBuilder.HallHalfX, -EnvironmentBuilder.HallHalfZ, EnvironmentBuilder.HallHalfX, EnvironmentBuilder.HallHalfZ), zones, footprints);

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

            // ---------- Operator aids: tutorial, wrist camera, commanded-pose ghost, proximity cue
            var tutorial = systems.gameObject.AddComponent<TutorialController>();
            tutorial.Configure(teleopOperator);
            var wristCamera = systems.gameObject.AddComponent<WristCameraFeed>();
            wristCamera.Configure(teleopOperator);
            var ghost = OperatorToolsBuilder.BuildGhost(teleopOperator);
            var proximity = OperatorToolsBuilder.BuildProximityIndicator(teleopOperator);
            var guidance = OperatorToolsBuilder.BuildGuidance(teleopOperator, workOrders);
            OperatorToolsBuilder.BuildInteractionFeedback(teleopOperator);
            var demo = systems.gameObject.AddComponent<DemoPilot>();
            demo.Configure(teleopOperator, workOrders);
            if (xrOrigin != null)
            {
                OperatorToolsBuilder.BuildVRControls(xrOrigin, teleopOperator, tutorial);
                OperatorToolsBuilder.BuildRightStickArbiter(systems.gameObject, xrOrigin, xrInput);
            }

            BuildHud(teleopOperator, workOrders, map, wristCamera, proximity, ghost, tutorial, guidance, demo);
            BuildVRPanel(teleopOperator, workOrders, proximity, tutorial, wristCamera, guidance);

            // ---------- Lighting and atmosphere
            ConfigureLighting();

            // ---------- Static flags, then navigation
            MarkStatic(environment.gameObject);
            TrimShadowCasters(environment);
            BakeNavMesh(environment);

            EditorSceneManager.SaveScene(scene, ScenePath);
            BakeOcclusion(scene);
            AssetDatabase.SaveAssets();

            var triangles = CountTriangles(environment) + CountTriangles(robots) + CountTriangles(cables);
            Debug.Log($"[Data Center] Built {ScenePath} in {(System.DateTime.Now - started).TotalSeconds:0.0}s. " +
                      $"Scene triangles (all LOD0, before culling): {triangles:N0}. Robots: {placed.Count}. Showcase bay: {(showcase != null ? "placed" : "missing")}.");
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
            rig.Configure(camera, input, BuilderAssets.Mask(BuilderAssets.EquipmentLayer));
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

            // Locomotion: the right stick keeps the rig's own teleport (push forward, release) and snap
            // turn. The left stick belongs to the robot, so walking, grab-move, climbing, jumping and
            // gravity are switched off, as is the left hand's teleport ray.
            var disabledProviders = new[] { "Move", "Grab Move", "Climb", "Climb Teleport", "Jump", "Gravity" };
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                var underLocomotion = t.parent != null && t.parent.name == "Locomotion";
                var leftTeleport = t.name.Contains("Teleport Interactor") && IsUnder(t, "Left Controller");
                if ((underLocomotion && System.Array.IndexOf(disabledProviders, t.name) >= 0) || leftTeleport)
                {
                    t.gameObject.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                }
            }

            // The teleport arc passes through the robot, cables, ports and the grab handle, so the
            // operator can land right beside the robot. Racks and walls still block it.
            var teleportMask = ~BuilderAssets.Mask(BuilderAssets.RobotLayer, BuilderAssets.CableLayer, BuilderAssets.PortLayer, BuilderAssets.HandleLayer);
            foreach (var ray in instance.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true))
            {
                if (!ray.name.Contains("Teleport"))
                    continue;

                ray.raycastMask = teleportMask;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ray);
            }

            // Left hand: "smooth motion" mode keeps the left stick out of teleport mode (with the move
            // provider off it then does nothing for the rig). Right hand: teleport plus snap turn.
            foreach (var manager in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (manager == null || manager.GetType().Name != "ControllerInputActionManager")
                    continue;

                var serialized = new SerializedObject(manager);
                var left = IsUnder(manager.transform, "Left Controller");
                serialized.FindProperty("m_SmoothMotionEnabled").boolValue = left;
                serialized.FindProperty("m_SmoothTurnEnabled").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
            }

            return origin;
        }

        static bool IsUnder(Transform t, string ancestorName)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (p.name == ancestorName)
                    return true;
            }

            return false;
        }

        static void BuildHud(TeleopOperator teleopOperator, WorkOrderManager workOrders, FacilityMap map,
            WristCameraFeed wristCamera, ProximityIndicator proximity, CommandGhost ghost, TutorialController tutorial, GuidanceController guidance, DemoPilot demo)
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
            controller.Configure(teleopOperator, workOrders, map, wristCamera, proximity, ghost, tutorial, guidance, demo);
        }

        static void BuildVRPanel(TeleopOperator teleopOperator, WorkOrderManager workOrders, ProximityIndicator proximity,
            TutorialController tutorial, WristCameraFeed wristCamera, GuidanceController guidance)
        {
            // Compact: about three quarters of the original size, and hidden until Y.
            var root = new GameObject("VR Status Panel");
            root.transform.localScale = Vector3.one * 0.72f;
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

            // Wrist camera feed beside the text. The texture is attached at runtime.
            var feed = GameObject.CreatePrimitive(PrimitiveType.Quad);
            feed.name = "Wrist Camera";
            feed.transform.SetParent(visual, false);
            feed.transform.localPosition = new Vector3(0.48f, 0f, 0f);
            feed.transform.localScale = new Vector3(0.28f, 0.175f, 1f);
            Object.DestroyImmediate(feed.GetComponent<Collider>());
            var feedRenderer = feed.GetComponent<MeshRenderer>();
            feedRenderer.sharedMaterial = BuilderAssets.UnlitMaterial("VR_WristCam", Color.white);
            feedRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var panel = root.AddComponent<VRStatusPanel>();
            panel.Configure(teleopOperator, workOrders, text, visual.gameObject, proximity, tutorial, wristCamera, feedRenderer, guidance);
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

        /// <summary>
        /// Keeps the shadow pass to what reads as shadow at operator height: racks, cabinets, carts
        /// and robots. The ceiling, walls, trays, signage and floor markings never cast a shadow an
        /// operator would notice (and the ceiling slab would otherwise shade the whole hall from the
        /// key light). The first profile showed 300 to 700 shadow casters per view before this.
        /// </summary>
        static void TrimShadowCasters(Transform environment)
        {
            var noShadow = new[] { "Ceiling", "Walls", "Cable Trays", "Signage", "Section Dividers", "Floor", "Aisle Tiles", "Hot Aisle Containment", "ODF Jumpers", "Busway", "Tutorial Bay", "Charging Pads" };
            var trimmed = 0;
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                var t = renderer.transform;
                var cast = true;
                while (t != null && t != environment)
                {
                    if (System.Array.IndexOf(noShadow, t.name) >= 0 || t.name.StartsWith("Row Label") || t.name.StartsWith("Id Plate") || t.name.StartsWith("Edge "))
                    {
                        cast = false;
                        break;
                    }

                    t = t.parent;
                }

                // Small parts inside a rack's footprint are already in its shadow.
                var size = renderer.bounds.size;
                if (cast && size.x < 0.05f && size.y < 0.05f)
                    cast = false;

                if (cast || renderer.shadowCastingMode == ShadowCastingMode.Off)
                    continue;

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                trimmed++;
            }

            Debug.Log($"[Data Center] Shadow casting turned off on {trimmed} decorative renderers.");
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

        /// <summary>
        /// Bakes occlusion culling. Rack rows, partitions and containment hide most of the hall from
        /// any one aisle, so this removes a large share of draw calls before they reach the GPU.
        /// </summary>
        static void BakeOcclusion(UnityEngine.SceneManagement.Scene scene)
        {
            var started = System.DateTime.Now;
            StaticOcclusionCulling.smallestOccluder = 1.0f;
            StaticOcclusionCulling.smallestHole = 0.2f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            if (!StaticOcclusionCulling.Compute())
            {
                Debug.LogWarning("[Data Center] Occlusion bake failed; the scene will render without occlusion culling.");
                return;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Data Center] Occlusion culling baked in {(System.DateTime.Now - started).TotalSeconds:0.0}s, data size {StaticOcclusionCulling.umbraDataSize / 1024f:0} KB.");
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
