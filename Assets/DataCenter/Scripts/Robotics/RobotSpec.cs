using UnityEngine;

namespace DataCenter.Robotics
{
    /// <summary>
    /// Performance and safety limits for a robot model. Shared by every robot of that model.
    /// </summary>
    [CreateAssetMenu(menuName = "Data Center/Robot Spec", fileName = "RobotSpec")]
    public class RobotSpec : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Model name shown in the UI.")]
        public string modelName = "MR-6 Mobile Manipulator";

        [Header("Base")]
        [Tooltip("Top driving speed, in metres per second.")]
        public float maxLinearSpeed = 0.9f;

        [Tooltip("Top turning rate, in degrees per second.")]
        public float maxAngularSpeed = 90f;

        [Tooltip("Base acceleration, in metres per second squared.")]
        public float linearAcceleration = 1.5f;

        [Header("Arm")]
        [Tooltip("Arm reach from shoulder to wrist centre, in metres.")]
        public float armReach = 0.84f;

        [Tooltip("Payload limit, in kilograms. Informational.")]
        public float payloadKg = 5f;

        [Header("Safety")]
        [Tooltip("Commands older than this stop the robot, in seconds.")]
        public float watchdogTimeout = 0.3f;

        [Tooltip("Speed multiplier applied to base and arm when the gripper is near equipment.")]
        [Range(0.05f, 1f)]
        public float nearEquipmentSpeedScale = 0.35f;

        [Tooltip("Gripper-to-equipment distance at which the proximity warning turns yellow, in metres.")]
        public float warnDistance = 0.25f;

        [Tooltip("Gripper-to-equipment distance at which the proximity warning turns red, in metres.")]
        public float dangerDistance = 0.08f;

        [Header("Power")]
        [Tooltip("Battery drain while idle, in fraction per minute.")]
        public float idleDrainPerMinute = 0.002f;

        [Tooltip("Additional battery drain at full motion, in fraction per minute.")]
        public float motionDrainPerMinute = 0.02f;
    }
}
