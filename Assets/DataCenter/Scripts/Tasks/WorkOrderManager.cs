using System;
using System.Collections.Generic;
using UnityEngine;
using DataCenter.Robotics;

namespace DataCenter.Tasks
{
    /// <summary>Progress on one work order.</summary>
    public enum OrderStatus
    {
        Pending,
        Active,
        Complete,
    }

    /// <summary>Live state of a <see cref="WorkOrder"/>.</summary>
    public class WorkOrderProgress
    {
        public WorkOrder order;
        public string sectionId;
        public OrderStatus status;
        public int errors;
        public float startedAt = -1f;
        public float completedAt = -1f;

        /// <summary>Seconds spent so far, or total once complete.</summary>
        public float elapsed => startedAt < 0f ? 0f : (completedAt >= 0f ? completedAt : Time.time) - startedAt;

        /// <summary>How many of the two ends are seated in the right ports, 0 to 2.</summary>
        public int endsDone;
    }

    /// <summary>Raised whenever any work order's progress changes.</summary>
    public struct WorkOrdersChangedEvent
    {
        public string sectionId;
    }

    /// <summary>
    /// Tracks work orders per section: highlights target ports, scores insertions, counts errors
    /// and times each order.
    /// </summary>
    public class WorkOrderManager : MonoBehaviour
    {
        [SerializeField, Tooltip("One set per section.")]
        List<WorkOrderSet> m_Sets = new List<WorkOrderSet>();

        readonly Dictionary<string, List<WorkOrderProgress>> m_BySection = new Dictionary<string, List<WorkOrderProgress>>();
        readonly HashSet<string> m_CompletedSections = new HashSet<string>();
        string m_FocusedSection;

        /// <summary>Raised when every order in a section is complete.</summary>
        public event Action<string> sectionCompleted;

        /// <summary>The section whose target ports are highlighted.</summary>
        public string focusedSection => m_FocusedSection;

        /// <summary>Section ids with orders, in set order.</summary>
        public IEnumerable<string> sections => m_BySection.Keys;

        void Awake()
        {
            for (var i = 0; i < m_Sets.Count; i++)
            {
                var set = m_Sets[i];
                if (set == null)
                    continue;

                var document = set.Load();
                var list = new List<WorkOrderProgress>();
                foreach (var order in document.orders)
                    list.Add(new WorkOrderProgress { order = order, sectionId = set.sectionId });

                m_BySection[set.sectionId] = list;
            }
        }

        void OnEnable()
        {
            PortSocket.changed += OnPortChanged;
            EventBus.Subscribe<PortRejectedEvent>(OnPortRejected);
        }

        void OnDisable()
        {
            PortSocket.changed -= OnPortChanged;
            EventBus.Unsubscribe<PortRejectedEvent>(OnPortRejected);
        }

        void Start()
        {
            foreach (var section in m_BySection.Keys)
            {
                ActivateNext(section);
                break;
            }
        }

        /// <summary>Orders for a section, in sequence. Empty if the section has none.</summary>
        public IReadOnlyList<WorkOrderProgress> GetOrders(string sectionId) =>
            sectionId != null && m_BySection.TryGetValue(sectionId, out var list) ? list : Array.Empty<WorkOrderProgress>();

        /// <summary>The order currently being worked in a section, or null when all are done.</summary>
        public WorkOrderProgress GetActive(string sectionId)
        {
            var list = GetOrders(sectionId);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].status == OrderStatus.Active)
                    return list[i];
            }

            return null;
        }

        public bool IsSectionComplete(string sectionId) => m_CompletedSections.Contains(sectionId);

        /// <summary>Moves the port highlights to <paramref name="sectionId"/>, for example when the operator switches robot.</summary>
        public void FocusSection(string sectionId)
        {
            if (m_FocusedSection == sectionId)
                return;

            SetHighlights(GetActive(m_FocusedSection), PortIndicator.Off);
            m_FocusedSection = sectionId;

            if (GetActive(sectionId) == null && !IsSectionComplete(sectionId))
                ActivateNext(sectionId);
            else
                SetHighlights(GetActive(sectionId), PortIndicator.Target);

            Publish(sectionId);
        }

        void ActivateNext(string sectionId)
        {
            m_FocusedSection ??= sectionId;
            var list = GetOrders(sectionId);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].status != OrderStatus.Pending)
                    continue;

                list[i].status = OrderStatus.Active;
                list[i].startedAt = Time.time;
                Evaluate(list[i]);
                if (sectionId == m_FocusedSection)
                    SetHighlights(list[i], PortIndicator.Target);
                Publish(sectionId);
                return;
            }
        }

        void SetHighlights(WorkOrderProgress progress, PortIndicator indicator)
        {
            if (progress == null)
                return;

            PortSocket.Find(progress.order.from)?.SetBaseIndicator(indicator);
            PortSocket.Find(progress.order.to)?.SetBaseIndicator(indicator);
        }

        void OnPortChanged(PortChangedEvent change)
        {
            var progress = FindOrderFor(change.port.portId);
            if (progress == null)
            {
                // A port no active order asked for.
                if (change.connected && change.connector != null)
                {
                    var active = ActiveForSection(SectionOf(change.port.portId));
                    if (active != null)
                    {
                        active.errors++;
                        change.port.SetBaseIndicator(PortIndicator.WrongPort);
                        ToastEvent.Show($"Wrong port: {change.port.portId} is not part of {active.order.id}", ToastLevel.Warning);
                        FeedbackEvent.Play(FeedbackCue.Warning, change.port.socket.position);
                        Publish(active.sectionId);
                    }
                }
                else if (!change.connected)
                {
                    // Clears a wrong-port warning once the stray cable is pulled.
                    change.port.SetBaseIndicator(PortIndicator.Off);
                }

                return;
            }

            Evaluate(progress);
            Publish(progress.sectionId);
        }

        void OnPortRejected(PortRejectedEvent rejection)
        {
            var active = ActiveForSection(SectionOf(rejection.port.portId));
            if (active != null)
            {
                active.errors++;
                Publish(active.sectionId);
            }

            var offered = rejection.connector != null && rejection.connector.type != null ? rejection.connector.type.code : "?";
            var wanted = rejection.port.type != null ? rejection.port.type.code : "?";
            var message = rejection.reason == InsertCheck.Occupied
                ? $"{rejection.port.portId} is already occupied"
                : $"Rejected: {offered} connector does not fit {wanted} port {rejection.port.portId}";

            ToastEvent.Show(message, ToastLevel.Error);
            FeedbackEvent.Play(FeedbackCue.Reject, rejection.port.socket.position);
        }

        void Evaluate(WorkOrderProgress progress)
        {
            if (progress.status != OrderStatus.Active)
                return;

            var from = PortSocket.Find(progress.order.from);
            var to = PortSocket.Find(progress.order.to);
            if (from == null || to == null)
            {
                Debug.LogWarning($"[Data Center] Work order {progress.order.id} references a missing port ({progress.order.from} / {progress.order.to}).");
                return;
            }

            progress.endsDone = (from.isOccupied ? 1 : 0) + (to.isOccupied ? 1 : 0);

            // Complete only when one cable joins the two ports. Two unrelated cables each plugged
            // into one of them would light both LEDs and still be wrong.
            if (!from.isOccupied || !to.isOccupied)
                return;

            var cableA = from.occupant.cable;
            var cableB = to.occupant.cable;
            if (cableA == null || cableA != cableB)
            {
                ToastEvent.Show($"{progress.order.id}: both ports are connected, but not by the same cable", ToastLevel.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(progress.order.cable) && cableA.cableId != progress.order.cable)
            {
                ToastEvent.Show($"{progress.order.id}: expected cable {progress.order.cable}", ToastLevel.Warning);
                return;
            }

            Complete(progress);
        }

        void Complete(WorkOrderProgress progress)
        {
            progress.status = OrderStatus.Complete;
            progress.completedAt = Time.time;
            SetHighlights(progress, PortIndicator.Off);

            ToastEvent.Show($"{progress.order.id} complete in {progress.elapsed:0.0}s with {progress.errors} error(s)", ToastLevel.Success);
            FeedbackEvent.Play(FeedbackCue.OrderComplete, PortSocket.Find(progress.order.to).socket.position);

            ActivateNext(progress.sectionId);

            if (GetActive(progress.sectionId) == null && !m_CompletedSections.Contains(progress.sectionId))
            {
                m_CompletedSections.Add(progress.sectionId);
                ToastEvent.Show($"Section {progress.sectionId} complete", ToastLevel.Success);
                FeedbackEvent.Play(FeedbackCue.SectionComplete, Vector3.zero);
                sectionCompleted?.Invoke(progress.sectionId);
            }
        }

        WorkOrderProgress FindOrderFor(string portId)
        {
            foreach (var list in m_BySection.Values)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.status == OrderStatus.Active && (p.order.from == portId || p.order.to == portId))
                        return p;
                }
            }

            return null;
        }

        WorkOrderProgress ActiveForSection(string sectionId) => GetActive(sectionId);

        /// <summary>Section letter from a port id such as A-R03-U40-P12.</summary>
        static string SectionOf(string portId)
        {
            if (string.IsNullOrEmpty(portId))
                return null;

            var dash = portId.IndexOf('-');
            return dash > 0 ? portId.Substring(0, dash) : portId;
        }

        void Publish(string sectionId)
        {
            // Robots stationed in the section report the order they are working on.
            var active = GetActive(sectionId);
            var task = active != null ? $"{active.order.id}: {active.endsDone}/2 ends"
                : IsSectionComplete(sectionId) ? "Section complete" : "Standby";

            var robots = TeleopRobot.all;
            for (var i = 0; i < robots.Count; i++)
            {
                if (robots[i].sectionId == sectionId)
                    robots[i].currentTask = task;
            }

            EventBus.Publish(new WorkOrdersChangedEvent { sectionId = sectionId });
        }

        /// <summary>Wires the manager from code. Used by the scene builder.</summary>
        public void Configure(List<WorkOrderSet> sets) => m_Sets = sets;
    }
}
