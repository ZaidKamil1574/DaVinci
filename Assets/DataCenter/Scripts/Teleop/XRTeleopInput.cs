using UnityEngine;
using UnityEngine.InputSystem;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Quest controller teleoperation, also used by WebXR in the Quest browser.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Either grip</b>: deadman for the arm. Gripping the target handle also grabs it, so one squeeze both enables and steers.</item>
    /// <item>Base controls are hold-to-run: deflecting the left stick or holding A / B enables motion by
    /// itself, and letting go stops the robot. A separate grip on top made driving two-handed work.</item>
    /// <item><b>Right trigger</b>: close the gripper while held, open on release.</item>
    /// <item><b>Left stick</b>: move the base, including sideways. Directions follow where you are looking, not where the robot faces.</item>
    /// <item><b>A</b> / <b>B</b>: rotate the base clockwise / anticlockwise while held.</item>
    /// <item><b>Left stick click</b>: home the arm.</item>
    /// <item><b>Y</b>: E-Stop. <b>X</b>: reset (refused while any motion control is held).</item>
    /// <item><b>Right stick</b>: left free for the rig's own teleport (forward) and snap turn.</item>
    /// </list>
    /// Bindings use XR usages ({GripButton}, {Primary2DAxis}) rather than device-specific paths, so
    /// the same actions bind to OpenXR Touch controllers and to WebXR's generic controllers.
    /// </remarks>
    public class XRTeleopInput : MonoBehaviour, ITeleopInput
    {
        [SerializeField, Tooltip("Stick dead zone.")]
        float m_DeadZone = 0.2f;


        [SerializeField, Tooltip("Turn rate from A / B, as a share of the robot's top turn rate.")]
        [Range(0.1f, 1f)]
        float m_TurnShare = 0.6f;

        InputAction m_LeftGrip;
        InputAction m_RightGrip;
        InputAction m_RightTrigger;
        InputAction m_LeftStick;
        InputAction m_TurnClockwise;
        InputAction m_TurnAnticlockwise;
        InputAction m_EStop;
        InputAction m_Reset;
        InputAction m_Home;
        bool m_TriggerWasPressed;

        public bool isActive => XRPlatform.isImmersive;

        public string displayName => "XR Controllers";


        void Awake()
        {
            m_LeftGrip = Button("Left Grip", "<XRController>{LeftHand}/{GripButton}", "<XRController>{LeftHand}/gripPressed");
            m_RightGrip = Button("Right Grip", "<XRController>{RightHand}/{GripButton}", "<XRController>{RightHand}/gripPressed");
            m_RightTrigger = Button("Right Trigger", "<XRController>{RightHand}/{TriggerButton}", "<XRController>{RightHand}/triggerPressed");
            m_TurnClockwise = Button("Turn Clockwise (A)", "<XRController>{RightHand}/{PrimaryButton}", "<XRController>{RightHand}/primaryButton");
            m_TurnAnticlockwise = Button("Turn Anticlockwise (B)", "<XRController>{RightHand}/{SecondaryButton}", "<XRController>{RightHand}/secondaryButton");
            m_EStop = Button("E-Stop (Y)", "<XRController>{LeftHand}/{SecondaryButton}", "<XRController>{LeftHand}/secondaryButton");
            m_Reset = Button("Reset (X)", "<XRController>{LeftHand}/{PrimaryButton}", "<XRController>{LeftHand}/primaryButton");
            m_Home = Button("Home (left stick click)", "<XRController>{LeftHand}/{Primary2DAxisClick}", "<XRController>{LeftHand}/thumbstickClicked");

            m_LeftStick = new InputAction("Left Stick", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}");
            m_LeftStick.AddBinding("<XRController>{LeftHand}/thumbstick");
        }

        static InputAction Button(string name, string primary, string fallback)
        {
            var action = new InputAction(name, InputActionType.Button, primary);
            action.AddBinding(fallback);
            return action;
        }

        InputAction[] all => new[] { m_LeftGrip, m_RightGrip, m_RightTrigger, m_LeftStick, m_TurnClockwise, m_TurnAnticlockwise, m_EStop, m_Reset, m_Home };

        void OnEnable()
        {
            foreach (var action in all)
                action.Enable();
        }

        void OnDisable()
        {
            foreach (var action in all)
                action.Disable();
        }

        void OnDestroy()
        {
            foreach (var action in all)
                action.Dispose();
        }

        public void Poll(ref TeleopIntent intent, in TeleopContext context)
        {
            var deadman = m_LeftGrip.IsPressed() || m_RightGrip.IsPressed();

            // Hold-to-run: the spring-return stick and the A / B buttons are enabling devices in
            // their own right, so any of them held counts as the deadman. Release them all and the
            // robot stops, exactly as when a grip is released.
            var driving = m_LeftStick.ReadValue<Vector2>().sqrMagnitude > m_DeadZone * m_DeadZone
                          || m_TurnClockwise.IsPressed() || m_TurnAnticlockwise.IsPressed();
            intent.deadman |= deadman || driving || m_Home.IsPressed();

            // Left stick: move, in the direction the operator is looking. The stick is turned into a
            // floor-plane direction from the head pose, then expressed in the robot's own frame, so
            // "push forward" means "away from me" whichever way the robot happens to face.
            var stick = ApplyDeadZone(m_LeftStick.ReadValue<Vector2>());
            if (stick != Vector2.zero && context.robotBase != null)
            {
                var view = context.camera != null ? context.camera.transform : context.robotBase;
                var forward = Flatten(view.forward, context.robotBase.forward);
                var right = Vector3.Cross(Vector3.up, forward);
                var world = right * stick.x + forward * stick.y;

                intent.baseDrive.y += Vector3.Dot(world, Flatten(context.robotBase.forward, Vector3.forward));
                intent.baseStrafe += Vector3.Dot(world, Flatten(context.robotBase.right, Vector3.right));
            }

            // A / B: rotate on the spot. Positive turn is clockwise seen from above.
            if (m_TurnClockwise.IsPressed())
                intent.baseDrive.x += m_TurnShare;
            if (m_TurnAnticlockwise.IsPressed())
                intent.baseDrive.x -= m_TurnShare;

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

            if (m_Reset.WasPressedThisFrame())
                intent.resetEStop = true;
        }

        static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : fallback;
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
