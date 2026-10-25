using UnityEngine;
using UnityEngine.AI;

namespace DataCenter.Robotics
{
    /// <summary>
    /// Omnidirectional (mecanum-style) mobile base on a NavMesh. Accepts either a velocity
    /// (joystick / WASD), which may include sideways motion, or a destination (point and click);
    /// a velocity cancels any path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sideways travel is what makes lining up with a rack practical: the robot can slide along the
    /// aisle while still facing the equipment, instead of turning away and back.
    /// </para>
    /// <para>
    /// Driven moves are also swept against equipment before they happen: the base footprint and
    /// the arm's tool tip, which travels with the base. A move into a rack, wall or cart stops at
    /// the surface and slides along it, and a turn that would swing into one does not happen. The
    /// NavMesh alone keeps the base's centre off rack footprints, but not its corners or an arm
    /// held out in front.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MobileBase : MonoBehaviour
    {
        [SerializeField, Tooltip("Wheels spun to match travel, purely visual. Rotate about local X.")]
        Transform[] m_Wheels;

        [SerializeField, Tooltip("Wheel radius for the visual spin, in metres.")]
        float m_WheelRadius = 0.08f;

        [SerializeField, Tooltip("Layers the base and arm cannot be driven into: racks, walls, carts. Defaults to Equipment.")]
        LayerMask m_BlockingLayers;

        [SerializeField, Tooltip("Clearance kept around the base footprint, in metres.")]
        float m_Clearance = 0.03f;

        // Gap left at a contact so the next sweep does not start touching, and the tool tip's size.
        const float k_Skin = 0.005f;
        const float k_ToolRadius = 0.03f;
        const float k_BumpInterval = 0.6f;
        const float k_ToastInterval = 4f;

        static readonly Collider[] s_Before = new Collider[16];
        static readonly Collider[] s_After = new Collider[16];

        BoxCollider m_Footprint;
        ArmIKController m_Arm;
        float m_NextBumpAt;
        float m_NextToastAt;

        NavMeshAgent m_Agent;
        Vector2 m_DriveTarget;
        float m_StrafeTarget;
        float m_Linear;
        float m_Lateral;
        float m_Angular;
        Vector3 m_LastPosition;

        /// <summary>Ground speed over the last tick, in metres per second.</summary>
        public float speed { get; private set; }

        /// <summary>Whether the base is following a navigation path.</summary>
        public bool isNavigating => m_Agent != null && m_Agent.isOnNavMesh && (m_Agent.pathPending || (m_Agent.hasPath && m_Agent.remainingDistance > m_Agent.stoppingDistance));

        /// <summary>Whether the base is moving, by either control.</summary>
        public bool isMoving => speed > 0.02f || Mathf.Abs(m_Angular) > 1f || isNavigating;

        /// <summary>Whether the last driven step was stopped or turned aside by equipment.</summary>
        public bool blocked { get; private set; }

        /// <summary>The current navigation destination, if any.</summary>
        public Vector3 destination => m_Agent != null ? m_Agent.destination : transform.position;

        void Awake()
        {
            m_Agent = GetComponent<NavMeshAgent>();
            m_Agent.updateRotation = true;
            m_Agent.autoBraking = true;
            m_LastPosition = transform.position;
            m_Footprint = GetComponent<BoxCollider>();
            m_Arm = GetComponent<ArmIKController>();
            if (m_BlockingLayers.value == 0)
                m_BlockingLayers = LayerMask.GetMask("Equipment");
        }

        /// <summary>Sets the drive command. x is turn, y is forward, each -1 to 1.</summary>
        public void SetDrive(Vector2 drive, float strafe = 0f)
        {
            m_DriveTarget = Vector2.ClampMagnitude(drive, 1.4142f);
            m_StrafeTarget = Mathf.Clamp(strafe, -1f, 1f);
            var commanding = m_DriveTarget.sqrMagnitude > 0.0001f || Mathf.Abs(m_StrafeTarget) > 0.01f;
            if (commanding && m_Agent.isOnNavMesh && m_Agent.hasPath)
                m_Agent.ResetPath();
        }

        /// <summary>Navigates to the nearest NavMesh point to <paramref name="worldPoint"/>.</summary>
        public bool SetDestination(Vector3 worldPoint)
        {
            if (!m_Agent.isOnNavMesh)
                return false;

            if (!NavMesh.SamplePosition(worldPoint, out var hit, 1f, NavMesh.AllAreas))
                return false;

            m_DriveTarget = Vector2.zero;
            return m_Agent.SetDestination(hit.position);
        }

        /// <summary>Stops immediately: clears the path and zeroes velocity.</summary>
        public void Stop()
        {
            m_DriveTarget = Vector2.zero;
            m_StrafeTarget = 0f;
            m_Linear = 0f;
            m_Lateral = 0f;
            m_Angular = 0f;
            if (m_Agent != null && m_Agent.isOnNavMesh)
            {
                m_Agent.ResetPath();
                m_Agent.velocity = Vector3.zero;
            }
        }

        /// <summary>Advances the base one step.</summary>
        /// <param name="spec">Limits for this robot model.</param>
        /// <param name="speedScale">Multiplier on top speed, for example the safety cap near racks.</param>
        public void Tick(float deltaTime, RobotSpec spec, float speedScale)
        {
            if (deltaTime <= 0f || !m_Agent.isOnNavMesh)
                return;

            var maxLinear = spec.maxLinearSpeed * speedScale;
            var maxAngular = spec.maxAngularSpeed * Mathf.Lerp(0.5f, 1f, speedScale);

            m_Agent.speed = maxLinear;
            m_Agent.angularSpeed = maxAngular * 2f;
            m_Agent.acceleration = spec.linearAcceleration;

            if (!isNavigating)
            {
                // Ramp toward the commanded velocity rather than stepping to it.
                // Forward and sideways share one speed limit, so a diagonal is no faster than straight.
                var planar = Vector2.ClampMagnitude(new Vector2(m_StrafeTarget, m_DriveTarget.y), 1f) * maxLinear;
                m_Linear = Mathf.MoveTowards(m_Linear, planar.y, spec.linearAcceleration * deltaTime);
                m_Lateral = Mathf.MoveTowards(m_Lateral, planar.x, spec.linearAcceleration * deltaTime);
                m_Angular = Mathf.MoveTowards(m_Angular, m_DriveTarget.x * maxAngular, maxAngular * 4f * deltaTime);

                blocked = false;
                if (Mathf.Abs(m_Angular) > 0.01f)
                {
                    var turn = Quaternion.Euler(0f, m_Angular * deltaTime, 0f);
                    if (CanTurn(turn, out var contact, out var obstacle))
                    {
                        transform.rotation = turn * transform.rotation;
                    }
                    else
                    {
                        m_Angular = 0f;
                        Bump(contact, obstacle);
                    }
                }

                var step = (transform.forward * m_Linear + transform.right * m_Lateral) * deltaTime;
                if (step.sqrMagnitude > 1e-10f)
                {
                    var allowed = Sweep(step, out var hit);
                    if (hit.collider != null)
                    {
                        // Shed the speed going into the obstacle and keep the part along it, so
                        // pushing at a rack does not build up and lurch when the way clears.
                        var normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
                        var velocity = transform.forward * m_Linear + transform.right * m_Lateral;
                        velocity -= normal * Mathf.Min(0f, Vector3.Dot(velocity, normal));
                        m_Linear = Vector3.Dot(velocity, transform.forward);
                        m_Lateral = Vector3.Dot(velocity, transform.right);
                        Bump(hit.point, hit.collider);
                    }

                    if (allowed.sqrMagnitude > 1e-10f)
                        m_Agent.Move(allowed);
                }
            }
            else
            {
                m_Linear = 0f;
                m_Lateral = 0f;
                m_Angular = 0f;
            }

            var travelled = transform.position - m_LastPosition;
            speed = travelled.magnitude / deltaTime;
            m_LastPosition = transform.position;

            if (m_Wheels != null && m_WheelRadius > 0f)
            {
                var forwardTravel = Vector3.Dot(travelled, transform.forward);
                var spin = forwardTravel / m_WheelRadius * Mathf.Rad2Deg;
                for (var i = 0; i < m_Wheels.Length; i++)
                {
                    if (m_Wheels[i] != null)
                        m_Wheels[i].Rotate(spin, 0f, 0f, Space.Self);
                }
            }
        }

        /// <summary>
        /// How much of <paramref name="step"/> the base can make: up to the first contact, then
        /// sliding along the surface with the rest. <paramref name="hit"/> is that contact, if any.
        /// </summary>
        Vector3 Sweep(Vector3 step, out RaycastHit hit)
        {
            var distance = step.magnitude;
            var direction = step / distance;
            if (!Cast(Vector3.zero, direction, distance + k_Skin, out hit))
                return step;

            var travel = direction * Mathf.Max(0f, hit.distance - k_Skin);
            var normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
            if (normal.sqrMagnitude < 1e-6f)
                return travel;

            var slide = Vector3.ProjectOnPlane(step - travel, normal.normalized);
            var slideDistance = slide.magnitude;
            if (slideDistance < 1e-5f)
                return travel;

            if (Cast(travel, slide / slideDistance, slideDistance + k_Skin, out var second))
                slide = slide / slideDistance * Mathf.Max(0f, second.distance - k_Skin);
            return travel + slide;
        }

        /// <summary>Casts the footprint and the tool tip from their current place plus <paramref name="offset"/>.</summary>
        bool Cast(Vector3 offset, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            var found = false;

            if (m_Footprint != null)
            {
                var centre = transform.TransformPoint(m_Footprint.center) + offset;
                if (Physics.BoxCast(centre, FootprintHalfExtents(), direction, out var hit, transform.rotation, distance, m_BlockingLayers, QueryTriggerInteraction.Ignore))
                {
                    nearest = hit;
                    found = true;
                }
            }

            var tip = m_Arm != null ? m_Arm.toolTip : null;
            if (tip != null && Physics.SphereCast(tip.position + offset, k_ToolRadius, direction, out var tipHit, distance, m_BlockingLayers, QueryTriggerInteraction.Ignore)
                && (!found || tipHit.distance < nearest.distance))
            {
                nearest = tipHit;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Whether turning by <paramref name="turn"/> keeps the base and the tool clear. Anything the
        /// base already touches is ignored, so a robot parked against a cart can still turn away.
        /// </summary>
        bool CanTurn(Quaternion turn, out Vector3 contact, out Collider obstacle)
        {
            contact = transform.position;
            obstacle = null;
            var pivot = transform.position;

            if (m_Footprint != null)
            {
                var centre = transform.TransformPoint(m_Footprint.center);
                var half = FootprintHalfExtents();
                var after = Physics.OverlapBoxNonAlloc(pivot + turn * (centre - pivot), half, s_After, turn * transform.rotation, m_BlockingLayers, QueryTriggerInteraction.Ignore);
                if (after > 0)
                {
                    var before = Physics.OverlapBoxNonAlloc(centre, half, s_Before, transform.rotation, m_BlockingLayers, QueryTriggerInteraction.Ignore);
                    for (var i = 0; i < after; i++)
                    {
                        if (System.Array.IndexOf(s_Before, s_After[i], 0, before) < 0)
                        {
                            contact = s_After[i].ClosestPoint(centre);
                            obstacle = s_After[i];
                            return false;
                        }
                    }
                }
            }

            var tip = m_Arm != null ? m_Arm.toolTip : null;
            if (tip != null)
            {
                var from = tip.position;
                var swing = pivot + turn * (from - pivot) - from;
                var distance = swing.magnitude;
                if (distance > 1e-5f && Physics.SphereCast(from, k_ToolRadius, swing / distance, out var hit, distance, m_BlockingLayers, QueryTriggerInteraction.Ignore))
                {
                    contact = hit.point;
                    obstacle = hit.collider;
                    return false;
                }
            }

            return true;
        }

        Vector3 FootprintHalfExtents()
        {
            var half = Vector3.Scale(m_Footprint.size * 0.5f, transform.lossyScale);
            // Clearance around the sides; the bottom stays a little off the floor and plinths.
            return new Vector3(half.x + m_Clearance, Mathf.Max(0.05f, half.y - 0.03f), half.z + m_Clearance);
        }

        /// <summary>
        /// Feedback for a blocked move: the sound of whatever it hit (a buzz too in VR), louder the
        /// faster it was going, and now and then a note on screen.
        /// </summary>
        void Bump(Vector3 point, Collider obstacle)
        {
            blocked = true;
            if (Time.time >= m_NextBumpAt)
            {
                m_NextBumpAt = Time.time + k_BumpInterval;
                var impact = Mathf.Clamp01(0.35f + speed / 0.6f);
                FeedbackEvent.Play(FeedbackEvent.ImpactCue(obstacle), point, impact);
            }

            if (Time.time >= m_NextToastAt)
            {
                m_NextToastAt = Time.time + k_ToastInterval;
                ToastEvent.Show("Blocked: the robot cannot drive through equipment", ToastLevel.Warning);
            }
        }

        /// <summary>Wires the base from code. Used by the procedural robot builder.</summary>
        public void Configure(Transform[] wheels, float wheelRadius)
        {
            m_Wheels = wheels;
            m_WheelRadius = wheelRadius;
        }
    }
}
