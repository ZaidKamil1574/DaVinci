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
    /// Sideways travel is what makes lining up with a rack practical: the robot can slide along the
    /// aisle while still facing the equipment, instead of turning away and back.
    /// </remarks>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MobileBase : MonoBehaviour
    {
        [SerializeField, Tooltip("Wheels spun to match travel, purely visual. Rotate about local X.")]
        Transform[] m_Wheels;

        [SerializeField, Tooltip("Wheel radius for the visual spin, in metres.")]
        float m_WheelRadius = 0.08f;

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

        /// <summary>The current navigation destination, if any.</summary>
        public Vector3 destination => m_Agent != null ? m_Agent.destination : transform.position;

        void Awake()
        {
            m_Agent = GetComponent<NavMeshAgent>();
            m_Agent.updateRotation = true;
            m_Agent.autoBraking = true;
            m_LastPosition = transform.position;
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

                if (Mathf.Abs(m_Angular) > 0.01f)
                    transform.Rotate(0f, m_Angular * deltaTime, 0f, Space.World);

                var step = transform.forward * m_Linear + transform.right * m_Lateral;
                if (step.sqrMagnitude > 1e-8f)
                    m_Agent.Move(step * deltaTime);
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

        /// <summary>Wires the base from code. Used by the procedural robot builder.</summary>
        public void Configure(Transform[] wheels, float wheelRadius)
        {
            m_Wheels = wheels;
            m_WheelRadius = wheelRadius;
        }
    }
}
