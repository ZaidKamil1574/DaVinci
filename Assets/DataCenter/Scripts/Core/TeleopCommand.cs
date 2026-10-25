using System;
using UnityEngine;

namespace DataCenter
{
    /// <summary>What a <see cref="TeleopCommand"/> asks the robot to do.</summary>
    public enum CommandType : byte
    {
        /// <summary>No motion. Keeps the robot's watchdog fed and carries the deadman state.</summary>
        Heartbeat,

        /// <summary>Move the tool to <see cref="TeleopCommand.position"/> / <see cref="TeleopCommand.rotation"/>, in the robot's base frame.</summary>
        ArmTarget,

        /// <summary>
        /// Drive the base at <see cref="TeleopCommand.velocity"/> (x is turn, y is forward) and
        /// <see cref="TeleopCommand.strafe"/> (sideways), each -1 to 1.
        /// </summary>
        BaseVelocity,

        /// <summary>Navigate the base to the world point <see cref="TeleopCommand.position"/>.</summary>
        BaseGoal,

        /// <summary>Set the gripper to <see cref="TeleopCommand.value"/>: 0 open, 1 closed.</summary>
        Gripper,

        /// <summary>Latch the emergency stop.</summary>
        EStop,

        /// <summary>Release a latched emergency stop.</summary>
        ResetEStop,
    }

    /// <summary>
    /// One message from the operator station to a robot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single flat envelope rather than a class hierarchy, because it has to survive being
    /// serialized by things that are not C#: <see cref="JsonUtility"/> today, a WebSocket or a
    /// rosbridge <c>geometry_msgs/PoseStamped</c> tomorrow. Every field is a plain value, so the
    /// mapping to any wire format is mechanical.
    /// </para>
    /// <para>
    /// Arm targets are expressed in the robot's <b>base frame</b>, not the world. That is how a real
    /// arm controller receives them, and it means the arm holds its pose relative to the base while
    /// the base drives, instead of reaching back for a point in space it has left behind.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct TeleopCommand
    {
        public CommandType type;

        /// <summary>The robot this is addressed to.</summary>
        public string robotId;

        /// <summary>Monotonic per-operator counter. Lets the robot discard commands that arrive out of order.</summary>
        public uint sequence;

        /// <summary>Operator clock when sent, in seconds. Used to measure one-way latency on a loopback link.</summary>
        public double sentAt;

        /// <summary>Whether the operator is holding the deadman switch. Motion is only honoured while this is set.</summary>
        public bool deadman;

        public Vector3 position;
        public Quaternion rotation;
        public Vector2 velocity;

        /// <summary>BaseVelocity: sideways speed for an omnidirectional base, -1 (left) to 1 (right).</summary>
        public float strafe;
        public float value;

        public string ToJson() => JsonUtility.ToJson(this);

        public static TeleopCommand FromJson(string json) => JsonUtility.FromJson<TeleopCommand>(json);

        public override string ToString() => $"#{sequence} {type} -> {robotId}";
    }
}
