using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataCenter
{
    /// <summary>A named area of the floor plan, such as a section.</summary>
    [Serializable]
    public struct MapZone
    {
        public string id;
        public string title;

        [Tooltip("Floor extents: x/y of the rect are world X/Z.")]
        public Rect area;

        public Color accent;
    }

    /// <summary>
    /// Floor plan of the hall in world X/Z: sections and equipment footprints. The minimap draws
    /// from this instead of rendering the scene a second time from above, which keeps it crisp at
    /// any size and costs nothing on the GPU, on Quest and in the browser alike.
    /// </summary>
    /// <remarks>Filled in by the scene builder from the same numbers it builds the hall with.</remarks>
    public class FacilityMap : MonoBehaviour
    {
        [SerializeField, Tooltip("Hall floor extents in world X/Z.")]
        Rect m_Bounds = Rect.MinMaxRect(-20f, -12.5f, 20f, 12.5f);

        [SerializeField]
        List<MapZone> m_Zones = new List<MapZone>();

        [SerializeField, Tooltip("Equipment footprints in world X/Z: rack rows, cabinets, carts.")]
        List<Rect> m_Obstacles = new List<Rect>();

        public Rect bounds => m_Bounds;
        public IReadOnlyList<MapZone> zones => m_Zones;
        public IReadOnlyList<Rect> obstacles => m_Obstacles;

        /// <summary>The zone containing a world position, or null.</summary>
        public MapZone? ZoneAt(Vector3 world)
        {
            var point = new Vector2(world.x, world.z);
            for (var i = 0; i < m_Zones.Count; i++)
            {
                if (m_Zones[i].area.Contains(point))
                    return m_Zones[i];
            }

            return null;
        }

        /// <summary>Looks up a zone by id.</summary>
        public bool TryGetZone(string id, out MapZone zone)
        {
            for (var i = 0; i < m_Zones.Count; i++)
            {
                if (m_Zones[i].id == id)
                {
                    zone = m_Zones[i];
                    return true;
                }
            }

            zone = default;
            return false;
        }

        /// <summary>Wires the map from code. Used by the scene builder.</summary>
        public void Configure(Rect hallBounds, List<MapZone> zones, List<Rect> obstacles)
        {
            m_Bounds = hallBounds;
            m_Zones = zones;
            m_Obstacles = obstacles;
        }
    }
}
