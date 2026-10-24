using UnityEngine;
using UnityEngine.InputSystem;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Mouse and keyboard teleoperation for the desktop and the non-immersive web build.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Hold <b>Space</b>: deadman. Nothing moves without it.</item>
    /// <item><b>Left-drag the handle</b>: move the tool in the camera plane. Scroll while dragging to push or pull.</item>
    /// <item><b>Arrows</b> / <b>R</b>, <b>F</b>: nudge the tool forward, back, left, right, up, down relative to the robot. Hold Shift for fine.</item>
    /// <item><b>Q</b>, <b>E</b>: wrist roll. <b>Z</b>, <b>X</b>: wrist pitch.</item>
    /// <item><b>G</b>: toggle gripper. <b>H</b>: home the arm.</item>
    /// <item><b>W A S D</b>: drive the base. <b>Right-click</b> the floor: navigate there.</item>
    /// <item><b>Backspace</b>: E-Stop. <b>Shift + Backspace</b>: reset.</item>
    /// <item><b>1</b>-<b>4</b>: take control of another robot. <b>Tab</b>: Direct / Console mode.</item>
    /// </list>
    /// </remarks>
    public class DesktopTeleopInput : MonoBehaviour, ITeleopInput
    {
        [SerializeField, Tooltip("Layers the handle is on.")]
        LayerMask m_HandleLayers;

        [SerializeField, Tooltip("Layers that count as floor for point-and-click navigation.")]
        LayerMask m_FloorLayers = ~0;

        [SerializeField, Tooltip("Tool nudge speed, in metres per second.")]
        float m_NudgeSpeed = 0.25f;

        [SerializeField, Tooltip("Tool nudge speed with Shift held, in metres per second.")]
        float m_FineNudgeSpeed = 0.04f;

        [SerializeField, Tooltip("Wrist rate, in degrees per second.")]
        float m_WristRate = 60f;

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

        /// <summary>Whether the mouse is dragging the handle. The camera rig yields scroll while this is true.</summary>
        public bool isDraggingHandle => m_Dragging;

        public bool isActive => Keyboard.current != null && (!m_DisableInXR || !XRPlatform.isImmersive);

        public string displayName => "Keyboard + Mouse";

        public void Poll(ref TeleopIntent intent, in TeleopContext context)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
                return;

            intent.deadman |= keyboard.spaceKey.isPressed;

            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            // Base
            var drive = Vector2.zero;
            if (keyboard.wKey.isPressed) drive.y += 1f;
            if (keyboard.sKey.isPressed) drive.y -= 1f;
            if (keyboard.dKey.isPressed) drive.x += 1f;
            if (keyboard.aKey.isPressed) drive.x -= 1f;
            intent.baseDrive += drive;

            // Tool nudges, robot relative so "forward" always means toward what the robot faces.
            var nudge = Vector3.zero;
            if (keyboard.upArrowKey.isPressed) nudge.z += 1f;
            if (keyboard.downArrowKey.isPressed) nudge.z -= 1f;
            if (keyboard.rightArrowKey.isPressed) nudge.x += 1f;
            if (keyboard.leftArrowKey.isPressed) nudge.x -= 1f;
            if (keyboard.rKey.isPressed) nudge.y += 1f;
            if (keyboard.fKey.isPressed) nudge.y -= 1f;
            if (nudge != Vector3.zero && context.robotBase != null)
                intent.armNudge += context.robotBase.TransformDirection(nudge.normalized) * (shift ? m_FineNudgeSpeed : m_NudgeSpeed);

            var wristRate = shift ? m_WristRate * 0.25f : m_WristRate;
            if (keyboard.qKey.isPressed) intent.wristRollRate += wristRate;
            if (keyboard.eKey.isPressed) intent.wristRollRate -= wristRate;
            if (keyboard.zKey.isPressed) intent.wristPitchRate += wristRate;
            if (keyboard.xKey.isPressed) intent.wristPitchRate -= wristRate;

            if (keyboard.gKey.wasPressedThisFrame)
                intent.gripper = GripperIntent.Toggle;

            if (keyboard.hKey.wasPressedThisFrame)
                intent.homeArm = true;

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
