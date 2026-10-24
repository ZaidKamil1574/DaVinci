using System;
using System.Collections.Generic;
using UnityEngine;
using DataCenter.Robotics;

namespace DataCenter.Teleop
{
    /// <summary>Which control scheme drives the arm and base.</summary>
    public enum ControlMode
    {
        /// <summary>Grab or drag the target handle; the arm follows.</summary>
        Direct,

        /// <summary>Virtual levers and knobs on a console, matching the Da Vinci surgeon console.</summary>
        Console,
    }

    /// <summary>
    /// The operator station. Reads every active <see cref="ITeleopInput"/>, keeps the commanded
    /// tool pose, applies the deadman rule, and streams <see cref="TeleopCommand"/>s to the selected
    /// robot through the <see cref="TeleopLink"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Commands are <b>state</b>, not events: every send repeats the full arm target, drive and
    /// gripper value. A lossy link then costs smoothness, never correctness — a dropped "close
    /// gripper" is repeated 33 ms later instead of being lost for good.
    /// </para>
    /// <para>
    /// The commanded pose is held in the robot's base frame, the same frame the robot receives, so
    /// it rides along when the base drives instead of being left behind in world space.
    /// </para>
    /// </remarks>
    public class TeleopOperator : MonoBehaviour
    {
        [SerializeField]
        TeleopLink m_Link;

        [SerializeField, Tooltip("Components implementing ITeleopInput. All active ones are read and merged.")]
        List<MonoBehaviour> m_InputSources = new List<MonoBehaviour>();

        [SerializeField]
        TargetHandle m_Handle;

        [SerializeField, Tooltip("Commands per second while active. Real teleop links run 20 to 100 Hz.")]
        float m_SendRate = 30f;

        [SerializeField, Tooltip("Extra distance beyond arm reach plus tool length the handle may be pulled, in metres.")]
        float m_HandleSlack = 0.05f;

        [SerializeField, Tooltip("Robot selected on start. Empty selects the first robot found.")]
        string m_InitialRobotId;

        readonly List<ITeleopInput> m_Inputs = new List<ITeleopInput>();
        TeleopRobot m_Robot;
        uint m_Sequence;
        double m_NextSendAt;
        Pose m_CommandedLocal;
        float m_GripperTarget;
        TeleopIntent m_Intent;
        ControlMode m_Mode;
        string m_ActiveInputName = "None";
        bool m_PendingNav;
        Vector3 m_PendingNavGoal;

        /// <summary>Raised after the controlled robot changes.</summary>
        public event Action<TeleopRobot> robotSelected;

        /// <summary>Raised after the control mode changes.</summary>
        public event Action<ControlMode> modeChanged;

        /// <summary>The robot being controlled, or null.</summary>
        public TeleopRobot robot => m_Robot;

        public TeleopLink link => m_Link;

        /// <summary>This frame's merged intent.</summary>
        public TeleopIntent intent => m_Intent;

        /// <summary>Whether the deadman is held this frame.</summary>
        public bool deadman => m_Intent.deadman;

        /// <summary>The active input source, for the HUD.</summary>
        public string activeInputName => m_ActiveInputName;

        public ControlMode mode => m_Mode;

        /// <summary>Commanded gripper position, 0 open to 1 closed.</summary>
        public float gripperTarget => m_GripperTarget;

        /// <summary>The commanded tool pose in world space. Where the ghost and the handle are drawn.</summary>
        public Pose commandedPose
        {
            get
            {
                if (m_Robot == null || m_Robot.arm == null)
                    return new Pose(transform.position, transform.rotation);

                var frame = m_Robot.arm.baseFrame;
                return new Pose(frame.TransformPoint(m_CommandedLocal.position), frame.rotation * m_CommandedLocal.rotation);
            }
        }

        /// <summary>Commands sent since start.</summary>
        public uint commandsSent => m_Sequence;

        /// <summary>Console mode drive, set by the console controls. Merged with input drive.</summary>
        public Vector2 consoleDrive { get; set; }

        /// <summary>Console mode tool velocity in robot frame, set by the console controls.</summary>
        public Vector3 consoleArmVelocity { get; set; }

        /// <summary>Console mode wrist roll rate, set by the console knob.</summary>
        public float consoleWristRollRate { get; set; }

        void Awake()
        {
            for (var i = 0; i < m_InputSources.Count; i++)
            {
                if (m_InputSources[i] is ITeleopInput input)
                    m_Inputs.Add(input);
            }
        }

        void Start()
        {
            var initial = string.IsNullOrEmpty(m_InitialRobotId) ? null : TeleopRobot.Find(m_InitialRobotId);
            if (initial == null && TeleopRobot.all.Count > 0)
                initial = TeleopRobot.all[0];

            Select(initial);
        }

        /// <summary>Adds an input source at runtime, such as a gamepad, a scripted test driver or a network operator.</summary>
        public void AddInputSource(ITeleopInput input)
        {
            if (input != null && !m_Inputs.Contains(input))
                m_Inputs.Add(input);
        }

        /// <summary>
        /// Asks for the selected robot to drive to <paramref name="worldPoint"/>, for example from a
        /// minimap click. Honoured on the next frame only if the deadman is held then.
        /// </summary>
        /// <returns>Whether the deadman is held now, so the caller can prompt if not.</returns>
        public bool RequestNavigation(Vector3 worldPoint)
        {
            m_PendingNav = true;
            m_PendingNavGoal = worldPoint;
            return m_Intent.deadman;
        }

        /// <summary>Removes an input source added with <see cref="AddInputSource"/>.</summary>
        public void RemoveInputSource(ITeleopInput input) => m_Inputs.Remove(input);

        /// <summary>Hands control to <paramref name="next"/>. The previous robot is told to hold.</summary>
        public void Select(TeleopRobot next)
        {
            if (next == m_Robot)
                return;

            if (m_Robot != null)
                Send(MakeCommand(CommandType.Heartbeat, false));

            m_Robot = next;
            ResyncToRobot();
            robotSelected?.Invoke(m_Robot);
        }

        /// <summary>Switches between direct and console control.</summary>
        public void SetMode(ControlMode mode)
        {
            if (mode == m_Mode)
                return;

            m_Mode = mode;
            modeChanged?.Invoke(mode);
            ToastEvent.Show(mode == ControlMode.Direct ? "Direct control: grab the handle" : "Console control: levers and knob", ToastLevel.Info);
        }

        /// <summary>Latches the selected robot's E-Stop over the hard-wired path, and over the link as backup.</summary>
        public void EmergencyStop()
        {
            if (m_Robot == null)
                return;

            m_Robot.EmergencyStop();
            Send(MakeCommand(CommandType.EStop, false));
        }

        /// <summary>Requests an E-Stop reset on the selected robot.</summary>
        public void ResetEmergencyStop()
        {
            if (m_Robot == null)
                return;

            if (m_Robot.ResetEmergencyStop())
            {
                m_Link.Flush();
                ResyncToRobot();
            }
        }

        void ResyncToRobot()
        {
            if (m_Robot == null || m_Robot.arm == null)
                return;

            m_CommandedLocal = m_Robot.arm.hasGoal
                ? new Pose(m_Robot.arm.goalPosition, m_Robot.arm.goalRotation)
                : m_Robot.arm.GetToolPoseLocal();
            m_GripperTarget = m_Robot.gripper != null && m_Robot.gripper.isClosing ? 1f : 0f;
        }

        void Update()
        {
            m_Intent = default;
            if (m_Robot == null)
                return;

            var frame = m_Robot.arm != null ? m_Robot.arm.baseFrame : m_Robot.transform;
            var context = new TeleopContext
            {
                camera = Camera.main,
                commandedPose = commandedPose,
                robotBase = frame,
                handle = m_Handle,
            };

            m_ActiveInputName = "None";
            for (var i = 0; i < m_Inputs.Count; i++)
            {
                if (!m_Inputs[i].isActive)
                    continue;

                m_Inputs[i].Poll(ref m_Intent, context);
                m_ActiveInputName = m_Inputs[i].displayName;
            }

            if (m_PendingNav)
            {
                m_PendingNav = false;
                m_Intent.hasNavGoal = true;
                m_Intent.navGoal = m_PendingNavGoal;
            }

            if (m_Intent.selectRobot > 0 && m_Intent.selectRobot <= TeleopRobot.all.Count)
            {
                Select(TeleopRobot.all[m_Intent.selectRobot - 1]);
                if (m_Robot == null)
                    return;
                frame = m_Robot.arm != null ? m_Robot.arm.baseFrame : m_Robot.transform;
            }

            if (m_Intent.toggleMode)
                SetMode(m_Mode == ControlMode.Direct ? ControlMode.Console : ControlMode.Direct);

            if (m_Intent.eStop)
                EmergencyStop();

            if (m_Intent.resetEStop)
                ResetEmergencyStop();

            var motionEnabled = m_Intent.deadman && !m_Robot.isEStopped;

            // While motion is disabled the command tracks where the arm really is, so the handle
            // shows where to grab and re-enabling can never yank the arm toward a stale goal left
            // over from an unreachable target, an E-Stop or a watchdog stop.
            if (!motionEnabled && m_Robot.arm != null && (m_Handle == null || !m_Handle.isGrabbed))
                m_CommandedLocal = m_Robot.arm.GetToolPoseLocal();

            if (motionEnabled)
                ApplyArmIntent(frame, Time.deltaTime);

            switch (m_Intent.gripper)
            {
                case GripperIntent.Toggle:
                    m_GripperTarget = m_GripperTarget > 0.5f ? 0f : 1f;
                    break;
                case GripperIntent.Close:
                    m_GripperTarget = 1f;
                    break;
                case GripperIntent.Open:
                    m_GripperTarget = 0f;
                    break;
            }

            if (motionEnabled && m_Intent.hasNavGoal)
            {
                var goal = MakeCommand(CommandType.BaseGoal, true);
                goal.position = m_Intent.navGoal;
                Send(goal);
            }

            var now = TeleopLink.now;
            if (now >= m_NextSendAt)
            {
                m_NextSendAt = now + 1.0 / Mathf.Max(1f, m_SendRate);
                StreamState(motionEnabled);
            }

            if (m_Handle != null)
                m_Handle.Show(commandedPose, motionEnabled, m_Robot.arm != null && m_Robot.arm.goalClamped);
        }

        void ApplyArmIntent(Transform frame, float deltaTime)
        {
            if (m_Intent.homeArm)
                m_CommandedLocal = new Pose(m_Robot.readyPosition, m_Robot.readyRotation);

            if (m_Intent.hasArmPose)
            {
                m_CommandedLocal = new Pose(
                    frame.InverseTransformPoint(m_Intent.armPose.position),
                    Quaternion.Inverse(frame.rotation) * m_Intent.armPose.rotation);
            }

            var velocity = frame.InverseTransformDirection(m_Intent.armNudge);
            if (m_Mode == ControlMode.Console)
                velocity += consoleArmVelocity;
            m_CommandedLocal.position += velocity * deltaTime;

            var roll = m_Intent.wristRollRate + (m_Mode == ControlMode.Console ? consoleWristRollRate : 0f);
            var pitch = m_Intent.wristPitchRate;
            if (Mathf.Abs(roll) > 0.01f || Mathf.Abs(pitch) > 0.01f)
                m_CommandedLocal.rotation *= Quaternion.Euler(pitch * deltaTime, 0f, roll * deltaTime);

            m_CommandedLocal.position = ClampToWorkspace(m_CommandedLocal.position);
        }

        /// <summary>
        /// Keeps the commanded tool point within reach of the shoulder, plus a little slack so the
        /// handle visibly strains against the limit rather than stopping dead.
        /// </summary>
        Vector3 ClampToWorkspace(Vector3 local)
        {
            var arm = m_Robot.arm;
            if (arm == null || arm.joints.Count < 2 || arm.joints[1].joint == null)
                return local;

            var shoulder = arm.baseFrame.InverseTransformPoint(arm.joints[1].joint.position);
            var toolLength = Vector3.Distance(arm.toolTip.position, arm.joints[arm.joints.Count - 1].joint.position);
            var limit = m_Robot.spec.armReach + toolLength + m_HandleSlack;

            var offset = local - shoulder;
            if (offset.magnitude > limit)
                local = shoulder + offset.normalized * limit;

            // Never below the deck or through the floor.
            local.y = Mathf.Max(local.y, 0.25f);
            return local;
        }

        void StreamState(bool motionEnabled)
        {
            if (!motionEnabled)
            {
                // Report the switch as it really is, even when motion is blocked. The robot refuses
                // an E-Stop reset while the deadman is held, and can only do that if it is told.
                Send(MakeCommand(CommandType.Heartbeat, m_Intent.deadman));
                return;
            }

            var arm = MakeCommand(CommandType.ArmTarget, true);
            arm.position = m_CommandedLocal.position;
            arm.rotation = m_CommandedLocal.rotation;
            Send(arm);

            var drive = MakeCommand(CommandType.BaseVelocity, true);
            drive.velocity = Vector2.ClampMagnitude(m_Intent.baseDrive + (m_Mode == ControlMode.Console ? consoleDrive : Vector2.zero), 1.4142f);
            Send(drive);

            var grip = MakeCommand(CommandType.Gripper, true);
            grip.value = m_GripperTarget;
            Send(grip);
        }

        TeleopCommand MakeCommand(CommandType type, bool deadmanHeld) => new TeleopCommand
        {
            type = type,
            robotId = m_Robot != null ? m_Robot.robotId : string.Empty,
            sequence = ++m_Sequence,
            sentAt = TeleopLink.now,
            deadman = deadmanHeld,
            rotation = Quaternion.identity,
        };

        void Send(TeleopCommand command)
        {
            if (m_Link != null && m_Link.transport != null && !string.IsNullOrEmpty(command.robotId))
                m_Link.transport.Send(command);
        }

        /// <summary>Wires the operator from code. Used by the scene builder.</summary>
        public void Configure(TeleopLink link, List<MonoBehaviour> inputs, TargetHandle handle, string initialRobotId)
        {
            m_Link = link;
            m_InputSources = inputs;
            m_Handle = handle;
            m_InitialRobotId = initialRobotId;
        }
    }
}
