using UnityEngine;
using UnityEngine.InputSystem;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Third-person orbit camera that follows the selected robot. Right-drag orbits, scroll zooms.
    /// </summary>
    public class DesktopCameraRig : MonoBehaviour
    {
        [SerializeField]
        Camera m_Camera;

        [SerializeField, Tooltip("Yields scroll to the handle while it is being dragged.")]
        DesktopTeleopInput m_Input;

        [SerializeField, Tooltip("Height above the robot's base the camera looks at, in metres.")]
        float m_FocusHeight = 1.3f;

        [SerializeField]
        float m_Distance = 2.6f;

        [SerializeField]
        Vector2 m_DistanceRange = new Vector2(0.8f, 12f);

        [SerializeField]
        Vector2 m_PitchRange = new Vector2(-10f, 75f);

        [SerializeField, Tooltip("Degrees per pixel of right-drag.")]
        float m_OrbitSensitivity = 0.25f;

        [SerializeField, Tooltip("How quickly the camera catches up with its target. Lower is floatier.")]
        float m_FollowSharpness = 6f;

        Transform m_Target;
        float m_Yaw;
        float m_Pitch = 16f;
        Vector3 m_Focus;
        bool m_Initialised;

        public Camera rigCamera => m_Camera;

        /// <summary>Follows <paramref name="target"/>, swinging round to look over its shoulder.</summary>
        public void Focus(Transform target, bool snap)
        {
            m_Target = target;
            if (target == null)
                return;

            // Behind and to the right of the robot, looking along its forward at what it is working on,
            // low enough to stay under the overhead cable trays (2.45 m).
            m_Yaw = target.eulerAngles.y + 160f;
            m_Pitch = 16f;
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

            if (m_Target != null)
            {
                var goal = m_Target.position + Vector3.up * m_FocusHeight;
                m_Focus = Vector3.Lerp(m_Focus, goal, 1f - Mathf.Exp(-m_FollowSharpness * Time.unscaledDeltaTime));
            }

            Apply();
        }

        void Apply()
        {
            if (m_Camera == null)
                return;

            var rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            m_Camera.transform.SetPositionAndRotation(m_Focus - rotation * Vector3.forward * m_Distance, rotation);
        }

        /// <summary>Wires the rig from code. Used by the scene builder.</summary>
        public void Configure(Camera rigCamera, DesktopTeleopInput input)
        {
            m_Camera = rigCamera;
            m_Input = input;
        }
    }
}
