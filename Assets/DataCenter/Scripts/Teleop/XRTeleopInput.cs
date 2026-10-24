using UnityEngine;
using UnityEngine.InputSystem;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Quest controller teleoperation, also used by WebXR in the Quest browser.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Either grip</b>: deadman. Gripping the target handle also grabs it, so one squeeze both enables and steers.</item>
    /// <item><b>Right trigger</b>: close the gripper while held, open on release.</item>
    /// <item><b>Left stick</b>: drive the base.</item>
    /// <item><b>Right stick</b>: wrist roll (x) and pitch (y), while the deadman is held.</item>
    /// <item><b>Y</b>: E-Stop. <b>Hold X</b> for one second: reset.</item>
    /// <item><b>A</b>: home the arm.</item>
    /// </list>
    /// Bindings use XR usages ({GripButton}, {Primary2DAxis}) rather than device-specific paths, so
    /// the same actions bind to OpenXR Touch controllers and to WebXR's generic controllers.
    /// </remarks>
    public class XRTeleopInput : MonoBehaviour, ITeleopInput
    {
        [SerializeField, Tooltip("Wrist rate at full stick, in degrees per second.")]
        float m_WristRate = 75f;

        [SerializeField, Tooltip("Stick dead zone.")]
        float m_DeadZone = 0.2f;

        [SerializeField, Tooltip("How long X must be held to reset the E-Stop, in seconds.")]
        float m_ResetHoldTime = 1f;

        InputAction m_LeftGrip;
        InputAction m_RightGrip;
        InputAction m_RightTrigger;
        InputAction m_LeftStick;
        InputAction m_RightStick;
        InputAction m_EStop;
        InputAction m_Reset;
        InputAction m_Home;
        float m_ResetHeldFor;
        bool m_ResetFired;
        bool m_TriggerWasPressed;

        public bool isActive => XRPlatform.isImmersive;

        public string displayName => "XR Controllers";

        /// <summary>Progress of the hold-to-reset gesture, 0 to 1, for the VR panel.</summary>
        public float resetHoldProgress => m_ResetHoldTime > 0f ? Mathf.Clamp01(m_ResetHeldFor / m_ResetHoldTime) : 0f;

        void Awake()
        {
            m_LeftGrip = Button("Left Grip", "<XRController>{LeftHand}/{GripButton}", "<XRController>{LeftHand}/gripPressed");
            m_RightGrip = Button("Right Grip", "<XRController>{RightHand}/{GripButton}", "<XRController>{RightHand}/gripPressed");
            m_RightTrigger = Button("Right Trigger", "<XRController>{RightHand}/{TriggerButton}", "<XRController>{RightHand}/triggerPressed");
            m_EStop = Button("E-Stop", "<XRController>{LeftHand}/{SecondaryButton}", "<XRController>{LeftHand}/secondaryButton");
            m_Reset = Button("Reset", "<XRController>{LeftHand}/{PrimaryButton}", "<XRController>{LeftHand}/primaryButton");
            m_Home = Button("Home", "<XRController>{RightHand}/{PrimaryButton}", "<XRController>{RightHand}/primaryButton");

            m_LeftStick = new InputAction("Left Stick", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}");
            m_LeftStick.AddBinding("<XRController>{LeftHand}/thumbstick");
            m_RightStick = new InputAction("Right Stick", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}");
            m_RightStick.AddBinding("<XRController>{RightHand}/thumbstick");
        }

        static InputAction Button(string name, string primary, string fallback)
        {
            var action = new InputAction(name, InputActionType.Button, primary);
            action.AddBinding(fallback);
            return action;
        }

        void OnEnable()
        {
            m_LeftGrip.Enable();
            m_RightGrip.Enable();
            m_RightTrigger.Enable();
            m_LeftStick.Enable();
            m_RightStick.Enable();
            m_EStop.Enable();
            m_Reset.Enable();
            m_Home.Enable();
        }

        void OnDisable()
        {
            m_LeftGrip.Disable();
            m_RightGrip.Disable();
            m_RightTrigger.Disable();
            m_LeftStick.Disable();
            m_RightStick.Disable();
            m_EStop.Disable();
            m_Reset.Disable();
            m_Home.Disable();
        }

        void OnDestroy()
        {
            m_LeftGrip.Dispose();
            m_RightGrip.Dispose();
            m_RightTrigger.Dispose();
            m_LeftStick.Dispose();
            m_RightStick.Dispose();
            m_EStop.Dispose();
            m_Reset.Dispose();
            m_Home.Dispose();
        }

        public void Poll(ref TeleopIntent intent, in TeleopContext context)
        {
            var deadman = m_LeftGrip.IsPressed() || m_RightGrip.IsPressed();
            intent.deadman |= deadman;

            var drive = ApplyDeadZone(m_LeftStick.ReadValue<Vector2>());
            intent.baseDrive += drive;

            var wrist = ApplyDeadZone(m_RightStick.ReadValue<Vector2>());
            intent.wristRollRate -= wrist.x * m_WristRate;
            intent.wristPitchRate += wrist.y * m_WristRate;

            if (context.handle != null && context.handle.TryGetGrabPose(out var pose))
            {
                intent.hasArmPose = true;
                intent.armPose = pose;
            }

            var trigger = m_RightTrigger.IsPressed();
            if (trigger != m_TriggerWasPressed)
                intent.gripper = trigger ? GripperIntent.Close : GripperIntent.Open;
            m_TriggerWasPressed = trigger;

            if (m_EStop.WasPressedThisFrame())
                intent.eStop = true;

            if (m_Home.WasPressedThisFrame())
                intent.homeArm = true;

            if (m_Reset.IsPressed())
            {
                m_ResetHeldFor += Time.unscaledDeltaTime;
                if (!m_ResetFired && m_ResetHeldFor >= m_ResetHoldTime)
                {
                    m_ResetFired = true;
                    intent.resetEStop = true;
                }
            }
            else
            {
                m_ResetHeldFor = 0f;
                m_ResetFired = false;
            }
        }

        Vector2 ApplyDeadZone(Vector2 stick)
        {
            var magnitude = stick.magnitude;
            if (magnitude <= m_DeadZone)
                return Vector2.zero;

            return stick / magnitude * Mathf.InverseLerp(m_DeadZone, 1f, magnitude);
        }
    }
}
