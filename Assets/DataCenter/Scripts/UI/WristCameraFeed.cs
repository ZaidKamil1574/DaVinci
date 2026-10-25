using UnityEngine;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>
    /// Renders the selected robot's wrist camera into a small <see cref="RenderTexture"/> at a
    /// fixed, low rate, for the HUD picture-in-picture and the VR panel.
    /// </summary>
    /// <remarks>
    /// The camera is rendered by hand with <see cref="Camera.Render"/> rather than left enabled, so
    /// it costs one extra scene pass every few frames instead of every frame. Resolution and rate
    /// are lower on Quest and in the browser, where that pass is most expensive.
    /// </remarks>
    public class WristCameraFeed : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [Header("Desktop")]
        [SerializeField] Vector2Int m_DesktopSize = new Vector2Int(480, 300);
        [SerializeField] float m_DesktopRate = 20f;

        [Header("Quest")]
        [SerializeField] Vector2Int m_QuestSize = new Vector2Int(256, 160);
        [SerializeField] float m_QuestRate = 10f;

        [Header("Web")]
        [SerializeField] Vector2Int m_WebSize = new Vector2Int(320, 200);
        [SerializeField] float m_WebRate = 12f;

        RenderTexture m_Texture;
        Camera m_Current;
        float m_NextRender;
        float m_Rate;

        /// <summary>The feed. Valid after Awake.</summary>
        public RenderTexture texture => m_Texture;

        /// <summary>Frames per second the feed is rendered at on this platform.</summary>
        public float rate => m_Rate;

        void Awake()
        {
            Vector2Int size;
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                    size = m_QuestSize;
                    m_Rate = m_QuestRate;
                    break;
                case RuntimePlatform.WebGLPlayer:
                    size = m_WebSize;
                    m_Rate = m_WebRate;
                    break;
                default:
                    size = m_DesktopSize;
                    m_Rate = m_DesktopRate;
                    break;
            }

            m_Texture = new RenderTexture(size.x, size.y, 16, RenderTextureFormat.ARGB32)
            {
                name = "Wrist Camera Feed",
                antiAliasing = 1,
                useMipMap = false,
            };
            m_Texture.Create();
        }

        void OnDestroy()
        {
            if (m_Current != null)
                m_Current.targetTexture = null;
            if (m_Texture != null)
                m_Texture.Release();
        }

        void LateUpdate()
        {
            var robot = m_Operator != null ? m_Operator.robot : null;
            var wristCamera = robot != null ? robot.wristCamera : null;

            if (wristCamera != m_Current)
            {
                if (m_Current != null)
                    m_Current.targetTexture = null;

                m_Current = wristCamera;
                if (m_Current != null)
                {
                    m_Current.enabled = false;
                    m_Current.targetTexture = m_Texture;
                    m_NextRender = 0f;
                }
            }

            if (m_Current == null || Time.unscaledTime < m_NextRender)
                return;

            m_NextRender = Time.unscaledTime + 1f / Mathf.Max(1f, m_Rate);
            m_Current.Render();
        }

        /// <summary>Wires the feed from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator) => m_Operator = teleopOperator;
    }
}
