using UnityEngine;

namespace DataCenter.Tasks
{
    /// <summary>
    /// A connector standard, such as RJ45 copper, LC duplex fiber or IEC C13 power. Ports and
    /// connectors each reference one, and only matching types mate.
    /// </summary>
    [CreateAssetMenu(menuName = "Data Center/Port Type", fileName = "PortType")]
    public class PortType : ScriptableObject
    {
        [Tooltip("Short code used in work orders and port ids, for example RJ45.")]
        public string code = "RJ45";

        [Tooltip("Name shown to the operator.")]
        public string displayName = "RJ45 Copper";

        [Tooltip("Everyday name for the cable, for instructions, for example \"network cable\".")]
        public string plainName = "network cable";

        [Tooltip("One sentence explaining the connector to someone who has never seen one.")]
        [TextArea]
        public string description = "RJ45 is the standard network (Ethernet) plug, the same clip-in connector as on a laptop or home router cable.";

        [Tooltip("Cable jacket colour.")]
        public Color cableColor = new Color(0.15f, 0.45f, 0.95f);

        [Tooltip("Cable thickness, in metres.")]
        public float cableDiameter = 0.007f;

        [Tooltip("How far from the socket the connector tip may be and still seat, in metres.")]
        public float captureRadius = 0.02f;

        [Tooltip("How far off the socket axis the connector may be and still seat, in degrees.")]
        [Range(1f, 60f)]
        public float captureAngle = 25f;
    }
}
