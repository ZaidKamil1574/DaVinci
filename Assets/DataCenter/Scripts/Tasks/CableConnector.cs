using UnityEngine;
using DataCenter.Robotics;

namespace DataCenter.Tasks
{
    /// <summary>
    /// One end of a cable. The robot gripper picks it up, carries it to a port, and seats it by
    /// opening the gripper with the tip inside the port's capture zone, or, with insertion assist,
    /// by bringing it near the port the work order wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Release-to-seat mirrors how the Da Vinci arm docked: letting go inside the port is what
    /// commits the insertion. While held, the nearest port is checked every frame so the operator
    /// gets a white "ready" LED, or a red flash and buzz for the wrong connector type, before
    /// committing.
    /// </para>
    /// <para>
    /// <b>Insertion assist</b> is shared autonomy for the last few centimetres: the operator does the
    /// coarse move, and once the plug is close to the work order's target port and pointing roughly
    /// at it, the robot aligns and inserts it by itself, as a real robot would finish a peg-in-hole
    /// with its own force-guided routine. It only engages for the port the order asks for and the
    /// right connector type, so it removes fiddliness without removing the chance to make a mistake.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public class CableConnector : MonoBehaviour, IGrippable
    {
        public enum ConnectorState
        {
            Free,
            Held,

            /// <summary>Insertion assist is aligning and pushing the plug into a port.</summary>
            Seating,
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

        [Header("Insertion assist")]
        [SerializeField, Tooltip("Distance from the target port at which the port LED shows the plug is about to snap, in metres.")]
        float m_AssistPreviewRadius = 0.08f;

        [SerializeField, Tooltip("Distance from the target port at which assist takes over and inserts, in metres.")]
        float m_AssistSeatRadius = 0.06f;

        [SerializeField, Range(10f, 90f), Tooltip("How far off the port axis the plug may point and still be inserted by assist, in degrees.")]
        float m_AssistSeatAngle = 50f;

        [SerializeField, Tooltip("Time assist takes to align and push the plug home, in seconds.")]
        float m_AssistSeatDuration = 0.3f;

        static readonly Collider[] s_Overlap = new Collider[48];

        /// <summary>Whether insertion assist is on. Shared by every connector; toggled from the HUD.</summary>
        public static bool insertionAssist { get; set; } = true;

        /// <summary>Distance from the target port at which assist takes over, in metres.</summary>
        public float assistSeatRadius => m_AssistSeatRadius;

        /// <summary>How far off the port axis the plug may point for assist to insert it, in degrees.</summary>
        public float assistSeatAngle => m_AssistSeatAngle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => insertionAssist = true;

        PortSocket m_SeatingPort;
        Pose m_SeatFrom;
        float m_SeatProgress;

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

        public bool canBeGripped => m_State != ConnectorState.Held && m_State != ConnectorState.Seating;

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

            // Insertion assist let go on purpose: carry on into the port from here.
            if (m_State == ConnectorState.Seating)
            {
                m_SeatFrom = new Pose(transform.position, transform.rotation);
                m_SeatProgress = 0f;
                return;
            }

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

            m_SeatingPort = null;
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
            if (m_State == ConnectorState.Seating)
            {
                AnimateAssistedSeat();
                return;
            }

            if (m_State != ConnectorState.Held)
                return;

            if (insertionAssist && TryAssist())
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

        /// <summary>
        /// Looks for the work order's target port near the tip. Previews it when in range, and hands
        /// over to the automatic insertion when close and roughly aligned.
        /// </summary>
        bool TryAssist()
        {
            var count = Physics.OverlapSphereNonAlloc(tip.position, m_AssistPreviewRadius, s_Overlap, m_PortLayers, QueryTriggerInteraction.Collide);
            PortSocket target = null;
            var targetDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var port = s_Overlap[i].GetComponentInParent<PortSocket>();
                if (port == null || !port.isWorkOrderTarget || port.isOccupied || port.type != m_Type)
                    continue;

                var distance = port.DistanceTo(this);
                if (distance < targetDistance)
                {
                    targetDistance = distance;
                    target = port;
                }
            }

            if (target == null)
                return false;

            m_LastRejected = null;
            var angle = Vector3.Angle(tip.forward, target.socket.forward);
            if (targetDistance <= m_AssistSeatRadius && angle <= m_AssistSeatAngle && m_Holder != null)
            {
                SetHovered(null);
                m_SeatingPort = target;
                m_State = ConnectorState.Seating;

                // Hand the plug over from the gripper to the insertion routine. OnReleased sees the
                // Seating state and starts the motion from wherever the plug is now.
                m_Holder.Release();
                FeedbackEvent.Play(FeedbackCue.Grip, tip.position);
                return true;
            }

            SetHovered(target);
            return true;
        }

        /// <summary>
        /// Moves the plug onto the port's axis a few centimetres out, square to it, then straight in,
        /// like a peg-in-hole routine, and seats it at the end.
        /// </summary>
        void AnimateAssistedSeat()
        {
            var port = m_SeatingPort;
            if (port == null || port.isOccupied)
            {
                // The port was taken or the scene reset mid-move: drop the plug.
                m_SeatingPort = null;
                m_State = ConnectorState.Free;
                m_Body.isKinematic = false;
                return;
            }

            m_SeatProgress = Mathf.Min(1f, m_SeatProgress + Time.deltaTime / Mathf.Max(0.05f, m_AssistSeatDuration));

            // Where the root must be for the tip to sit on a given socket pose.
            var tipLocalPosition = transform.InverseTransformPoint(tip.position);
            var tipLocalRotation = Quaternion.Inverse(transform.rotation) * tip.rotation;
            var seatedRotation = port.socket.rotation * Quaternion.Inverse(tipLocalRotation);
            var seated = port.socket.position - seatedRotation * tipLocalPosition;
            var approach = seated - port.socket.forward * 0.03f;

            // First 60%: align onto the axis. Last 40%: push straight in.
            const float alignShare = 0.6f;
            if (m_SeatProgress < alignShare)
            {
                var t = Mathf.SmoothStep(0f, 1f, m_SeatProgress / alignShare);
                transform.SetPositionAndRotation(Vector3.Lerp(m_SeatFrom.position, approach, t), Quaternion.Slerp(m_SeatFrom.rotation, seatedRotation, t));
            }
            else
            {
                var t = Mathf.SmoothStep(0f, 1f, (m_SeatProgress - alignShare) / (1f - alignShare));
                transform.SetPositionAndRotation(Vector3.Lerp(approach, seated, t), seatedRotation);
            }

            if (m_SeatProgress >= 1f)
            {
                m_SeatingPort = null;
                SeatInto(port);
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
