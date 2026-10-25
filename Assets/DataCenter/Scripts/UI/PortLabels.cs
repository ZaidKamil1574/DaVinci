using System.Text.RegularExpressions;
using DataCenter.Tasks;

namespace DataCenter.UI
{
    /// <summary>
    /// Everyday names for ports, for instructions: "patch panel port 12" rather than
    /// <c>A-R03-U38-P12</c>.
    /// </summary>
    /// <remarks>
    /// The device comes from the port's parent object, which the scene builder names after the
    /// device and its rack unit ("Patch Panel U38"); the port number and rack come from the id
    /// (section-rack-unit-port).
    /// </remarks>
    public static class PortLabels
    {
        static readonly Regex k_UnitSuffix = new Regex(@"\s+U\d+$");

        /// <summary>For example "patch panel port 12".</summary>
        public static string Name(PortSocket port) => port == null ? string.Empty : $"{Device(port)} port {Number(port)}";

        /// <summary>For example "rack A-R03".</summary>
        public static string Rack(PortSocket port)
        {
            var parts = port != null && port.portId != null ? port.portId.Split('-') : null;
            return parts != null && parts.Length >= 2 ? $"rack {parts[0]}-{parts[1]}" : string.Empty;
        }

        /// <summary>The device the port is on, in plain words: "network switch", "power strip".</summary>
        public static string Device(PortSocket port)
        {
            var parent = port != null ? port.transform.parent : null;
            var name = parent != null ? k_UnitSuffix.Replace(parent.name, string.Empty) : string.Empty;
            switch (name)
            {
                case "Patch Panel": return "patch panel";
                case "ToR Switch": return "network switch";
                case "Fiber Panel": return "fiber panel";
                case "Core Line Card": return "core switch";
                case "PDU-A": return "power strip";
                case "Server PSU": return "server power supply";
                case "Training Panel": return "training panel";
                default: return name.Length > 0 ? name.ToLowerInvariant() : "port";
            }
        }

        /// <summary>The port's number on its device, from the end of its id.</summary>
        public static int Number(PortSocket port)
        {
            var id = port != null ? port.portId : null;
            var at = id != null ? id.LastIndexOf("-P", System.StringComparison.Ordinal) : -1;
            return at >= 0 && int.TryParse(id.Substring(at + 2), out var number) ? number : 0;
        }

        /// <summary>First letter upper case, for the start of a line.</summary>
        public static string Capitalise(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
