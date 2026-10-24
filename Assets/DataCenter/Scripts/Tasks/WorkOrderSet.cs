using System;
using UnityEngine;

namespace DataCenter.Tasks
{
    /// <summary>
    /// A section's work orders. Holds the JSON as a <see cref="TextAsset"/> so orders can be edited
    /// as data, or later fetched from a ticketing system, without touching the scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Data Center/Work Order Set", fileName = "WorkOrders")]
    public class WorkOrderSet : ScriptableObject
    {
        [Tooltip("Section id these orders belong to, for example A.")]
        public string sectionId = "A";

        [Tooltip("JSON in the WorkOrderDocument format.")]
        public TextAsset json;

        WorkOrderDocument m_Cached;

        /// <summary>Parses <see cref="json"/>. Cached after the first call.</summary>
        public WorkOrderDocument Load()
        {
            if (m_Cached != null)
                return m_Cached;

            if (json == null)
            {
                Debug.LogError($"[Data Center] Work order set '{name}' has no JSON assigned.", this);
                return m_Cached = new WorkOrderDocument { section = sectionId };
            }

            try
            {
                m_Cached = JsonUtility.FromJson<WorkOrderDocument>(json.text) ?? new WorkOrderDocument();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[Data Center] Could not parse work orders in '{json.name}': {e.Message}", this);
                m_Cached = new WorkOrderDocument();
            }

            if (string.IsNullOrEmpty(m_Cached.section))
                m_Cached.section = sectionId;
            return m_Cached;
        }

        void OnValidate() => m_Cached = null;
    }
}
