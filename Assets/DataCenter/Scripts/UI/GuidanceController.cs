using System.Collections.Generic;
using TMPro;
using UnityEngine;
using DataCenter.Robotics;
using DataCenter.Tasks;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>What the operator should do next.</summary>
    public enum GuidanceKind
    {
        None,
        Pick,
        Plug,
        WrongCable,
        EStopped,
        SectionComplete,
    }

    /// <summary>One key to press right now and what it does, for the live hints.</summary>
    public struct KeyHint
    {
        public string key;
        public string action;

        public KeyHint(string key, string action)
        {
            this.key = key;
            this.action = action;
        }
    }

    /// <summary>
    /// Step-by-step guidance for the selected robot's work order: a floating marker and label at
    /// whatever to do next (the cable end to pick up, or the port to plug into), a guide line from
    /// the gripper to it, and an approach line out of the target port to line the plug up with.
    /// </summary>
    /// <remarks>
    /// The next step is worked out from what the robot and ports actually show each refresh, never
    /// from a script, so it stays right whatever order the operator does things in. The same
    /// instruction is shown on the desktop HUD and the VR dashboard.
    /// On the desktop it also works out which keys to press now, and how far, from where the
    /// gripper is relative to the target in the same screen-relative directions the arrow keys use.
    /// Those show under the marker and on the HUD.
    /// </remarks>
    public class GuidanceController : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField]
        WorkOrderManager m_WorkOrders;

        [SerializeField, Tooltip("Floating marker root: a bobbing diamond and a label, placed at the target.")]
        Transform m_Marker;

        [SerializeField]
        Transform m_Diamond;

        [SerializeField]
        TextMeshPro m_Label;

        [SerializeField, Tooltip("Line from the gripper to the target.")]
        LineRenderer m_PathLine;

        [SerializeField, Tooltip("Line straight out of the target port, along which the plug goes in.")]
        LineRenderer m_ApproachLine;

        [SerializeField, Tooltip("Refreshes of the step logic per second. Positions update every frame.")]
        float m_RefreshRate = 8f;

        static readonly Color k_Pick = new Color(0.1f, 0.83f, 1f);
        static readonly Color k_Plug = new Color(0.18f, 0.9f, 0.42f);
        static readonly Color k_Warn = new Color(1f, 0.69f, 0.13f);
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        // Offsets smaller than this need no key: the gripper's 5 cm grab range and the assist's
        // 6 cm seat radius both take care of them.
        const float k_Tolerance = 0.02f;
        const int k_MaxMoveHints = 3;

        readonly List<KeyHint> m_Keys = new List<KeyHint>();
        readonly List<(float distance, string key, string action)> m_Moves = new List<(float, string, string)>();

        MaterialPropertyBlock m_Block;
        float m_NextRefresh;
        Transform m_Target;
        PortSocket m_TargetPort;
        Color m_Color;

        /// <summary>The current step kind.</summary>
        public GuidanceKind kind { get; private set; }

        /// <summary>Full instruction sentence, for the HUD and the VR dashboard.</summary>
        public string instruction { get; private set; } = string.Empty;

        /// <summary>Keys to press now to get on with the step, most useful first. Desktop only; empty in VR.</summary>
        public IReadOnlyList<KeyHint> keyHints => m_Keys;

        void Start()
        {
            // A dark outline keeps the label readable against bright floor tiles and dark racks
            // alike. Set at runtime: in the Editor it would create a stray material instance.
            if (m_Label != null)
            {
                m_Label.outlineWidth = 0.2f;
                m_Label.outlineColor = new Color32(0, 0, 0, 200);
            }
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= m_NextRefresh)
            {
                m_NextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, m_RefreshRate);
                Refresh();
            }

            Place();
        }

        void Refresh()
        {
            m_Target = null;
            m_TargetPort = null;
            m_Keys.Clear();

            var robot = m_Operator != null ? m_Operator.robot : null;
            if (robot == null || m_WorkOrders == null)
            {
                Set(GuidanceKind.None, string.Empty, null, k_Pick);
                return;
            }

            var vr = XRPlatform.isImmersive;
            if (robot.isEStopped)
            {
                Set(GuidanceKind.EStopped, vr
                    ? "The emergency stop is on. Let go of all the controls, then press X to carry on."
                    : "The emergency stop is on. Let go of all the keys, then press Shift + Backspace (or the RESET button) to carry on.", null, k_Warn);
                AddKey(vr, "SHIFT + BKSP", "carry on");
                return;
            }

            var active = m_WorkOrders.GetActive(robot.sectionId);
            if (active == null)
            {
                Set(GuidanceKind.SectionComplete, vr
                    ? "Every job in this area is done. Pick another robot from the menu on your left wrist."
                    : "Every job in this area is done. Press 1-4, or click a robot on the left, to help another one.", null, k_Plug);
                AddKey(vr, "1 - 4", "next robot");
                return;
            }

            var order = active.order;
            var from = PortSocket.Find(order.from);
            var to = PortSocket.Find(order.to);
            var cable = VerletCable.Find(order.cable);
            if (from == null || to == null || cable == null)
            {
                Set(GuidanceKind.None, order.summary, null, k_Pick);
                return;
            }

            // The next port is whichever end of the order is still empty, "from" first.
            var nextPort = !IsCable(from, cable) ? from : to;
            var endsDone = (IsCable(from, cable) ? 1 : 0) + (IsCable(to, cable) ? 1 : 0);
            var cableName = from.type != null && !string.IsNullOrEmpty(from.type.plainName) ? from.type.plainName : $"{order.portType} cable";
            var portName = PortLabels.Name(nextPort);
            var held = robot.gripper != null ? robot.gripper.held as CableConnector : null;

            if (held != null && held.cable != cable)
            {
                Set(GuidanceKind.WrongCable, vr
                    ? $"That is not the cable this job needs. Let go of L2 to put it down, then pick up the {cableName} labelled {cable.cableId}."
                    : $"That is not the cable this job needs. Tap Shift to put it down, then pick up the {cableName} labelled {cable.cableId}.",
                    held.transform, k_Warn);
                AddKey(vr, "SHIFT", "tap to put it down");
                m_Label.text = "WRONG CABLE\n<size=70%>put it down</size>" + KeyLine(vr);
                return;
            }

            if (held != null)
            {
                m_TargetPort = nextPort;
                var step = endsDone * 2 + 2;
                var plugText = CableConnector.insertionAssist
                    ? vr
                        ? $"Step {step} of 4: Plug it into {portName}, the port with the blinking blue light. Bring the plug close, pointing at the port, and it slides in by itself."
                        : $"Step {step} of 4: Plug it into {portName} (blinking blue light). Bring it close with the keys below; at the chime it slides in by itself."
                    : vr
                        ? $"Step {step} of 4: Plug it into {portName}. Line the plug up with the green line (hold R2 and use the right stick to turn it), push it in, then let go of L2."
                        : $"Step {step} of 4: Plug it into {portName}. Line the plug up with the green line (Q and E tilt it), push it in, then tap Shift to let go.";
                Set(GuidanceKind.Plug, plugText,
                    nextPort.socket, k_Plug);
                if (!vr)
                    AddPlugHints(robot, held, nextPort);
                m_Label.text = $"{step} · PLUG IN\n<size=70%>{portName}</size>" + KeyLine(vr);
                return;
            }

            // Pick the end that is not already plugged in.
            var end = cable.endA.state != CableConnector.ConnectorState.Seated ? cable.endA : cable.endB;
            var pickStep = endsDone * 2 + 1;
            var then = endsDone == 0 ? "on the cart" : "hanging from the first port";
            Set(GuidanceKind.Pick, vr
                ? $"Step {pickStep} of 4: Pick up the {cableName} labelled {cable.cableId} ({then}). Steer the gripper to the floating marker with the right stick (press the stick in to reach further), then hold L2 to grab it."
                : $"Step {pickStep} of 4: Pick up the {cableName} labelled {cable.cableId} ({then}). Steer the gripper to the marker with the keys below, and tap Shift at the chime.",
                end.transform, k_Pick);
            if (!vr)
            {
                var inRange = robot.gripper != null && robot.gripper.FindNearestInRange() is CableConnector near && near.cable == cable;
                if (inRange)
                    AddKey(false, "SHIFT", "tap to grab");
                else
                    AddMoveHints(robot, end.transform.position);
            }

            m_Label.text = $"{pickStep} · PICK UP\n<size=70%>{cableName} {cable.cableId}</size>" + KeyLine(vr);
        }

        void AddKey(bool vr, string key, string action)
        {
            if (!vr)
                m_Keys.Add(new KeyHint(key, action));
        }

        /// <summary>
        /// Keys that close the gap from the gripper to <paramref name="target"/>: the robot's base
        /// first if the arm cannot reach, otherwise the arm keys for the largest offsets.
        /// </summary>
        void AddMoveHints(TeleopRobot robot, Vector3 target)
        {
            var arm = robot.arm;
            if (arm == null || arm.toolTip == null)
                return;

            if (AddDriveHints(robot, target))
                return;

            DesktopTeleopInput.GetArmAxes(Camera.main, arm.baseFrame, out var right, out var reach);
            var offset = target - arm.toolTip.position;
            var up = offset.y;
            var side = Vector3.Dot(offset, right);
            var depth = Vector3.Dot(offset, reach);

            m_Moves.Clear();
            m_Moves.Add((Mathf.Abs(up), up > 0f ? "UP" : "DOWN", (up > 0f ? "raise " : "lower ") + Centimetres(up)));
            m_Moves.Add((Mathf.Abs(side), side > 0f ? "RIGHT" : "LEFT", (side > 0f ? "right " : "left ") + Centimetres(side)));
            m_Moves.Add((Mathf.Abs(depth), depth > 0f ? "R" : "F", (depth > 0f ? "reach out " : "pull back ") + Centimetres(depth)));
            m_Moves.Sort((a, b) => b.distance.CompareTo(a.distance));

            for (var i = 0; i < m_Moves.Count && m_Keys.Count < k_MaxMoveHints; i++)
            {
                if (m_Moves[i].distance > k_Tolerance)
                    m_Keys.Add(new KeyHint(m_Moves[i].key, m_Moves[i].action));
            }
        }

        /// <summary>Adds base keys when the target is beyond the arm's reach. Returns whether it did.</summary>
        bool AddDriveHints(TeleopRobot robot, Vector3 target)
        {
            var arm = robot.arm;
            if (arm.joints.Count < 2 || arm.joints[1].joint == null)
                return false;

            var shoulder = arm.joints[1].joint.position;
            var shortfall = Vector3.Distance(shoulder, target) - (robot.spec.armReach + 0.1f);
            if (shortfall <= 0f)
                return false;

            var heading = Vector3.ProjectOnPlane(robot.transform.forward, Vector3.up);
            var toTarget = Vector3.ProjectOnPlane(target - robot.transform.position, Vector3.up);
            var turn = Vector3.SignedAngle(heading, toTarget, Vector3.up);
            if (Mathf.Abs(turn) > 35f)
                m_Keys.Add(new KeyHint(turn > 0f ? "D" : "A", turn > 0f ? "turn right" : "turn left"));
            else
                m_Keys.Add(new KeyHint("W", $"drive {shortfall:0.0} m closer (out of reach)"));
            return true;
        }

        /// <summary>Plug step: move to the port, then square the plug up if assist would refuse it.</summary>
        void AddPlugHints(TeleopRobot robot, CableConnector held, PortSocket port)
        {
            var socket = port.socket;
            var distance = Vector3.Distance(held.tip.position, socket.position);
            var seatRadius = CableConnector.insertionAssist ? held.assistSeatRadius : (port.type != null ? port.type.captureRadius : 0.02f);
            var seatAngle = CableConnector.insertionAssist ? held.assistSeatAngle : (port.type != null ? port.type.captureAngle : 25f);
            var angle = Vector3.Angle(held.tip.forward, socket.forward);

            if (distance > seatRadius)
            {
                // Aim a little in front of the port, so the plug arrives along the green line.
                AddMoveHints(robot, socket.position - socket.forward * 0.02f);
                if (m_Keys.Count > 0 || distance > seatRadius * 2f)
                    return;
            }

            if (angle > seatAngle * 0.8f)
            {
                // Tilt (Q / E) fixes the vertical part; the base turn fixes the horizontal part.
                var tilt = Mathf.Asin(Mathf.Clamp(socket.forward.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(held.tip.forward.y, -1f, 1f));
                if (Mathf.Abs(tilt * Mathf.Rad2Deg) > 10f)
                {
                    m_Keys.Add(new KeyHint(tilt > 0f ? "Q" : "E", tilt > 0f ? "tilt up" : "tilt down"));
                }
                else
                {
                    var yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(held.tip.forward, Vector3.up), Vector3.ProjectOnPlane(socket.forward, Vector3.up), Vector3.up);
                    m_Keys.Add(new KeyHint(yaw > 0f ? "D" : "A", yaw > 0f ? "turn right" : "turn left"));
                }

                return;
            }

            if (!CableConnector.insertionAssist)
                m_Keys.Add(new KeyHint("SHIFT", "tap to let go and plug it in"));
        }

        static string Centimetres(float metres) => $"{Mathf.Abs(metres) * 100f:0} cm";

        /// <summary>The keys as one line under the marker label, like "[UP] 12 cm   [R] reach 5 cm".</summary>
        string KeyLine(bool vr)
        {
            if (vr || m_Keys.Count == 0)
                return string.Empty;

            var line = new System.Text.StringBuilder("\n<size=55%><color=#FFFFFF>");
            for (var i = 0; i < m_Keys.Count; i++)
            {
                if (i > 0)
                    line.Append("   ");
                line.Append('[').Append(m_Keys[i].key).Append("] ").Append(m_Keys[i].action);
            }

            return line.Append("</color></size>").ToString();
        }

        static bool IsCable(PortSocket port, VerletCable cable) => port.occupant != null && port.occupant.cable == cable;

        void Set(GuidanceKind next, string text, Transform target, Color color)
        {
            kind = next;
            instruction = text;
            m_Target = target;

            if (color != m_Color)
            {
                m_Color = color;
                m_Block ??= new MaterialPropertyBlock();
                if (m_Diamond != null && m_Diamond.TryGetComponent<Renderer>(out var diamond))
                {
                    diamond.GetPropertyBlock(m_Block);
                    m_Block.SetColor(k_BaseColor, color);
                    diamond.SetPropertyBlock(m_Block);
                }

                if (m_Label != null)
                    m_Label.color = color;
                if (m_PathLine != null)
                    m_PathLine.startColor = m_PathLine.endColor = color;
            }
        }

        void Place()
        {
            var robot = m_Operator != null ? m_Operator.robot : null;
            var visible = m_Target != null && robot != null && robot.arm != null;

            if (m_Marker != null && m_Marker.gameObject.activeSelf != visible)
                m_Marker.gameObject.SetActive(visible);
            if (m_PathLine != null)
                m_PathLine.enabled = visible;
            if (m_ApproachLine != null)
                m_ApproachLine.enabled = visible && m_TargetPort != null;

            if (!visible)
                return;

            // Marker floats above the target; for a port, in front of it so it does not cover it.
            var point = m_Target.position;
            var anchor = m_TargetPort != null
                ? point - m_TargetPort.socket.forward * 0.12f + Vector3.up * 0.09f
                : point + Vector3.up * 0.14f;
            var bob = Mathf.Sin(Time.time * 3f) * 0.012f;
            m_Marker.position = anchor + Vector3.up * bob;

            var head = Camera.main != null ? Camera.main.transform : null;
            if (head != null)
            {
                var look = m_Marker.position - head.position;
                if (look.sqrMagnitude > 0.0001f)
                    m_Marker.rotation = Quaternion.LookRotation(look, Vector3.up);
            }

            if (m_Diamond != null)
                m_Diamond.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f) * Quaternion.Euler(45f, 0f, 45f);

            if (m_PathLine != null)
            {
                m_PathLine.SetPosition(0, robot.arm.toolTip.position);
                m_PathLine.SetPosition(1, point);
            }

            if (m_ApproachLine != null && m_TargetPort != null)
            {
                var socket = m_TargetPort.socket;
                m_ApproachLine.SetPosition(0, socket.position);
                m_ApproachLine.SetPosition(1, socket.position - socket.forward * 0.25f);
            }
        }

        /// <summary>Wires the guidance from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, WorkOrderManager workOrders, Transform marker, Transform diamond,
            TextMeshPro label, LineRenderer pathLine, LineRenderer approachLine)
        {
            m_Operator = teleopOperator;
            m_WorkOrders = workOrders;
            m_Marker = marker;
            m_Diamond = diamond;
            m_Label = label;
            m_PathLine = pathLine;
            m_ApproachLine = approachLine;
        }
    }
}
