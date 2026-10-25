using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataCenter
{
    /// <summary>
    /// Typed publish/subscribe for events that have no natural owner to subscribe to, such as
    /// "a port was connected" or "show this toast". Systems with a clear owner (a robot's state)
    /// use plain C# events on that owner instead.
    /// </summary>
    /// <remarks>
    /// Event types are structs so publishing allocates nothing. Handlers are cleared on
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>, which keeps the bus correct
    /// when Enter Play Mode Options skip the domain reload.
    /// </remarks>
    public static class EventBus
    {
        static readonly List<Action> s_Resets = new List<Action>();

        internal static void RegisterReset(Action reset) => s_Resets.Add(reset);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll()
        {
            for (var i = 0; i < s_Resets.Count; i++)
                s_Resets[i]();
        }

        public static void Subscribe<T>(Action<T> handler) where T : struct => Channel<T>.handlers += handler;

        public static void Unsubscribe<T>(Action<T> handler) where T : struct => Channel<T>.handlers -= handler;

        public static void Publish<T>(T message) where T : struct => Channel<T>.handlers?.Invoke(message);

        static class Channel<T> where T : struct
        {
            public static Action<T> handlers;

            static Channel() => RegisterReset(() => handlers = null);
        }
    }

    /// <summary>Severity of a <see cref="ToastEvent"/>, mapped to a colour by the UI.</summary>
    public enum ToastLevel
    {
        Info,
        Success,
        Warning,
        Error,
    }

    /// <summary>A short message for the operator, shown on the HUD and the VR panel.</summary>
    public struct ToastEvent
    {
        public string message;
        public ToastLevel level;

        public ToastEvent(string message, ToastLevel level = ToastLevel.Info)
        {
            this.message = message;
            this.level = level;
        }

        public static void Show(string message, ToastLevel level = ToastLevel.Info)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Mirrored to the console so test runs and bug reports carry the operator's view of events.
            Debug.Log($"[Data Center] {level}: {message}");
#endif
            EventBus.Publish(new ToastEvent(message, level));
        }
    }

    /// <summary>Feedback sounds, played by whichever audio component is listening.</summary>
    public enum FeedbackCue
    {
        LinkUp,
        Reject,
        Grip,
        Release,
        EStop,
        OrderComplete,
        SectionComplete,
        Warning,

        /// <summary>The gripper is close enough to a plug to grab it.</summary>
        GrabReady,

        /// <summary>A held plug is lined up with a port: it will plug in (assist) or seat on release.</summary>
        PlugReady,

        /// <summary>The robot ran into a wall or partition: a dull thud.</summary>
        Bump,

        /// <summary>The robot ran into a rack, server or other metal equipment: a clank.</summary>
        BumpMetal,

        /// <summary>The robot ran into the cable cart: a light rattle.</summary>
        BumpCart,

        /// <summary>A loose plug knocked against something: a small plastic click, louder for harder hits.</summary>
        PlugTap,
    }

    /// <summary>Request to play a <see cref="FeedbackCue"/> at a position.</summary>
    public struct FeedbackEvent
    {
        public FeedbackCue cue;
        public Vector3 position;

        /// <summary>How hard, 0 to 1. Scales the volume of impact sounds.</summary>
        public float strength;

        public static void Play(FeedbackCue cue, Vector3 position, float strength = 1f) =>
            EventBus.Publish(new FeedbackEvent { cue = cue, position = position, strength = strength });

        /// <summary>
        /// The impact sound for running into <paramref name="collider"/>: walls and partitions thud,
        /// the cable cart rattles, and racks, servers and other equipment clank. Read from the
        /// object names the scene builder gives them.
        /// </summary>
        public static FeedbackCue ImpactCue(Collider collider)
        {
            for (var t = collider != null ? collider.transform : null; t != null; t = t.parent)
            {
                var name = t.name;
                if (name.StartsWith("Wall") || name == "Walls" || name == "Section Dividers")
                    return FeedbackCue.Bump;
                if (name.StartsWith("Cable Cart"))
                    return FeedbackCue.BumpCart;
            }

            return FeedbackCue.BumpMetal;
        }
    }

    /// <summary>
    /// Lets world-space pointer input ask whether the pointer is over screen UI, without the
    /// input layer depending on the UI assembly.
    /// </summary>
    public static class UIPointerGate
    {
        /// <summary>Set by the HUD. Returns whether a screen position is over an interactive HUD element.</summary>
        public static Func<Vector2, bool> isOverUI;

        public static bool IsOverUI(Vector2 screenPosition) => isOverUI != null && isOverUI(screenPosition);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => isOverUI = null;
    }
}
