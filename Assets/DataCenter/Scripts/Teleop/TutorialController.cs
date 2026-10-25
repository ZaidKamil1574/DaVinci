using System;
using UnityEngine;
using DataCenter.Robotics;
using DataCenter.Tasks;

namespace DataCenter.Teleop
{
    /// <summary>
    /// A one-minute guided run in the Staging training bay that teaches the four things every
    /// operator must know: deadman, grab, insert and E-Stop.
    /// </summary>
    /// <remarks>
    /// Each step completes on observed robot state, never on a button press, so the tutorial
    /// checks that the operator did the thing rather than read about it.
    /// </remarks>
    public class TutorialController : MonoBehaviour
    {
        public enum Step
        {
            NotStarted,
            Deadman,
            Grab,
            Pick,
            Insert,
            EStop,
            Reset,
            Done,
        }

        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField, Tooltip("Section the training bay is in.")]
        string m_SectionId = "D";

        [SerializeField, Tooltip("Cable used for training.")]
        string m_CableId = "D-C01";

        [SerializeField, Tooltip("Port the plug goes into.")]
        string m_TargetPort = "D-T01-U20-P01";

        [SerializeField, Tooltip("Seconds the deadman must be held to pass the first step.")]
        float m_DeadmanHold = 1f;

        [SerializeField, Tooltip("How far the tool must move to pass the grab step, in metres.")]
        float m_GrabDistance = 0.12f;

        Step m_Step;
        float m_StartedAt;
        float m_FinishedAt;
        float m_DeadmanSince = -1f;
        Vector3 m_GrabStart;

        /// <summary>Raised whenever the step changes, including start and finish.</summary>
        public event Action<Step> stepChanged;

        public Step step => m_Step;
        public bool isRunning => m_Step != Step.NotStarted && m_Step != Step.Done;
        public float elapsed => m_Step == Step.NotStarted ? 0f : (m_Step == Step.Done ? m_FinishedAt : Time.time) - m_StartedAt;

        /// <summary>Total steps, for progress display.</summary>
        public const int stepCount = 6;

        /// <summary>Current step number for display, 1-based.</summary>
        public int stepNumber => Mathf.Clamp((int)m_Step, 1, stepCount);

        /// <summary>Instruction for the current step, worded for the active input device.</summary>
        public string instruction
        {
            get
            {
                var vr = XRPlatform.isImmersive;
                switch (m_Step)
                {
                    case Step.Deadman:
                        return vr ? "Squeeze and hold a GRIP for a second. Every control is hold-to-run: let go and the robot stops."
                            : "Hold SPACE for a second. Every key is hold-to-run: hold it to move, let go and the robot stops.";
                    case Step.Grab:
                        return vr ? "Push the right stick to move the gripper, or grip the cyan handle and move your hand. The arm follows."
                            : "Move the gripper with the arrow keys (R / F to reach out and back). Tap for small moves, hold for big ones.";
                    case Step.Pick:
                        return vr ? "Put the gripper over a blue plug on the cart and hold L2 (left trigger) to close."
                            : "Put the gripper on a blue plug on the cart (follow the keys under the marker) and tap SHIFT to grab it.";
                    case Step.Insert:
                        return "Bring it to training port 1 (blinking cyan), square it up and open the gripper to seat it.";
                    case Step.EStop:
                        return vr ? "Hit the E-STOP: X on the left controller, or the red button." : "Hit the E-STOP: BACKSPACE, or the red button.";
                    case Step.Reset:
                        return vr ? "Let go of the grip and sticks, then press X to reset." : "Let go of every key, then press SHIFT + BACKSPACE to reset.";
                    case Step.Done:
                        return $"Tutorial complete in {Mathf.FloorToInt(elapsed)}s. You are cleared to operate.";
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>Starts from the beginning: takes the training robot and resets the training cable.</summary>
        public void Begin()
        {
            if (m_Operator == null)
                return;

            TeleopRobot trainee = null;
            foreach (var robot in TeleopRobot.all)
            {
                if (robot.sectionId == m_SectionId)
                    trainee = robot;
            }

            if (trainee == null)
            {
                ToastEvent.Show($"No robot in section {m_SectionId} for the tutorial", ToastLevel.Error);
                return;
            }

            if (trainee.isEStopped)
                trainee.ResetEmergencyStop();

            foreach (var cable in FindObjectsByType<VerletCable>())
            {
                if (cable.cableId != m_CableId)
                    continue;

                cable.endA.ResetToHome();
                cable.endB.ResetToHome();
                cable.Reinitialise();
            }

            m_Operator.SetMode(ControlMode.Direct);
            m_Operator.Select(trainee);
            m_StartedAt = Time.time;
            m_DeadmanSince = -1f;
            Go(Step.Deadman);
        }

        /// <summary>Stops the tutorial without completing it.</summary>
        public void Skip()
        {
            if (!isRunning)
                return;

            m_Step = Step.NotStarted;
            stepChanged?.Invoke(m_Step);
        }

        void Go(Step next)
        {
            m_Step = next;
            if (next == Step.Grab && m_Operator.robot != null)
                m_GrabStart = m_Operator.robot.arm.toolTip.position;

            if (next == Step.Done)
            {
                m_FinishedAt = Time.time;
                ToastEvent.Show($"Tutorial complete in {Mathf.FloorToInt(elapsed)}s", ToastLevel.Success);
                FeedbackEvent.Play(FeedbackCue.SectionComplete, Vector3.zero);
            }
            else if (next != Step.Deadman)
            {
                FeedbackEvent.Play(FeedbackCue.LinkUp, Vector3.zero);
            }

            stepChanged?.Invoke(next);
        }

        void Update()
        {
            if (!isRunning || m_Operator == null)
                return;

            var robot = m_Operator.robot;
            if (robot == null || robot.sectionId != m_SectionId)
                return;

            switch (m_Step)
            {
                case Step.Deadman:
                    if (m_Operator.deadman)
                    {
                        if (m_DeadmanSince < 0f)
                            m_DeadmanSince = Time.time;
                        if (Time.time - m_DeadmanSince >= m_DeadmanHold)
                            Go(Step.Grab);
                    }
                    else
                    {
                        m_DeadmanSince = -1f;
                    }

                    break;

                case Step.Grab:
                    if (Vector3.Distance(robot.arm.toolTip.position, m_GrabStart) >= m_GrabDistance)
                        Go(Step.Pick);
                    break;

                case Step.Pick:
                    if (robot.gripper.held is CableConnector held && held.cable != null && held.cable.cableId == m_CableId)
                        Go(Step.Insert);
                    break;

                case Step.Insert:
                    var port = PortSocket.Find(m_TargetPort);
                    if (port != null && port.isOccupied)
                        Go(Step.EStop);
                    break;

                case Step.EStop:
                    if (robot.isEStopped)
                        Go(Step.Reset);
                    break;

                case Step.Reset:
                    if (!robot.isEStopped)
                        Go(Step.Done);
                    break;
            }
        }

        /// <summary>Wires the tutorial from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator) => m_Operator = teleopOperator;
    }
}
