using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using DataCenter.Robotics;
using DataCenter.Tasks;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Auto demo: takes each robot in turn and completes one of its work orders by itself, picking
    /// up each end of the cable and plugging it in, so a visitor can watch a job done start to finish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The demo is just another <see cref="ITeleopInput"/>: it holds the deadman and steers the
    /// tool, and the operator, link, robot and safety layer treat it exactly as they treat a
    /// person. The guidance, key hints, chimes and haptics run as usual while it works.
    /// </para>
    /// <para>
    /// Any key, or the E-Stop, hands control straight back to the operator. A step that cannot be
    /// finished in time (an end out of reach, say) skips that robot rather than hanging.
    /// </para>
    /// </remarks>
    public class DemoPilot : MonoBehaviour, ITeleopInput
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField]
        WorkOrderManager m_WorkOrders;

        [SerializeField, Tooltip("Longest one arm move may take before the demo gives up on that robot, in seconds.")]
        float m_MoveTimeout = 10f;

        bool m_Deadman;
        bool m_HasPose;
        Pose m_Pose;
        bool m_Home;
        GripperIntent m_Gripper;
        Coroutine m_Run;
        string m_Failure;

        /// <summary>Raised when the demo starts (true) or ends (false).</summary>
        public event Action<bool> runningChanged;

        /// <summary>Whether the demo is driving right now.</summary>
        public bool isRunning => m_Run != null;

        public bool isActive => isRunning;

        public string displayName => "Auto demo";

        public void Poll(ref TeleopIntent intent, in TeleopContext context)
        {
            intent.deadman |= m_Deadman;
            if (m_HasPose)
            {
                intent.hasArmPose = true;
                intent.armPose = m_Pose;
            }

            intent.homeArm |= m_Home;
            if (m_Gripper != GripperIntent.None)
            {
                intent.gripper = m_Gripper;
                m_Gripper = GripperIntent.None;
            }
        }

        void Start()
        {
            if (m_Operator != null)
                m_Operator.AddInputSource(this);
        }

        void Update()
        {
            if (!isRunning)
                return;

            // The operator can take over at any moment by pressing a key.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key != null && key.wasPressedThisFrame && !IsModifierOnly(key.keyCode))
                    {
                        Stop($"Demo stopped ({key.displayName} pressed): you have control");
                        return;
                    }
                }
            }

            if (m_Operator != null && m_Operator.robot != null && m_Operator.robot.isEStopped)
                Stop("Demo stopped by the E-Stop");
        }

        /// <summary>
        /// Keys that switch windows or apps (Cmd-Tab, Alt-Tab) rather than drive the robot, so
        /// pressing one alone does not take over.
        /// </summary>
        static bool IsModifierOnly(Key key)
        {
            switch (key)
            {
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.LeftCtrl:
                case Key.RightCtrl:
                case Key.LeftMeta:
                case Key.RightMeta:
                case Key.CapsLock:
                case Key.ContextMenu:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Starts the demo, or stops it if it is running.</summary>
        public void Toggle()
        {
            if (isRunning)
                Stop("Demo stopped");
            else
                Begin();
        }

        /// <summary>Runs one work order on each robot in turn.</summary>
        public void Begin()
        {
            if (isRunning || m_Operator == null || m_WorkOrders == null)
                return;

            m_Run = StartCoroutine(RunAll());
            runningChanged?.Invoke(true);
        }

        /// <summary>Stops at once and gives control back.</summary>
        public void Stop(string reason)
        {
            if (!isRunning)
                return;

            StopCoroutine(m_Run);
            Finish();
            ToastEvent.Show(reason, ToastLevel.Info);
        }

        void Finish()
        {
            m_Run = null;
            m_Deadman = false;
            m_HasPose = false;
            m_Home = false;
            m_Gripper = GripperIntent.None;
            runningChanged?.Invoke(false);
        }

        IEnumerator RunAll()
        {
            var done = 0;
            foreach (var robot in new List<TeleopRobot>(TeleopRobot.all))
            {
                var active = m_WorkOrders.GetActive(robot.sectionId);
                if (robot == null || active == null || robot.isEStopped)
                    continue;

                m_Operator.Select(robot);
                ToastEvent.Show($"Demo: {robot.displayName} is doing {active.order.id}. {active.order.summary}", ToastLevel.Info);

                // Give the camera time to swing round to the robot.
                yield return new WaitForSeconds(1.2f);

                m_Failure = null;
                yield return RunOrder(robot, active.order);
                if (m_Failure == null)
                    done++;
                else
                    ToastEvent.Show($"Demo: {robot.displayName} skipped. {m_Failure}", ToastLevel.Warning);

                yield return Home(robot);
            }

            ToastEvent.Show(done > 0 ? $"Demo finished: {done} job{(done == 1 ? "" : "s")} done. You have control." : "Demo: no jobs left to do.", done > 0 ? ToastLevel.Success : ToastLevel.Info);
            Finish();
        }

        /// <summary>Plugs each end of the order's cable into its port, "from" first.</summary>
        IEnumerator RunOrder(TeleopRobot robot, WorkOrder order)
        {
            var cable = VerletCable.Find(order.cable);
            var ports = new[] { PortSocket.Find(order.from), PortSocket.Find(order.to) };
            if (cable == null || ports[0] == null || ports[1] == null)
            {
                m_Failure = "The cable or a port for this job is missing.";
                yield break;
            }

            foreach (var port in ports)
            {
                if (port.occupant != null && port.occupant.cable == cable)
                    continue;

                var end = cable.endA.state == CableConnector.ConnectorState.Free ? cable.endA
                    : cable.endB.state == CableConnector.ConnectorState.Free ? cable.endB
                    : null;
                if (end == null)
                {
                    m_Failure = "Neither end of the cable is free to pick up.";
                    yield break;
                }

                yield return PickUp(robot, end);
                if (m_Failure != null)
                    yield break;

                yield return Insert(robot, end, port);
                if (m_Failure != null)
                    yield break;
            }
        }

        IEnumerator PickUp(TeleopRobot robot, CableConnector end)
        {
            var down = Quaternion.LookRotation(Vector3.down, robot.transform.forward);
            var above = end.transform.position + Vector3.up * 0.08f;

            yield return MoveTool(robot, above, down, 0.008f, "reach the cable");
            yield return MoveTool(robot, end.transform.position + Vector3.up * 0.004f, down, 0.006f, "reach the cable");
            if (m_Failure != null)
                yield break;

            m_Gripper = GripperIntent.Close;
            yield return WaitUntil(() => end.state == CableConnector.ConnectorState.Held, 2f);
            if (end.state != CableConnector.ConnectorState.Held)
            {
                m_Failure = "It could not grab the plug.";
                yield break;
            }

            yield return MoveTool(robot, above + Vector3.up * 0.1f, down, 0.02f, "lift the cable");
        }

        IEnumerator Insert(TeleopRobot robot, CableConnector end, PortSocket port)
        {
            var into = Quaternion.LookRotation(port.socket.forward, Vector3.up);
            yield return MoveTool(robot, port.socket.position - port.socket.forward * 0.08f, into, 0.008f, "reach the port");
            if (m_Failure != null)
                yield break;

            // With insertion assist the plug goes in on the way and the gripper opens by itself;
            // without, push it home and let go.
            yield return MoveTool(robot, port.socket.position - port.socket.forward * 0.002f, into, 0.006f, null);
            if (port.occupant != end && robot.gripper.held == (IGrippable)end)
                m_Gripper = GripperIntent.Open;

            yield return WaitUntil(() => port.occupant == end, 3f);
            if (port.occupant != end)
            {
                m_Failure = "The plug did not go into the port.";
                yield break;
            }

            yield return MoveTool(robot, port.socket.position - port.socket.forward * 0.15f, into, 0.03f, null);
        }

        IEnumerator Home(TeleopRobot robot)
        {
            m_HasPose = false;
            m_Deadman = true;
            m_Home = true;
            yield return new WaitForSeconds(0.5f);
            yield return WaitUntil(() => robot.arm == null || robot.arm.peakJointSpeed < 0.5f, 4f);
            m_Home = false;
            m_Deadman = false;
        }

        /// <summary>
        /// Commands the goal pose and waits for the arm to get there. The goal is sent whole rather
        /// than glided towards: the robot's joint speed limits already make the motion smooth, and
        /// the IK solves cleanly from where the arm is to the goal, where in-between poses can lead
        /// it into a configuration it cannot get back out of. With a <paramref name="task"/>, failing
        /// to arrive in time stops this robot's job; without one, falling short is fine (the plug
        /// may already be in).
        /// </summary>
        IEnumerator MoveTool(TeleopRobot robot, Vector3 goal, Quaternion rotation, float tolerance, string task)
        {
            if (m_Failure != null || robot.arm == null)
                yield break;

            var tip = robot.arm.toolTip;
            m_Pose = new Pose(goal, rotation);
            m_Deadman = true;
            m_HasPose = true;

            var until = Time.time + m_MoveTimeout;
            while (true)
            {
                var arrived = Vector3.Distance(tip.position, goal) < tolerance
                    && Quaternion.Angle(tip.rotation, rotation) < 5f;
                if (arrived)
                    yield break;

                if (Time.time > until)
                {
                    if (task != null)
                    {
                        var shortBy = Vector3.Distance(tip.position, goal) * 100f;
                        var turnedBy = Quaternion.Angle(tip.rotation, rotation);
                        var blocked = m_Operator != null && m_Operator.toolBlocked ? ", blocked by equipment" : string.Empty;
                        m_Failure = $"It could not {task} ({shortBy:0.0} cm and {turnedBy:0} degrees off{blocked}).";
                    }

                    yield break;
                }

                yield return null;
            }
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            var until = Time.time + timeout;
            while (!condition() && Time.time < until)
                yield return null;
        }

        /// <summary>Wires the demo from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, WorkOrderManager workOrders)
        {
            m_Operator = teleopOperator;
            m_WorkOrders = workOrders;
        }
    }
}
