using UnityEngine;
using DataCenter.Robotics;

namespace DataCenter.Tasks
{
    /// <summary>
    /// One end of a cable. The robot gripper picks it up, carries it to a port, and seats it by
    /// opening the gripper with the tip inside the port's capture zone.
    /// </summary>
    /// <remarks>
    /// Release-to-seat mirrors how the Da Vinci arm docked: letting go inside the port is what
    /// commits the insertion. While held, the nearest port is checked every frame so the operator
    /// gets a white "ready" LED, or a red flash and buzz for the wrong connector type, before
    /// committing.
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public class CableConnector : MonoBehaviour, IGrippable
    {
        public enum ConnectorState
        {
            Free,
            Held,
            Seated,
        }

        [SerializeField]
        PortType m_Type;

        [SerializeField, Tooltip("Front of the plug. Forward is the insertion direction.")]
        Transform m_Tip;

        [SerializeField, Tooltip("Where the cable leaves the boot.")]
        Transform m_CableAnchor;

        [SerializeField, Tooltip("Layers searched for ports while held.")]
        LayerMask m_PortLayers = ~0;

        [SerializeField, Tooltip("Label shown in the UI, for example \"Cable 01 end A\".")]
        string m_Label = "Connector";

        static readonly Collider[] s_Overlap = new Collider[16];

        Rigidbody m_Body;
        ConnectorState m_State;
        PortSocket m_Seated;
        PortSocket m_Hovered;
        PortSocket m_LastRejected;
        Gripper m_Holder;
        Vector3 m_HomePosition;
        Quaternion m_HomeRotation;

        public PortType type => m_Type;
        public Transform tip => m_Tip != null ? m_Tip : transform;
        public Transform cableAnchor => m_CableAnchor != null ? m_CableAnchor : transform;
        public ConnectorState state => m_State;
        public PortSocket seatedPort => m_Seated;
        public string label => m_Label;

        /// <summary>The cable this connector belongs to. Set by <see cref="VerletCable"/>.</summary>
        public VerletCable cable { get; internal set; }

        /// <summary>The port the held connector would seat in if released now, or null.</summary>
        public PortSocket hoveredPort => m_Hovered;

        public bool canBeGripped => m_State != ConnectorState.Held;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_HomePosition = transform.position;
            m_HomeRotation = transform.rotation;
        }

        public void OnGripped(Gripper gripper, Transform attachPoint)
        {
            if (m_Seated != null)
            {
                var port = m_Seated;
                m_Seated = null;
                port.Unseat(this);
            }

            m_Holder = gripper;
            m_State = ConnectorState.Held;
            m_Body.isKinematic = true;
            m_Body.interpolation = RigidbodyInterpolation.None;

            // Square the plug up in the fingers. A gripper with V-groove fingers self-centres a
            // connector like this, and it means alignment at the port depends only on the wrist.
            transform.SetParent(attachPoint, false);
            var tipOffset = transform.InverseTransformPoint(tip.position);
            transform.localRotation = Quaternion.identity;
            transform.localPosition = new Vector3(0f, 0f, -tipOffset.z + 0.012f);

            FeedbackEvent.Play(FeedbackCue.Grip, transform.position);
        }

        public void OnReleased(Gripper gripper)
        {
            m_Holder = null;
            transform.SetParent(null, true);

            var target = m_Hovered;
            SetHovered(null);

            if (target != null && target.Check(this) == InsertCheck.Ok)
            {
                SeatInto(target);
                return;
            }

            m_State = ConnectorState.Free;
            m_Body.isKinematic = false;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
            FeedbackEvent.Play(FeedbackCue.Release, transform.position);
        }

        /// <summary>Seats into <paramref name="port"/> immediately. Also used to pre-wire a scene.</summary>
        public void SeatInto(PortSocket port)
        {
            m_State = ConnectorState.Seated;
            m_Seated = port;
            m_Body.isKinematic = true;

            // Snap so the tip sits on the socket point, facing into it.
            transform.SetParent(port.transform, true);
            transform.rotation = port.socket.rotation * Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * tip.rotation);
            transform.position += port.socket.position - tip.position;

            port.Seat(this);
        }

        /// <summary>Returns to where the scene started it. Used by the tutorial and the reset button.</summary>
        public void ResetToHome()
        {
            if (m_Holder != null)
                m_Holder.Release();

            if (m_Seated != null)
            {
                var port = m_Seated;
                m_Seated = null;
                port.Unseat(this);
            }

            transform.SetParent(null, false);
            transform.SetPositionAndRotation(m_HomePosition, m_HomeRotation);
            m_State = ConnectorState.Free;
            m_Body.isKinematic = false;
            m_Body.linearVelocity = Vector3.zero;
            m_Body.angularVelocity = Vector3.zero;
        }

        void Update()
        {
            if (m_State != ConnectorState.Held)
                return;

            var nearest = FindNearestPort();
            if (nearest == null)
            {
                SetHovered(null);
                m_LastRejected = null;
                return;
            }

            var check = nearest.Check(this);
            switch (check)
            {
                case InsertCheck.Ok:
                    SetHovered(nearest);
                    m_LastRejected = null;
                    break;
                case InsertCheck.WrongType:
                case InsertCheck.Occupied:
                    SetHovered(null);
                    // Once per approach, not every frame.
                    if (m_LastRejected != nearest)
                    {
                        m_LastRejected = nearest;
                        nearest.Reject(this, check);
                    }
                    break;
                default:
                    SetHovered(null);
                    break;
            }
        }

        PortSocket FindNearestPort()
        {
            var count = Physics.OverlapSphereNonAlloc(tip.position, 0.05f, s_Overlap, m_PortLayers, QueryTriggerInteraction.Collide);
            PortSocket best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var port = s_Overlap[i].GetComponentInParent<PortSocket>();
                if (port == null)
                    continue;

                var distance = port.DistanceTo(this);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = port;
                }
            }

            return best;
        }

        void SetHovered(PortSocket port)
        {
            if (m_Hovered == port)
                return;

            if (m_Hovered != null)
                m_Hovered.SetHover(false);

            m_Hovered = port;

            if (m_Hovered != null)
                m_Hovered.SetHover(true);
        }

        /// <summary>Wires the connector from code. Used by the scene builder.</summary>
        public void Configure(PortType type, Transform tipPoint, Transform anchor, LayerMask portLayers, string label)
        {
            m_Type = type;
            m_Tip = tipPoint;
            m_CableAnchor = anchor;
            m_PortLayers = portLayers;
            m_Label = label;
        }
    }
}
