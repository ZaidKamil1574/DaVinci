using UnityEngine;

namespace DataCenter
{
    /// <summary>
    /// Scene host for the active <see cref="ITeleopTransport"/>. Operator and robots both hold a
    /// reference to this, never to each other.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TeleopLink : MonoBehaviour
    {
        [SerializeField, Tooltip("Impairments applied by the loopback transport. Editable live from the link panel.")]
        LinkConditions m_Conditions = new LinkConditions();

        [SerializeField, Tooltip("Seed for jitter and loss, so a recorded demo behaves the same every run.")]
        int m_Seed = 7;

        LocalLoopbackTransport m_Loopback;

        /// <summary>The transport in use. Replace via <see cref="SetTransport"/> to go over a real network.</summary>
        public ITeleopTransport transport { get; private set; }

        /// <summary>Live link impairments. Only meaningful for the loopback transport.</summary>
        public LinkConditions conditions => m_Conditions;

        /// <summary>The link's clock, in seconds. Unscaled so pausing gameplay does not freeze the network.</summary>
        public static double now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            m_Loopback = new LocalLoopbackTransport(m_Conditions, m_Seed);
            transport = m_Loopback;
        }

        void Update() => transport.Tick(now);

        /// <summary>
        /// Swaps in another transport, such as a WebSocket client. Existing subscribers must
        /// re-subscribe, so call this before robots and operator enable.
        /// </summary>
        public void SetTransport(ITeleopTransport replacement)
        {
            if (replacement != null)
                transport = replacement;
        }

        /// <summary>Clears in-flight commands, for example after an E-Stop reset.</summary>
        public void Flush() => m_Loopback?.Clear();
    }
}
