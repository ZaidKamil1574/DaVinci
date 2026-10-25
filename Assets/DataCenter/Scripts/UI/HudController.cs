using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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

        [SerializeField]
        WristCameraFeed m_WristCamera;

        [SerializeField]
        ProximityIndicator m_Proximity;

        [SerializeField]
        CommandGhost m_Ghost;

        [SerializeField]
        TutorialController m_Tutorial;

        [SerializeField]
        GuidanceController m_Guidance;

        [SerializeField, Tooltip("Console tool speed at full stick, in metres per second.")]
        float m_ConsoleToolSpeed = 0.2f;

        [SerializeField, Tooltip("Console wrist rate at full stick, in degrees per second.")]
        float m_ConsoleWristRate = 70f;

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
        Label m_OrderNext;
        VisualElement m_OrderKeys;
        string m_KeysShown = string.Empty;
        SparklineElement m_LatencyGraph, m_FpsGraph;
        bool m_HelpUserToggled;

        // Taller than this (panel units) and the controls panel starts folded; see OnHelpGeometryChanged.
        const float k_HelpMaxHeight = 240f;
        Label m_FrameTime;

        // Dashed guides on the graphs: a comfortable teleoperation latency, and the frame rate to hold.
        const float k_LatencyBudgetMs = 100f;
        VisualElement m_LinkPanel, m_TutorialCard, m_TutorialDots, m_Console, m_ProximityPill, m_ProximityDot, m_WristView;
        Label m_TutorialStep, m_TutorialTime, m_TutorialText, m_ProximityValue, m_ProximityCap, m_CommandGap, m_LinkInFlight, m_LinkDetail, m_WristRate, m_WristOverlay;
        Slider m_LatencySlider, m_JitterSlider, m_LossSlider;
        VirtualStick m_DriveStick, m_ToolStick, m_WristStick;
        bool m_ConsoleWasActive;

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
            m_Root.Q<Button>("help-toggle").clicked += OnHelpToggleClicked;
            m_Help.RegisterCallback<GeometryChangedEvent>(OnHelpGeometryChanged);
            var assist = m_Root.Q<Button>("assist-button");
            if (assist != null)
            {
                assist.clicked += () => SetAssist(!CableConnector.insertionAssist);
                RefreshAssistButton();
            }

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
            BuildLinkPanel();
            BuildTutorial();
            BuildConsole();
            BuildWristCam();
            BuildPerformance();
            RebuildRobotList();
        }

        void BuildPerformance()
        {
            m_LatencyGraph = AddGraph("latency-graph", k_LatencyBudgetMs);
            m_FpsGraph = AddGraph("fps-graph", 60f);
        }

        SparklineElement AddGraph(string hostName, float guide)
        {
            var host = m_Root.Q(hostName);
            if (host == null)
                return null;

            host.Clear();
            var graph = new SparklineElement(Mathf.RoundToInt(m_RefreshRate * 8f)) { guide = guide };
            host.Add(graph);
            return graph;
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

            if (m_Tutorial != null)
                m_Tutorial.stepChanged -= OnTutorialStep;
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
            m_OrderNext = m_Root.Q<Label>("order-next");
            m_OrderKeys = m_Root.Q("order-keys");
            m_FrameTime = m_Root.Q<Label>("perf-frame");

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

        // ---------------------------------------------------------------- Link panel

        void BuildLinkPanel()
        {
            m_LinkPanel = m_Root.Q("link-panel");
            m_LinkInFlight = m_Root.Q<Label>("link-inflight");
            m_LinkDetail = m_Root.Q<Label>("link-detail");
            m_LatencySlider = m_Root.Q<Slider>("link-latency-slider");
            m_JitterSlider = m_Root.Q<Slider>("link-jitter-slider");
            m_LossSlider = m_Root.Q<Slider>("link-loss-slider");

            var linkButton = m_Root.Q<Button>("link-button");
            linkButton.clicked += () =>
            {
                m_LinkPanel.ToggleInClassList("dc-hidden");
                linkButton.EnableInClassList("dc-button--active", !m_LinkPanel.ClassListContains("dc-hidden"));
            };

            var presets = m_Root.Q("link-presets");
            presets.Clear();
            foreach (var preset in LinkPresets.all)
            {
                var p = preset;
                var button = new Button(() => ApplyPreset(p)) { text = p.name.ToUpperInvariant() };
                button.AddToClassList("dc-button");
                presets.Add(button);
            }

            var conditions = m_Operator != null && m_Operator.link != null ? m_Operator.link.conditions : null;
            if (conditions == null)
                return;

            m_LatencySlider.SetValueWithoutNotify(conditions.latencyMs);
            m_JitterSlider.SetValueWithoutNotify(conditions.jitterMs);
            m_LossSlider.SetValueWithoutNotify(conditions.packetLossPercent);
            m_LatencySlider.RegisterValueChangedCallback(e => conditions.latencyMs = e.newValue);
            m_JitterSlider.RegisterValueChangedCallback(e => conditions.jitterMs = e.newValue);
            m_LossSlider.RegisterValueChangedCallback(e => conditions.packetLossPercent = e.newValue);
        }

        void ApplyPreset(LinkPresets.Preset preset)
        {
            if (m_Operator == null || m_Operator.link == null)
                return;

            LinkPresets.Apply(m_Operator.link.conditions, preset);
            m_LatencySlider.SetValueWithoutNotify(preset.latencyMs);
            m_JitterSlider.SetValueWithoutNotify(preset.jitterMs);
            m_LossSlider.SetValueWithoutNotify(preset.lossPercent);
            ToastEvent.Show($"Link: {preset.name} ({preset.latencyMs:0} ms, ±{preset.jitterMs:0} ms, {preset.lossPercent:0.#}% loss)");
        }

        void UpdateLinkDetail()
        {
            if (m_LinkPanel == null || m_LinkPanel.ClassListContains("dc-hidden") || m_Operator == null || m_Operator.link == null)
                return;

            // Keep sliders honest when the VR wrist menu changes the preset.
            var conditions = m_Operator.link.conditions;
            m_LatencySlider.SetValueWithoutNotify(conditions.latencyMs);
            m_JitterSlider.SetValueWithoutNotify(conditions.jitterMs);
            m_LossSlider.SetValueWithoutNotify(conditions.packetLossPercent);

            var stats = m_Operator.link.transport.stats;
            if (m_Operator.link.transport is LocalLoopbackTransport loopback)
                m_LinkInFlight.text = $"{loopback.inFlight} in flight";

            var watchdog = m_Operator.robot != null ? m_Operator.robot.spec.watchdogTimeout * 1000f : 300f;
            m_LinkDetail.text = $"Sent {stats.sent:N0}  ·  delivered {stats.delivered:N0}  ·  dropped {stats.dropped:N0}\n" +
                                "Commands are state, not events: each update repeats the full target, so loss costs smoothness, not correctness. " +
                                $"The robot stops by itself if nothing arrives for {watchdog:0} ms while it is cleared to move.";
        }

        // ---------------------------------------------------------------- Tutorial

        void BuildTutorial()
        {
            m_TutorialCard = m_Root.Q("tutorial");
            m_TutorialStep = m_Root.Q<Label>("tutorial-step");
            m_TutorialTime = m_Root.Q<Label>("tutorial-time");
            m_TutorialText = m_Root.Q<Label>("tutorial-text");
            m_TutorialDots = m_Root.Q("tutorial-dots");
            m_Root.Q<Button>("tutorial-skip").clicked += () => m_Tutorial?.Skip();
            m_Root.Q<Button>("tutorial-button").clicked += () =>
            {
                if (m_Tutorial == null)
                    return;
                if (m_Tutorial.isRunning)
                    m_Tutorial.Skip();
                else
                    m_Tutorial.Begin();
            };

            m_TutorialDots.Clear();
            for (var i = 0; i < TutorialController.stepCount; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("dc-dot");
                m_TutorialDots.Add(dot);
            }

            if (m_Tutorial != null)
                m_Tutorial.stepChanged += OnTutorialStep;
        }

        void OnTutorialStep(TutorialController.Step step)
        {
            var visible = step != TutorialController.Step.NotStarted;
            m_TutorialCard.EnableInClassList("dc-hidden", !visible);
            m_Toasts.EnableInClassList("dc-toasts--below-tutorial", visible);
            m_Root.Q<Button>("tutorial-button").text = m_Tutorial.isRunning ? "SKIP TUTORIAL" : "TUTORIAL";
            if (!visible)
                return;

            m_TutorialText.text = m_Tutorial.instruction;
            var done = step == TutorialController.Step.Done;
            m_TutorialStep.text = done ? "TUTORIAL COMPLETE" : $"STEP {m_Tutorial.stepNumber} / {TutorialController.stepCount}";
            for (var i = 0; i < m_TutorialDots.childCount; i++)
            {
                var dot = m_TutorialDots[i];
                dot.EnableInClassList("dc-dot--ok", done || i < m_Tutorial.stepNumber - 1);
                dot.EnableInClassList("dc-dot--accent", !done && i == m_Tutorial.stepNumber - 1);
            }

            if (done)
            {
                m_TutorialCard.schedule.Execute(() =>
                {
                    m_TutorialCard.AddToClassList("dc-hidden");
                    m_Toasts.RemoveFromClassList("dc-toasts--below-tutorial");
                }).StartingIn(6000);
            }
        }

        // ---------------------------------------------------------------- Console

        void BuildConsole()
        {
            m_Console = m_Root.Q("console");
            var sticks = m_Root.Q("console-sticks");
            sticks.Clear();
            m_DriveStick = new VirtualStick("BASE");
            m_ToolStick = new VirtualStick("TOOL reach / height");
            m_WristStick = new VirtualStick("WRIST ROLL");
            sticks.Add(m_DriveStick);
            sticks.Add(m_ToolStick);
            sticks.Add(m_WristStick);
        }

        void UpdateConsole()
        {
            if (m_Operator == null || m_Console == null)
                return;

            var active = m_Operator.mode == ControlMode.Console && !XRPlatform.isImmersive;
            m_Console.EnableInClassList("dc-hidden", !active);
            if (!active)
            {
                if (m_ConsoleWasActive)
                {
                    m_Operator.consoleDrive = Vector2.zero;
                    m_Operator.consoleArmVelocity = Vector3.zero;
                    m_Operator.consoleWristRollRate = 0f;
                    m_ConsoleWasActive = false;
                }

                return;
            }

            m_ConsoleWasActive = true;
            m_Operator.consoleDrive = m_DriveStick.value;
            var tool = m_ToolStick.value;
            m_Operator.consoleArmVelocity = new Vector3(0f, tool.x, tool.y) * m_ConsoleToolSpeed;
            m_Operator.consoleWristRollRate = -m_WristStick.value.x * m_ConsoleWristRate;
        }

        // ---------------------------------------------------------------- Wrist camera and proximity

        void BuildWristCam()
        {
            m_WristView = m_Root.Q("wristcam-view");
            m_WristRate = m_Root.Q<Label>("wristcam-rate");
            m_WristOverlay = m_Root.Q<Label>("wristcam-overlay");
            m_ProximityPill = m_Root.Q("proximity");
            m_ProximityDot = m_Root.Q("proximity-dot");
            m_ProximityValue = m_Root.Q<Label>("proximity-value");
            m_ProximityCap = m_Root.Q<Label>("proximity-cap");
            m_CommandGap = m_Root.Q<Label>("command-gap");

            if (m_WristCamera != null && m_WristCamera.texture != null)
            {
                m_WristView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(m_WristCamera.texture));
                m_WristRate.text = $"{m_WristCamera.texture.width}x{m_WristCamera.texture.height}  ·  {m_WristCamera.rate:0} fps";
            }
        }

        void UpdateProximity()
        {
            if (m_Ghost != null)
                m_CommandGap.text = $"{m_Ghost.positionGap * 1000f:0} mm";

            var robot = m_Operator != null ? m_Operator.robot : null;
            if (m_WristOverlay != null && robot != null)
                m_WristOverlay.text = $"{robot.robotId}  ·  {(robot.gripper != null && robot.gripper.held != null ? "HOLDING" : "OPEN")}";

            var sensor = m_Proximity != null ? m_Proximity.sensor : null;
            if (sensor == null)
                return;

            var zone = sensor.zone;
            var inRange = !float.IsInfinity(sensor.toolDistance);
            m_ProximityValue.text = inRange ? $"{sensor.toolDistance * 100f:0.0} cm" : "clear";
            m_ProximityCap.text = sensor.speedScale < 0.99f ? $"speed {sensor.speedScale * 100f:0}%" : string.Empty;
            m_ProximityDot.EnableInClassList("dc-dot--ok", zone == ProximityZone.Clear);
            m_ProximityDot.EnableInClassList("dc-dot--warn", zone == ProximityZone.Warning);
            m_ProximityDot.EnableInClassList("dc-dot--danger", zone == ProximityZone.Danger);
            m_ProximityPill.EnableInClassList("dc-proximity--warn", zone == ProximityZone.Warning);
            m_ProximityPill.EnableInClassList("dc-proximity--danger", zone == ProximityZone.Danger);
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

            var drive = AddHelpGroup(body, "DRIVE BASE");
            AddHelp(drive, "W S", "forward / back");
            AddHelp(drive, "A D", "turn");

            var arm = AddHelpGroup(body, "MOVE ARM");
            AddHelp(arm, "UP DOWN", "up / down");
            AddHelp(arm, "LEFT RIGHT", "left / right");
            AddHelp(arm, "R F", "reach / back");
            AddHelp(arm, "H", "hold: home");

            var gripper = AddHelpGroup(body, "GRIPPER");
            AddHelp(gripper, "SHIFT", "tap: grab / let go");
            AddHelp(gripper, "Q E", "tilt up / down");
            AddHelp(gripper, "Z X", "roll");

            var view = AddHelpGroup(body, "CAMERA");
            AddHelp(view, "SHIFT+WASD", "move");
            AddHelp(view, "R-DRAG", "look around");
            AddHelp(view, "SCROLL", "zoom");
            AddHelp(view, "V", "follow robot");

            var safety = AddHelpGroup(body, "SAFETY");
            AddHelp(safety, "BKSP", "E-Stop");
            AddHelp(safety, "SHIFT+BKSP", "reset");
            AddHelp(safety, "1 - 4", "switch robot");
            AddHelp(safety, "C", "hide keys");
        }

        static VisualElement AddHelpGroup(VisualElement parent, string title)
        {
            var group = new VisualElement();
            group.AddToClassList("dc-help__group");
            var label = new Label(title);
            label.AddToClassList("dc-help__group-title");
            group.Add(label);
            parent.Add(group);
            return group;
        }

        /// <summary>
        /// One row: key caps, then what they do. <paramref name="keys"/> is space-separated caps;
        /// a "+" inside a cap splits it into a chord, like SHIFT+BKSP. "1 - 4" stays one cap.
        /// </summary>
        static void AddHelp(VisualElement parent, string keys, string action)
        {
            var row = new VisualElement();
            row.AddToClassList("dc-help__row");
            var caps = new VisualElement();
            caps.AddToClassList("dc-help__keys");

            foreach (var chord in keys == "1 - 4" ? new[] { keys } : keys.Split('+'))
            {
                if (caps.childCount > 0)
                {
                    var plus = new Label("+");
                    plus.AddToClassList("dc-help__plus");
                    caps.Add(plus);
                }

                foreach (var key in chord == "1 - 4" ? new[] { chord } : chord.Split(' '))
                    caps.Add(KeyCap(key));
            }

            var actionLabel = new Label(action);
            actionLabel.AddToClassList("dc-help__action");
            row.Add(caps);
            row.Add(actionLabel);
            parent.Add(row);
        }

        static Label KeyCap(string key)
        {
            var cap = new Label(KeyGlyph(key));
            cap.AddToClassList("dc-key");
            return cap;
        }

        /// <summary>Arrow keys as arrows; the HUD font has them, the world-space label font does not.</summary>
        static string KeyGlyph(string key)
        {
            switch (key)
            {
                case "UP": return "\u2191";
                case "DOWN": return "\u2193";
                case "LEFT": return "\u2190";
                case "RIGHT": return "\u2192";
                default: return key;
            }
        }

        /// <summary>The guidance's live keys as chips under NEXT, rebuilt only when they change.</summary>
        void UpdateKeyHints()
        {
            if (m_OrderKeys == null)
                return;

            var hints = m_Guidance != null && !XRPlatform.isImmersive ? m_Guidance.keyHints : null;
            var signature = string.Empty;
            if (hints != null)
            {
                for (var i = 0; i < hints.Count; i++)
                    signature += hints[i].key + hints[i].action + "|";
            }

            if (signature == m_KeysShown)
                return;

            m_KeysShown = signature;
            m_OrderKeys.Clear();
            m_OrderKeys.EnableInClassList("dc-hidden", hints == null || hints.Count == 0);
            if (hints == null)
                return;

            for (var i = 0; i < hints.Count; i++)
            {
                var item = new VisualElement();
                item.AddToClassList("dc-keys__item");
                item.Add(KeyCap(hints[i].key));
                var action = new Label(hints[i].action);
                action.AddToClassList("dc-keys__action");
                item.Add(action);
                m_OrderKeys.Add(item);
            }
        }

        /// <summary>Insertion assist on or off for every connector. Shown on the HUD button.</summary>
        void SetAssist(bool on)
        {
            CableConnector.insertionAssist = on;
            RefreshAssistButton();
            ToastEvent.Show(on
                ? "Insertion assist on: bring the plug near the target port and it plugs in by itself"
                : "Insertion assist off: line the plug up and open the gripper to seat it");
        }

        void RefreshAssistButton()
        {
            var button = m_Root.Q<Button>("assist-button");
            if (button == null)
                return;

            button.text = CableConnector.insertionAssist ? "ASSIST ON" : "ASSIST OFF";
            button.EnableInClassList("dc-button--active", CableConnector.insertionAssist);
        }

        /// <summary>
        /// In a narrow window the key groups stack into a tall column that would cover the robot,
        /// so the panel starts folded there. Only until the operator opens or closes it themselves.
        /// </summary>
        void OnHelpGeometryChanged(GeometryChangedEvent e)
        {
            if (m_HelpUserToggled || m_Help.ClassListContains("dc-help--collapsed") || e.newRect.height <= k_HelpMaxHeight)
                return;

            m_Help.AddToClassList("dc-help--collapsed");
            m_Root.Q<Button>("help-toggle").text = "SHOW KEYS  (C)";
        }

        void OnHelpToggleClicked()
        {
            m_HelpUserToggled = true;
            ToggleHelp();
        }

        void ToggleHelp()
        {
            m_Help.ToggleInClassList("dc-help--collapsed");
            m_Root.Q<Button>("help-toggle").text = m_Help.ClassListContains("dc-help--collapsed") ? "SHOW KEYS  (C)" : "HIDE  (C)";
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

            // The deadman banner, E-Stop and console sticks must never lag, so they update every frame.
            UpdateSafety();
            UpdateConsole();

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame && !XRPlatform.isImmersive)
                OnHelpToggleClicked();

            if (Time.unscaledTime < m_NextRefresh)
                return;

            m_NextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, m_RefreshRate);
            UpdateLink();
            UpdateRobots();
            UpdateOrders();
            m_Minimap?.Refresh(1f / Mathf.Max(1f, m_RefreshRate));
            UpdateProximity();
            UpdateLinkDetail();
            if (m_Tutorial != null && m_Tutorial.isRunning)
            {
                m_TutorialTime.text = $"{m_Tutorial.elapsed:0}s";
                m_TutorialText.text = m_Tutorial.instruction;
            }
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
            m_DeadmanKey.text = immersive ? "GRIP" : "HOLD";
            m_DeadmanText.text = stopped ? "EMERGENCY STOP LATCHED"
                : watchdog ? "LINK WATCHDOG STOP"
                : live ? "MOTION ENABLED"
                : immersive ? "HOLD TO ENABLE MOTION"
                : "HOLD A MOVE KEY TO RUN";

            m_EStop.EnableInClassList("dc-estop--latched", stopped);
            m_EStopButton.EnableInClassList("dc-estop__button--latched", stopped);
            m_EStopButton.text = stopped ? "STOPPED" : "E-STOP";
        }

        void UpdateLink()
        {
            // The headset holds 72 Hz; a desktop or browser aims for 60.
            var target = XRPlatform.isImmersive ? 72f : 60f;
            m_Fps.text = $"{m_FpsSmoothed:0} fps";
            m_Fps.EnableInClassList("dc-perf__value--warn", m_FpsSmoothed < target - 5f);
            if (m_FrameTime != null)
                m_FrameTime.text = $"{1000f / Mathf.Max(1f, m_FpsSmoothed):0.0} ms";
            if (m_FpsGraph != null)
            {
                m_FpsGraph.guide = target;
                m_FpsGraph.Push(m_FpsSmoothed);
            }

            if (m_Operator == null || m_Operator.link == null || m_Operator.link.transport == null)
                return;

            var stats = m_Operator.link.transport.stats;
            m_Latency.text = stats.delivered > 0 ? $"{stats.averageLatencyMs:0} ms" : "-- ms";
            m_Latency.EnableInClassList("dc-perf__value--warn", stats.delivered > 0 && stats.averageLatencyMs > k_LatencyBudgetMs);
            if (stats.delivered > 0)
                m_LatencyGraph?.Push(stats.lastLatencyMs);
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

            if (m_OrderNext != null && m_Guidance != null)
                m_OrderNext.text = m_Guidance.instruction;
            UpdateKeyHints();

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
        public void Configure(TeleopOperator teleopOperator, WorkOrderManager workOrders, FacilityMap map,
            WristCameraFeed wristCamera, ProximityIndicator proximity, CommandGhost ghost, TutorialController tutorial, GuidanceController guidance)
        {
            m_Guidance = guidance;
            m_Operator = teleopOperator;
            m_WorkOrders = workOrders;
            m_Map = map;
            m_WristCamera = wristCamera;
            m_Proximity = proximity;
            m_Ghost = ghost;
            m_Tutorial = tutorial;
        }
    }
}
