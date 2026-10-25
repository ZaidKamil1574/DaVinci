using UnityEngine;
using UnityEngine.InputSystem;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Third-person orbit camera that follows the selected robot. Right-drag orbits, scroll zooms,
    /// and Shift + W A S D moves the camera freely around the hall.
    /// </summary>
    /// <remarks>
    /// Free movement detaches the camera from the robot until the robot is driven (W A S D without
    /// Shift), another robot is selected, or V is pressed, which all bring it back.
    /// </remarks>
    public class DesktopCameraRig : MonoBehaviour
    {
        [SerializeField]
        Camera m_Camera;

        [SerializeField, Tooltip("Yields scroll to the handle while it is being dragged.")]
        DesktopTeleopInput m_Input;

        [SerializeField, Tooltip("Height above the robot's base the camera looks at, in metres.")]
        float m_FocusHeight = 1.3f;

        [SerializeField]
        float m_Distance = 2.2f;

        [SerializeField]
        Vector2 m_DistanceRange = new Vector2(0.8f, 12f);

        [SerializeField]
        Vector2 m_PitchRange = new Vector2(-10f, 75f);

        [SerializeField, Tooltip("Degrees per pixel of right-drag.")]
        float m_OrbitSensitivity = 0.25f;

        [SerializeField, Tooltip("How quickly the camera catches up with its target. Lower is floatier.")]
        float m_FollowSharpness = 6f;

        [SerializeField, Tooltip("Layers the camera will not pass through: racks, walls, cabinets. It pulls in toward the robot instead.")]
        LayerMask m_CollisionLayers;

        [SerializeField, Tooltip("Shift + W A S D camera speed, in metres per second.")]
        float m_FreeMoveSpeed = 2.5f;

        Transform m_Target;
        float m_Yaw;
        float m_Pitch = 16f;
        Vector3 m_Focus;
        bool m_Initialised;
        float m_CurrentDistance = -1f;
        bool m_Free;

        public Camera rigCamera => m_Camera;

        /// <summary>Whether the camera has been moved away from the robot with Shift + W A S D.</summary>
        public bool isFree => m_Free;

        /// <summary>Goes back to following the robot after free movement.</summary>
        public void Follow()
        {
            if (!m_Free)
                return;

            m_Free = false;
            ToastEvent.Show("Camera back on the robot");
        }

        /// <summary>Follows <paramref name="target"/>, swinging round to look over its shoulder.</summary>
        public void Focus(Transform target, bool snap)
        {
            m_Target = target;
            m_Free = false;
            if (target == null)
                return;

            // Over the robot's shoulder: behind it and a little to one side, looking the way it faces,
            // at what it is working on. Collision below keeps the camera inside the aisle.
            m_Yaw = target.eulerAngles.y + 25f;
            m_Pitch = 26f;
            if (snap || !m_Initialised)
            {
                m_Focus = target.position + Vector3.up * m_FocusHeight;
                m_Initialised = true;
                Apply();
            }
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    var delta = mouse.delta.ReadValue();
                    m_Yaw += delta.x * m_OrbitSensitivity;
                    m_Pitch = Mathf.Clamp(m_Pitch - delta.y * m_OrbitSensitivity, m_PitchRange.x, m_PitchRange.y);
                }

                var scroll = mouse.scroll.ReadValue().y;
                var overUI = UIPointerGate.IsOverUI(mouse.position.ReadValue());
                if (Mathf.Abs(scroll) > 0.01f && !overUI && (m_Input == null || !m_Input.isDraggingHandle))
                    m_Distance = Mathf.Clamp(m_Distance * (scroll > 0f ? 0.9f : 1.1f), m_DistanceRange.x, m_DistanceRange.y);
            }

            MoveFreely();

            if (m_Target != null && !m_Free)
            {
                var goal = m_Target.position + Vector3.up * m_FocusHeight;
                m_Focus = Vector3.Lerp(m_Focus, goal, 1f - Mathf.Exp(-m_FollowSharpness * Time.unscaledDeltaTime));
            }

            Apply();
        }

        void MoveFreely()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            var move = Vector2.zero;
            if (keyboard.wKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (shift && move != Vector2.zero)
            {
                if (!m_Free)
                {
                    m_Free = true;
                    ToastEvent.Show("Moving the camera. Press V or drive the robot to follow it again");
                }

                // Along the floor, the way the camera faces, so W is always "into the screen".
                var heading = Quaternion.Euler(0f, m_Yaw, 0f);
                m_Focus += heading * new Vector3(move.x, 0f, move.y).normalized * (m_FreeMoveSpeed * Time.unscaledDeltaTime);
            }
            else if (m_Free && (move != Vector2.zero || keyboard.vKey.wasPressedThisFrame))
            {
                // Driving the robot, or V, brings the camera back to it.
                Follow();
            }
        }

        void Apply()
        {
            if (m_Camera == null)
                return;

            var rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            var back = rotation * Vector3.back;

            // Pull in rather than pass through racks and walls: aisles are about two metres wide,
            // narrower than a comfortable orbit distance. Pull in at once, ease back out.
            var allowed = m_Distance;
            if (m_CollisionLayers.value != 0 && Physics.SphereCast(m_Focus, 0.15f, back, out var hit, m_Distance, m_CollisionLayers, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.4f, hit.distance - 0.05f);

            m_CurrentDistance = m_CurrentDistance < 0f || allowed < m_CurrentDistance
                ? allowed
                : Mathf.Lerp(m_CurrentDistance, allowed, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));

            m_Camera.transform.SetPositionAndRotation(m_Focus + back * m_CurrentDistance, rotation);
        }

        /// <summary>Wires the rig from code. Used by the scene builder.</summary>
        public void Configure(Camera rigCamera, DesktopTeleopInput input, LayerMask collisionLayers)
        {
            m_CollisionLayers = collisionLayers;
            m_Camera = rigCamera;
            m_Input = input;
        }
    }
}
