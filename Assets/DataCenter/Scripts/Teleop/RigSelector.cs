using UnityEngine;
using DataCenter.Robotics;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Switches between the desktop camera rig and the XR rig as a headset starts or stops
    /// rendering, and moves both to the selected robot's operator spot.
    /// </summary>
    /// <remarks>
    /// The switch is live rather than decided once at startup because in the browser the user
    /// enters and leaves immersive mode at will, from the same page.
    /// </remarks>
    public class RigSelector : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField, Tooltip("Root of the desktop camera rig.")]
        GameObject m_DesktopRig;

        [SerializeField]
        DesktopCameraRig m_DesktopCamera;

        [SerializeField, Tooltip("Root of the XR Origin.")]
        Transform m_XROrigin;

        [SerializeField, Tooltip("The XR Origin's head camera. Disabled on the desktop so only one camera renders.")]
        Camera m_XRCamera;

        [SerializeField, Tooltip("Where the XR user stands relative to the robot: x right, z forward, in metres.")]
        Vector2 m_OperatorSpot = new Vector2(0.75f, -0.9f);

        bool m_Immersive;
        bool m_Initialised;

        /// <summary>Whether the XR rig is the active view.</summary>
        public bool immersive => m_Immersive;

        void OnEnable()
        {
            if (m_Operator != null)
                m_Operator.robotSelected += OnRobotSelected;
        }

        void OnDisable()
        {
            if (m_Operator != null)
                m_Operator.robotSelected -= OnRobotSelected;
        }

        void Start()
        {
            if (m_Operator != null && m_Operator.robot != null)
                OnRobotSelected(m_Operator.robot);
        }

        void Update()
        {
            var immersive = XRPlatform.isImmersive;
            if (m_Initialised && immersive == m_Immersive)
                return;

            m_Initialised = true;
            m_Immersive = immersive;

            // The XR Origin itself stays active either way, so its controllers keep tracking and
            // the browser can enter VR at any moment. Only the cameras are toggled, so exactly one
            // renders and Camera.main always names the one the operator is looking through.
            if (m_DesktopRig != null)
                m_DesktopRig.SetActive(!immersive);
            if (m_XRCamera != null)
                m_XRCamera.enabled = immersive;
        }

        void OnRobotSelected(TeleopRobot robot)
        {
            if (robot == null)
                return;

            if (m_DesktopCamera != null)
                m_DesktopCamera.Focus(robot.transform, false);

            if (m_XROrigin != null)
            {
                // Stand beside and slightly behind the robot, facing the same way, like an engineer
                // supervising it at the rack.
                var robotTransform = robot.transform;
                var spot = robotTransform.position + robotTransform.right * m_OperatorSpot.x + robotTransform.forward * m_OperatorSpot.y;
                spot.y = robotTransform.position.y;
                m_XROrigin.SetPositionAndRotation(spot, Quaternion.Euler(0f, robotTransform.eulerAngles.y, 0f));
            }
        }

        /// <summary>Wires the selector from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, GameObject desktopRig, DesktopCameraRig desktopCamera, Transform xrOrigin, Camera xrCamera)
        {
            m_XRCamera = xrCamera;
            m_Operator = teleopOperator;
            m_DesktopRig = desktopRig;
            m_DesktopCamera = desktopCamera;
            m_XROrigin = xrOrigin;
        }
    }
}
