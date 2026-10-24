using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataCenter.Tasks
{
    /// <summary>Why a connector would or would not seat in a <see cref="PortSocket"/>.</summary>
    public enum InsertCheck
    {
        Ok,
        WrongType,
        Occupied,
        TooFar,
        Misaligned,
    }

    /// <summary>How a port's status LED is lit.</summary>
    public enum PortIndicator
    {
        Off,
        Target,
        Ready,
        Negotiating,
        LinkUp,
        WrongPort,
        Rejected,
    }

    /// <summary>A port connected or disconnected.</summary>
    public struct PortChangedEvent
    {
        public PortSocket port;
        public CableConnector connector;
        public bool connected;
    }

    /// <summary>A connector was offered to a port and refused.</summary>
    public struct PortRejectedEvent
    {
        public PortSocket port;
        public CableConnector connector;
        public InsertCheck reason;
    }

    /// <summary>
    /// A physical port on a patch panel, switch or PDU. Accepts a <see cref="CableConnector"/> of
    /// the same <see cref="PortType"/> when it is close enough and square enough to the socket axis.
    /// </summary>
    /// <remarks>
    /// The acceptance test is the Da Vinci project's <c>DaVinciTrocarPort.CanCapture</c>, applied to
    /// a connector instead of a cannula: distance and angle both matter, because a connector that
    /// arrives sideways should not slide home just for being close.
    /// </remarks>
    public class PortSocket : MonoBehaviour
    {
        static readonly Dictionary<string, PortSocket> s_ById = new Dictionary<string, PortSocket>();

        [SerializeField, Tooltip("Unique id: section-rack-unit-port, for example A-R03-U40-P12.")]
        string m_PortId;

        [SerializeField]
        PortType m_Type;

        [SerializeField, Tooltip("Where the seated connector tip sits. Forward points into the port, the insertion direction.")]
        Transform m_Socket;

        [SerializeField, Tooltip("Status LED beside the port.")]
        Renderer m_Led;

        [SerializeField, Tooltip("Seconds between seating and link-up, like a real port negotiating speed.")]
        float m_NegotiateTime = 0.6f;

        static MaterialPropertyBlock s_Block;
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_Blink = Shader.PropertyToID("_BlinkRate");

        CableConnector m_Occupant;
        PortIndicator m_Indicator;
        PortIndicator m_BaseIndicator;
        float m_NegotiateUntil;
        float m_TransientUntil;

        /// <summary>Raised when a connector seats or leaves.</summary>
        public static event Action<PortChangedEvent> changed;

        public string portId => m_PortId;
        public PortType type => m_Type;
        public Transform socket => m_Socket != null ? m_Socket : transform;
        public CableConnector occupant => m_Occupant;
        public bool isOccupied => m_Occupant != null;

        /// <summary>Whether a seated connector has finished negotiating.</summary>
        public bool linkUp => m_Occupant != null && Time.time >= m_NegotiateUntil;

        /// <summary>Every enabled port, by id.</summary>
        public static IReadOnlyDictionary<string, PortSocket> byId => s_ById;

        public static PortSocket Find(string portId) => portId != null && s_ById.TryGetValue(portId, out var port) ? port : null;

        void OnEnable()
        {
            if (!string.IsNullOrEmpty(m_PortId))
                s_ById[m_PortId] = this;
            ApplyIndicator();
        }

        void OnDisable()
        {
            if (!string.IsNullOrEmpty(m_PortId) && s_ById.TryGetValue(m_PortId, out var registered) && registered == this)
                s_ById.Remove(m_PortId);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_ById.Clear();
            changed = null;
        }

        /// <summary>Whether <paramref name="connector"/> would seat here right now, and if not, why.</summary>
        public InsertCheck Check(CableConnector connector)
        {
            if (connector == null)
                return InsertCheck.TooFar;

            if (m_Occupant != null && m_Occupant != connector)
                return InsertCheck.Occupied;

            var radius = m_Type != null ? m_Type.captureRadius : 0.02f;
            if (Vector3.Distance(connector.tip.position, socket.position) > radius)
                return InsertCheck.TooFar;

            // Type is checked after distance on purpose: the operator should only hear "wrong type"
            // when they are actually trying to plug into this port.
            if (connector.type != m_Type)
                return InsertCheck.WrongType;

            var angle = m_Type != null ? m_Type.captureAngle : 25f;
            if (Vector3.Angle(connector.tip.forward, socket.forward) > angle)
                return InsertCheck.Misaligned;

            return InsertCheck.Ok;
        }

        /// <summary>Distance from a connector tip to the socket, for picking the nearest candidate.</summary>
        public float DistanceTo(CableConnector connector) => Vector3.Distance(connector.tip.position, socket.position);

        /// <summary>Seats a connector that has passed <see cref="Check"/>.</summary>
        internal void Seat(CableConnector connector)
        {
            m_Occupant = connector;
            m_NegotiateUntil = Time.time + m_NegotiateTime;
            SetIndicatorTransient(PortIndicator.Negotiating, m_NegotiateTime);
            changed?.Invoke(new PortChangedEvent { port = this, connector = connector, connected = true });
            Invoke(nameof(AnnounceLinkUp), m_NegotiateTime);
        }

        void AnnounceLinkUp()
        {
            if (m_Occupant == null)
                return;

            FeedbackEvent.Play(FeedbackCue.LinkUp, socket.position);
            ApplyIndicator();
        }

        /// <summary>Releases the seated connector.</summary>
        internal void Unseat(CableConnector connector)
        {
            if (m_Occupant != connector)
                return;

            CancelInvoke(nameof(AnnounceLinkUp));
            m_Occupant = null;
            ApplyIndicator();
            changed?.Invoke(new PortChangedEvent { port = this, connector = connector, connected = false });
        }

        /// <summary>Publishes a refusal and flashes the LED red.</summary>
        internal void Reject(CableConnector connector, InsertCheck reason)
        {
            SetIndicatorTransient(PortIndicator.Rejected, 0.8f);
            EventBus.Publish(new PortRejectedEvent { port = this, connector = connector, reason = reason });
        }

        /// <summary>
        /// Sets the steady indicator the task layer wants (target highlight, wrong-port warning).
        /// Link state and transient flashes take priority over it.
        /// </summary>
        public void SetBaseIndicator(PortIndicator indicator)
        {
            m_BaseIndicator = indicator;
            ApplyIndicator();
        }

        /// <summary>Shows <see cref="PortIndicator.Ready"/> while a held connector hovers in range.</summary>
        internal void SetHover(bool hovering)
        {
            if (Time.time < m_TransientUntil)
                return;

            if (hovering)
                SetLed(PortIndicator.Ready);
            else
                ApplyIndicator();
        }

        void SetIndicatorTransient(PortIndicator indicator, float seconds)
        {
            m_TransientUntil = Time.time + seconds;
            SetLed(indicator);
            CancelInvoke(nameof(ApplyIndicator));
            Invoke(nameof(ApplyIndicator), seconds);
        }

        void ApplyIndicator()
        {
            if (Time.time < m_TransientUntil)
                return;

            if (m_Occupant != null)
                SetLed(m_BaseIndicator == PortIndicator.WrongPort ? PortIndicator.WrongPort : PortIndicator.LinkUp);
            else
                SetLed(m_BaseIndicator == PortIndicator.WrongPort ? PortIndicator.Off : m_BaseIndicator);
        }

        void SetLed(PortIndicator indicator)
        {
            m_Indicator = indicator;
            if (m_Led == null)
                return;

            s_Block ??= new MaterialPropertyBlock();
            m_Led.GetPropertyBlock(s_Block);
            s_Block.SetColor(k_BaseColor, IndicatorColor(indicator));
            s_Block.SetFloat(k_Blink, indicator == PortIndicator.Target || indicator == PortIndicator.Negotiating ? 3f : 0f);
            m_Led.SetPropertyBlock(s_Block);
        }

        /// <summary>The indicator currently shown.</summary>
        public PortIndicator indicator => m_Indicator;

        /// <summary>LED colour per state. Matches the HUD's status tokens.</summary>
        public static Color IndicatorColor(PortIndicator indicator)
        {
            switch (indicator)
            {
                case PortIndicator.Target: return new Color(0.1f, 0.85f, 1f);
                case PortIndicator.Ready: return new Color(0.95f, 0.95f, 1f);
                case PortIndicator.Negotiating: return new Color(1f, 0.7f, 0.1f);
                case PortIndicator.LinkUp: return new Color(0.15f, 1f, 0.35f);
                case PortIndicator.WrongPort: return new Color(1f, 0.55f, 0.05f);
                case PortIndicator.Rejected: return new Color(1f, 0.12f, 0.1f);
                default: return new Color(0.06f, 0.07f, 0.08f);
            }
        }

        /// <summary>Wires the port from code. Used by the scene builder.</summary>
        public void Configure(string portId, PortType type, Transform socketPoint, Renderer led)
        {
            m_PortId = portId;
            m_Type = type;
            m_Socket = socketPoint;
            m_Led = led;
        }

        void OnDrawGizmosSelected()
        {
            var radius = m_Type != null ? m_Type.captureRadius : 0.02f;
            Gizmos.color = isOccupied ? Color.green : new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(socket.position, radius);
            Gizmos.DrawLine(socket.position, socket.position - socket.forward * 0.06f);
        }
    }
}
