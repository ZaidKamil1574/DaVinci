using System.Collections.Generic;
using NUnit.Framework;

namespace DataCenter.Tests
{
    /// <summary>The simulated link must delay, reorder and drop the way it claims to.</summary>
    public class LoopbackTransportTests
    {
        static TeleopCommand Command(uint sequence, double sentAt) => new TeleopCommand
        {
            type = CommandType.Heartbeat,
            robotId = "R",
            sequence = sequence,
            sentAt = sentAt,
        };

        [Test]
        public void DeliversAfterLatencyNotBefore()
        {
            var link = new LocalLoopbackTransport(new LinkConditions { latencyMs = 100f, jitterMs = 0f });
            var received = new List<TeleopCommand>();
            link.CommandReceived += received.Add;

            link.Tick(0.0);
            link.Send(Command(1, 0.0));

            link.Tick(0.099);
            Assert.That(received, Is.Empty, "Delivered before the latency elapsed");

            link.Tick(0.1001);
            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(link.stats.lastLatencyMs, Is.EqualTo(100f).Within(0.5f));
        }

        [Test]
        public void ZeroLatencyDeliversOnNextTick()
        {
            var link = new LocalLoopbackTransport(new LinkConditions { latencyMs = 0f, jitterMs = 0f });
            var count = 0;
            link.CommandReceived += _ => count++;

            link.Send(Command(1, 0.0));
            link.Tick(0.0);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void PacketLossDropsRoughlyTheConfiguredShare()
        {
            var link = new LocalLoopbackTransport(new LinkConditions { latencyMs = 0f, jitterMs = 0f, packetLossPercent = 20f }, seed: 42);
            for (uint i = 1; i <= 5000; i++)
                link.Send(Command(i, 0.0));

            link.Tick(1.0);
            Assert.That(link.stats.lossRatio, Is.EqualTo(0.2f).Within(0.02f));
            Assert.That(link.stats.delivered + link.stats.dropped, Is.EqualTo(5000));
        }

        [Test]
        public void JitterCanReorderAndQueueStaysSortedByDeliveryTime()
        {
            var link = new LocalLoopbackTransport(new LinkConditions { latencyMs = 50f, jitterMs = 40f }, seed: 3);
            var order = new List<uint>();
            link.CommandReceived += c => order.Add(c.sequence);

            for (uint i = 1; i <= 200; i++)
                link.Send(Command(i, i * 0.001));

            // Tick finely so delivery order reflects the queue's delivery times.
            for (var t = 0.0; t < 1.0; t += 0.001)
                link.Tick(t);

            Assert.That(order.Count, Is.EqualTo(200));
            var reordered = false;
            for (var i = 1; i < order.Count; i++)
                reordered |= order[i] < order[i - 1];
            Assert.That(reordered, Is.True, "Jitter of 40 ms on 1 ms spacing should reorder some commands");
        }

        [Test]
        public void CommandRoundTripsThroughJson()
        {
            var original = new TeleopCommand
            {
                type = CommandType.ArmTarget,
                robotId = "R-A1",
                sequence = 77,
                sentAt = 12.5,
                deadman = true,
                position = new UnityEngine.Vector3(0.1f, 1.4f, 0.6f),
                rotation = UnityEngine.Quaternion.Euler(10f, 20f, 30f),
            };

            var copy = TeleopCommand.FromJson(original.ToJson());
            Assert.That(copy.type, Is.EqualTo(original.type));
            Assert.That(copy.robotId, Is.EqualTo(original.robotId));
            Assert.That(copy.sequence, Is.EqualTo(original.sequence));
            Assert.That(copy.deadman, Is.True);
            Assert.That((copy.position - original.position).magnitude, Is.LessThan(1e-5f));
            Assert.That(UnityEngine.Quaternion.Angle(copy.rotation, original.rotation), Is.LessThan(0.01f));
        }
    }
}
