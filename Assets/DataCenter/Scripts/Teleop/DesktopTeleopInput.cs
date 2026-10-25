using UnityEngine;
using UnityEngine.InputSystem;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Mouse and keyboard teleoperation for the desktop and the non-immersive web build.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Every motion key is hold-to-run: holding it is the deadman, letting go stops. <b>Space</b> is a deadman on its own, for clicks.</item>
    /// <item><b>W A S D</b>: drive the base. <b>Space + right-click</b> the floor: navigate there.</item>
    /// <item><b>Arrows</b>: move the gripper up, down, left and right as seen on screen. <b>R</b>, <b>F</b>: reach out, pull back.
    /// A tap moves a little; holding speeds up.</item>
    /// <item><b>Q</b>, <b>E</b>: tilt the gripper up, down. <b>Z</b>, <b>X</b>: roll it.</item>
    /// <item>Tap <b>Shift</b> (or <b>G</b>): grab or let go. <b>Shift + W A S D</b> moves the camera instead, see <see cref="DesktopCameraRig"/>.</item>
    /// <item>Hold <b>H</b>: bring the arm home. <b>1</b>-<b>4</b>: take control of another robot. <b>Tab</b>: Direct / Console mode.</item>
    /// <item><b>Backspace</b>: E-Stop. <b>Shift + Backspace</b>: reset.</item>
    /// <item><b>Left-drag the handle</b>: move the tool in the camera plane. Scroll while dragging to push or pull.</item>
    /// </list>
    /// </remarks>
    public class DesktopTeleopInput : MonoBehaviour, ITeleopInput
    {
        [SerializeField, Tooltip("Layers the handle is on.")]
        LayerMask m_HandleLayers;

        [SerializeField, Tooltip("Layers that count as floor for point-and-click navigation.")]
        LayerMask m_FloorLayers = ~0;

        [SerializeField, Tooltip("Tool speed when a key is first pressed, in metres per second. A tap is a fine adjustment.")]
        float m_SlowNudgeSpeed = 0.05f;

        [SerializeField, Tooltip("Tool speed once a key has been held, in metres per second.")]
        float m_FastNudgeSpeed = 0.3f;

        [SerializeField, Tooltip("Seconds a key is held at the slow speed before speeding up.")]
        float m_RampDelay = 0.25f;

        [SerializeField, Tooltip("Seconds from slow to full speed.")]
        float m_RampTime = 1f;

        [SerializeField, Tooltip("Wrist rate, in degrees per second.")]
        float m_WristRate = 60f;

        [SerializeField, Tooltip("Longest press of Shift alone that counts as a tap, which grabs or lets go.")]
        float m_ShiftTapTime = 0.35f;

        [SerializeField, Tooltip("Seconds a grab tap enables motion for, so the gripper command reaches the robot. Nothing else moves meanwhile.")]
        float m_GripPulse = 0.35f;

        [SerializeField, Tooltip("Push/pull per scroll notch while dragging, in metres.")]
        float m_ScrollDepthStep = 0.02f;

        [SerializeField, Tooltip("Turned off while a headset is active.")]
        bool m_DisableInXR = true;

        bool m_Dragging;
        Plane m_DragPlane;
        Vector3 m_DragOffset;
        Quaternion m_DragRotation;
        Vector2 m_RightPressPosition;
        float m_RightPressTime;
        float m_ArmHeldSince = -1f;
        float m_ShiftDownAt;
        bool m_ShiftCombined;
        float m_GripPulseUntil = -1f;

        /// <summary>Whether the mouse is dragging the handle. The camera rig yields scroll while this is true.</summary>
        public bool isDraggingHandle => m_Dragging;

        public bool isActive => Keyboard.current != null && (!m_DisableInXR || !XRPlatform.isImmersive);

        public string displayName => "Keyboard + Mouse";

        /// <summary>
        /// The world directions the arrow keys and R/F move the tool in: right and reach are the
        /// camera's right and forward, flattened, so the gripper moves the way it looks on screen.
        /// Up is always straight up. Shared with the guidance hints, so they name the right keys.
        /// </summary>
        public static void GetArmAxes(Camera camera, Transform robotBase, out Vector3 right, out Vector3 reach)
        {
            var view = camera != null ? camera.transform : robotBase;
            right = view != null ? Vector3.ProjectOnPlane(view.right, Vector3.up) : Vector3.right;
            reach = view != null ? Vector3.ProjectOnPlane(view.forward, Vector3.up) : Vector3.forward;

            // Looking straight down flattens forward to nothing; fall back to the robot's heading.
            if (reach.sqrMagnitude < 0.01f && robotBase != null)
                reach = Vector3.ProjectOnPlane(robotBase.forward, Vector3.up);
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(Vector3.up, reach);

            right.Normalize();
            reach.Normalize();
        }

        public void Poll(ref TeleopIntent intent, in TeleopContext context)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
                return;

            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            // Base. With Shift held, W A S D belong to the camera.
            var drive = Vector2.zero;
            if (!shift)
            {
                if (keyboard.wKey.isPressed) drive.y += 1f;
                if (keyboard.sKey.isPressed) drive.y -= 1f;
                if (keyboard.dKey.isPressed) drive.x += 1f;
                if (keyboard.aKey.isPressed) drive.x -= 1f;
            }

            intent.baseDrive += drive;

            // Tool, as seen on screen: arrows up, down, left, right; R and F reach out and pull back.
            var axes = Vector3.zero;
            if (keyboard.upArrowKey.isPressed) axes.y += 1f;
            if (keyboard.downArrowKey.isPressed) axes.y -= 1f;
            if (keyboard.rightArrowKey.isPressed) axes.x += 1f;
            if (keyboard.leftArrowKey.isPressed) axes.x -= 1f;
            if (keyboard.rKey.isPressed) axes.z += 1f;
            if (keyboard.fKey.isPressed) axes.z -= 1f;

            if (axes != Vector3.zero)
            {
                // A tap moves a centimetre or so; holding speeds up, so lining up is easy and long moves are quick.
                if (m_ArmHeldSince < 0f)
                    m_ArmHeldSince = Time.unscaledTime;
                var ramp = Mathf.InverseLerp(m_RampDelay, m_RampDelay + m_RampTime, Time.unscaledTime - m_ArmHeldSince);
                var speed = Mathf.Lerp(m_SlowNudgeSpeed, m_FastNudgeSpeed, ramp * ramp);

                GetArmAxes(context.camera, context.robotBase, out var right, out var reach);
                var direction = right * axes.x + Vector3.up * axes.y + reach * axes.z;
                intent.armNudge += direction.normalized * speed;
            }
            else
            {
                m_ArmHeldSince = -1f;
            }

            var wrist = false;
            if (keyboard.qKey.isPressed) { intent.wristPitchRate -= m_WristRate; wrist = true; }
            if (keyboard.eKey.isPressed) { intent.wristPitchRate += m_WristRate; wrist = true; }
            if (keyboard.zKey.isPressed) { intent.wristRollRate += m_WristRate; wrist = true; }
            if (keyboard.xKey.isPressed) { intent.wristRollRate -= m_WristRate; wrist = true; }

            // Homing moves the arm, so it is hold-to-run too: hold H until the arm is home.
            var home = keyboard.hKey.isPressed;
            intent.homeArm |= home;

            // A grab is a tap, with nothing to hold, so it enables motion briefly: commands only
            // reach the robot while the deadman is held. The fingers finish closing by themselves.
            if (keyboard.gKey.wasPressedThisFrame || ShiftTapped(keyboard))
            {
                intent.gripper = GripperIntent.Toggle;
                m_GripPulseUntil = Time.unscaledTime + m_GripPulse;
            }

            // Hold-to-run, as on the Quest: holding any motion key is the deadman. Space still is,
            // for point-and-click moves that have no key to hold.
            intent.deadman |= keyboard.spaceKey.isPressed || drive != Vector2.zero || axes != Vector3.zero || wrist || home || m_Dragging
                || Time.unscaledTime < m_GripPulseUntil;

            // 1-4 switch robots, matching the order of the fleet list.
            if (keyboard.digit1Key.wasPressedThisFrame) intent.selectRobot = 1;
            if (keyboard.digit2Key.wasPressedThisFrame) intent.selectRobot = 2;
            if (keyboard.digit3Key.wasPressedThisFrame) intent.selectRobot = 3;
            if (keyboard.digit4Key.wasPressedThisFrame) intent.selectRobot = 4;

            if (keyboard.tabKey.wasPressedThisFrame)
                intent.toggleMode = true;

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                if (shift)
                    intent.resetEStop = true;
                else
                    intent.eStop = true;
            }

            if (mouse != null && context.camera != null)
                PollMouse(mouse, ref intent, context);
        }

        /// <summary>
        /// A quick press and release of either Shift with nothing else pressed. Shift held with
        /// another key (W A S D for the camera, Backspace for reset) is a modifier, not a tap.
        /// </summary>
        bool ShiftTapped(Keyboard keyboard)
        {
            var left = keyboard.leftShiftKey;
            var right = keyboard.rightShiftKey;
            if (left.wasPressedThisFrame || right.wasPressedThisFrame)
            {
                m_ShiftDownAt = Time.unscaledTime;
                m_ShiftCombined = false;
            }

            if ((left.isPressed || right.isPressed) && !m_ShiftCombined)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key != null && key.isPressed && key != left && key != right)
                    {
                        m_ShiftCombined = true;
                        break;
                    }
                }
            }

            var released = (left.wasReleasedThisFrame || right.wasReleasedThisFrame) && !left.isPressed && !right.isPressed;
            return released && !m_ShiftCombined && Time.unscaledTime - m_ShiftDownAt <= m_ShiftTapTime;
        }

        void PollMouse(Mouse mouse, ref TeleopIntent intent, in TeleopContext context)
        {
            var screen = mouse.position.ReadValue();
            var ray = context.camera.ScreenPointToRay(screen);

            if (mouse.leftButton.wasPressedThisFrame && !UIPointerGate.IsOverUI(screen))
            {
                if (Physics.Raycast(ray, out var hit, 30f, m_HandleLayers, QueryTriggerInteraction.Collide))
                {
                    var pose = context.commandedPose;
                    m_DragPlane = new Plane(-context.camera.transform.forward, pose.position);
                    m_DragPlane.Raycast(ray, out var enter);
                    m_DragOffset = pose.position - ray.GetPoint(enter);
                    m_DragRotation = pose.rotation;
                    m_Dragging = true;
                }
            }

            if (m_Dragging)
            {
                if (!mouse.leftButton.isPressed)
                {
                    m_Dragging = false;
                }
                else
                {
                    var scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        // Push the drag plane away from or toward the camera.
                        var notches = Mathf.Sign(scroll);
                        m_DragPlane.Translate(-context.camera.transform.forward * (notches * m_ScrollDepthStep));
                    }

                    if (m_DragPlane.Raycast(ray, out var enter))
                    {
                        intent.hasArmPose = true;

                        // Keep wrist edits made with the keys while dragging.
                        m_DragRotation = context.commandedPose.rotation;
                        intent.armPose = new Pose(ray.GetPoint(enter) + m_DragOffset, m_DragRotation);
                    }
                }
            }

            // Right click without a drag is point-and-click navigation; right drag orbits the camera.
            if (mouse.rightButton.wasPressedThisFrame)
            {
                m_RightPressPosition = screen;
                m_RightPressTime = Time.unscaledTime;
            }

            if (mouse.rightButton.wasReleasedThisFrame
                && (screen - m_RightPressPosition).sqrMagnitude < 36f
                && Time.unscaledTime - m_RightPressTime < 0.35f
                && !UIPointerGate.IsOverUI(screen)
                && Physics.Raycast(ray, out var floorHit, 60f, m_FloorLayers, QueryTriggerInteraction.Ignore)
                && floorHit.normal.y > 0.7f)
            {
                intent.hasNavGoal = true;
                intent.navGoal = floorHit.point;
            }
        }

        /// <summary>Wires the input from code. Used by the scene builder.</summary>
        public void Configure(LayerMask handleLayers, LayerMask floorLayers)
        {
            m_HandleLayers = handleLayers;
            m_FloorLayers = floorLayers;
        }
    }
}
