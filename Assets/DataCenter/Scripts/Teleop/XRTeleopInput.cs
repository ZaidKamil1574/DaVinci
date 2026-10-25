using UnityEngine;
using UnityEngine.InputSystem;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Quest controller teleoperation, also used by WebXR in the Quest browser.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Left stick</b>: move the base, including sideways, in the direction the operator is looking.</item>
    /// <item><b>A</b> / <b>B</b>: rotate the base clockwise / anticlockwise.</item>
    /// <item><b>Right stick</b>: move the gripper left/right and up/down. <b>Hold the stick in</b> and push to reach forward/back.</item>
    /// <item><b>R2 (right trigger) + right stick</b>: rotate the wrist (left/right rolls, up/down pitches).</item>
    /// <item><b>L2 (left trigger)</b>: close the gripper while held.</item>
    /// <item><b>Right grip</b>: grab the handle at the gripper; while held, the right stick teleports (forward) and snap turns.</item>
    /// <item><b>X</b>: E-Stop; press again with hands off the controls to reset. <b>Y</b>: show / hide the dashboard.</item>
    /// <item><b>Left stick click</b>: home the arm.</item>
    /// </list>
    /// <para>
    /// Every motion control is hold-to-run: deflecting a stick or holding A / B enables motion by
    /// itself, and letting go stops the robot. A grip held also enables motion, for the handle.
    /// </para>
    /// <para>
    /// Directions follow the operator's view, not the robot's heading, so "push right" moves the
    /// base or gripper to the operator's right whichever way the robot faces.
    /// </para>
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

        [SerializeField, Tooltip("Gripper speed at full right stick, in metres per second.")]
        float m_ArmSpeed = 0.15f;

        [SerializeField, Tooltip("Wrist rate at full right stick with R2 held, in degrees per second.")]
        float m_WristRate = 60f;

        InputAction m_LeftGrip;
        InputAction m_RightGrip;
        InputAction m_LeftTrigger;
        InputAction m_RightTrigger;
        InputAction m_LeftStick;
        InputAction m_RightStick;
        InputAction m_RightStickClick;
        InputAction m_TurnClockwise;
        InputAction m_TurnAnticlockwise;
        InputAction m_Stop;
        InputAction m_Dashboard;
        InputAction m_Home;
        bool m_GripperWasClosed;

        public bool isActive => XRPlatform.isImmersive;

        public string displayName => "XR Controllers";

        /// <summary>Whether the right grip is held. The right stick then belongs to teleport and turn.</summary>
        public bool rightGripHeld => m_RightGrip != null && m_RightGrip.IsPressed();

        void Awake()
        {
            m_LeftGrip = Button("Left Grip", "<XRController>{LeftHand}/{GripButton}", "<XRController>{LeftHand}/gripPressed");
            m_RightGrip = Button("Right Grip", "<XRController>{RightHand}/{GripButton}", "<XRController>{RightHand}/gripPressed");
            m_LeftTrigger = Button("Gripper (L2)", "<XRController>{LeftHand}/{TriggerButton}", "<XRController>{LeftHand}/triggerPressed");
            m_RightTrigger = Button("Wrist modifier (R2)", "<XRController>{RightHand}/{TriggerButton}", "<XRController>{RightHand}/triggerPressed");
            m_TurnClockwise = Button("Turn Clockwise (A)", "<XRController>{RightHand}/{PrimaryButton}", "<XRController>{RightHand}/primaryButton");
            m_TurnAnticlockwise = Button("Turn Anticlockwise (B)", "<XRController>{RightHand}/{SecondaryButton}", "<XRController>{RightHand}/secondaryButton");
            m_Stop = Button("E-Stop / reset (X)", "<XRController>{LeftHand}/{PrimaryButton}", "<XRController>{LeftHand}/primaryButton");
            m_Dashboard = Button("Dashboard (Y)", "<XRController>{LeftHand}/{SecondaryButton}", "<XRController>{LeftHand}/secondaryButton");
            m_Home = Button("Home (left stick click)", "<XRController>{LeftHand}/{Primary2DAxisClick}", "<XRController>{LeftHand}/thumbstickClicked");
            m_RightStickClick = Button("Reach (right stick click)", "<XRController>{RightHand}/{Primary2DAxisClick}", "<XRController>{RightHand}/thumbstickClicked");

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

        InputAction[] all => new[]
        {
            m_LeftGrip, m_RightGrip, m_LeftTrigger, m_RightTrigger, m_LeftStick, m_RightStick, m_RightStickClick,
            m_TurnClockwise, m_TurnAnticlockwise, m_Stop, m_Dashboard, m_Home,
        };

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
            var grip = m_LeftGrip.IsPressed() || m_RightGrip.IsPressed();
            var leftStick = ApplyDeadZone(m_LeftStick.ReadValue<Vector2>());

            // While the right grip is held the right stick is the rig's teleport and snap turn.
            var rightStick = m_RightGrip.IsPressed() ? Vector2.zero : ApplyDeadZone(m_RightStick.ReadValue<Vector2>());

            var turning = m_TurnClockwise.IsPressed() || m_TurnAnticlockwise.IsPressed();

            // Hold-to-run: any motion control held enables motion; let go of everything and it stops.
            intent.deadman |= grip || leftStick != Vector2.zero || rightStick != Vector2.zero || turning || m_Home.IsPressed();

            // Directions on the floor plane as the operator sees them.
            var view = context.camera != null ? context.camera.transform : context.robotBase;
            var viewForward = view != null ? Flatten(view.forward, Vector3.forward) : Vector3.forward;
            var viewRight = Vector3.Cross(Vector3.up, viewForward);

            // Left stick: move the base.
            if (leftStick != Vector2.zero && context.robotBase != null)
            {
                var world = viewRight * leftStick.x + viewForward * leftStick.y;
                intent.baseDrive.y += Vector3.Dot(world, Flatten(context.robotBase.forward, Vector3.forward));
                intent.baseStrafe += Vector3.Dot(world, Flatten(context.robotBase.right, Vector3.right));
            }

            // A / B: rotate on the spot. Positive turn is clockwise seen from above.
            if (m_TurnClockwise.IsPressed())
                intent.baseDrive.x += m_TurnShare;
            if (m_TurnAnticlockwise.IsPressed())
                intent.baseDrive.x -= m_TurnShare;

            // Right stick: the gripper. R2 turns it into wrist rotation; holding the stick in turns
            // up/down into reaching forward/back.
            if (rightStick != Vector2.zero)
            {
                if (m_RightTrigger.IsPressed())
                {
                    intent.wristRollRate -= rightStick.x * m_WristRate;
                    intent.wristPitchRate += rightStick.y * m_WristRate;
                }
                else if (m_RightStickClick.IsPressed())
                {
                    intent.armNudge += (viewRight * rightStick.x + viewForward * rightStick.y) * m_ArmSpeed;
                }
                else
                {
                    intent.armNudge += (viewRight * rightStick.x + Vector3.up * rightStick.y) * m_ArmSpeed;
                }
            }

            if (context.handle != null && context.handle.TryGetGrabPose(out var pose))
            {
                intent.hasArmPose = true;
                intent.armPose = pose;
            }

            // L2: gripper, closed while held.
            var closed = m_LeftTrigger.IsPressed();
            if (closed != m_GripperWasClosed)
                intent.gripper = closed ? GripperIntent.Close : GripperIntent.Open;
            m_GripperWasClosed = closed;

            // X: E-Stop, and the same button resets it. The robot refuses the reset while any
            // control is still held, so a reset can never start motion.
            if (m_Stop.WasPressedThisFrame())
            {
                if (context.robotEStopped)
                    intent.resetEStop = true;
                else
                    intent.eStop = true;
            }

            if (m_Dashboard.WasPressedThisFrame())
                intent.toggleDashboard = true;

            if (m_Home.WasPressedThisFrame())
                intent.homeArm = true;
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
