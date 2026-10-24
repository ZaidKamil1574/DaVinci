using UnityEngine;

namespace DataCenter.Teleop
{
    /// <summary>What a gripper input asked for this frame.</summary>
    public enum GripperIntent
    {
        None,
        Toggle,
        Close,
        Open,
    }

    /// <summary>
    /// Everything an operator asked for in one frame, independent of the device it came from.
    /// </summary>
    /// <remarks>
    /// Input sources only describe intent; <see cref="TeleopOperator"/> turns intent into
    /// commands. So a keyboard, a Quest controller and a future gamepad or hand-tracking source all
    /// produce the same thing, and safety rules are applied once rather than per device.
    /// </remarks>
    public struct TeleopIntent
    {
        /// <summary>Deadman held. Nothing moves without it.</summary>
        public bool deadman;

        /// <summary>Base drive: x is turn, y is forward, each -1 to 1.</summary>
        public Vector2 baseDrive;

        /// <summary>Whether <see cref="armPose"/> carries an absolute tool target, from a grab or drag.</summary>
        public bool hasArmPose;

        /// <summary>Absolute tool target in world space.</summary>
        public Pose armPose;

        /// <summary>Tool velocity in world space, in metres per second, from keys or a stick.</summary>
        public Vector3 armNudge;

        /// <summary>Wrist pitch rate about the tool's X axis, in degrees per second.</summary>
        public float wristPitchRate;

        /// <summary>Wrist roll rate about the tool's approach axis, in degrees per second.</summary>
        public float wristRollRate;

        public GripperIntent gripper;

        /// <summary>E-Stop pressed this frame.</summary>
        public bool eStop;

        /// <summary>E-Stop reset requested this frame.</summary>
        public bool resetEStop;

        /// <summary>Whether <see cref="navGoal"/> carries a point-and-click destination.</summary>
        public bool hasNavGoal;

        /// <summary>World-space navigation destination.</summary>
        public Vector3 navGoal;

        /// <summary>Snap the tool back to the robot's ready pose this frame.</summary>
        public bool homeArm;

        /// <summary>Take control of robot number N (1-based, in fleet order) this frame. 0 for none.</summary>
        public int selectRobot;

        /// <summary>Switch between Direct and Console control this frame.</summary>
        public bool toggleMode;
    }

    /// <summary>What an input source may need to know to interpret raw input.</summary>
    public struct TeleopContext
    {
        /// <summary>Camera the operator is looking through.</summary>
        public Camera camera;

        /// <summary>The currently commanded tool pose, in world space.</summary>
        public Pose commandedPose;

        /// <summary>The controlled robot's base, for robot-relative keys.</summary>
        public Transform robotBase;

        /// <summary>The grab handle at the commanded pose.</summary>
        public TargetHandle handle;
    }

    /// <summary>
    /// A device that produces <see cref="TeleopIntent"/>. Implemented by the XR controllers and by
    /// mouse and keyboard, so the operator logic never branches on platform.
    /// </summary>
    public interface ITeleopInput
    {
        /// <summary>Whether this source should be read right now, for example only while a headset is active.</summary>
        bool isActive { get; }

        /// <summary>Short name for the HUD, such as "Keyboard + Mouse" or "Quest Controllers".</summary>
        string displayName { get; }

        /// <summary>Adds this frame's input to <paramref name="intent"/>. Several sources may contribute.</summary>
        void Poll(ref TeleopIntent intent, in TeleopContext context);
    }
}
