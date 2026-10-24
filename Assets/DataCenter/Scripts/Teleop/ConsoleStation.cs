using UnityEngine;

namespace DataCenter.Teleop
{
    /// <summary>
    /// The VR operator console for Console mode: a travel lever for the base, a lift lever for the
    /// tool and a twist knob for the wrist, on a stand in front of the operator.
    /// </summary>
    /// <remarks>
    /// The same split as the Da Vinci surgeon console (<c>DaVinciArmConsole</c>): controls report
    /// values, this class turns them into rates, and the operator applies them. Levers are rate
    /// controls, not position controls, so releasing one holds the robot where it is.
    /// </remarks>
    public class ConsoleStation : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField, Tooltip("Left lever: base drive. Forward/back drives, left/right turns.")]
        XRLever2D m_TravelLever;

        [SerializeField, Tooltip("Right lever: tool. Forward/back reaches out and in, left/right lowers and raises.")]
        XRLever2D m_LiftLever;

        [SerializeField, Tooltip("Knob: wrist roll rate.")]
        XRTwistKnob m_WristKnob;

        [SerializeField, Tooltip("Shown only in Console mode while a headset is active.")]
        GameObject m_Visual;

        [SerializeField, Tooltip("Tool speed at full lever, in metres per second.")]
        float m_ToolSpeed = 0.2f;

        [SerializeField, Tooltip("Wrist rate at full knob, in degrees per second.")]
        float m_WristRate = 70f;

        bool m_WasDriving;

        void Update()
        {
            if (m_Operator == null)
                return;

            var show = m_Operator.mode == ControlMode.Console && XRPlatform.isImmersive;
            if (m_Visual != null && m_Visual.activeSelf != show)
                m_Visual.SetActive(show);

            if (!show)
            {
                // Hand the console channels back cleanly when leaving, so nothing keeps creeping.
                if (m_WasDriving)
                {
                    m_Operator.consoleDrive = Vector2.zero;
                    m_Operator.consoleArmVelocity = Vector3.zero;
                    m_Operator.consoleWristRollRate = 0f;
                    m_WasDriving = false;
                }

                return;
            }

            m_WasDriving = true;
            var travel = m_TravelLever != null ? m_TravelLever.value : Vector2.zero;
            var lift = m_LiftLever != null ? m_LiftLever.value : Vector2.zero;

            m_Operator.consoleDrive = travel;
            m_Operator.consoleArmVelocity = new Vector3(0f, -lift.x, lift.y) * m_ToolSpeed;
            m_Operator.consoleWristRollRate = (m_WristKnob != null ? m_WristKnob.value : 0f) * m_WristRate;
        }

        /// <summary>Wires the console from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, XRLever2D travel, XRLever2D lift, XRTwistKnob knob, GameObject visual)
        {
            m_Operator = teleopOperator;
            m_TravelLever = travel;
            m_LiftLever = lift;
            m_WristKnob = knob;
            m_Visual = visual;
        }
    }
}
