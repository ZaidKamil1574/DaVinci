using UnityEngine;

namespace DataCenter.Robotics
{
    /// <summary>Proximity band, matching the green / yellow / red indicator.</summary>
    public enum ProximityZone
    {
        Clear,
        Warning,
        Danger,
    }

    /// <summary>
    /// Measures how close the gripper and the base are to equipment and caps the robot's speed as
    /// they get close, the way collaborative robots slow down inside a protective separation
    /// distance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The robot-side half of the proximity feature: it owns the safety behaviour (the speed cap),
    /// while the UI only displays what it measures. It plugs into <see cref="TeleopRobot.speedLimiter"/>
    /// so the robot controller does not need to know how the limit is computed.
    /// </para>
    /// <para>
    /// One overlap query per probe per frame, with a preallocated buffer, so it is cheap enough to
    /// leave running on every robot on a Quest.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(TeleopRobot))]
    public class ProximitySensor : MonoBehaviour
    {
        [SerializeField, Tooltip("Layers that count as equipment to keep clear of.")]
        LayerMask m_EquipmentLayers;

        [SerializeField, Tooltip("Base footprint radius, in metres, used for the base's own clearance.")]
        float m_BaseRadius = 0.45f;

        [SerializeField, Tooltip("Base clearance at which the base speed starts to drop, in metres.")]
        float m_BaseWarnDistance = 0.5f;

        static readonly Collider[] s_Hits = new Collider[24];

        TeleopRobot m_Robot;

        /// <summary>Distance from the tool tip to the nearest equipment surface, in metres. Infinity when nothing is in range.</summary>
        public float toolDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>Nearest point on equipment to the tool tip. Only meaningful when <see cref="toolDistance"/> is finite.</summary>
        public Vector3 toolClosestPoint { get; private set; }

        /// <summary>Clearance between the base footprint and the nearest equipment, in metres.</summary>
        public float baseClearance { get; private set; } = float.PositiveInfinity;

        /// <summary>Current band for the tool.</summary>
        public ProximityZone zone { get; private set; }

        /// <summary>Speed multiplier currently applied, 0 to 1.</summary>
        public float speedScale { get; private set; } = 1f;

        void Awake()
        {
            m_Robot = GetComponent<TeleopRobot>();
        }

        void OnEnable() => m_Robot.speedLimiter = ComputeSpeedScale;

        void OnDisable()
        {
            if (m_Robot != null)
                m_Robot.speedLimiter = null;
        }

        void LateUpdate() => Measure();

        void Measure()
        {
            var spec = m_Robot.spec;
            var tool = m_Robot.arm != null ? m_Robot.arm.toolTip : null;
            if (spec == null || tool == null)
                return;

            toolDistance = Nearest(tool.position, spec.warnDistance * 1.5f, out var closest);
            toolClosestPoint = closest;
            zone = toolDistance <= spec.dangerDistance ? ProximityZone.Danger
                : toolDistance <= spec.warnDistance ? ProximityZone.Warning
                : ProximityZone.Clear;

            var basePoint = transform.position + Vector3.up * 0.3f;
            var baseDistance = Nearest(basePoint, m_BaseRadius + m_BaseWarnDistance, out _);
            baseClearance = Mathf.Max(0f, baseDistance - m_BaseRadius);
        }

        float Nearest(Vector3 point, float radius, out Vector3 closest)
        {
            closest = point;
            var count = Physics.OverlapSphereNonAlloc(point, radius, s_Hits, m_EquipmentLayers, QueryTriggerInteraction.Ignore);
            var best = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = s_Hits[i];

                // ClosestPoint is only defined for convex shapes; fall back to the bounds otherwise.
                var candidate = hit is MeshCollider mesh && !mesh.convex ? hit.bounds.ClosestPoint(point) : hit.ClosestPoint(point);
                var distance = Vector3.Distance(point, candidate);
                if (distance < best)
                {
                    best = distance;
                    closest = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Linear ramp from full speed at the warning distance to the spec's floor at the danger
        /// distance. Base clearance only counts while the base is driving, so a robot parked beside
        /// its cart is not slowed for being parked.
        /// </summary>
        float ComputeSpeedScale()
        {
            var spec = m_Robot.spec;
            if (spec == null)
                return 1f;

            var ramp = Mathf.InverseLerp(spec.dangerDistance, spec.warnDistance, toolDistance);
            if (m_Robot.mobileBase != null && m_Robot.mobileBase.isMoving)
                ramp = Mathf.Min(ramp, Mathf.InverseLerp(0.05f, m_BaseWarnDistance, baseClearance));

            speedScale = Mathf.Lerp(spec.nearEquipmentSpeedScale, 1f, ramp);
            return speedScale;
        }

        /// <summary>Wires the sensor from code. Used by the robot builder.</summary>
        public void Configure(LayerMask equipmentLayers) => m_EquipmentLayers = equipmentLayers;
    }
}
