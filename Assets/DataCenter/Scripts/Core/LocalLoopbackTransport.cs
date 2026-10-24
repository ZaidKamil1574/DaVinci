using System;
using System.Collections.Generic;

namespace DataCenter
{
    /// <summary>
    /// In-process transport that delivers commands back to the same application after a
    /// simulated network delay, with jitter and packet loss.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Commands wait in a queue ordered by delivery time. With jitter, two commands sent close
    /// together can swap order, exactly as UDP-style teleop links do; the robot handles that by
    /// discarding anything older than the last sequence number it applied.
    /// </para>
    /// <para>
    /// Plain C# with an injected clock and seed, so it can be unit tested without a scene.
    /// </para>
    /// </remarks>
    public class LocalLoopbackTransport : ITeleopTransport
    {
        struct Pending
        {
            public double deliverAt;
            public TeleopCommand command;
        }

        /// <summary>Smoothing factor for <see cref="LinkStats.averageLatencyMs"/>. Higher reacts faster.</summary>
        const float k_LatencySmoothing = 0.1f;

        readonly List<Pending> m_Queue = new List<Pending>(64);
        readonly List<TeleopCommand> m_Delivering = new List<TeleopCommand>(64);
        readonly LinkConditions m_Conditions;
        readonly Random m_Random;
        LinkStats m_Stats;
        double m_Now;

        public event Action<TeleopCommand> CommandReceived;

        public LinkStats stats => m_Stats;

        /// <summary>The impairments currently applied. Edits take effect on the next <see cref="Send"/>.</summary>
        public LinkConditions conditions => m_Conditions;

        /// <summary>Commands sent but not yet delivered.</summary>
        public int inFlight => m_Queue.Count;

        public LocalLoopbackTransport(LinkConditions conditions, int seed = 1)
        {
            m_Conditions = conditions ?? new LinkConditions();
            m_Random = new Random(seed);
        }

        public void Send(TeleopCommand command)
        {
            m_Stats.sent++;

            if (m_Random.NextDouble() * 100.0 < m_Conditions.packetLossPercent)
            {
                m_Stats.dropped++;
                return;
            }

            var jitter = (m_Random.NextDouble() * 2.0 - 1.0) * m_Conditions.jitterMs;
            var delaySeconds = Math.Max(0.0, m_Conditions.latencyMs + jitter) / 1000.0;
            var deliverAt = Math.Max(m_Now, command.sentAt) + delaySeconds;

            // Insertion sort from the back: commands are mostly sent in delivery order, so this is
            // nearly always a single comparison.
            var index = m_Queue.Count;
            while (index > 0 && m_Queue[index - 1].deliverAt > deliverAt)
                index--;

            m_Queue.Insert(index, new Pending { deliverAt = deliverAt, command = command });
        }

        public void Tick(double now)
        {
            m_Now = now;

            var due = 0;
            while (due < m_Queue.Count && m_Queue[due].deliverAt <= now)
                due++;

            if (due == 0)
                return;

            // Copy out before raising, so a handler that sends in response cannot disturb the loop.
            for (var i = 0; i < due; i++)
            {
                var command = m_Queue[i].command;
                var latencyMs = (float)((now - command.sentAt) * 1000.0);

                m_Stats.delivered++;
                m_Stats.lastLatencyMs = latencyMs;
                m_Stats.averageLatencyMs = m_Stats.delivered == 1
                    ? latencyMs
                    : m_Stats.averageLatencyMs + (latencyMs - m_Stats.averageLatencyMs) * k_LatencySmoothing;
                m_Stats.lastDeliveredAt = now;

                m_Delivering.Add(command);
            }

            m_Queue.RemoveRange(0, due);

            for (var i = 0; i < m_Delivering.Count; i++)
                CommandReceived?.Invoke(m_Delivering[i]);

            m_Delivering.Clear();
        }

        /// <summary>Drops everything in flight. Used when the link is reset.</summary>
        public void Clear() => m_Queue.Clear();
    }
}
