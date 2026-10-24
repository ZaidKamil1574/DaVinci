using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using DataCenter.Robotics;
using DataCenter.Teleop;

namespace DataCenter.UI
{
    /// <summary>What a wrist menu button does.</summary>
    public enum WristAction
    {
        SelectRobot1,
        SelectRobot2,
        SelectRobot3,
        SelectRobot4,
        EStop,
        ResetEStop,
        ToggleMode,
        CycleLink,
        Tutorial,
    }

    /// <summary>
    /// The VR control menu on the left wrist: robot selection, E-Stop, mode, link preset and the
    /// tutorial, pressed with the right hand by poking or pointing. Also used, with a single E-Stop
    /// button, for the free-standing E-Stop pedestal beside the operator.
    /// </summary>
    /// <remarks>
    /// Built from 3D buttons with <see cref="XRSimpleInteractable"/> rather than a world-space canvas,
    /// so it needs no UI event system and behaves the same under OpenXR on Quest and WebXR in the
    /// browser. Labels are large enough to read at arm's length on a Quest 2.
    /// </remarks>
    public class WristMenu : MonoBehaviour
    {
        [Serializable]
        public struct Button
        {
            public WristAction action;
            public XRSimpleInteractable interactable;
            public TextMeshPro label;
            public Renderer face;
        }

        [SerializeField]
        TeleopOperator m_Operator;

        [SerializeField]
        TutorialController m_Tutorial;

        [SerializeField]
        List<Button> m_Buttons = new List<Button>();

        [SerializeField, Tooltip("Shown only while a headset is active.")]
        GameObject m_Visual;

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color k_Idle = new Color(0.1f, 0.13f, 0.18f);
        static readonly Color k_Selected = new Color(0.06f, 0.42f, 0.52f);
        static readonly Color k_Stop = new Color(0.84f, 0.11f, 0.09f);
        static readonly Color k_Ok = new Color(0.1f, 0.5f, 0.25f);

        readonly List<Action> m_Unsubscribe = new List<Action>();
        MaterialPropertyBlock m_Block;
        float m_NextRefresh;

        void OnEnable()
        {
            foreach (var button in m_Buttons)
            {
                if (button.interactable == null)
                    continue;

                var action = button.action;
                var interactable = button.interactable;
                UnityEngine.Events.UnityAction<UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs> handler = _ => Run(action);
                interactable.selectEntered.AddListener(handler);
                m_Unsubscribe.Add(() => interactable.selectEntered.RemoveListener(handler));
            }
        }

        void OnDisable()
        {
            foreach (var unsubscribe in m_Unsubscribe)
                unsubscribe();
            m_Unsubscribe.Clear();
        }

        void Run(WristAction action)
        {
            if (m_Operator == null)
                return;

            FeedbackEvent.Play(FeedbackCue.Grip, transform.position);
            switch (action)
            {
                case WristAction.SelectRobot1:
                case WristAction.SelectRobot2:
                case WristAction.SelectRobot3:
                case WristAction.SelectRobot4:
                    var index = action - WristAction.SelectRobot1;
                    if (index < TeleopRobot.all.Count)
                        m_Operator.Select(TeleopRobot.all[index]);
                    break;
                case WristAction.EStop:
                    m_Operator.EmergencyStop();
                    break;
                case WristAction.ResetEStop:
                    m_Operator.ResetEmergencyStop();
                    break;
                case WristAction.ToggleMode:
                    m_Operator.SetMode(m_Operator.mode == ControlMode.Direct ? ControlMode.Console : ControlMode.Direct);
                    break;
                case WristAction.CycleLink:
                    var conditions = m_Operator.link.conditions;
                    var next = (LinkPresets.IndexOf(conditions) + 1) % LinkPresets.all.Length;
                    LinkPresets.Apply(conditions, LinkPresets.all[next]);
                    ToastEvent.Show($"Link: {LinkPresets.all[next].name}");
                    break;
                case WristAction.Tutorial:
                    if (m_Tutorial != null)
                    {
                        if (m_Tutorial.isRunning)
                            m_Tutorial.Skip();
                        else
                            m_Tutorial.Begin();
                    }

                    break;
            }

            Refresh();
        }

        void LateUpdate()
        {
            var immersive = XRPlatform.isImmersive;
            if (m_Visual != null && m_Visual.activeSelf != immersive)
                m_Visual.SetActive(immersive);

            if (!immersive || Time.unscaledTime < m_NextRefresh)
                return;

            m_NextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        void Refresh()
        {
            if (m_Operator == null)
                return;

            var robots = TeleopRobot.all;
            var selected = m_Operator.robot;
            foreach (var button in m_Buttons)
            {
                string text = null;
                var color = k_Idle;
                switch (button.action)
                {
                    case WristAction.SelectRobot1:
                    case WristAction.SelectRobot2:
                    case WristAction.SelectRobot3:
                    case WristAction.SelectRobot4:
                        var index = button.action - WristAction.SelectRobot1;
                        if (index < robots.Count)
                        {
                            var robot = robots[index];
                            text = $"{robot.sectionId}  {robot.displayName}";
                            color = robot == selected ? k_Selected : robot.state == RobotState.EStopped ? k_Stop : k_Idle;
                        }

                        break;
                    case WristAction.EStop:
                        color = k_Stop;
                        text = selected != null && selected.isEStopped ? "STOPPED" : "E-STOP";
                        break;
                    case WristAction.ResetEStop:
                        color = selected != null && selected.isEStopped ? k_Ok : k_Idle;
                        text = "RESET";
                        break;
                    case WristAction.ToggleMode:
                        text = m_Operator.mode == ControlMode.Direct ? "MODE: DIRECT" : "MODE: CONSOLE";
                        break;
                    case WristAction.CycleLink:
                        var preset = LinkPresets.IndexOf(m_Operator.link.conditions);
                        text = $"LINK: {(preset >= 0 ? LinkPresets.all[preset].name : "CUSTOM")}";
                        break;
                    case WristAction.Tutorial:
                        text = m_Tutorial != null && m_Tutorial.isRunning ? "SKIP TUTORIAL" : "TUTORIAL";
                        break;
                }

                if (button.label != null && text != null && button.label.text != text)
                    button.label.text = text;

                if (button.face != null)
                {
                    m_Block ??= new MaterialPropertyBlock();
                    button.face.GetPropertyBlock(m_Block);
                    m_Block.SetColor(k_BaseColor, color);
                    button.face.SetPropertyBlock(m_Block);
                }
            }
        }

        /// <summary>Wires the menu from code. Used by the scene builder.</summary>
        public void Configure(TeleopOperator teleopOperator, TutorialController tutorial, List<Button> buttons, GameObject visual)
        {
            m_Operator = teleopOperator;
            m_Tutorial = tutorial;
            m_Buttons = buttons;
            m_Visual = visual;
        }
    }
}
