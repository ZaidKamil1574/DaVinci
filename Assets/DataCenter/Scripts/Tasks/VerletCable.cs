using System.Collections.Generic;
using UnityEngine;

namespace DataCenter.Tasks
{
    /// <summary>
    /// A cable between two <see cref="CableConnector"/>s, simulated as a Verlet rope and drawn
    /// with a <see cref="LineRenderer"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately cheap: a handful of points, a few constraint passes, no colliders. That runs the
    /// same on a Quest and in a browser, where a physics-joint rope or Obi would not. The rope is
    /// visual only; the connectors are the physical objects, so the cable never fights the gripper.
    /// </remarks>
    [RequireComponent(typeof(LineRenderer))]
    public class VerletCable : MonoBehaviour
    {
        [SerializeField]
        CableConnector m_EndA;

        [SerializeField]
        CableConnector m_EndB;

        [SerializeField, Tooltip("Rest length of the cable, in metres.")]
        float m_Length = 2.5f;

        [SerializeField, Range(4, 48), Tooltip("Simulated points. More looks smoother and costs more.")]
        int m_Points = 18;

        [SerializeField, Range(1, 16), Tooltip("Length-constraint passes per step. More is stiffer.")]
        int m_Iterations = 6;

        [SerializeField, Range(0.8f, 1f), Tooltip("Velocity kept per step. Lower settles faster.")]
        float m_Damping = 0.97f;

        [SerializeField, Tooltip("Height of the floor the cable rests on, in world units.")]
        float m_FloorHeight;

        [SerializeField, Tooltip("Cable id, for example A-C01.")]
        string m_CableId = "C01";

        Vector3[] m_Current;
        Vector3[] m_Previous;
        LineRenderer m_Line;
        float m_Accumulator;

        const float k_Step = 1f / 60f;

        public CableConnector endA => m_EndA;
        public CableConnector endB => m_EndB;
        public string cableId => m_CableId;

        static readonly Dictionary<string, VerletCable> s_ById = new Dictionary<string, VerletCable>();

        /// <summary>An enabled cable by id, or null.</summary>
        public static VerletCable Find(string id) => id != null && s_ById.TryGetValue(id, out var cable) ? cable : null;

        /// <summary>Every enabled cable.</summary>
        public static IEnumerable<VerletCable> all => s_ById.Values;

        void OnEnable()
        {
            if (!string.IsNullOrEmpty(m_CableId))
                s_ById[m_CableId] = this;
        }

        void OnDisable()
        {
            if (!string.IsNullOrEmpty(m_CableId) && s_ById.TryGetValue(m_CableId, out var registered) && registered == this)
                s_ById.Remove(m_CableId);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_ById.Clear();

        /// <summary>Straight-line distance between the two ends as a share of the cable length.</summary>
        public float tension => m_EndA != null && m_EndB != null
            ? Vector3.Distance(m_EndA.cableAnchor.position, m_EndB.cableAnchor.position) / Mathf.Max(0.01f, m_Length)
            : 0f;

        /// <summary>The connector at the other end from <paramref name="end"/>.</summary>
        public CableConnector Other(CableConnector end) => end == m_EndA ? m_EndB : m_EndA;

        void Awake()
        {
            m_Line = GetComponent<LineRenderer>();
            m_Line.useWorldSpace = true;
            m_Line.positionCount = m_Points;

            if (m_EndA != null)
                m_EndA.cable = this;
            if (m_EndB != null)
                m_EndB.cable = this;

            m_Current = new Vector3[m_Points];
            m_Previous = new Vector3[m_Points];
            Reinitialise();
        }

        /// <summary>Lays the points along a sagging curve between the ends.</summary>
        public void Reinitialise()
        {
            if (m_EndA == null || m_EndB == null)
                return;

            var a = m_EndA.cableAnchor.position;
            var b = m_EndB.cableAnchor.position;
            for (var i = 0; i < m_Points; i++)
            {
                var t = i / (float)(m_Points - 1);
                var p = Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * m_Length * 0.15f);
                p.y = Mathf.Max(p.y, m_FloorHeight);
                m_Current[i] = p;
                m_Previous[i] = p;
            }
        }

        void LateUpdate()
        {
            if (m_EndA == null || m_EndB == null)
                return;

            // Fixed step so the rope behaves the same at 72 Hz on Quest as at 144 Hz on a desktop.
            m_Accumulator = Mathf.Min(m_Accumulator + Time.deltaTime, k_Step * 4f);
            while (m_Accumulator >= k_Step)
            {
                Step(k_Step);
                m_Accumulator -= k_Step;
            }

            m_Line.SetPositions(m_Current);
        }

        void Step(float dt)
        {
            var gravity = Physics.gravity * (dt * dt);
            var last = m_Points - 1;

            for (var i = 1; i < last; i++)
            {
                var velocity = (m_Current[i] - m_Previous[i]) * m_Damping;
                m_Previous[i] = m_Current[i];
                m_Current[i] += velocity + gravity;
            }

            m_Current[0] = m_EndA.cableAnchor.position;
            m_Current[last] = m_EndB.cableAnchor.position;

            var segment = m_Length / last;
            for (var iteration = 0; iteration < m_Iterations; iteration++)
            {
                for (var i = 0; i < last; i++)
                {
                    var delta = m_Current[i + 1] - m_Current[i];
                    var distance = delta.magnitude;
                    if (distance < 1e-6f)
                        continue;

                    // Rope, not rod: only pull together when stretched, never push apart.
                    if (distance <= segment)
                        continue;

                    var correction = delta * ((distance - segment) / distance);
                    var pinnedA = i == 0;
                    var pinnedB = i + 1 == last;

                    if (pinnedA && !pinnedB)
                        m_Current[i + 1] -= correction;
                    else if (pinnedB && !pinnedA)
                        m_Current[i] += correction;
                    else if (!pinnedA)
                    {
                        m_Current[i] += correction * 0.5f;
                        m_Current[i + 1] -= correction * 0.5f;
                    }
                }

                for (var i = 1; i < last; i++)
                {
                    if (m_Current[i].y < m_FloorHeight + 0.005f)
                        m_Current[i].y = m_FloorHeight + 0.005f;
                }
            }
        }

        /// <summary>Wires the cable from code. Used by the scene builder.</summary>
        public void Configure(string cableId, CableConnector a, CableConnector b, float length, float floorHeight)
        {
            m_CableId = cableId;
            m_EndA = a;
            m_EndB = b;
            m_Length = length;
            m_FloorHeight = floorHeight;
        }
    }
}
