using System.Text;
using TMPro;
using UnityEngine;
using DataCenter.Robotics;
using DataCenter.Tasks;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>
    /// World-space status panel for VR. Shows the same essentials as the desktop HUD and drifts
    /// lazily to stay in view: it only moves once the head has turned well away from it, so it
    /// never swims with every small head motion.
    /// </summary>
    public class VRStatusPanel : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField]
        WorkOrderManager m_WorkOrders;

        [SerializeField]
        TextMeshPro m_Text;

        [SerializeField, Tooltip("Root shown only while a headset is rendering.")]
        GameObject m_Visual;

        [SerializeField]
        ProximityIndicator m_Proximity;

        [SerializeField]
        TutorialController m_Tutorial;

        [SerializeField]
        WristCameraFeed m_WristCamera;

        [SerializeField, Tooltip("Quad showing the wrist camera feed.")]
        Renderer m_WristQuad;

        [SerializeField, Tooltip("Distance in front of the head, in metres.")]
        float m_Distance = 1.1f;

        [SerializeField, Tooltip("Drop below eye level, in metres. Keeps the panel out of the work area.")]
        float m_Drop = 0.35f;

        [SerializeField, Tooltip("Head turn away from the panel before it follows, in degrees.")]
        float m_FollowAngle = 35f;

        [SerializeField]
        float m_FollowSharpness = 3f;

        [SerializeField, Tooltip("Text refreshes per second.")]
        float m_RefreshRate = 6f;

        readonly StringBuilder m_Builder = new StringBuilder(512);
        string m_LastMessage;
        ToastLevel m_LastLevel;
        float m_LastMessageUntil;
        Vector3 m_TargetPosition;
        bool m_Following;
        bool m_Placed;
        float m_NextRefresh;

        void OnEnable() => EventBus.Subscribe<ToastEvent>(OnToast);

        void OnDisable() => EventBus.Unsubscribe<ToastEvent>(OnToast);

        // The desktop HUD shows these as toasts; in the headset they are the panel's last line, so
        // refusals ("release the deadman first") are visible where the operator is looking.
        void OnToast(ToastEvent toast)
        {
            m_LastMessage = toast.message;
            m_LastLevel = toast.level;
            m_LastMessageUntil = Time.unscaledTime + 5f;
            m_NextRefresh = 0f;
        }

        void AppendLastMessage()
        {
            if (string.IsNullOrEmpty(m_LastMessage) || Time.unscaledTime > m_LastMessageUntil)
                return;

            var color = m_LastLevel == ToastLevel.Error ? "#FF453A" : m_LastLevel == ToastLevel.Warning ? "#FFB020" : m_LastLevel == ToastLevel.Success ? "#2EE66B" : "#19D3FF";
            m_Builder.Append("\n<size=85%><color=").Append(color).Append('>').Append(m_LastMessage).Append("</color></size>");
        }

        void Start()
        {
            // The feed's texture is created at runtime, so it is attached here rather than in the scene.
            if (m_WristQuad != null && m_WristCamera != null && m_WristCamera.texture != null)
                m_WristQuad.material.SetTexture("_BaseMap", m_WristCamera.texture);
        }

        void LateUpdate()
        {
            var immersive = XRPlatform.isImmersive;
            if (m_Visual != null && m_Visual.activeSelf != immersive)
                m_Visual.SetActive(immersive);

            if (!immersive)
            {
                m_Placed = false;
                return;
            }

            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null)
                return;

            var flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.01f)
                flatForward = head.up;

            var desired = head.position + flatForward * m_Distance + Vector3.down * m_Drop;
            var toPanel = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);

            if (!m_Placed)
            {
                transform.position = desired;
                m_Placed = true;
            }
            else if (Vector3.Angle(toPanel, flatForward) > m_FollowAngle)
            {
                m_Following = true;
            }

            if (m_Following)
            {
                m_TargetPosition = desired;
                transform.position = Vector3.Lerp(transform.position, m_TargetPosition, 1f - Mathf.Exp(-m_FollowSharpness * Time.deltaTime));
                if (Vector3.Distance(transform.position, m_TargetPosition) < 0.02f)
                    m_Following = false;
            }

            // Face the head, upright.
            var look = transform.position - head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(look, Vector3.up);

            if (Time.unscaledTime >= m_NextRefresh)
            {
                m_NextRefresh = Time.unscaledTime + 1f / m_RefreshRate;
                Refresh();
            }
        }

        void Refresh()
        {
            if (m_Text == null || m_Operator == null)
                return;

            var robot = m_Operator.robot;
            m_Builder.Clear();

            if (robot == null)
            {
                m_Text.text = "No robot selected";
                return;
            }

            var stopped = robot.isEStopped;
            var live = m_Operator.deadman && !stopped;

            m_Builder.Append("<b>").Append(robot.displayName).Append("</b>  <size=70%><color=#8B98A5>")
                .Append(robot.robotId).Append(" · Section ").Append(robot.sectionId).Append("</color></size>\n");

            m_Builder.Append(StateColor(robot.state)).Append(robot.state.ToString().ToUpperInvariant()).Append("</color>")
                .Append("   <color=#8B98A5>BAT</color> ").Append(Mathf.RoundToInt(robot.battery * 100f)).Append('%');

            var stats = m_Operator.link.transport.stats;
            m_Builder.Append("   <color=#8B98A5>LINK</color> ").Append(Mathf.RoundToInt(stats.averageLatencyMs)).Append(" ms\n");

            if (stopped)
                m_Builder.Append("<color=#FF453A><b>E-STOP LATCHED</b></color>  <size=70%>let go of everything, then press X</size>\n");
            else if (robot.watchdogTripped)
                m_Builder.Append("<color=#FFB020><b>LINK WATCHDOG STOP</b></color>\n");
            else if (live)
                m_Builder.Append("<color=#2EE66B><b>MOTION ENABLED</b></color>\n");
            else
                m_Builder.Append("<color=#8B98A5>Left stick / A / B drive  ·  grip the handle to move the arm</color>\n");

            var sensor = m_Proximity != null ? m_Proximity.sensor : null;
            if (sensor != null && !float.IsInfinity(sensor.toolDistance))
            {
                var color = sensor.zone == ProximityZone.Danger ? "#FF453A" : sensor.zone == ProximityZone.Warning ? "#FFB020" : "#2EE66B";
                m_Builder.Append("<color=").Append(color).Append(">PROXIMITY ").Append((sensor.toolDistance * 100f).ToString("0.0")).Append(" cm</color>");
                if (sensor.speedScale < 0.99f)
                    m_Builder.Append("  <color=#8B98A5>speed ").Append(Mathf.RoundToInt(sensor.speedScale * 100f)).Append("%</color>");
                m_Builder.Append('\n');
            }

            if (m_Tutorial != null && (m_Tutorial.isRunning || m_Tutorial.step == TutorialController.Step.Done))
            {
                m_Builder.Append("\n<color=#2EE66B><b>TUTORIAL ").Append(m_Tutorial.stepNumber).Append('/').Append(TutorialController.stepCount)
                    .Append("</b></color>  <size=80%>").Append(m_Tutorial.instruction).Append("</size>");
                AppendLastMessage();
                m_Text.SetText(m_Builder);
                return;
            }

            var active = m_WorkOrders != null ? m_WorkOrders.GetActive(robot.sectionId) : null;
            if (active != null)
            {
                m_Builder.Append("\n<color=#19D3FF><b>").Append(active.order.id).Append("</b></color>  ")
                    .Append(active.endsDone).Append("/2 ends   ")
                    .Append(Mathf.FloorToInt(active.elapsed)).Append("s   ")
                    .Append(active.errors).Append(" err\n")
                    .Append("<size=80%>").Append(active.order.from).Append("  →  ").Append(active.order.to).Append("</size>");
            }
            else if (m_WorkOrders != null && m_WorkOrders.IsSectionComplete(robot.sectionId))
            {
                m_Builder.Append("\n<color=#2EE66B><b>Section complete</b></color>");
            }

            AppendLastMessage();
            m_Text.SetText(m_Builder);
        }

        static string StateColor(RobotState state)
        {
            switch (state)
            {
                case RobotState.Idle: return "<color=#2EE66B>";
                case RobotState.Fault: return "<color=#FFB020>";
                case RobotState.EStopped: return "<color=#FF453A>";
                default: return "<color=#19D3FF>";
            }
        }

        /// <summary>Wires the panel from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, WorkOrderManager workOrders, TextMeshPro text, GameObject visual,
            ProximityIndicator proximity, TutorialController tutorial, WristCameraFeed wristCamera, Renderer wristQuad)
        {
            m_Proximity = proximity;
            m_Tutorial = tutorial;
            m_WristCamera = wristCamera;
            m_WristQuad = wristQuad;
            m_Operator = teleopOperator;
            m_WorkOrders = workOrders;
            m_Text = text;
            m_Visual = visual;
        }
    }
}
