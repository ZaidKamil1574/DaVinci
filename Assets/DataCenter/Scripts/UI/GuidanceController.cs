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

    /// <summary>
    /// Step-by-step guidance for the selected robot's work order: a floating marker and label at
    /// whatever to do next (the cable end to pick up, or the port to plug into), a guide line from
    /// the gripper to it, and an approach line out of the target port to line the plug up with.
    /// </summary>
    /// <remarks>
    /// The next step is worked out from what the robot and ports actually show each refresh, never
    /// from a script, so it stays right whatever order the operator does things in. The same
    /// instruction is shown on the desktop HUD and the VR dashboard.
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

        MaterialPropertyBlock m_Block;
        float m_NextRefresh;
        Transform m_Target;
        PortSocket m_TargetPort;
        Color m_Color;

        /// <summary>The current step kind.</summary>
        public GuidanceKind kind { get; private set; }

        /// <summary>Full instruction sentence, for the HUD and the VR dashboard.</summary>
        public string instruction { get; private set; } = string.Empty;

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
                    ? "E-STOP latched. Let go of every control, then press X to reset."
                    : "E-STOP latched. Release SPACE, then press SHIFT + BACKSPACE (or RESET).", null, k_Warn);
                return;
            }

            var active = m_WorkOrders.GetActive(robot.sectionId);
            if (active == null)
            {
                Set(GuidanceKind.SectionComplete, vr
                    ? $"Section {robot.sectionId} is done. Pick another robot on the wrist menu."
                    : $"Section {robot.sectionId} is done. Press 1-4 or click another robot.", null, k_Plug);
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
            var typeName = from.type != null ? from.type.code : order.portType;
            var held = robot.gripper != null ? robot.gripper.held as CableConnector : null;

            if (held != null && held.cable != cable)
            {
                Set(GuidanceKind.WrongCable, vr
                    ? $"That is the wrong cable. Open the gripper (let go of L2) and pick up the {typeName} cable {cable.cableId}."
                    : $"That is the wrong cable. Open the gripper (G) and pick up the {typeName} cable {cable.cableId}.",
                    held.transform, k_Warn);
                m_Label.text = "WRONG CABLE\n<size=70%>put it down</size>";
                return;
            }

            if (held != null)
            {
                m_TargetPort = nextPort;
                var step = endsDone * 2 + 2;
                Set(GuidanceKind.Plug, vr
                    ? $"Step {step}/4: Plug into {nextPort.portId}. Line the plug up with the green line (R2 + right stick to turn it), push it in, then let go of L2."
                    : $"Step {step}/4: Plug into {nextPort.portId}. Line the plug up with the green line (Q E Z X to turn it), push it in, then press G to open.",
                    nextPort.socket, k_Plug);
                m_Label.text = $"{step} · PLUG IN\n<size=70%>{nextPort.portId}</size>";
                return;
            }

            // Pick the end that is not already plugged in.
            var end = cable.endA.state != CableConnector.ConnectorState.Seated ? cable.endA : cable.endB;
            var pickStep = endsDone * 2 + 1;
            Set(GuidanceKind.Pick, vr
                ? $"Step {pickStep}/4: Move the gripper over the marked end of {typeName} cable {cable.cableId} on the cart (right stick, hold it in to reach) and hold L2 to grab it."
                : $"Step {pickStep}/4: Move the gripper over the marked end of {typeName} cable {cable.cableId} on the cart (drag the handle, R/F for height) and press G to grab it.",
                end.transform, k_Pick);
            m_Label.text = $"{pickStep} · PICK UP\n<size=70%>{typeName} cable {cable.cableId}</size>";
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
