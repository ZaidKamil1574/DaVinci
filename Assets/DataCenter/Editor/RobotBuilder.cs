using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using DataCenter.Robotics;

namespace DataCenter.Editor
{
    /// <summary>
    /// Builds the mobile manipulator prefab from primitives: a differential-drive base, a mast,
    /// and a six-axis arm with a UR5e-like joint layout and a spherical wrist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Joint transforms and visuals are kept apart. Each joint is an unscaled empty that the IK
    /// rotates; the boxes and cylinders that draw it hang underneath as leaves in a child called
    /// "Visual". A better model can replace the visuals without touching the kinematics.
    /// </para>
    /// <para>
    /// Rest pose (every joint at zero) is the arm pointing straight up, which makes every axis
    /// easy to reason about. The builder then sets a tucked "ready" pose for the saved prefab.
    /// </para>
    /// </remarks>
    static class RobotBuilder
    {
        public const string PrefabPath = BuilderAssets.PrefabsFolder + "/Robot_MR6.prefab";

        // Geometry, in metres. Base origin is on the floor at the centre of the wheelbase.
        const float k_DeckHeight = 0.40f;
        const float k_MastTop = 1.15f;
        const float k_MastForward = 0.15f;
        const float k_ShoulderRise = 0.15f;
        const float k_UpperArm = 0.45f;
        const float k_Forearm = 0.40f;
        const float k_ToolLength = 0.20f;
        const float k_WheelRadius = 0.08f;

        public static GameObject Build(RobotSpec spec)
        {
            var white = BuilderAssets.LitMaterial("Robot_Body", new Color(0.88f, 0.89f, 0.9f), 0f, 0.55f);
            var accent = BuilderAssets.LitMaterial("Robot_Accent", new Color(1f, 0.42f, 0.08f), 0f, 0.5f);
            var dark = BuilderAssets.LitMaterial("Robot_Dark", new Color(0.12f, 0.13f, 0.15f), 0.3f, 0.45f);
            var metal = BuilderAssets.LitMaterial("Robot_Metal", new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.6f);
            var rubber = BuilderAssets.LitMaterial("Robot_Rubber", new Color(0.05f, 0.05f, 0.05f), 0f, 0.2f);
            var lens = BuilderAssets.LedMaterial("Robot_StatusLight", new Color(0.1f, 0.85f, 1f), false, 1.4f);

            var root = new GameObject("Robot_MR6");
            root.layer = BuilderAssets.Layer(BuilderAssets.RobotLayer);

            // ---------- Base
            var baseVisual = Child("Visual", root.transform, Vector3.zero);
            Box("Chassis", baseVisual, new Vector3(0f, 0.24f, 0f), new Vector3(0.56f, 0.30f, 0.80f), white);
            Box("Bumper Front", baseVisual, new Vector3(0f, 0.14f, 0.41f), new Vector3(0.58f, 0.08f, 0.04f), dark);
            Box("Bumper Rear", baseVisual, new Vector3(0f, 0.14f, -0.41f), new Vector3(0.58f, 0.08f, 0.04f), dark);
            Box("Accent Stripe", baseVisual, new Vector3(0f, 0.33f, 0f), new Vector3(0.565f, 0.03f, 0.805f), accent);
            Box("Deck", baseVisual, new Vector3(0f, k_DeckHeight - 0.01f, 0f), new Vector3(0.52f, 0.02f, 0.76f), dark);
            Box("Lidar", baseVisual, new Vector3(0f, k_DeckHeight + 0.03f, 0.3f), new Vector3(0.09f, 0.06f, 0.09f), dark);
            Box("Status Light", baseVisual, new Vector3(0f, 0.30f, 0.402f), new Vector3(0.3f, 0.015f, 0.005f), lens);
            Box("Status Light Rear", baseVisual, new Vector3(0f, 0.30f, -0.402f), new Vector3(0.3f, 0.015f, 0.005f), lens);

            var wheels = new List<Transform>();
            foreach (var corner in new[] { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1) })
            {
                var pivot = Child($"Wheel {wheels.Count + 1}", root.transform, new Vector3(corner.x * 0.29f, k_WheelRadius, corner.y * 0.27f));
                Cylinder("Tyre", pivot, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(k_WheelRadius * 2f, 0.025f, k_WheelRadius * 2f), rubber);
                Box("Hub Mark", pivot, new Vector3(corner.x * 0.026f, 0.04f, 0f), new Vector3(0.004f, 0.03f, 0.012f), metal);
                wheels.Add(pivot);
            }

            // Mast: carries the arm high enough to reach the top of a 42U rack.
            var mastHeight = k_MastTop - k_DeckHeight;
            Box("Mast", baseVisual, new Vector3(0f, k_DeckHeight + mastHeight * 0.5f, k_MastForward), new Vector3(0.16f, mastHeight, 0.16f), white);
            Box("Mast Accent", baseVisual, new Vector3(0f, k_DeckHeight + mastHeight * 0.5f, k_MastForward + 0.081f), new Vector3(0.05f, mastHeight * 0.8f, 0.004f), accent);
            Box("Controller Box", baseVisual, new Vector3(0f, k_DeckHeight + 0.11f, -0.2f), new Vector3(0.36f, 0.22f, 0.26f), dark);

            // ---------- Arm. Each joint is an empty; visuals are leaves.
            var j1 = Child("J1 Base", root.transform, new Vector3(0f, k_MastTop, k_MastForward));
            Cylinder("J1 Housing", j1, new Vector3(0f, 0.05f, 0f), Quaternion.identity, new Vector3(0.15f, 0.05f, 0.15f), white);
            Cylinder("J1 Ring", j1, new Vector3(0f, 0.105f, 0f), Quaternion.identity, new Vector3(0.152f, 0.006f, 0.152f), accent);

            var j2 = Child("J2 Shoulder", j1, new Vector3(0f, k_ShoulderRise, 0f));
            Cylinder("J2 Housing", j2, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.14f, 0.09f, 0.14f), white);
            Cylinder("J2 Cap", j2, new Vector3(0.092f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.11f, 0.004f, 0.11f), dark);
            Box("Upper Arm", j2, new Vector3(0f, k_UpperArm * 0.5f, 0f), new Vector3(0.085f, k_UpperArm - 0.08f, 0.085f), white);

            var j3 = Child("J3 Elbow", j2, new Vector3(0f, k_UpperArm, 0f));
            Cylinder("J3 Housing", j3, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.115f, 0.075f, 0.115f), white);
            Cylinder("J3 Cap", j3, new Vector3(0.077f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.09f, 0.004f, 0.09f), accent);
            Box("Forearm", j3, new Vector3(0f, k_Forearm * 0.5f, 0f), new Vector3(0.07f, k_Forearm - 0.07f, 0.07f), white);

            // Spherical wrist: J4, J5 and J6 share one pivot.
            var j4 = Child("J4 Wrist 1", j3, new Vector3(0f, k_Forearm, 0f));
            Cylinder("J4 Housing", j4, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.085f, 0.055f, 0.085f), white);
            var wristCentre = Child("Wrist Centre", j3, new Vector3(0f, k_Forearm, 0f));

            var j5 = Child("J5 Wrist 2", j4, Vector3.zero);
            Cylinder("J5 Housing", j5, new Vector3(0f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.075f, 0.05f, 0.075f), dark);

            var j6 = Child("J6 Wrist 3", j5, Vector3.zero);
            Cylinder("J6 Flange", j6, new Vector3(0f, 0.055f, 0f), Quaternion.identity, new Vector3(0.07f, 0.012f, 0.07f), metal);

            // ---------- Gripper, on J6. Tool axis is J6's +Y; the tool frame's +Z is aligned to it.
            var toolRotation = Quaternion.Euler(-90f, 0f, 0f);
            var gripperRoot = Child("Gripper", j6, Vector3.zero);
            Box("Gripper Body", gripperRoot, new Vector3(0f, 0.095f, 0f), new Vector3(0.095f, 0.06f, 0.05f), dark);
            Box("Gripper Accent", gripperRoot, new Vector3(0f, 0.095f, 0.026f), new Vector3(0.06f, 0.012f, 0.002f), accent);

            var leftFinger = Child("Finger L", gripperRoot, new Vector3(-0.035f, 0.16f, 0f));
            Box("Pad", leftFinger, Vector3.zero, new Vector3(0.012f, 0.07f, 0.022f), metal);
            var rightFinger = Child("Finger R", gripperRoot, new Vector3(0.035f, 0.16f, 0f));
            Box("Pad", rightFinger, Vector3.zero, new Vector3(0.012f, 0.07f, 0.022f), metal);

            var toolTip = Child("Tool Tip", j6, new Vector3(0f, k_ToolLength, 0f));
            toolTip.localRotation = toolRotation;
            var attach = Child("Attach Point", j6, new Vector3(0f, k_ToolLength - 0.012f, 0f));
            attach.localRotation = toolRotation;

            // Wrist camera, looking down the tool axis past the fingers.
            var cameraMount = Child("Wrist Camera", gripperRoot, new Vector3(0f, 0.1f, 0.045f));
            cameraMount.localRotation = toolRotation * Quaternion.Euler(8f, 0f, 0f);
            Box("Camera Body", gripperRoot, new Vector3(0f, 0.1f, 0.036f), new Vector3(0.03f, 0.02f, 0.02f), dark);
            var wristCamera = cameraMount.gameObject.AddComponent<Camera>();
            wristCamera.fieldOfView = 70f;
            wristCamera.nearClipPlane = 0.02f;
            wristCamera.farClipPlane = 8f;
            wristCamera.enabled = false;
            wristCamera.tag = "Untagged";

            // ---------- Components
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f;
            agent.height = 1.6f;
            agent.speed = spec.maxLinearSpeed;
            agent.angularSpeed = spec.maxAngularSpeed * 2f;
            agent.acceleration = spec.linearAcceleration;
            agent.stoppingDistance = 0.05f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            var body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.3f, 0f);
            body.size = new Vector3(0.58f, 0.6f, 0.84f);

            var mobileBase = root.AddComponent<MobileBase>();
            mobileBase.Configure(wheels.ToArray(), k_WheelRadius);

            var arm = root.AddComponent<ArmIKController>();
            var joints = new List<RevoluteJoint>
            {
                new RevoluteJoint(j1, Vector3.up, -175f, 175f, 90f, 1f),
                new RevoluteJoint(j2, Vector3.right, -60f, 110f, 70f, 0.8f),
                // Elbow bends one way only, so the solver never flips between elbow-up and elbow-down.
                new RevoluteJoint(j3, Vector3.right, -5f, 160f, 90f, 1f),
                new RevoluteJoint(j4, Vector3.right, -150f, 150f, 150f, 1f),
                new RevoluteJoint(j5, Vector3.forward, -120f, 120f, 150f, 1f),
                new RevoluteJoint(j6, Vector3.up, -180f, 180f, 180f, 1f),
            };
            arm.Configure(joints, 3, root.transform, wristCentre, toolTip, spec.armReach);

            // Tucked ready pose, so the prefab looks right in the Editor before anything runs.
            joints[1].angle = -10f;
            joints[2].angle = 140f;
            joints[3].angle = -40f;

            var gripper = root.AddComponent<Gripper>();
            gripper.Configure(leftFinger, rightFinger, attach, BuilderAssets.Mask(BuilderAssets.CableLayer));

            var robot = root.AddComponent<TeleopRobot>();
            robot.Configure("R-A1", "Atlas", "A", spec, null, arm, mobileBase, gripper, wristCamera);

            // Speed cap and proximity warnings near equipment.
            var sensor = root.AddComponent<ProximitySensor>();
            sensor.Configure(BuilderAssets.Mask(BuilderAssets.EquipmentLayer));

            SetLayerRecursively(root, BuilderAssets.Layer(BuilderAssets.RobotLayer));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------------------------------------------------------------- Helpers shared with the environment builder

        public static Transform Child(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!keepCollider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Cylinder(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
