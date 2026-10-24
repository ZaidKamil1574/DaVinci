using System;
using UnityEngine;

namespace DataCenter.Robotics
{
    /// <summary>Something a <see cref="Gripper"/> can pick up, such as a cable connector.</summary>
    /// <remarks>
    /// Lives in the robotics layer so the gripper needs no knowledge of cables. The task layer
    /// implements it.
    /// </remarks>
    public interface IGrippable
    {
        /// <summary>Whether this can be picked up right now.</summary>
        bool canBeGripped { get; }

        /// <summary>Called when the gripper closes on this. The implementer attaches itself to <paramref name="attachPoint"/>.</summary>
        void OnGripped(Gripper gripper, Transform attachPoint);

        /// <summary>Called when the gripper opens.</summary>
        void OnReleased(Gripper gripper);

        Transform transform { get; }
    }

    /// <summary>
    /// Two-finger parallel gripper. Closing within range of an <see cref="IGrippable"/> picks it up;
    /// opening lets it go.
    /// </summary>
    public class Gripper : MonoBehaviour
    {
        [SerializeField, Tooltip("Left finger. Slides along its local X.")]
        Transform m_LeftFinger;

        [SerializeField, Tooltip("Right finger. Slides along its local X.")]
        Transform m_RightFinger;

        [SerializeField, Tooltip("Where a gripped object is held. Forward is the approach direction.")]
        Transform m_AttachPoint;

        [SerializeField, Tooltip("Finger offset from centre when fully open, in metres.")]
        float m_OpenWidth = 0.035f;

        [SerializeField, Tooltip("Finger offset from centre when fully closed, in metres.")]
        float m_ClosedWidth = 0.008f;

        [SerializeField, Tooltip("Full open-to-close time, in seconds.")]
        float m_StrokeTime = 0.35f;

        [SerializeField, Tooltip("How far from the attach point an object can be and still be picked up, in metres.")]
        float m_GripRadius = 0.05f;

        [SerializeField, Tooltip("Layers searched for grippable objects.")]
        LayerMask m_GrippableLayers = ~0;

        static readonly Collider[] s_Overlap = new Collider[16];

        float m_Amount;
        float m_Target;
        IGrippable m_Held;
        bool m_GripAttempted;

        /// <summary>Raised when an object is picked up or let go. Null means released.</summary>
        public event Action<IGrippable> heldChanged;

        /// <summary>What the gripper is holding, or null.</summary>
        public IGrippable held => m_Held;

        /// <summary>0 fully open, 1 fully closed.</summary>
        public float amount => m_Amount;

        /// <summary>Whether the gripper has been told to close.</summary>
        public bool isClosing => m_Target > 0.5f;

        public Transform attachPoint => m_AttachPoint;

        public float gripRadius => m_GripRadius;

        /// <summary>Opens (0) or closes (1) the gripper.</summary>
        public void SetClosed(float target)
        {
            target = Mathf.Clamp01(target);
            var opening = target < 0.5f && m_Target >= 0.5f;
            m_Target = target;

            if (target >= 0.5f)
                return;

            m_GripAttempted = false;
            if (opening && m_Held != null)
                Release();
        }

        /// <summary>Lets go of whatever is held without opening, for example when an E-Stop drops power.</summary>
        public void Release()
        {
            if (m_Held == null)
                return;

            var released = m_Held;
            m_Held = null;
            released.OnReleased(this);
            heldChanged?.Invoke(null);
        }

        /// <summary>Advances the finger motion.</summary>
        public void Tick(float deltaTime, float speedScale)
        {
            var rate = deltaTime * speedScale / Mathf.Max(0.01f, m_StrokeTime);
            m_Amount = Mathf.MoveTowards(m_Amount, m_Target, rate);

            // Fingers stop on the part rather than closing through it.
            var visualAmount = m_Held != null ? Mathf.Min(m_Amount, 0.7f) : m_Amount;
            var offset = Mathf.Lerp(m_OpenWidth, m_ClosedWidth, visualAmount);
            if (m_LeftFinger != null)
                m_LeftFinger.localPosition = new Vector3(-offset, m_LeftFinger.localPosition.y, m_LeftFinger.localPosition.z);
            if (m_RightFinger != null)
                m_RightFinger.localPosition = new Vector3(offset, m_RightFinger.localPosition.y, m_RightFinger.localPosition.z);

            // Try once per close, half way through the stroke, like fingers meeting a part.
            if (m_Target >= 0.5f && !m_GripAttempted && m_Amount >= 0.5f)
            {
                m_GripAttempted = true;
                TryGrip();
            }
        }

        void TryGrip()
        {
            if (m_Held != null || m_AttachPoint == null)
                return;

            var count = Physics.OverlapSphereNonAlloc(m_AttachPoint.position, m_GripRadius, s_Overlap, m_GrippableLayers, QueryTriggerInteraction.Collide);
            IGrippable best = null;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var candidate = s_Overlap[i].GetComponentInParent<IGrippable>();
                if (candidate == null || !candidate.canBeGripped)
                    continue;

                var distance = Vector3.Distance(m_AttachPoint.position, s_Overlap[i].ClosestPoint(m_AttachPoint.position));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best == null)
                return;

            m_Held = best;
            best.OnGripped(this, m_AttachPoint);
            heldChanged?.Invoke(best);
        }

        /// <summary>The nearest grippable within range, for UI hints. Allocation free.</summary>
        public IGrippable FindNearestInRange()
        {
            if (m_AttachPoint == null)
                return null;

            var count = Physics.OverlapSphereNonAlloc(m_AttachPoint.position, m_GripRadius, s_Overlap, m_GrippableLayers, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                var candidate = s_Overlap[i].GetComponentInParent<IGrippable>();
                if (candidate != null && candidate.canBeGripped)
                    return candidate;
            }

            return null;
        }

        /// <summary>Wires the gripper from code. Used by the procedural robot builder.</summary>
        public void Configure(Transform left, Transform right, Transform attach, LayerMask grippableLayers)
        {
            m_LeftFinger = left;
            m_RightFinger = right;
            m_AttachPoint = attach;
            m_GrippableLayers = grippableLayers;
        }

        void OnDrawGizmosSelected()
        {
            if (m_AttachPoint == null)
                return;

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(m_AttachPoint.position, m_GripRadius);
        }
    }
}
