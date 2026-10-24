using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataCenter.Tasks
{
    /// <summary>One cabling job: connect port <see cref="from"/> to port <see cref="to"/>.</summary>
    /// <remarks>Field names match the JSON files in Data/WorkOrders, read by <see cref="JsonUtility"/>.</remarks>
    [Serializable]
    public class WorkOrder
    {
        public string id;
        public string summary;
        public string portType;
        public string from;
        public string to;

        [Tooltip("Optional: the cable expected to be used. Empty accepts any cable of the right type.")]
        public string cable;
    }

    /// <summary>The work orders for one section, as stored in JSON.</summary>
    [Serializable]
    public class WorkOrderDocument
    {
        public string section;
        public string title;
        public List<WorkOrder> orders = new List<WorkOrder>();
    }
}
