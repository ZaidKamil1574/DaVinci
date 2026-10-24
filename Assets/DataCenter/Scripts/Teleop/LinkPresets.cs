using UnityEngine;

namespace DataCenter.Teleop
{
    /// <summary>Named network conditions for the link panel and the VR wrist menu.</summary>
    public static class LinkPresets
    {
        public struct Preset
        {
            public string name;
            public float latencyMs;
            public float jitterMs;
            public float lossPercent;
        }

        /// <summary>From an on-site wired console to a bad remote link. Values are one-way.</summary>
        public static readonly Preset[] all =
        {
            new Preset { name = "LAN", latencyMs = 4f, jitterMs = 1f, lossPercent = 0f },
            new Preset { name = "Wi-Fi", latencyMs = 25f, jitterMs = 8f, lossPercent = 0.5f },
            new Preset { name = "LTE", latencyMs = 70f, jitterMs = 25f, lossPercent = 1f },
            new Preset { name = "Satellite", latencyMs = 300f, jitterMs = 40f, lossPercent = 2f },
            new Preset { name = "Degraded", latencyMs = 180f, jitterMs = 120f, lossPercent = 12f },
        };

        public static void Apply(LinkConditions conditions, Preset preset)
        {
            conditions.latencyMs = preset.latencyMs;
            conditions.jitterMs = preset.jitterMs;
            conditions.packetLossPercent = preset.lossPercent;
        }

        /// <summary>The preset matching the current conditions, or -1 for a custom setting.</summary>
        public static int IndexOf(LinkConditions conditions)
        {
            for (var i = 0; i < all.Length; i++)
            {
                if (Mathf.Approximately(all[i].latencyMs, conditions.latencyMs)
                    && Mathf.Approximately(all[i].jitterMs, conditions.jitterMs)
                    && Mathf.Approximately(all[i].lossPercent, conditions.packetLossPercent))
                    return i;
            }

            return -1;
        }
    }
}
