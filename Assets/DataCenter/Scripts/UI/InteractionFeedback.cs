using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using DataCenter.Robotics;
using DataCenter.Tasks;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>
    /// Sound and touch cues for the two moments that need precision: the gripper reaching a plug,
    /// and a held plug reaching its port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chime plays when a free plug comes into grip range (grab now: Shift, or L2 in VR) and
    /// another when a held plug lines up with a port (insertion assist is about to plug it in, or,
    /// with assist off, letting go will seat it). Both play on every platform.
    /// </para>
    /// <para>
    /// In VR the controllers also buzz: pulses that come faster and stronger as the gripper closes
    /// on the plug or the plug closes on its port, on the right controller that moves the arm; a
    /// firmer buzz on the hand that should act when it is time to grab; and a buzz for every grip,
    /// release, link-up, rejection, bump and E-Stop. Haptics go through the XR rig's
    /// <see cref="HapticImpulsePlayer"/>s, which work over OpenXR on the Quest and over WebXR in
    /// the browser, with the XR input device API as a fallback.
    /// </para>
    /// </remarks>
    public class InteractionFeedback : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField, Tooltip("Distance from the plug or port at which the proximity pulses start, in metres.")]
        float m_HapticRange = 0.15f;

        [SerializeField, Tooltip("Seconds between proximity pulses at the edge of the range.")]
        float m_SlowPulseInterval = 0.45f;

        [SerializeField, Tooltip("Seconds between proximity pulses when touching.")]
        float m_FastPulseInterval = 0.08f;

        [SerializeField, Range(0f, 1f), Tooltip("Scales every haptic pulse.")]
        float m_HapticStrength = 1f;

        [SerializeField, Tooltip("Seconds out of range before the same chime can play again, so it does not chatter at the edge.")]
        float m_RearmDelay = 0.4f;

        enum Hand
        {
            Left,
            Right,
            Both,
        }

        readonly List<HapticImpulsePlayer> m_Left = new List<HapticImpulsePlayer>();
        readonly List<HapticImpulsePlayer> m_Right = new List<HapticImpulsePlayer>();
        float m_NextHapticScanAt;
        float m_NextPulseAt;
        bool m_GrabReady;
        bool m_PlugReady;
        float m_GrabRearmAt;
        float m_PlugRearmAt;

        /// <summary>Whether the gripper is in range of a free plug right now.</summary>
        public bool grabReady => m_GrabReady;

        /// <summary>Whether a held plug is lined up with a port right now.</summary>
        public bool plugReady => m_PlugReady;

        void OnEnable() => EventBus.Subscribe<FeedbackEvent>(OnFeedback);

        void OnDisable() => EventBus.Unsubscribe<FeedbackEvent>(OnFeedback);

        void Update()
        {
            var robot = m_Operator != null ? m_Operator.robot : null;
            var gripper = robot != null ? robot.gripper : null;
            var tip = robot != null && robot.arm != null ? robot.arm.toolTip : null;
            if (gripper == null || tip == null || robot.isEStopped)
            {
                SetGrabReady(false, Vector3.zero);
                SetPlugReady(false, Vector3.zero);
                return;
            }

            float distance;
            if (gripper.held is CableConnector held)
            {
                SetGrabReady(false, Vector3.zero);
                SetPlugReady(held.hoveredPort != null, held.tip.position);
                distance = NearestPortDistance(held);
            }
            else
            {
                SetPlugReady(false, Vector3.zero);
                var inReach = gripper.held == null && gripper.FindNearestInRange() is CableConnector near && near.state == CableConnector.ConnectorState.Free;
                SetGrabReady(inReach, tip.position);
                distance = NearestFreePlugDistance(tip.position);
            }

            ProximityPulses(distance);
        }

        void SetGrabReady(bool ready, Vector3 position)
        {
            if (ready == m_GrabReady)
                return;

            m_GrabReady = ready;
            if (!ready)
            {
                m_GrabRearmAt = Time.time + m_RearmDelay;
                return;
            }

            if (Time.time >= m_GrabRearmAt)
                FeedbackEvent.Play(FeedbackCue.GrabReady, position);
        }

        void SetPlugReady(bool ready, Vector3 position)
        {
            if (ready == m_PlugReady)
                return;

            m_PlugReady = ready;
            if (!ready)
            {
                m_PlugRearmAt = Time.time + m_RearmDelay;
                return;
            }

            if (Time.time >= m_PlugRearmAt)
                FeedbackEvent.Play(FeedbackCue.PlugReady, position);
        }

        /// <summary>Distance from the tool to the nearest plug lying free, or infinity.</summary>
        static float NearestFreePlugDistance(Vector3 tool)
        {
            var best = float.PositiveInfinity;
            foreach (var cable in VerletCable.all)
            {
                best = Mathf.Min(best, FreeDistance(cable.endA, tool));
                best = Mathf.Min(best, FreeDistance(cable.endB, tool));
            }

            return best;
        }

        static float FreeDistance(CableConnector end, Vector3 tool) =>
            end != null && end.state == CableConnector.ConnectorState.Free ? Vector3.Distance(end.transform.position, tool) : float.PositiveInfinity;

        /// <summary>
        /// Distance from a held plug to the nearest port it could go into: with insertion assist,
        /// the work order's target ports, which are the only ones assist plugs into; without, any
        /// free port of the same type.
        /// </summary>
        static float NearestPortDistance(CableConnector held)
        {
            var best = float.PositiveInfinity;
            foreach (var port in PortSocket.byId.Values)
            {
                if (port.isOccupied || port.type != held.type || (CableConnector.insertionAssist && !port.isWorkOrderTarget))
                    continue;

                best = Mathf.Min(best, port.DistanceTo(held));
            }

            return best;
        }

        /// <summary>Pulses on the arm hand that come faster and stronger as the gap closes.</summary>
        void ProximityPulses(float distance)
        {
            if (!XRPlatform.isImmersive || distance >= m_HapticRange || Time.time < m_NextPulseAt)
                return;

            var closeness = 1f - Mathf.Clamp01(distance / m_HapticRange);
            m_NextPulseAt = Time.time + Mathf.Lerp(m_SlowPulseInterval, m_FastPulseInterval, closeness);
            Pulse(Hand.Right, Mathf.Lerp(0.08f, 0.4f, closeness), 0.025f);
        }

        void OnFeedback(FeedbackEvent e)
        {
            if (!XRPlatform.isImmersive)
                return;

            switch (e.cue)
            {
                // The left trigger grabs, so the left hand is told it is time.
                case FeedbackCue.GrabReady:
                    Pulse(Hand.Left, 0.7f, 0.1f);
                    Pulse(Hand.Right, 0.35f, 0.06f);
                    break;
                case FeedbackCue.PlugReady:
                    Pulse(Hand.Both, 0.6f, 0.1f);
                    break;
                case FeedbackCue.Grip:
                    Pulse(Hand.Both, 0.5f, 0.06f);
                    break;
                case FeedbackCue.Release:
                    Pulse(Hand.Both, 0.3f, 0.04f);
                    break;
                case FeedbackCue.LinkUp:
                    Pulse(Hand.Both, 0.8f, 0.15f);
                    break;
                case FeedbackCue.Reject:
                    Pulse(Hand.Both, 0.7f, 0.2f);
                    break;
                case FeedbackCue.Bump:
                case FeedbackCue.BumpMetal:
                case FeedbackCue.BumpCart:
                    Pulse(Hand.Both, Mathf.Lerp(0.5f, 1f, e.strength), 0.1f);
                    break;
                case FeedbackCue.EStop:
                    Pulse(Hand.Both, 1f, 0.3f);
                    break;
                case FeedbackCue.OrderComplete:
                case FeedbackCue.SectionComplete:
                    Pulse(Hand.Both, 0.6f, 0.2f);
                    break;
            }
        }

        void Pulse(Hand hand, float amplitude, float duration)
        {
            amplitude = Mathf.Clamp01(amplitude * m_HapticStrength);
            if (amplitude <= 0f)
                return;

            FindHapticPlayers();
            if (hand != Hand.Right)
                Send(m_Left, XRNode.LeftHand, amplitude, duration);
            if (hand != Hand.Left)
                Send(m_Right, XRNode.RightHand, amplitude, duration);
        }

        static void Send(List<HapticImpulsePlayer> players, XRNode node, float amplitude, float duration)
        {
            var sent = false;
            foreach (var player in players)
            {
                if (player != null && player.isActiveAndEnabled)
                    sent |= player.SendHapticImpulse(amplitude, duration);
            }

            if (!sent)
            {
                var device = InputDevices.GetDeviceAtXRNode(node);
                if (device.isValid)
                    device.SendHapticImpulse(0u, amplitude, duration);
            }
        }

        /// <summary>
        /// Finds the rig's haptic players and sorts them by hand from their place in the hierarchy
        /// (Left Controller, Right Controller). Repeats now and then, since the rig is only switched
        /// on when a headset is in use.
        /// </summary>
        void FindHapticPlayers()
        {
            if (Time.unscaledTime < m_NextHapticScanAt && (m_Left.Count > 0 || m_Right.Count > 0))
                return;

            m_NextHapticScanAt = Time.unscaledTime + 5f;
            m_Left.Clear();
            m_Right.Clear();
            foreach (var player in FindObjectsByType<HapticImpulsePlayer>(FindObjectsSortMode.None))
            {
                for (var t = player.transform; t != null; t = t.parent)
                {
                    var name = t.name;
                    if (name.Contains("Left"))
                    {
                        m_Left.Add(player);
                        break;
                    }

                    if (name.Contains("Right"))
                    {
                        m_Right.Add(player);
                        break;
                    }
                }
            }
        }

        /// <summary>Wires the feedback from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator) => m_Operator = teleopOperator;
    }
}
