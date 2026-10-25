using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataCenter.Robotics
{
    /// <summary>What a robot is doing, as reported to the operator.</summary>
    public enum RobotState
    {
        Idle,
        Moving,
        Manipulating,
        Fault,
        EStopped,
    }

    /// <summary>
    /// The robot-side controller. Receives <see cref="TeleopCommand"/>s from the
    /// <see cref="TeleopLink"/>, enforces safety, and drives the base, arm and gripper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Safety is enforced here, on the robot, not at the operator station. A real robot cannot
    /// trust its link: if commands stop arriving it must stop by itself. So the watchdog, deadman
    /// gating and E-Stop latch all live on this side of the transport.
    /// </para>
    /// <para>
    /// The E-Stop is deliberately <b>not</b> sent through the simulated network. Physical E-Stops
    /// are hard-wired so latency and packet loss cannot delay them; <see cref="EmergencyStop"/>
    /// models that by acting immediately. An <see cref="CommandType.EStop"/> command over the link
    /// is still honoured, as a second path.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public class TeleopRobot : MonoBehaviour
    {
        static readonly List<TeleopRobot> s_All = new List<TeleopRobot>();

        [SerializeField, Tooltip("Unique id commands are addressed to.")]
        string m_RobotId = "R-A1";

        [SerializeField, Tooltip("Name shown in the UI.")]
        string m_DisplayName = "Atlas";

        [SerializeField, Tooltip("Section this robot is stationed in, for example \"A\".")]
        string m_SectionId = "A";

        [SerializeField]
        RobotSpec m_Spec;

        [SerializeField]
        TeleopLink m_Link;

        [SerializeField]
        ArmIKController m_Arm;

        [SerializeField]
        MobileBase m_Base;

        [SerializeField]
        Gripper m_Gripper;

        [SerializeField, Tooltip("Wrist camera, if fitted. Rendering is managed by the UI layer.")]
        Camera m_WristCamera;

        [SerializeField, Range(0f, 1f), Tooltip("Starting charge.")]
        float m_Battery = 0.86f;

        readonly uint[] m_LastSequence = new uint[Enum.GetValues(typeof(CommandType)).Length];
        RobotState m_State;
        double m_LastCommandAt = double.NegativeInfinity;
        bool m_Deadman;
        bool m_EStopLatched;
        bool m_WatchdogTripped;
        bool m_HasEverReceived;
        float m_SpeedScale = 1f;
        string m_CurrentTask = "Standby";

        /// <summary>Every enabled robot, in enable order. Use this instead of searching the scene.</summary>
        public static IReadOnlyList<TeleopRobot> all => s_All;

        /// <summary>Raised when an enabled robot is added to or removed from <see cref="all"/>.</summary>
        public static event Action registryChanged;

        /// <summary>Raised whenever <see cref="state"/> changes.</summary>
        public event Action<TeleopRobot> stateChanged;

        public string robotId => m_RobotId;
        public string displayName => m_DisplayName;
        public string sectionId => m_SectionId;
        public RobotSpec spec => m_Spec;
        public ArmIKController arm => m_Arm;
        public MobileBase mobileBase => m_Base;
        public Gripper gripper => m_Gripper;
        public Camera wristCamera => m_WristCamera;
        public RobotState state => m_State;

        /// <summary>Remaining charge, 0 to 1.</summary>
        public float battery => m_Battery;

        /// <summary>Whether the E-Stop is latched. Only <see cref="ResetEmergencyStop"/> clears it.</summary>
        public bool isEStopped => m_EStopLatched;

        /// <summary>Whether the watchdog has stopped the robot for lack of fresh commands.</summary>
        public bool watchdogTripped => m_WatchdogTripped;

        /// <summary>Deadman state from the most recent command.</summary>
        public bool deadmanHeld => m_Deadman;

        /// <summary>Age of the most recent command, in seconds.</summary>
        public float commandAge => m_HasEverReceived ? (float)(TeleopLink.now - m_LastCommandAt) : float.PositiveInfinity;

        /// <summary>Current speed multiplier from safety systems, 0 to 1.</summary>
        public float speedScale => m_SpeedScale;

        /// <summary>Human-readable current task, set by the task layer.</summary>
        public string currentTask
        {
            get => m_CurrentTask;
            set => m_CurrentTask = string.IsNullOrEmpty(value) ? "Standby" : value;
        }

        /// <summary>
        /// Optional extra speed limit, 0 to 1, evaluated every frame. The proximity monitor sets this
        /// to slow the robot near equipment. Null means no limit.
        /// </summary>
        public Func<float> speedLimiter { get; set; }

        void OnEnable()
        {
            // Keep the fleet sorted by id: OnEnable order is not guaranteed, and keys 1-4, the
            // fleet list and the wrist menu all index into this.
            var index = 0;
            while (index < s_All.Count && string.CompareOrdinal(s_All[index].m_RobotId, m_RobotId) < 0)
                index++;
            s_All.Insert(index, this);
            registryChanged?.Invoke();
            if (m_Link != null && m_Link.transport != null)
                m_Link.transport.CommandReceived += OnCommand;
        }

        void OnDisable()
        {
            s_All.Remove(this);
            registryChanged?.Invoke();
            if (m_Link != null && m_Link.transport != null)
                m_Link.transport.CommandReceived -= OnCommand;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_All.Clear();
            registryChanged = null;
        }

        void Start()
        {
            // TeleopLink creates its transport in Awake, which may run after our OnEnable.
            if (m_Link != null && m_Link.transport != null)
            {
                m_Link.transport.CommandReceived -= OnCommand;
                m_Link.transport.CommandReceived += OnCommand;
            }

            // Boot into a ready pose in front of the robot, the way a real arm homes on power-up.
            if (m_Arm != null && m_Arm.toolTip != null)
                m_Arm.SetGoal(readyPosition, readyRotation);
        }

        /// <summary>Home position for the tool, in base frame: tucked in front of the mast, above the deck.</summary>
        public Vector3 readyPosition => new Vector3(0f, 1.48f, 0.58f);

        /// <summary>Home orientation for the tool: pointing straight ahead.</summary>
        public Quaternion readyRotation => Quaternion.identity;

        void OnCommand(TeleopCommand command)
        {
            if (command.robotId != m_RobotId)
                return;

            // Out-of-order arrival: anything older than what we already applied for this type is stale.
            var slot = (int)command.type;
            if (command.sequence != 0 && command.sequence <= m_LastSequence[slot])
                return;
            m_LastSequence[slot] = command.sequence;

            m_LastCommandAt = TeleopLink.now;
            m_HasEverReceived = true;
            m_Deadman = command.deadman;
            m_WatchdogTripped = false;

            switch (command.type)
            {
                case CommandType.EStop:
                    EmergencyStop();
                    return;
                case CommandType.ResetEStop:
                    ResetEmergencyStop();
                    return;
            }

            if (m_EStopLatched || !m_Deadman)
                return;

            switch (command.type)
            {
                case CommandType.ArmTarget:
                    if (m_Arm != null)
                        m_Arm.SetGoal(command.position, command.rotation);
                    break;
                case CommandType.BaseVelocity:
                    if (m_Base != null)
                        m_Base.SetDrive(command.velocity, command.strafe);
                    break;
                case CommandType.BaseGoal:
                    if (m_Base != null && m_Base.SetDestination(command.position))
                        currentTask = "Navigating";
                    break;
                case CommandType.Gripper:
                    if (m_Gripper != null)
                        m_Gripper.SetClosed(command.value);
                    break;
            }
        }

        /// <summary>
        /// Latches the E-Stop immediately, bypassing the network: stops the base, freezes the arm
        /// and opens nothing. A held connector stays held, as a real gripper keeps its grip on a
        /// safe-torque-off stop.
        /// </summary>
        public void EmergencyStop()
        {
            if (m_EStopLatched)
                return;

            m_EStopLatched = true;
            HaltMotion();
            FeedbackEvent.Play(FeedbackCue.EStop, transform.position);
            ToastEvent.Show($"{m_DisplayName}: EMERGENCY STOP", ToastLevel.Error);
            UpdateState();
        }

        /// <summary>
        /// Releases the E-Stop. Refused while the deadman is still held, so the robot cannot lurch
        /// into motion the instant it is reset.
        /// </summary>
        public bool ResetEmergencyStop()
        {
            if (!m_EStopLatched)
                return true;

            if (m_Deadman && commandAge < 0.5f)
            {
                ToastEvent.Show("Release the deadman before resetting the E-Stop", ToastLevel.Warning);
                return false;
            }

            m_EStopLatched = false;
            m_WatchdogTripped = false;
            ToastEvent.Show($"{m_DisplayName}: E-Stop reset", ToastLevel.Success);
            UpdateState();
            return true;
        }

        void HaltMotion()
        {
            if (m_Base != null)
                m_Base.Stop();
            if (m_Arm != null)
                m_Arm.Hold();
        }

        void Update()
        {
            var deltaTime = Time.deltaTime;

            // Watchdog: a robot cleared to move whose operator falls silent stops by itself. A robot
            // already holding (deadman released, or deselected) has nothing to stop.
            if (m_Deadman && !m_EStopLatched && !m_WatchdogTripped && commandAge > m_Spec.watchdogTimeout)
            {
                m_WatchdogTripped = true;
                m_Deadman = false;
                HaltMotion();
                ToastEvent.Show($"{m_DisplayName}: link watchdog stop ({m_Spec.watchdogTimeout * 1000f:0} ms)", ToastLevel.Warning);
            }

            // Releasing the deadman holds position; it is not a fault.
            var motionAllowed = !m_EStopLatched && !m_WatchdogTripped && m_Deadman;
            if (!motionAllowed)
            {
                if (m_Base != null)
                    m_Base.SetDrive(Vector2.zero);
            }

            m_SpeedScale = speedLimiter != null ? Mathf.Clamp01(speedLimiter()) : 1f;

            // The arm keeps converging on its last goal only while motion is allowed; otherwise it
            // is frozen mid-move. The homing move at boot is allowed before any operator connects.
            var armMayMove = motionAllowed || (!m_HasEverReceived && !m_EStopLatched);
            if (m_Arm != null)
                m_Arm.Tick(armMayMove ? deltaTime : 0f, m_SpeedScale);

            if (m_Base != null && !m_EStopLatched)
                m_Base.Tick(deltaTime, m_Spec, m_SpeedScale);

            if (m_Gripper != null && !m_EStopLatched)
                m_Gripper.Tick(deltaTime, 1f);

            DrainBattery(deltaTime);
            UpdateState();
        }

        void DrainBattery(float deltaTime)
        {
            var motion = 0f;
            if (m_Base != null && m_Base.isMoving)
                motion += 0.7f;
            if (m_Arm != null && m_Arm.peakJointSpeed > 1f)
                motion += 0.3f;

            var perMinute = m_Spec.idleDrainPerMinute + m_Spec.motionDrainPerMinute * motion;
            m_Battery = Mathf.Max(0f, m_Battery - perMinute * deltaTime / 60f);
        }

        void UpdateState()
        {
            RobotState next;
            if (m_EStopLatched)
                next = RobotState.EStopped;
            else if (m_WatchdogTripped)
                next = RobotState.Fault;
            else if (m_Base != null && m_Base.isMoving)
                next = RobotState.Moving;
            else if ((m_Arm != null && m_Arm.peakJointSpeed > 2f) || (m_Gripper != null && m_Gripper.held != null))
                next = RobotState.Manipulating;
            else
                next = RobotState.Idle;

            if (next == m_State)
                return;

            m_State = next;
            stateChanged?.Invoke(this);
        }

        /// <summary>Wires the robot from code. Used by the scene builder.</summary>
        public void Configure(string robotId, string displayName, string sectionId, RobotSpec spec, TeleopLink link,
            ArmIKController arm, MobileBase mobileBase, Gripper gripper, Camera wristCamera)
        {
            m_RobotId = robotId;
            m_DisplayName = displayName;
            m_SectionId = sectionId;
            m_Spec = spec;
            m_Link = link;
            m_Arm = arm;
            m_Base = mobileBase;
            m_Gripper = gripper;
            m_WristCamera = wristCamera;
        }

        /// <summary>Points this robot at a link. Used when a prefab instance is placed in a scene.</summary>
        public void SetLink(TeleopLink link) => m_Link = link;

        /// <summary>Sets identity fields on a placed prefab instance.</summary>
        public void SetIdentity(string robotId, string displayName, string sectionId)
        {
            m_RobotId = robotId;
            m_DisplayName = displayName;
            m_SectionId = sectionId;
        }

        /// <summary>Finds an enabled robot by id. Linear, so cache the result.</summary>
        public static TeleopRobot Find(string robotId)
        {
            for (var i = 0; i < s_All.Count; i++)
            {
                if (s_All[i].m_RobotId == robotId)
                    return s_All[i];
            }

            return null;
        }
    }
}
