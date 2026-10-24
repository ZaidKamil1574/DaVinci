using System.Collections.Generic;
using UnityEngine;

namespace DataCenter.Robotics
{
    /// <summary>
    /// Drives a chain of <see cref="RevoluteJoint"/>s so the tool reaches a goal pose, with
    /// smoothing, joint limits and per-joint speed limits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generalised from the Da Vinci project's <c>DaVinciArm</c>. That arm solved in two priorities
    /// split at the remote centre of motion: proximal joints pinned the cannula, distal joints aimed
    /// the instrument, and the second solve could not disturb the first. This arm uses the same
    /// split for a different reason. A six-axis industrial arm has a <i>spherical wrist</i> — the
    /// last three axes meet at one point — so the first three joints alone decide where that point
    /// is, and the last three alone decide which way the tool faces. Solving position on the
    /// proximal set and orientation on the distal set is therefore exact, not an approximation.
    /// </para>
    /// <para>
    /// The goal is held in the frame of <see cref="baseFrame"/>, matching
    /// <see cref="TeleopCommand.position"/>, so a goal stays put relative to the robot as it drives.
    /// </para>
    /// <para>
    /// Each frame the solver runs from the current pose, and the result is then rate limited per
    /// joint. The arm therefore moves like a servo-driven machine rather than snapping, and an
    /// unreachable goal leaves it reaching as far as its limits allow instead of thrashing.
    /// </para>
    /// </remarks>
    public class ArmIKController : MonoBehaviour
    {
        [SerializeField, Tooltip("Joints from the base outward, in chain order.")]
        List<RevoluteJoint> m_Joints = new List<RevoluteJoint>();

        [SerializeField, Tooltip("Number of joints, from the base, that position the wrist centre. The rest orient the tool.")]
        int m_WristJointIndex = 3;

        [SerializeField, Tooltip("Frame the goal is expressed in. Usually the robot's root.")]
        Transform m_BaseFrame;

        [SerializeField, Tooltip("Point where the wrist axes meet. Must be a child of the first wrist joint, at its pivot.")]
        Transform m_WristCentre;

        [SerializeField, Tooltip("Tool centre point. Forward (Z) is the approach direction a held connector is inserted along.")]
        Transform m_ToolTip;

        [Header("Solver")]
        [SerializeField, Range(1, 32), Tooltip("Solver passes per frame for each of position and orientation.")]
        int m_Iterations = 10;

        [SerializeField, Tooltip("Position tolerance, in metres.")]
        float m_Tolerance = 0.0005f;

        [SerializeField, Tooltip("Orientation tolerance, in degrees.")]
        float m_AngleTolerance = 0.25f;

        [SerializeField, Tooltip("How quickly the internal goal chases the commanded goal. Lower feels heavier.")]
        float m_FollowSharpness = 10f;

        [SerializeField, Tooltip("Distance from the shoulder the wrist centre may be asked to reach, in metres. Goals beyond it are pulled in.")]
        float m_MaxReach = 0.84f;

        readonly List<RevoluteJoint> m_PositionJoints = new List<RevoluteJoint>();
        readonly List<RevoluteJoint> m_OrientationJoints = new List<RevoluteJoint>();
        float[] m_PreviousAngles;
        float[] m_JointSpeeds;

        Vector3 m_GoalPosition;
        Quaternion m_GoalRotation = Quaternion.identity;
        Vector3 m_SmoothedPosition;
        Quaternion m_SmoothedRotation = Quaternion.identity;
        bool m_HasGoal;
        Vector3 m_ToolOffsetFromWrist;

        /// <summary>All joints, base outward.</summary>
        public IReadOnlyList<RevoluteJoint> joints => m_Joints;

        /// <summary>The frame goals are expressed in.</summary>
        public Transform baseFrame => m_BaseFrame != null ? m_BaseFrame : transform;

        /// <summary>Tool centre point. Forward is the approach direction.</summary>
        public Transform toolTip => m_ToolTip;

        /// <summary>Whether the arm is currently tracking a goal rather than holding still.</summary>
        public bool hasGoal => m_HasGoal;

        /// <summary>The commanded goal position, in base frame.</summary>
        public Vector3 goalPosition => m_GoalPosition;

        /// <summary>The commanded goal rotation, in base frame.</summary>
        public Quaternion goalRotation => m_GoalRotation;

        /// <summary>Distance from the tool to the goal after the last solve, in metres.</summary>
        public float positionError { get; private set; }

        /// <summary>Angle between the tool and the goal after the last solve, in degrees.</summary>
        public float orientationError { get; private set; }

        /// <summary>Fastest any joint turned in the last tick, in degrees per second.</summary>
        public float peakJointSpeed { get; private set; }

        /// <summary>Whether the last goal was outside the workspace and had to be pulled in.</summary>
        public bool goalClamped { get; private set; }

        /// <summary>Current speed of joint <paramref name="index"/>, in degrees per second.</summary>
        public float JointSpeed(int index) => m_JointSpeeds != null && index < m_JointSpeeds.Length ? m_JointSpeeds[index] : 0f;

        void Awake()
        {
            CaptureRestPose(false);
            RebuildChains();
        }

        void OnValidate()
        {
            m_WristJointIndex = Mathf.Clamp(m_WristJointIndex, 0, m_Joints.Count);
            if (Application.isPlaying)
                RebuildChains();
        }

        /// <summary>
        /// Wires the arm up from code. Used by the procedural robot builder; a hand-authored rig
        /// assigns the same fields in the inspector instead.
        /// </summary>
        public void Configure(List<RevoluteJoint> joints, int wristJointIndex, Transform baseFrame, Transform wristCentre, Transform toolTip, float maxReach)
        {
            m_Joints = joints;
            m_WristJointIndex = wristJointIndex;
            m_BaseFrame = baseFrame;
            m_WristCentre = wristCentre;
            m_ToolTip = toolTip;
            m_MaxReach = maxReach;
            CaptureRestPose(true);
            RebuildChains();
        }

        /// <summary>Records the current pose as every joint's zero. See <see cref="RevoluteJoint.CaptureRest"/>.</summary>
        public void CaptureRestPose(bool force = true)
        {
            for (var i = 0; i < m_Joints.Count; i++)
            {
                if (force || !m_Joints[i].restCaptured)
                    m_Joints[i].CaptureRest();
            }
        }

        /// <summary>Splits the chain at the wrist.</summary>
        public void RebuildChains()
        {
            m_PositionJoints.Clear();
            m_OrientationJoints.Clear();

            for (var i = 0; i < m_Joints.Count; i++)
            {
                if (!m_Joints[i].isValid)
                    continue;

                if (i < m_WristJointIndex)
                    m_PositionJoints.Add(m_Joints[i]);
                else
                    m_OrientationJoints.Add(m_Joints[i]);
            }

            m_PreviousAngles = new float[m_Joints.Count];
            m_JointSpeeds = new float[m_Joints.Count];

            // Constant for a spherical wrist: rotating the wrist joints moves the tool around the
            // wrist centre without changing this offset in the tool's own frame.
            if (m_WristCentre != null && m_ToolTip != null)
                m_ToolOffsetFromWrist = Quaternion.Inverse(m_ToolTip.rotation) * (m_WristCentre.position - m_ToolTip.position);
        }

        /// <summary>Sets the goal the tool should reach, in <see cref="baseFrame"/> space.</summary>
        public void SetGoal(Vector3 localPosition, Quaternion localRotation)
        {
            if (!m_HasGoal)
            {
                // Start smoothing from where the tool actually is, so taking control never jerks the arm.
                m_SmoothedPosition = baseFrame.InverseTransformPoint(m_ToolTip.position);
                m_SmoothedRotation = Quaternion.Inverse(baseFrame.rotation) * m_ToolTip.rotation;
            }

            m_GoalPosition = localPosition;
            m_GoalRotation = localRotation;
            m_HasGoal = true;
        }

        /// <summary>Stops tracking and freezes the arm where it is.</summary>
        public void Hold()
        {
            m_HasGoal = false;
            for (var i = 0; i < m_JointSpeeds.Length; i++)
                m_JointSpeeds[i] = 0f;
            peakJointSpeed = 0f;
        }

        /// <summary>The tool's current pose in <see cref="baseFrame"/> space.</summary>
        public Pose GetToolPoseLocal() => new Pose(
            baseFrame.InverseTransformPoint(m_ToolTip.position),
            Quaternion.Inverse(baseFrame.rotation) * m_ToolTip.rotation);

        /// <summary>
        /// Advances the arm one step toward its goal.
        /// </summary>
        /// <param name="deltaTime">Seconds since the last tick.</param>
        /// <param name="speedScale">Multiplier on every joint's speed limit, for example a safety cap near racks.</param>
        public void Tick(float deltaTime, float speedScale)
        {
            if (!m_HasGoal || m_ToolTip == null || deltaTime <= 0f)
            {
                peakJointSpeed = 0f;
                return;
            }

            var t = 1f - Mathf.Exp(-m_FollowSharpness * deltaTime);
            m_SmoothedPosition = Vector3.Lerp(m_SmoothedPosition, m_GoalPosition, t);
            m_SmoothedRotation = Quaternion.Slerp(m_SmoothedRotation, m_GoalRotation, t);

            var frame = baseFrame;
            var targetPosition = frame.TransformPoint(m_SmoothedPosition);
            var targetRotation = frame.rotation * m_SmoothedRotation;

            for (var i = 0; i < m_Joints.Count; i++)
                m_PreviousAngles[i] = m_Joints[i].angle;

            // Priority one: put the wrist centre where the tool needs it. Priority two: turn the
            // wrist. With a spherical wrist the second cannot move what the first placed.
            var wristTarget = targetPosition + targetRotation * m_ToolOffsetFromWrist;
            wristTarget = ClampToReach(wristTarget);

            if (m_WristCentre != null && m_PositionJoints.Count > 0)
                CcdSolver.Solve(m_PositionJoints, m_WristCentre, wristTarget, m_Iterations, m_Tolerance);

            if (m_OrientationJoints.Count > 0)
                CcdSolver.SolveOrientation(m_OrientationJoints, m_ToolTip, targetRotation, m_Iterations, m_AngleTolerance);

            // Rate limit what the solver asked for. The solver is free to jump; the motors are not.
            var peak = 0f;
            for (var i = 0; i < m_Joints.Count; i++)
            {
                var joint = m_Joints[i];
                var maxStep = joint.maxSpeed * speedScale * deltaTime;
                var step = Mathf.Clamp(joint.angle - m_PreviousAngles[i], -maxStep, maxStep);
                joint.angle = m_PreviousAngles[i] + step;

                m_JointSpeeds[i] = Mathf.Abs(step) / deltaTime;
                peak = Mathf.Max(peak, m_JointSpeeds[i]);
            }

            peakJointSpeed = peak;
            positionError = Vector3.Distance(m_ToolTip.position, frame.TransformPoint(m_GoalPosition));
            orientationError = Quaternion.Angle(m_ToolTip.rotation, frame.rotation * m_GoalRotation);
        }

        /// <summary>
        /// Pulls a wrist target inside the sphere the shoulder can reach. Without this an
        /// out-of-reach goal straightens the arm into its singularity and the wrist starts to spin.
        /// </summary>
        Vector3 ClampToReach(Vector3 wristTarget)
        {
            goalClamped = false;
            if (m_PositionJoints.Count < 2)
                return wristTarget;

            var shoulder = m_PositionJoints[1].joint.position;
            var offset = wristTarget - shoulder;
            if (offset.magnitude <= m_MaxReach)
                return wristTarget;

            goalClamped = true;
            return shoulder + offset.normalized * m_MaxReach;
        }

        void OnDrawGizmosSelected()
        {
            for (var i = 0; i < m_Joints.Count; i++)
            {
                var joint = m_Joints[i];
                if (joint.joint == null)
                    continue;

                Gizmos.color = i < m_WristJointIndex ? new Color(0.3f, 0.7f, 1f) : new Color(1f, 0.6f, 0.2f);
                var pivot = joint.joint.position;
                var axis = joint.joint.TransformDirection(joint.localAxis);
                Gizmos.DrawLine(pivot - axis * 0.08f, pivot + axis * 0.08f);
                Gizmos.DrawSphere(pivot, 0.012f);
            }

            if (m_HasGoal)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(baseFrame.TransformPoint(m_GoalPosition), 0.02f);
            }
        }
    }
}
