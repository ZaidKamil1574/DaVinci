using System;
using UnityEngine;

namespace DataCenter
{
    /// <summary>
    /// Carries <see cref="TeleopCommand"/>s from the operator station to the robots.
    /// </summary>
    /// <remarks>
    /// The rest of the simulator only talks to this interface, so the in-process
    /// <see cref="LocalLoopbackTransport"/> can be swapped for a WebSocket or rosbridge client
    /// without touching input, robots or UI. A networked implementation would serialize with
    /// <see cref="TeleopCommand.ToJson"/> in <see cref="Send"/> and raise
    /// <see cref="CommandReceived"/> from its receive loop, marshalled onto the main thread in
    /// <see cref="Tick"/>.
    /// </remarks>
    public interface ITeleopTransport
    {
        /// <summary>Raised on the main thread, from <see cref="Tick"/>, for each command that arrives.</summary>
        event Action<TeleopCommand> CommandReceived;

        /// <summary>Queues a command for delivery. May be dropped by an unreliable link.</summary>
        void Send(TeleopCommand command);

        /// <summary>Delivers whatever is due. Call once per frame with a monotonic clock in seconds.</summary>
        void Tick(double now);

        /// <summary>Counters and timings for the link panel.</summary>
        LinkStats stats { get; }
    }

    /// <summary>Simulated network impairments applied by <see cref="LocalLoopbackTransport"/>.</summary>
    [Serializable]
    public class LinkConditions
    {
        [Range(0f, 1000f), Tooltip("One-way delay added to every command, in milliseconds.")]
        public float latencyMs = 40f;

        [Range(0f, 300f), Tooltip("Random variation either side of the latency, in milliseconds.")]
        public float jitterMs = 8f;

        [Range(0f, 50f), Tooltip("Share of commands silently dropped, in percent.")]
        public float packetLossPercent;
    }

    /// <summary>Running counters for a transport.</summary>
    [Serializable]
    public struct LinkStats
    {
        public int sent;
        public int delivered;
        public int dropped;

        /// <summary>One-way latency of the most recent delivery, in milliseconds.</summary>
        public float lastLatencyMs;

        /// <summary>Exponentially smoothed one-way latency, in milliseconds.</summary>
        public float averageLatencyMs;

        /// <summary>Clock time of the most recent delivery, in seconds.</summary>
        public double lastDeliveredAt;

        /// <summary>Share of sent commands that were dropped, in the range 0 to 1.</summary>
        public float lossRatio => sent > 0 ? dropped / (float)sent : 0f;
    }
}
