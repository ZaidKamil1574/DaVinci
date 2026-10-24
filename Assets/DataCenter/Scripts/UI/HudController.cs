using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using DataCenter.Robotics;
using DataCenter.Tasks;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>
    /// Binds the UI Toolkit operator HUD (Hud.uxml) to the simulation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Structure and look live in UXML and USS; this class only fills in values and toggles classes.
    /// Status is expressed as modifier classes (<c>dc-chip--danger</c>), never as inline colours, so
    /// the design tokens stay the single source of truth.
    /// </para>
    /// <para>
    /// Text is refreshed at <see cref="m_RefreshRate"/> rather than every frame. Setting a label's
    /// text dirties layout, and a 72 Hz headset gains nothing from numbers that change faster than
    /// anyone can read them.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class HudController : MonoBehaviour
    {
        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField]
        WorkOrderManager m_WorkOrders;

        [SerializeField, Tooltip("Floor plan drawn by the minimap.")]
        FacilityMap m_Map;

        [SerializeField, Tooltip("Text refreshes per second.")]
        float m_RefreshRate = 10f;

        [SerializeField, Tooltip("Seconds a toast stays on screen.")]
        float m_ToastDuration = 4f;

        [SerializeField, Tooltip("Most toasts shown at once.")]
        int m_MaxToasts = 4;

        static readonly string[] k_SectionNames = { "A", "SERVER HALL", "B", "NETWORK CORE", "C", "POWER ROOM", "D", "STAGING" };

        UIDocument m_Document;
        VisualElement m_Root;
        Label m_SectionLabel, m_Latency, m_Loss, m_Age, m_Fps, m_InputSource, m_RobotCount;
        Label m_OrderStatus, m_OrderId, m_OrderSummary, m_FromPort, m_ToPort, m_EndpointType, m_Elapsed, m_Errors, m_Gripper, m_ProgressLabel;
        VisualElement m_FromDot, m_ToDot, m_Progress, m_RobotList, m_OrderList, m_Deadman, m_EStop, m_Toasts, m_Help;
        Label m_DeadmanText, m_DeadmanKey;
        Button m_ModeDirect, m_ModeConsole, m_EStopButton;
        MinimapElement m_Minimap;

        readonly Dictionary<TeleopRobot, RobotCard> m_Cards = new Dictionary<TeleopRobot, RobotCard>();
        readonly Queue<VisualElement> m_ToastQueue = new Queue<VisualElement>();
        float m_NextRefresh;
        float m_FpsSmoothed = 60f;
        bool m_OrdersDirty = true;

        class RobotCard
        {
            public VisualElement root;
            public Label name, id, state, battery, task;
            public VisualElement batteryFill;
        }

        void OnEnable()
        {
            m_Document = GetComponent<UIDocument>();
            m_Root = m_Document.rootVisualElement;
            if (m_Root == null)
                return;

            Query();
            BuildHelp();

            m_ModeDirect.clicked += OnModeDirect;
            m_ModeConsole.clicked += OnModeConsole;
            m_EStopButton.clicked += OnEStop;
            m_Root.Q<Button>("estop-reset").clicked += OnReset;
            m_Root.Q<Button>("help-toggle").clicked += ToggleHelp;

            EventBus.Subscribe<ToastEvent>(OnToast);
            EventBus.Subscribe<WorkOrdersChangedEvent>(OnOrdersChanged);
            TeleopRobot.registryChanged += RebuildRobotList;

            if (m_Operator != null)
            {
                m_Operator.robotSelected += OnRobotSelected;
                m_Operator.modeChanged += OnModeChanged;
            }

            UIPointerGate.isOverUI = IsOverUI;
            BuildMinimap();
            RebuildRobotList();
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<ToastEvent>(OnToast);
            EventBus.Unsubscribe<WorkOrdersChangedEvent>(OnOrdersChanged);
            TeleopRobot.registryChanged -= RebuildRobotList;

            if (m_Operator != null)
            {
                m_Operator.robotSelected -= OnRobotSelected;
                m_Operator.modeChanged -= OnModeChanged;
            }

            if (UIPointerGate.isOverUI == IsOverUI)
                UIPointerGate.isOverUI = null;
        }

        void Query()
        {
            m_SectionLabel = m_Root.Q<Label>("section-label");
            m_Latency = m_Root.Q<Label>("link-latency");
            m_Loss = m_Root.Q<Label>("link-loss");
            m_Age = m_Root.Q<Label>("link-age");
            m_Fps = m_Root.Q<Label>("fps");
            m_InputSource = m_Root.Q<Label>("input-source");
            m_RobotCount = m_Root.Q<Label>("robot-count");
            m_RobotList = m_Root.Q("robot-list");

            m_OrderStatus = m_Root.Q<Label>("order-status");
            m_OrderId = m_Root.Q<Label>("order-id");
            m_OrderSummary = m_Root.Q<Label>("order-summary");
            m_FromPort = m_Root.Q<Label>("endpoint-from-port");
            m_ToPort = m_Root.Q<Label>("endpoint-to-port");
            m_EndpointType = m_Root.Q<Label>("endpoint-type");
            m_FromDot = m_Root.Q("endpoint-from-dot");
            m_ToDot = m_Root.Q("endpoint-to-dot");
            m_Elapsed = m_Root.Q<Label>("order-elapsed");
            m_Errors = m_Root.Q<Label>("order-errors");
            m_Gripper = m_Root.Q<Label>("gripper-state");
            m_Progress = m_Root.Q("section-progress");
            m_ProgressLabel = m_Root.Q<Label>("section-progress-label");
            m_OrderList = m_Root.Q("order-list");

            m_Deadman = m_Root.Q("deadman");
            m_DeadmanText = m_Root.Q<Label>("deadman-text");
            m_DeadmanKey = m_Root.Q<Label>("deadman-key");
            m_EStop = m_Root.Q("estop");
            m_EStopButton = m_Root.Q<Button>("estop-button");
            m_ModeDirect = m_Root.Q<Button>("mode-direct");
            m_ModeConsole = m_Root.Q<Button>("mode-console");
            m_Toasts = m_Root.Q("toasts");
            m_Help = m_Root.Q("help");
        }

        void BuildMinimap()
        {
            var host = m_Root.Q("minimap");
            if (host == null)
                return;

            host.Clear();
            m_Minimap = new MinimapElement();
            host.Add(m_Minimap);
            m_Minimap.Bind(m_Map, () => m_Operator != null ? m_Operator.robot : null, () => m_WorkOrders != null ? m_WorkOrders.focusedSection : null, m_WorkOrders);
            m_Minimap.robotClicked += robot => m_Operator?.Select(robot);
            m_Minimap.floorClicked += OnMinimapFloorClicked;
        }

        void OnMinimapFloorClicked(Vector3 world)
        {
            if (m_Operator == null || m_Operator.robot == null)
                return;

            if (!m_Operator.RequestNavigation(world))
                ToastEvent.Show("Hold the deadman (Space) while clicking the map to send the robot", ToastLevel.Warning);
        }

        void BuildHelp()
        {
            var body = m_Root.Q("help-body");
            body.Clear();
            AddHelp(body, "HOLD SPACE", "Deadman - enable motion");
            AddHelp(body, "DRAG HANDLE", "Move tool (scroll: depth)");
            AddHelp(body, "ARROWS  R F", "Nudge tool  (Shift: fine)");
            AddHelp(body, "Q E  /  Z X", "Wrist roll / pitch");
            AddHelp(body, "G", "Gripper open / close");
            AddHelp(body, "W A S D", "Drive base");
            AddHelp(body, "RIGHT-CLICK", "Navigate to point");
            AddHelp(body, "RIGHT-DRAG", "Orbit camera");
            AddHelp(body, "H", "Home arm");
            AddHelp(body, "1 - 4", "Switch robot");
            AddHelp(body, "BACKSPACE", "E-Stop");
        }

        static void AddHelp(VisualElement parent, string key, string action)
        {
            var row = new VisualElement();
            row.AddToClassList("dc-help__row");
            var keyLabel = new Label(key);
            keyLabel.AddToClassList("dc-help__key");
            var actionLabel = new Label(action);
            actionLabel.AddToClassList("dc-help__action");
            row.Add(keyLabel);
            row.Add(actionLabel);
            parent.Add(row);
        }

        void ToggleHelp()
        {
            m_Help.ToggleInClassList("dc-help--collapsed");
            m_Root.Q<Button>("help-toggle").text = m_Help.ClassListContains("dc-help--collapsed") ? "SHOW" : "HIDE";
        }

        bool IsOverUI(Vector2 screenPosition)
        {
            var panel = m_Root?.panel;
            if (panel == null)
                return false;

            // Screen space has its origin bottom-left; panels are top-left.
            var panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = panel.Pick(panelPosition);
            return picked != null && picked != m_Root;
        }

        void OnModeDirect() => m_Operator?.SetMode(ControlMode.Direct);

        void OnModeConsole() => m_Operator?.SetMode(ControlMode.Console);

        void OnEStop() => m_Operator?.EmergencyStop();

        void OnReset() => m_Operator?.ResetEmergencyStop();

        void OnModeChanged(ControlMode mode)
        {
            m_ModeDirect.EnableInClassList("dc-button--active", mode == ControlMode.Direct);
            m_ModeConsole.EnableInClassList("dc-button--active", mode == ControlMode.Console);
        }

        void OnRobotSelected(TeleopRobot robot)
        {
            foreach (var pair in m_Cards)
                pair.Value.root.EnableInClassList("dc-robot-card--selected", pair.Key == robot);

            if (robot != null)
            {
                m_SectionLabel.text = $"SECTION {robot.sectionId} · {SectionName(robot.sectionId)}";
                if (m_WorkOrders != null)
                    m_WorkOrders.FocusSection(robot.sectionId);
            }

            m_OrdersDirty = true;
        }

        static string SectionName(string id)
        {
            for (var i = 0; i < k_SectionNames.Length; i += 2)
            {
                if (k_SectionNames[i] == id)
                    return k_SectionNames[i + 1];
            }

            return string.Empty;
        }

        void RebuildRobotList()
        {
            if (m_RobotList == null)
                return;

            m_RobotList.Clear();
            m_Cards.Clear();

            foreach (var robot in TeleopRobot.all)
            {
                var card = CreateCard(robot);
                m_Cards[robot] = card;
                m_RobotList.Add(card.root);
            }

            m_RobotCount.text = $"{TeleopRobot.all.Count} online";
            if (m_Operator != null)
                OnRobotSelected(m_Operator.robot);
        }

        RobotCard CreateCard(TeleopRobot robot)
        {
            var card = new RobotCard { root = new VisualElement() };
            card.root.AddToClassList("dc-robot-card");
            card.root.RegisterCallback<ClickEvent>(_ => m_Operator?.Select(robot));

            var top = Row();
            card.name = new Label(robot.displayName);
            card.name.AddToClassList("dc-robot-card__name");
            card.state = new Label("IDLE");
            card.state.AddToClassList("dc-chip");
            top.Add(card.name);
            top.Add(card.state);

            var middle = Row();
            card.id = new Label($"{robot.robotId} · Section {robot.sectionId}");
            card.id.AddToClassList("dc-label-muted");
            middle.Add(card.id);

            var bottom = Row();
            var bar = new VisualElement();
            bar.AddToClassList("dc-bar");
            card.batteryFill = new VisualElement();
            card.batteryFill.AddToClassList("dc-bar__fill");
            bar.Add(card.batteryFill);
            card.battery = new Label("--%");
            card.battery.AddToClassList("dc-robot-card__battery");
            bottom.Add(bar);
            bottom.Add(card.battery);

            card.task = new Label("Standby");
            card.task.AddToClassList("dc-label-muted");

            card.root.Add(top);
            card.root.Add(middle);
            card.root.Add(bottom);
            card.root.Add(card.task);
            return card;
        }

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("dc-robot-card__row");
            return row;
        }

        void OnOrdersChanged(WorkOrdersChangedEvent e) => m_OrdersDirty = true;

        void Update()
        {
            if (m_Root == null)
                return;

            var dt = Time.unscaledDeltaTime;
            if (dt > 0f)
                m_FpsSmoothed = Mathf.Lerp(m_FpsSmoothed, 1f / dt, 0.05f);

            // The deadman banner and E-Stop must never lag, so they update every frame.
            UpdateSafety();

            if (Time.unscaledTime < m_NextRefresh)
                return;

            m_NextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, m_RefreshRate);
            UpdateLink();
            UpdateRobots();
            UpdateOrders();
            m_Minimap?.Refresh(1f / Mathf.Max(1f, m_RefreshRate));
        }

        void UpdateSafety()
        {
            var robot = m_Operator != null ? m_Operator.robot : null;
            var stopped = robot != null && robot.isEStopped;
            var watchdog = robot != null && robot.watchdogTripped;
            var live = m_Operator != null && m_Operator.deadman && !stopped;

            m_Deadman.EnableInClassList("dc-deadman--live", live && !watchdog);
            m_Deadman.EnableInClassList("dc-deadman--stopped", stopped || watchdog);

            var immersive = XRPlatform.isImmersive;
            m_DeadmanKey.text = immersive ? "GRIP" : "SPACE";
            m_DeadmanText.text = stopped ? "EMERGENCY STOP LATCHED"
                : watchdog ? "LINK WATCHDOG STOP"
                : live ? "MOTION ENABLED"
                : "HOLD TO ENABLE MOTION";

            m_EStop.EnableInClassList("dc-estop--latched", stopped);
            m_EStopButton.EnableInClassList("dc-estop__button--latched", stopped);
            m_EStopButton.text = stopped ? "STOPPED" : "E-STOP";
        }

        void UpdateLink()
        {
            m_Fps.text = $"{m_FpsSmoothed:0}";
            m_Fps.EnableInClassList("dc-chip--warn", m_FpsSmoothed < 60f);

            if (m_Operator == null || m_Operator.link == null || m_Operator.link.transport == null)
                return;

            var stats = m_Operator.link.transport.stats;
            m_Latency.text = stats.delivered > 0 ? $"{stats.averageLatencyMs:0} ms" : "-- ms";
            m_Loss.text = $"{stats.lossRatio * 100f:0.0}%";

            var robot = m_Operator.robot;
            var age = robot != null ? robot.commandAge : float.PositiveInfinity;
            m_Age.text = float.IsInfinity(age) ? "-- ms" : $"{Mathf.Min(age * 1000f, 9999f):0} ms";
            m_InputSource.text = m_Operator.activeInputName;
        }

        void UpdateRobots()
        {
            foreach (var pair in m_Cards)
            {
                var robot = pair.Key;
                var card = pair.Value;
                if (robot == null)
                    continue;

                card.state.text = StateLabel(robot);
                card.state.EnableInClassList("dc-chip--ok", robot.state == RobotState.Idle);
                card.state.EnableInClassList("dc-chip--accent", robot.state == RobotState.Moving || robot.state == RobotState.Manipulating);
                card.state.EnableInClassList("dc-chip--warn", robot.state == RobotState.Fault);
                card.state.EnableInClassList("dc-chip--danger", robot.state == RobotState.EStopped);

                var battery = robot.battery;
                card.battery.text = $"{battery * 100f:0}%";
                card.batteryFill.style.width = Length.Percent(battery * 100f);
                card.batteryFill.EnableInClassList("dc-bar__fill--ok", battery > 0.5f);
                card.batteryFill.EnableInClassList("dc-bar__fill--warn", battery <= 0.5f && battery > 0.2f);
                card.batteryFill.EnableInClassList("dc-bar__fill--danger", battery <= 0.2f);

                var cap = robot.speedScale < 0.99f ? $"  ·  speed capped {robot.speedScale * 100f:0}%" : string.Empty;
                card.task.text = robot.currentTask + cap;
            }
        }

        static string StateLabel(TeleopRobot robot)
        {
            switch (robot.state)
            {
                case RobotState.Moving: return "MOVING";
                case RobotState.Manipulating: return "MANIPULATING";
                case RobotState.Fault: return "FAULT";
                case RobotState.EStopped: return "E-STOPPED";
                default: return "IDLE";
            }
        }

        void UpdateOrders()
        {
            var robot = m_Operator != null ? m_Operator.robot : null;
            if (m_WorkOrders == null || robot == null)
                return;

            var section = robot.sectionId;
            var orders = m_WorkOrders.GetOrders(section);
            var active = m_WorkOrders.GetActive(section);

            var gripper = robot.gripper;
            m_Gripper.text = gripper == null ? "-" : gripper.held != null ? "HOLDING" : gripper.isClosing ? "CLOSED" : "OPEN";

            if (active != null)
            {
                m_OrderStatus.text = "ACTIVE";
                SetChip(m_OrderStatus, "dc-chip--accent");
                m_OrderId.text = active.order.id;
                m_OrderSummary.text = active.order.summary;
                m_FromPort.text = active.order.from;
                m_ToPort.text = active.order.to;
                m_EndpointType.text = active.order.portType;
                m_Elapsed.text = FormatTime(active.elapsed);
                m_Errors.text = active.errors.ToString();
                SetDot(m_FromDot, PortSocket.Find(active.order.from));
                SetDot(m_ToDot, PortSocket.Find(active.order.to));
            }
            else if (orders.Count > 0)
            {
                m_OrderStatus.text = "COMPLETE";
                SetChip(m_OrderStatus, "dc-chip--ok");
                m_OrderId.text = $"SECTION {section}";
                m_OrderSummary.text = "All work orders in this section are complete.";
                var last = orders[orders.Count - 1];
                m_FromPort.text = last.order.from;
                m_ToPort.text = last.order.to;
                m_Elapsed.text = FormatTime(last.elapsed);
                m_Errors.text = last.errors.ToString();
                m_FromDot.EnableInClassList("dc-dot--ok", true);
                m_ToDot.EnableInClassList("dc-dot--ok", true);
            }
            else
            {
                m_OrderStatus.text = "NONE";
                SetChip(m_OrderStatus, null);
                m_OrderId.text = $"SECTION {section}";
                m_OrderSummary.text = "No work orders assigned.";
            }

            var done = 0;
            for (var i = 0; i < orders.Count; i++)
            {
                if (orders[i].status == OrderStatus.Complete)
                    done++;
            }

            m_Progress.style.width = Length.Percent(orders.Count > 0 ? done * 100f / orders.Count : 0f);
            m_ProgressLabel.text = $"{done} / {orders.Count}";

            if (m_OrdersDirty)
            {
                m_OrdersDirty = false;
                RebuildOrderList(orders);
            }
        }

        void RebuildOrderList(IReadOnlyList<WorkOrderProgress> orders)
        {
            m_OrderList.Clear();
            for (var i = 0; i < orders.Count; i++)
            {
                var progress = orders[i];
                var row = new VisualElement();
                row.AddToClassList("dc-order-row");
                row.EnableInClassList("dc-order-row--active", progress.status == OrderStatus.Active);

                var dot = new VisualElement();
                dot.AddToClassList("dc-dot");
                dot.EnableInClassList("dc-dot--ok", progress.status == OrderStatus.Complete);
                dot.EnableInClassList("dc-dot--accent", progress.status == OrderStatus.Active);

                var text = new Label($"{progress.order.id}  {progress.order.summary}");
                text.AddToClassList("dc-order-row__text");

                row.Add(dot);
                row.Add(text);
                m_OrderList.Add(row);
            }
        }

        static void SetDot(VisualElement dot, PortSocket port)
        {
            var seated = port != null && port.isOccupied;
            var linked = port != null && port.linkUp;
            dot.EnableInClassList("dc-dot--ok", linked);
            dot.EnableInClassList("dc-dot--warn", seated && !linked);
            dot.EnableInClassList("dc-dot--accent", !seated);
        }

        static void SetChip(Label chip, string modifier)
        {
            chip.RemoveFromClassList("dc-chip--ok");
            chip.RemoveFromClassList("dc-chip--accent");
            chip.RemoveFromClassList("dc-chip--warn");
            chip.RemoveFromClassList("dc-chip--danger");
            if (modifier != null)
                chip.AddToClassList(modifier);
        }

        static string FormatTime(float seconds)
        {
            var total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        void OnToast(ToastEvent toast)
        {
            if (m_Toasts == null)
                return;

            var label = new Label(toast.message);
            label.AddToClassList("dc-toast");
            label.AddToClassList("dc-toast--hidden");
            switch (toast.level)
            {
                case ToastLevel.Success: label.AddToClassList("dc-toast--success"); break;
                case ToastLevel.Warning: label.AddToClassList("dc-toast--warning"); break;
                case ToastLevel.Error: label.AddToClassList("dc-toast--error"); break;
            }

            m_Toasts.Add(label);
            m_ToastQueue.Enqueue(label);

            // Removing the hidden class one frame later is what triggers the CSS transition.
            label.schedule.Execute(() => label.RemoveFromClassList("dc-toast--hidden")).StartingIn(16);
            label.schedule.Execute(() => label.AddToClassList("dc-toast--hidden")).StartingIn((long)(m_ToastDuration * 1000f));
            label.schedule.Execute(() => label.RemoveFromHierarchy()).StartingIn((long)(m_ToastDuration * 1000f) + 300);

            while (m_ToastQueue.Count > m_MaxToasts)
                m_ToastQueue.Dequeue().RemoveFromHierarchy();
        }

        /// <summary>Wires the HUD from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, WorkOrderManager workOrders, FacilityMap map)
        {
            m_Operator = teleopOperator;
            m_WorkOrders = workOrders;
            m_Map = map;
        }
    }
}
