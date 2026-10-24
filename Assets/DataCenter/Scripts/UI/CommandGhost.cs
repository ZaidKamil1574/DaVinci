using UnityEngine;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>
    /// Draws a translucent gripper at the <i>commanded</i> pose and a line to where the real gripper
    /// is. With link latency the two separate, and the gap is what the operator is waiting for.
    /// </summary>
    /// <remarks>
    /// Predictive displays like this are standard in high-latency teleoperation (space robotics
    /// most famously): the operator steers the ghost, which responds instantly, and trusts the robot
    /// to catch up.
    /// </remarks>
    public class CommandGhost : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField, Tooltip("Ghost geometry. Its origin is the tool tip and its forward the approach axis.")]
        GameObject m_Ghost;

        [SerializeField]
        LineRenderer m_Line;

        [SerializeField, Tooltip("Position gap below which the ghost hides, in metres.")]
        float m_ShowDistance = 0.006f;

        [SerializeField, Tooltip("Angle gap below which the ghost hides, in degrees.")]
        float m_ShowAngle = 2.5f;

        /// <summary>Gap between commanded and actual tool position, in metres.</summary>
        public float positionGap { get; private set; }

        /// <summary>Gap between commanded and actual tool orientation, in degrees.</summary>
        public float angleGap { get; private set; }

        void LateUpdate()
        {
            var robot = m_Operator != null ? m_Operator.robot : null;
            if (robot == null || robot.arm == null || robot.arm.toolTip == null)
            {
                SetVisible(false);
                return;
            }

            var commanded = m_Operator.commandedPose;
            var actual = robot.arm.toolTip;
            positionGap = Vector3.Distance(commanded.position, actual.position);
            angleGap = Quaternion.Angle(commanded.rotation, actual.rotation);

            var visible = positionGap > m_ShowDistance || angleGap > m_ShowAngle;
            SetVisible(visible);
            if (!visible)
                return;

            m_Ghost.transform.SetPositionAndRotation(commanded.position, commanded.rotation);
            if (m_Line != null)
            {
                m_Line.SetPosition(0, actual.position);
                m_Line.SetPosition(1, commanded.position);
            }
        }

        void SetVisible(bool visible)
        {
            if (m_Ghost != null && m_Ghost.activeSelf != visible)
                m_Ghost.SetActive(visible);
            if (m_Line != null && m_Line.enabled != visible)
                m_Line.enabled = visible;
        }

        /// <summary>Wires the ghost from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, GameObject ghost, LineRenderer line)
        {
            m_Operator = teleopOperator;
            m_Ghost = ghost;
            m_Line = line;
        }
    }
}
