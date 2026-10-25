using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using DataCenter.Teleop;
using DataCenter.UI;
using static DataCenter.Editor.RobotBuilder;

namespace DataCenter.Editor
{
    /// <summary>
    /// Builds the operator aids added in Phase 4: the commanded-pose ghost, the proximity cue, and
    /// the VR controls (console stand, wrist menu, E-Stop pedestal).
    /// </summary>
    static class OperatorToolsBuilder
    {
        static Material Transparent(string name, Color color)
        {
            // URP Unlit in transparent mode. Small and few, so the overdraw is affordable on Quest.
            var material = BuilderAssets.UnlitMaterial(name, color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(material);
            return material;
        }

        static LineRenderer Line(GameObject host, Material material, float width)
        {
            var line = host.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        static void NoShadows(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>A see-through gripper drawn at the commanded pose, with a line to the real one.</summary>
        public static CommandGhost BuildGhost(TeleopOperator teleopOperator)
        {
            var root = new GameObject("Command Ghost");
            var material = Transparent("Ghost", new Color(0.1f, 0.83f, 1f, 0.35f));

            // Origin is the tool tip; the gripper sits behind it along -Z, the approach axis.
            var ghost = new GameObject("Ghost Gripper");
            ghost.transform.SetParent(root.transform, false);
            Box("Body", ghost.transform, new Vector3(0f, 0f, -0.105f), new Vector3(0.095f, 0.05f, 0.06f), material);
            Box("Finger L", ghost.transform, new Vector3(-0.03f, 0f, -0.04f), new Vector3(0.012f, 0.022f, 0.07f), material);
            Box("Finger R", ghost.transform, new Vector3(0.03f, 0f, -0.04f), new Vector3(0.012f, 0.022f, 0.07f), material);
            NoShadows(ghost);
            ghost.SetActive(false);

            var line = Line(root, BuilderAssets.UnlitMaterial("Ghost_Line", new Color(0.1f, 0.83f, 1f)), 0.003f);

            var component = root.AddComponent<CommandGhost>();
            component.Configure(teleopOperator, ghost, line);
            return component;
        }

        /// <summary>The green / yellow / red marker at the tool tip and its line to the nearest equipment.</summary>
        public static ProximityIndicator BuildProximityIndicator(TeleopOperator teleopOperator)
        {
            var root = new GameObject("Proximity Indicator");
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one * 0.018f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<MeshRenderer>().sharedMaterial = BuilderAssets.UnlitMaterial("Proximity_Marker", new Color(0.18f, 0.9f, 0.42f));
            NoShadows(marker);

            // Vertex colours tint the line by zone; URP Unlit multiplies them in.
            var line = Line(root, BuilderAssets.UnlitMaterial("Proximity_Line", Color.white), 0.004f);

            var component = root.AddComponent<ProximityIndicator>();
            component.Configure(teleopOperator, marker.GetComponent<Renderer>(), line);
            return component;
        }

        /// <summary>The next-step marker, label and guide lines.</summary>
        /// <summary>Grab-ready and plug-ready chimes, and controller haptics in VR.</summary>
        public static InteractionFeedback BuildInteractionFeedback(TeleopOperator teleopOperator)
        {
            var feedback = new GameObject("Interaction Feedback").AddComponent<InteractionFeedback>();
            feedback.Configure(teleopOperator);
            return feedback;
        }

        public static GuidanceController BuildGuidance(TeleopOperator teleopOperator, DataCenter.Tasks.WorkOrderManager workOrders)
        {
            var root = new GameObject("Guidance");
            var marker = new GameObject("Marker").transform;
            marker.SetParent(root.transform, false);

            var diamond = GameObject.CreatePrimitive(PrimitiveType.Cube);
            diamond.name = "Diamond";
            diamond.transform.SetParent(marker, false);
            diamond.transform.localScale = Vector3.one * 0.035f;
            Object.DestroyImmediate(diamond.GetComponent<Collider>());
            diamond.GetComponent<MeshRenderer>().sharedMaterial = BuilderAssets.UnlitMaterial("Guidance_Marker", Color.white);

            // About 3.5 cm capitals: readable at two metres in the headset without covering the
            // target. Bottom-aligned so the text grows upward, clear of the diamond.
            var label = EnvironmentBuilder.Text("Label", marker, new Vector3(0f, 0.09f, 0f), "", 0.75f, Color.white, new Vector2(0.5f, 0.1f));
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Bottom;
            NoShadows(marker.gameObject);

            var pathHost = new GameObject("Path Line");
            pathHost.transform.SetParent(root.transform, false);
            var path = Line(pathHost, BuilderAssets.UnlitMaterial("Guidance_Line", Color.white), 0.004f);

            var approachHost = new GameObject("Approach Line");
            approachHost.transform.SetParent(root.transform, false);
            var approach = Line(approachHost, BuilderAssets.UnlitMaterial("Guidance_Approach", new Color(0.18f, 0.9f, 0.42f)), 0.006f);

            var guidance = root.AddComponent<GuidanceController>();
            guidance.Configure(teleopOperator, workOrders, marker, diamond.transform, label, path, approach);
            marker.gameObject.SetActive(false);
            return guidance;
        }

        /// <summary>
        /// Gives the rig's right-stick teleport and snap turn the stick only while the right grip is
        /// held, so the stick otherwise moves the gripper.
        /// </summary>
        public static void BuildRightStickArbiter(GameObject host, XROrigin origin, XRTeleopInput input)
        {
            var gated = new List<Behaviour>();
            foreach (var behaviour in origin.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType().Name;
                var right = IsUnder(behaviour.transform, "Right Controller");
                if ((type == "ControllerInputActionManager" && right) || type == "SnapTurnProvider")
                    gated.Add(behaviour);

                // Smooth turn stays off for good: only snap turn is offered, and only on the grip.
                if (type == "ContinuousTurnProvider")
                {
                    behaviour.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
                }
            }

            var arbiter = host.AddComponent<RightStickArbiter>();
            arbiter.Configure(input, gated);
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

        // ---------------------------------------------------------------- VR controls

        public static void BuildVRControls(XROrigin origin, TeleopOperator teleopOperator, TutorialController tutorial)
        {
            BuildConsole(origin.transform, teleopOperator);
            BuildWristMenu(origin.transform, teleopOperator, tutorial);
            BuildEStopPedestal(origin.transform, teleopOperator, tutorial);
        }

        /// <summary>
        /// A console stand in front of the operator with the Da Vinci lever (ported) for the base, a
        /// second lever for the tool and a twist knob for the wrist. Child of the XR Origin, so it
        /// moves with the operator when another robot is selected.
        /// </summary>
        static void BuildConsole(Transform origin, TeleopOperator teleopOperator)
        {
            var root = new GameObject("Console Station").transform;
            root.SetParent(origin, false);
            root.localPosition = new Vector3(0f, 0f, 0.45f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            var body = BuilderAssets.LitMaterial("Console_Body", new Color(0.16f, 0.18f, 0.22f), 0.3f, 0.5f);
            var accent = BuilderAssets.UnlitMaterial("Console_Accent", new Color(0.1f, 0.83f, 1f));
            var handleMaterial = BuilderAssets.LitMaterial("Console_Handle", new Color(0.9f, 0.9f, 0.92f), 0.2f, 0.7f);
            Box("Pedestal", visual, new Vector3(0f, 0.45f, 0f), new Vector3(0.12f, 0.9f, 0.12f), body);
            Box("Desk", visual, new Vector3(0f, 0.92f, 0f), new Vector3(0.6f, 0.04f, 0.28f), body);
            Box("Desk Edge", visual, new Vector3(0f, 0.92f, -0.141f), new Vector3(0.6f, 0.02f, 0.004f), accent);

            var travel = Lever(visual, "Travel Lever (base)", new Vector3(-0.2f, 0.94f, 0f), handleMaterial, accent);
            var lift = Lever(visual, "Lift Lever (tool)", new Vector3(0.2f, 0.94f, 0f), handleMaterial, accent);
            var knob = Knob(visual, "Wrist Knob", new Vector3(0f, 0.94f, 0.04f), handleMaterial, accent);

            Label(visual, "BASE", new Vector3(-0.2f, 0.945f, -0.11f));
            Label(visual, "WRIST", new Vector3(0f, 0.945f, -0.11f));
            Label(visual, "TOOL", new Vector3(0.2f, 0.945f, -0.11f));
            NoShadows(visual.gameObject);

            var station = root.gameObject.AddComponent<ConsoleStation>();
            station.Configure(teleopOperator, travel, lift, knob, visual.gameObject);
            visual.gameObject.SetActive(false);
        }

        static XRLever2D Lever(Transform parent, string name, Vector3 position, Material handleMaterial, Material accent)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;

            var handle = new GameObject("Handle").transform;
            handle.SetParent(root, false);
            Cylinder("Shaft", handle, new Vector3(0f, 0.07f, 0f), Quaternion.identity, new Vector3(0.018f, 0.07f, 0.018f), handleMaterial);
            var grip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            grip.name = "Grip";
            grip.transform.SetParent(handle, false);
            grip.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            grip.transform.localScale = Vector3.one * 0.045f;
            grip.GetComponent<MeshRenderer>().sharedMaterial = accent;
            Object.DestroyImmediate(grip.GetComponent<Collider>());
            Cylinder("Boot", root, Vector3.zero, Quaternion.identity, new Vector3(0.06f, 0.01f, 0.06f), handleMaterial);

            var collider = root.gameObject.AddComponent<SphereCollider>();
            collider.center = new Vector3(0f, 0.14f, 0f);
            collider.radius = 0.05f;

            var lever = root.gameObject.AddComponent<XRLever2D>();
            lever.Configure(handle);
            return lever;
        }

        static XRTwistKnob Knob(Transform parent, string name, Vector3 position, Material handleMaterial, Material accent)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;

            var handle = new GameObject("Handle").transform;
            handle.SetParent(root, false);
            Cylinder("Cap", handle, new Vector3(0f, 0.025f, 0f), Quaternion.identity, new Vector3(0.07f, 0.025f, 0.07f), handleMaterial);
            Box("Pointer", handle, new Vector3(0f, 0.051f, 0.022f), new Vector3(0.008f, 0.004f, 0.026f), accent);

            var collider = root.gameObject.AddComponent<SphereCollider>();
            collider.center = new Vector3(0f, 0.03f, 0f);
            collider.radius = 0.05f;

            var knob = root.gameObject.AddComponent<XRTwistKnob>();
            knob.Configure(handle);
            return knob;
        }

        static void Label(Transform parent, string text, Vector3 position)
        {
            var label = EnvironmentBuilder.Text(text, parent, position, text, 0.35f, new Color(0.7f, 0.76f, 0.82f), new Vector2(0.16f, 0.04f));
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>The menu on the left controller, angled toward the face, pressed with the right hand.</summary>
        static void BuildWristMenu(Transform origin, TeleopOperator teleopOperator, TutorialController tutorial)
        {
            Transform leftController = null;
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Left Controller")
                {
                    leftController = t;
                    break;
                }
            }

            if (leftController == null)
            {
                Debug.LogWarning("[Data Center] No 'Left Controller' under the XR Origin; wrist menu not built.");
                return;
            }

            var root = new GameObject("Wrist Menu").transform;
            root.SetParent(leftController, false);
            root.localPosition = new Vector3(0.02f, 0.06f, -0.04f);
            root.localRotation = Quaternion.Euler(-45f, 0f, 0f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            Box("Backing", visual, new Vector3(0f, 0f, 0.004f), new Vector3(0.2f, 0.21f, 0.004f), BuilderAssets.UnlitMaterial("VR_Panel", new Color(0.055f, 0.075f, 0.105f)));

            var layout = new (WristAction action, Vector2 position, Vector2 size)[]
            {
                (WristAction.SelectRobot1, new Vector2(-0.047f, 0.08f), new Vector2(0.088f, 0.032f)),
                (WristAction.SelectRobot2, new Vector2(0.047f, 0.08f), new Vector2(0.088f, 0.032f)),
                (WristAction.SelectRobot3, new Vector2(-0.047f, 0.042f), new Vector2(0.088f, 0.032f)),
                (WristAction.SelectRobot4, new Vector2(0.047f, 0.042f), new Vector2(0.088f, 0.032f)),
                (WristAction.ToggleMode, new Vector2(0f, 0.004f), new Vector2(0.182f, 0.032f)),
                (WristAction.CycleLink, new Vector2(0f, -0.034f), new Vector2(0.182f, 0.032f)),
                (WristAction.Tutorial, new Vector2(-0.047f, -0.072f), new Vector2(0.088f, 0.032f)),
                (WristAction.ResetEStop, new Vector2(0.047f, -0.072f), new Vector2(0.088f, 0.032f)),
            };

            var buttons = new List<WristMenu.Button>();
            foreach (var item in layout)
                buttons.Add(MenuButton(visual, item.action, item.position, item.size));

            NoShadows(visual.gameObject);
            var menu = root.gameObject.AddComponent<WristMenu>();
            menu.Configure(teleopOperator, tutorial, buttons, visual.gameObject);
            visual.gameObject.SetActive(false);
        }

        static WristMenu.Button MenuButton(Transform parent, WristAction action, Vector2 position, Vector2 size)
        {
            var root = new GameObject(action.ToString()).transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(position.x, position.y, 0f);

            var face = Box("Face", root, Vector3.zero, new Vector3(size.x, size.y, 0.008f), BuilderAssets.UnlitMaterial("VR_Button", Color.white));
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 0.03f);

            var label = EnvironmentBuilder.Text("Label", root, new Vector3(0f, 0f, -0.005f), action.ToString(), 0.16f, Color.white, new Vector2(size.x - 0.006f, size.y));
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.08f;
            label.fontSizeMax = 0.16f;

            var interactable = root.gameObject.AddComponent<XRSimpleInteractable>();
            return new WristMenu.Button { action = action, interactable = interactable, label = label, face = face.GetComponent<Renderer>() };
        }

        /// <summary>A big red mushroom-head E-Stop on a post beside the operator, as on a real cell.</summary>
        static void BuildEStopPedestal(Transform origin, TeleopOperator teleopOperator, TutorialController tutorial)
        {
            var root = new GameObject("E-Stop Pedestal").transform;
            root.SetParent(origin, false);
            root.localPosition = new Vector3(-0.45f, 0f, 0.25f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            Box("Post", visual, new Vector3(0f, 0.5f, 0f), new Vector3(0.08f, 1f, 0.08f), BuilderAssets.LitMaterial("EStop_Post", new Color(1f, 0.8f, 0.1f), 0f, 0.5f));
            Box("Housing", visual, new Vector3(0f, 1.03f, 0f), new Vector3(0.14f, 0.06f, 0.14f), BuilderAssets.LitMaterial("EStop_Housing", new Color(1f, 0.8f, 0.1f), 0f, 0.5f));

            var buttonRoot = new GameObject("EStop").transform;
            buttonRoot.SetParent(visual, false);
            buttonRoot.localPosition = new Vector3(0f, 1.08f, 0f);
            var head = Cylinder("Mushroom", buttonRoot, Vector3.zero, Quaternion.identity, new Vector3(0.11f, 0.02f, 0.11f), BuilderAssets.UnlitMaterial("VR_Button_EStop", new Color(0.84f, 0.11f, 0.09f)));
            var collider = buttonRoot.gameObject.AddComponent<SphereCollider>();
            collider.radius = 0.08f;
            var interactable = buttonRoot.gameObject.AddComponent<XRSimpleInteractable>();
            var label = EnvironmentBuilder.Text("Label", visual, new Vector3(0f, 1.03f, -0.072f), "E-STOP", 0.3f, new Color(0.08f, 0.08f, 0.08f), new Vector2(0.13f, 0.05f));

            NoShadows(visual.gameObject);
            var menu = root.gameObject.AddComponent<WristMenu>();
            menu.Configure(teleopOperator, tutorial, new List<WristMenu.Button>
            {
                new WristMenu.Button { action = WristAction.EStop, interactable = interactable, label = label, face = head.GetComponent<Renderer>() },
            }, visual.gameObject);
            visual.gameObject.SetActive(false);
        }
    }
}
