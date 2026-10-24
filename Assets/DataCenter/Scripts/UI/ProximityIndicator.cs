using UnityEngine;
using DataCenter.Robotics;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>
    /// World-space proximity cue for the selected robot: a marker at the tool tip and a line to the
    /// nearest equipment, coloured green, yellow or red with the <see cref="ProximitySensor"/> zone.
    /// </summary>
    /// <remarks>The HUD and VR panel show the same zone and the live distance as text.</remarks>
    public class ProximityIndicator : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField, Tooltip("Small marker placed at the tool tip.")]
        Renderer m_Marker;

        [SerializeField, Tooltip("Line from the tool tip to the closest equipment point.")]
        LineRenderer m_Line;

        static readonly Color k_Clear = new Color(0.18f, 0.9f, 0.42f);
        static readonly Color k_Warning = new Color(1f, 0.69f, 0.13f);
        static readonly Color k_Danger = new Color(1f, 0.27f, 0.23f);
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock m_Block;
        ProximitySensor m_Sensor;
        TeleopRobot m_SensorOwner;
        ProximityZone m_LastZone = (ProximityZone)(-1);

        /// <summary>The sensor of the selected robot, or null.</summary>
        public ProximitySensor sensor => m_Sensor;

        public static Color ZoneColor(ProximityZone zone) => zone == ProximityZone.Danger ? k_Danger : zone == ProximityZone.Warning ? k_Warning : k_Clear;

        void LateUpdate()
        {
            var robot = m_Operator != null ? m_Operator.robot : null;
            if (robot != m_SensorOwner)
            {
                m_SensorOwner = robot;
                m_Sensor = robot != null ? robot.GetComponent<ProximitySensor>() : null;
            }

            if (m_Sensor == null || robot.arm == null)
            {
                SetVisible(false, false);
                return;
            }

            var tip = robot.arm.toolTip.position;
            var inRange = !float.IsInfinity(m_Sensor.toolDistance);
            SetVisible(true, inRange && m_Sensor.zone != ProximityZone.Clear);

            if (m_Marker != null)
                m_Marker.transform.position = tip;

            if (m_Line != null && m_Line.enabled)
            {
                m_Line.SetPosition(0, tip);
                m_Line.SetPosition(1, m_Sensor.toolClosestPoint);
            }

            if (m_Sensor.zone == m_LastZone)
                return;

            m_LastZone = m_Sensor.zone;
            var color = ZoneColor(m_LastZone);
            m_Block ??= new MaterialPropertyBlock();
            if (m_Marker != null)
            {
                m_Marker.GetPropertyBlock(m_Block);
                m_Block.SetColor(k_BaseColor, color);
                m_Marker.SetPropertyBlock(m_Block);
            }

            if (m_Line != null)
            {
                m_Line.startColor = color;
                m_Line.endColor = color;
            }
        }

        void SetVisible(bool marker, bool line)
        {
            if (m_Marker != null && m_Marker.enabled != marker)
                m_Marker.enabled = marker;
            if (m_Line != null && m_Line.enabled != line)
                m_Line.enabled = line;
        }

        /// <summary>Wires the indicator from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, Renderer marker, LineRenderer line)
        {
            m_Operator = teleopOperator;
            m_Marker = marker;
            m_Line = line;
        }
    }
}
