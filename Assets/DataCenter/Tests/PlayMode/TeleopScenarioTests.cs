using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using DataCenter.Robotics;
using DataCenter.Tasks;
using DataCenter.Teleop;

namespace DataCenter.Tests
{
    /// <summary>
    /// End-to-end checks on the generated scene, driven through the same operator, link and robot
    /// code a person uses. A scripted input source stands in for the hands.
    /// </summary>
    public class TeleopScenarioTests
    {
        const string ScenePath = "Assets/DataCenter/Scenes/DataCenter.unity";
        const string FromPort = "A-R03-U38-P12";
        const string ToPort = "A-R03-U40-P12";

        /// <summary>Feeds intent to the operator from test code.</summary>
        class ScriptedInput : ITeleopInput
        {
            public TeleopIntent next;
            public bool isActive => true;
            public string displayName => "Test Script";

            public void Poll(ref TeleopIntent intent, in TeleopContext context)
            {
                intent.deadman |= next.deadman;
                intent.baseDrive += next.baseDrive;
                intent.baseStrafe += next.baseStrafe;
                if (next.hasArmPose)
                {
                    intent.hasArmPose = true;
                    intent.armPose = next.armPose;
                }

                if (next.gripper != GripperIntent.None)
                    intent.gripper = next.gripper;
                intent.eStop |= next.eStop;
                intent.resetEStop |= next.resetEStop;

                // One-shot fields fire once.
                next.gripper = GripperIntent.None;
                next.eStop = false;
                next.resetEStop = false;
            }
        }

        TeleopOperator m_Operator;
        TeleopRobot m_Robot;
        TeleopLink m_Link;
        ScriptedInput m_Input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("DataCenter");
#endif
            yield return null;

            m_Operator = UnityEngine.Object.FindAnyObjectByType<TeleopOperator>();
            m_Link = UnityEngine.Object.FindAnyObjectByType<TeleopLink>();
            Assert.That(m_Operator, Is.Not.Null, "Scene has no TeleopOperator");
            m_Robot = m_Operator.robot;
            Assert.That(m_Robot, Is.Not.Null, "Operator selected no robot");

            m_Input = new ScriptedInput();
            m_Operator.AddInputSource(m_Input);

            // Let the arm finish homing.
            yield return WaitUntil(() => m_Robot.arm.peakJointSpeed < 0.5f, 6f, "arm to finish homing");
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeout, string what)
        {
            var until = Time.time + timeout;
            while (!condition())
            {
                if (Time.time > until)
                    Assert.Fail($"Timed out after {timeout:0.0}s waiting for {what}");
                yield return null;
            }
        }

        /// <summary>Commands the tool to a pose and waits for the real arm to arrive.</summary>
        IEnumerator MoveTool(Vector3 position, Quaternion rotation, float tolerance = 0.004f, float timeout = 8f)
        {
            m_Input.next.deadman = true;
            m_Input.next.hasArmPose = true;
            m_Input.next.armPose = new Pose(position, rotation);
            yield return WaitUntil(() =>
                    Vector3.Distance(m_Robot.arm.toolTip.position, position) < tolerance
                    && Quaternion.Angle(m_Robot.arm.toolTip.rotation, rotation) < 4f,
                timeout,
                $"tool to reach {position} (error {Vector3.Distance(m_Robot.arm.toolTip.position, position) * 1000f:0.0} mm, {Quaternion.Angle(m_Robot.arm.toolTip.rotation, rotation):0.0} deg)");
        }

        IEnumerator Grip(bool close)
        {
            m_Input.next.gripper = close ? GripperIntent.Close : GripperIntent.Open;
            // Stroke time, plus the link delay before the robot even hears the command.
            yield return new WaitForSeconds(0.6f + m_Link.conditions.latencyMs / 1000f);
        }

        /// <summary>Picks up <paramref name="connector"/> from above and lifts it clear.</summary>
        IEnumerator PickUp(CableConnector connector)
        {
            var forward = m_Robot.transform.forward;
            var down = Quaternion.LookRotation(Vector3.down, forward);
            var above = connector.transform.position + Vector3.up * 0.08f;

            yield return MoveTool(above, down, 0.006f);
            yield return MoveTool(connector.transform.position + Vector3.up * 0.004f, down, 0.004f);
            yield return Grip(true);
            Assert.That(connector.state, Is.EqualTo(CableConnector.ConnectorState.Held), $"{connector.name} was not picked up");
            yield return MoveTool(above + Vector3.up * 0.1f, down, 0.01f);
        }

        /// <summary>Presents the held connector square to <paramref name="port"/>, pushes it home and lets go.</summary>
        IEnumerator Insert(PortSocket port)
        {
            var into = Quaternion.LookRotation(port.socket.forward, Vector3.up);
            yield return MoveTool(port.socket.position - port.socket.forward * 0.08f, into, 0.006f);
            yield return MoveTool(port.socket.position - port.socket.forward * 0.002f, into, 0.004f);
            yield return Grip(false);
            yield return MoveTool(port.socket.position - port.socket.forward * 0.1f, into, 0.01f);
        }

        [UnityTest]
        public IEnumerator SelectingAnotherRobotHandsOverControl()
        {
            Assert.That(TeleopRobot.all.Count, Is.EqualTo(4), "Expected one robot per section");
            var first = m_Operator.robot;
            var second = TeleopRobot.all[1];
            m_Operator.Select(second);
            yield return null;

            Assert.That(m_Operator.robot, Is.EqualTo(second));
            Assert.That(second.sectionId, Is.EqualTo("B"));
            var orders = UnityEngine.Object.FindAnyObjectByType<WorkOrderManager>();
            Assert.That(orders.focusedSection, Is.EqualTo("B"), "Port highlights did not follow the selected robot");

            // The previous robot was told to hold, and drives nowhere while unselected.
            m_Input.next.deadman = true;
            m_Input.next.baseDrive = new Vector2(0f, 1f);
            var before = first.transform.position;
            yield return new WaitForSeconds(0.6f);
            Assert.That(Vector3.Distance(first.transform.position, before), Is.LessThan(0.005f), "Unselected robot moved");
            Assert.That(second.mobileBase.speed, Is.GreaterThan(0.05f), "Selected robot did not drive");
        }

        [UnityTest]
        public IEnumerator ProximityCapsSpeedNearTheRack()
        {
            var sensor = m_Robot.GetComponent<ProximitySensor>();
            Assert.That(sensor, Is.Not.Null, "Robot has no proximity sensor");
            Assert.That(sensor.zone, Is.EqualTo(ProximityZone.Clear), "Ready pose should be clear of equipment");
            Assert.That(sensor.speedScale, Is.EqualTo(1f).Within(0.01f));

            // Reach right up to the patch panel face.
            var port = PortSocket.Find(FromPort);
            var into = Quaternion.LookRotation(port.socket.forward, Vector3.up);
            yield return MoveTool(port.socket.position - port.socket.forward * 0.05f, into, 0.006f);

            Assert.That(sensor.zone, Is.Not.EqualTo(ProximityZone.Clear), $"No warning {sensor.toolDistance * 100f:0.0} cm from the rack");
            Assert.That(sensor.speedScale, Is.LessThan(1f), "Speed was not capped near equipment");
            Assert.That(m_Robot.speedScale, Is.LessThan(1f), "Robot did not apply the cap");
        }

        [UnityTest]
        public IEnumerator TutorialStartsInStagingAndAdvancesOnDeadman()
        {
            var tutorial = UnityEngine.Object.FindAnyObjectByType<TutorialController>();
            Assert.That(tutorial, Is.Not.Null);

            tutorial.Begin();
            yield return null;
            Assert.That(m_Operator.robot.sectionId, Is.EqualTo("D"), "Tutorial did not take the Staging robot");
            Assert.That(tutorial.step, Is.EqualTo(TutorialController.Step.Deadman));

            m_Input.next.deadman = true;
            yield return new WaitForSeconds(1.3f);
            Assert.That(tutorial.step, Is.EqualTo(TutorialController.Step.Grab), "Holding the deadman did not pass step 1");
        }

        [UnityTest]
        public IEnumerator BaseDrivesSidewaysWithoutTurning()
        {
            var start = m_Robot.transform.position;
            var yaw = m_Robot.transform.eulerAngles.y;
            m_Input.next.deadman = true;
            m_Input.next.baseStrafe = 1f;
            yield return new WaitForSeconds(0.8f);

            var moved = m_Robot.transform.position - start;
            var sideways = Vector3.Dot(moved, m_Robot.transform.right);
            var forward = Vector3.Dot(moved, m_Robot.transform.forward);
            Assert.That(sideways, Is.GreaterThan(0.1f), $"Did not slide right: {sideways * 100f:0.0} cm");
            Assert.That(Mathf.Abs(forward), Is.LessThan(0.02f), "Sideways command also moved it forward or back");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, m_Robot.transform.eulerAngles.y)), Is.LessThan(0.5f), "Sideways command turned the robot");
        }

        [UnityTest]
        public IEnumerator HomesToReadyPose()
        {
            var expected = m_Robot.arm.baseFrame.TransformPoint(m_Robot.readyPosition);
            Assert.That(Vector3.Distance(m_Robot.arm.toolTip.position, expected), Is.LessThan(0.01f));
            Assert.That(m_Robot.state, Is.EqualTo(RobotState.Idle));
            yield break;
        }

        [UnityTest]
        public IEnumerator NothingMovesWithoutDeadman()
        {
            var start = m_Robot.arm.toolTip.position;
            m_Input.next.deadman = false;
            m_Input.next.hasArmPose = true;
            m_Input.next.armPose = new Pose(start + m_Robot.transform.forward * 0.2f, m_Robot.arm.toolTip.rotation);
            m_Input.next.baseDrive = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1f);

            Assert.That(Vector3.Distance(m_Robot.arm.toolTip.position, start), Is.LessThan(0.002f), "Arm moved without the deadman");
            Assert.That(m_Robot.mobileBase.speed, Is.LessThan(0.01f), "Base moved without the deadman");
        }

        [UnityTest]
        public IEnumerator CompletesFirstSectionAWorkOrderEndToEnd()
        {
            var from = PortSocket.Find(FromPort);
            var to = PortSocket.Find(ToPort);
            Assert.That(from, Is.Not.Null);
            Assert.That(to, Is.Not.Null);

            CableConnector endA = null, endB = null;
            foreach (var cable in UnityEngine.Object.FindObjectsByType<VerletCable>())
            {
                if (cable.cableId == "A-C01")
                {
                    endA = cable.endA;
                    endB = cable.endB;
                }
            }

            Assert.That(endA, Is.Not.Null, "Cable A-C01 not found");

            var orders = UnityEngine.Object.FindAnyObjectByType<WorkOrderManager>();
            var active = orders.GetActive("A");
            Assert.That(active, Is.Not.Null, $"No active order in section A ({orders.GetOrders("A").Count} orders loaded)");
            Assert.That(active.order.from, Is.EqualTo(FromPort));
            Assert.That(endA.cable, Is.Not.Null, "Connector has no cable back-reference");

            yield return PickUp(endA);
            yield return Insert(from);
            Assert.That(from.occupant, Is.EqualTo(endA), "End A did not seat in the patch panel");

            yield return PickUp(endB);
            yield return Insert(to);
            Assert.That(to.occupant, Is.EqualTo(endB), "End B did not seat in the switch");

            Assert.That(from.occupant, Is.EqualTo(endA), "End A came out of the patch panel while placing end B");

            yield return new WaitForSeconds(0.8f);
            Assert.That(from.linkUp && to.linkUp, Is.True, "Ports did not report link up");
            Assert.That(active.status, Is.EqualTo(OrderStatus.Complete),
                $"WO-A-001 was not marked complete: status {active.status}, ends {active.endsDone}, errors {active.errors}");
            Assert.That(active.errors, Is.EqualTo(0));

            // The queue moves on to the next order in the section.
            var next = orders.GetActive("A");
            Assert.That(next, Is.Not.Null, "No follow-up order activated");
            Assert.That(next.order.id, Is.EqualTo("WO-A-002"));
        }

        [UnityTest]
        public IEnumerator RejectsWrongConnectorType()
        {
            CableConnector fiber = null;
            foreach (var cable in UnityEngine.Object.FindObjectsByType<VerletCable>())
            {
                if (cable.cableId == "A-F01")
                    fiber = cable.endA;
            }

            Assert.That(fiber, Is.Not.Null, "Fiber lead A-F01 not found");

            var rejected = 0;
            void OnRejected(PortRejectedEvent e) => rejected++;
            EventBus.Subscribe<PortRejectedEvent>(OnRejected);
            try
            {
                yield return PickUp(fiber);
                var port = PortSocket.Find(FromPort);
                yield return Insert(port);
                Assert.That(rejected, Is.GreaterThan(0), "No rejection was raised");
                Assert.That(port.isOccupied, Is.False, "LC connector seated in an RJ45 port");
            }
            finally
            {
                EventBus.Unsubscribe<PortRejectedEvent>(OnRejected);
            }
        }

        [UnityTest]
        public IEnumerator WatchdogStopsRobotWhenCommandsStop()
        {
            m_Input.next.deadman = true;
            m_Input.next.baseDrive = new Vector2(0f, 0.5f);
            yield return new WaitForSeconds(0.6f);
            Assert.That(m_Robot.deadmanHeld, Is.True);

            // The operator station goes silent mid-motion, as if the network died.
            m_Operator.enabled = false;
            yield return new WaitForSeconds(m_Robot.spec.watchdogTimeout + 0.2f);

            Assert.That(m_Robot.watchdogTripped, Is.True, "Watchdog did not trip");
            Assert.That(m_Robot.state, Is.EqualTo(RobotState.Fault));
            yield return new WaitForSeconds(0.3f);
            Assert.That(m_Robot.mobileBase.speed, Is.LessThan(0.02f), "Base still moving after watchdog stop");
        }

        [UnityTest]
        public IEnumerator EStopLatchesAndResetNeedsDeadmanReleased()
        {
            m_Input.next.eStop = true;
            yield return null;
            yield return null;
            Assert.That(m_Robot.isEStopped, Is.True);
            Assert.That(m_Robot.state, Is.EqualTo(RobotState.EStopped));

            // Motion requests are ignored while latched.
            var start = m_Robot.arm.toolTip.position;
            m_Input.next.deadman = true;
            m_Input.next.hasArmPose = true;
            m_Input.next.armPose = new Pose(start + Vector3.up * 0.15f, m_Robot.arm.toolTip.rotation);
            yield return new WaitForSeconds(0.8f);
            Assert.That(Vector3.Distance(m_Robot.arm.toolTip.position, start), Is.LessThan(0.002f), "Arm moved while E-Stopped");

            // Reset is refused while the deadman is held...
            m_Input.next.resetEStop = true;
            yield return new WaitForSeconds(0.1f);
            Assert.That(m_Robot.isEStopped, Is.True, "E-Stop reset while deadman held");

            // ...and accepted once it is released.
            m_Input.next.deadman = false;
            m_Input.next.hasArmPose = false;
            yield return new WaitForSeconds(0.6f);
            m_Input.next.resetEStop = true;
            yield return new WaitForSeconds(0.1f);
            Assert.That(m_Robot.isEStopped, Is.False, "E-Stop did not reset");
        }
    }
}
