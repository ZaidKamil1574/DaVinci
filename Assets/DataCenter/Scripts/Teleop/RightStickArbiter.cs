using System.Collections.Generic;
using UnityEngine;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Shares the right thumbstick between the robot and the XR rig: normally it moves the gripper;
    /// while the right grip is held it belongs to the rig's teleport and snap turn.
    /// </summary>
    /// <remarks>
    /// The rig's teleport and turn components read the stick on their own, so rather than editing
    /// the rig's input actions this simply enables them only while the grip is held. They are
    /// listed by the scene builder (the right hand's ControllerInputActionManager and the turn
    /// providers).
    /// </remarks>
    public class RightStickArbiter : MonoBehaviour
    {
        [SerializeField]
        XRTeleopInput m_Input;

        [SerializeField, Tooltip("Rig behaviours that use the right stick for locomotion. Enabled only while the right grip is held.")]
        List<Behaviour> m_Locomotion = new List<Behaviour>();

        bool m_Enabled = true;

        void Update()
        {
            var locomotion = m_Input != null && XRPlatform.isImmersive && m_Input.rightGripHeld;
            if (locomotion == m_Enabled)
                return;

            m_Enabled = locomotion;
            for (var i = 0; i < m_Locomotion.Count; i++)
            {
                if (m_Locomotion[i] != null)
                    m_Locomotion[i].enabled = locomotion;
            }
        }

        /// <summary>Wires the arbiter from code. Used by the scene builder.</summary>
        public void Configure(XRTeleopInput input, List<Behaviour> locomotion)
        {
            m_Input = input;
            m_Locomotion = locomotion;
        }
    }
}
