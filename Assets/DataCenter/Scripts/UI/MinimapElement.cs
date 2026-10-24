using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using DataCenter.Robotics;
using DataCenter.Tasks;

namespace DataCenter.UI
{
    /// <summary>
    /// Vector minimap of the hall, drawn with <see cref="Painter2D"/> from a <see cref="FacilityMap"/>.
    /// Click a robot to take control of it; click the floor to send the selected robot there.
    /// </summary>
    /// <remarks>
    /// Colours come from USS custom properties on <c>.dc-minimap</c>, which in turn reference the
    /// design tokens, so even this hand-drawn element re-themes with the rest of the HUD.
    /// </remarks>
    public class MinimapElement : VisualElement
    {
        static readonly CustomStyleProperty<Color> k_Floor = new CustomStyleProperty<Color>("--minimap-floor");
        static readonly CustomStyleProperty<Color> k_Equipment = new CustomStyleProperty<Color>("--minimap-equipment");
        static readonly CustomStyleProperty<Color> k_Ok = new CustomStyleProperty<Color>("--minimap-ok");
        static readonly CustomStyleProperty<Color> k_Active = new CustomStyleProperty<Color>("--minimap-active");
        static readonly CustomStyleProperty<Color> k_Warn = new CustomStyleProperty<Color>("--minimap-warn");
        static readonly CustomStyleProperty<Color> k_Danger = new CustomStyleProperty<Color>("--minimap-danger");
        static readonly CustomStyleProperty<Color> k_Selected = new CustomStyleProperty<Color>("--minimap-selected");

        /// <summary>Robots are this many pixels across.</summary>
        const float k_RobotRadius = 6f;

        Color m_Floor = new Color(0.04f, 0.06f, 0.09f, 0.9f);
        Color m_Equipment = new Color(0.33f, 0.38f, 0.45f);
        Color m_Ok = new Color(0.18f, 0.9f, 0.42f);
        Color m_Active = new Color(0.1f, 0.83f, 1f);
        Color m_Warn = new Color(1f, 0.69f, 0.13f);
        Color m_Danger = new Color(1f, 0.27f, 0.23f);
        Color m_Selected = Color.white;

        readonly Dictionary<string, Label> m_ZoneLabels = new Dictionary<string, Label>();
        FacilityMap m_Map;
        Func<TeleopRobot> m_GetSelected;
        Func<string> m_GetFocusedSection;
        WorkOrderManager m_WorkOrders;
        float m_Scale;
        Vector2 m_Offset;
        float m_Pulse;

        /// <summary>Raised when a robot dot is clicked.</summary>
        public event Action<TeleopRobot> robotClicked;

        /// <summary>Raised when empty floor is clicked, with the world position.</summary>
        public event Action<Vector3> floorClicked;

        public MinimapElement()
        {
            AddToClassList("dc-minimap");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
            RegisterCallback<GeometryChangedEvent>(_ => LayoutLabels());
            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        public void Bind(FacilityMap map, Func<TeleopRobot> getSelected, Func<string> getFocusedSection, WorkOrderManager workOrders)
        {
            m_Map = map;
            m_GetSelected = getSelected;
            m_GetFocusedSection = getFocusedSection;
            m_WorkOrders = workOrders;

            foreach (var label in m_ZoneLabels.Values)
                label.RemoveFromHierarchy();
            m_ZoneLabels.Clear();

            if (m_Map == null)
                return;

            foreach (var zone in m_Map.zones)
            {
                var label = new Label(zone.id) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("dc-minimap__zone-label");
                label.style.color = zone.accent;
                Add(label);
                m_ZoneLabels[zone.id] = label;
            }

            LayoutLabels();
            MarkDirtyRepaint();
        }

        /// <summary>Redraws. Call at the HUD's refresh rate; robots move between calls.</summary>
        public void Refresh(float deltaTime)
        {
            m_Pulse = (m_Pulse + deltaTime * 2f) % 1f;
            MarkDirtyRepaint();
        }

        void OnStyleResolved(CustomStyleResolvedEvent e)
        {
            var style = e.customStyle;
            if (style.TryGetValue(k_Floor, out var c)) m_Floor = c;
            if (style.TryGetValue(k_Equipment, out c)) m_Equipment = c;
            if (style.TryGetValue(k_Ok, out c)) m_Ok = c;
            if (style.TryGetValue(k_Active, out c)) m_Active = c;
            if (style.TryGetValue(k_Warn, out c)) m_Warn = c;
            if (style.TryGetValue(k_Danger, out c)) m_Danger = c;
            if (style.TryGetValue(k_Selected, out c)) m_Selected = c;
            MarkDirtyRepaint();
        }

        /// <summary>Fits the hall into the element, preserving aspect, and caches the transform.</summary>
        void UpdateTransform()
        {
            var rect = contentRect;
            if (m_Map == null || rect.width <= 0f || rect.height <= 0f)
            {
                m_Scale = 0f;
                return;
            }

            var bounds = m_Map.bounds;
            m_Scale = Mathf.Min(rect.width / bounds.width, rect.height / bounds.height);
            var size = new Vector2(bounds.width, bounds.height) * m_Scale;
            m_Offset = new Vector2(rect.x + (rect.width - size.x) * 0.5f, rect.y + (rect.height - size.y) * 0.5f);
        }

        /// <summary>World X/Z to element pixels. World +Z is up on the map.</summary>
        Vector2 ToLocal(float x, float z)
        {
            var bounds = m_Map.bounds;
            return m_Offset + new Vector2((x - bounds.xMin) * m_Scale, (bounds.yMax - z) * m_Scale);
        }

        Vector3 ToWorld(Vector2 local)
        {
            var bounds = m_Map.bounds;
            var relative = (local - m_Offset) / m_Scale;
            return new Vector3(bounds.xMin + relative.x, 0f, bounds.yMax - relative.y);
        }

        void LayoutLabels()
        {
            UpdateTransform();
            if (m_Map == null || m_Scale <= 0f)
                return;

            foreach (var zone in m_Map.zones)
            {
                if (!m_ZoneLabels.TryGetValue(zone.id, out var label))
                    continue;

                var corner = ToLocal(zone.area.xMin, zone.area.yMax);
                label.style.left = corner.x + 4f;
                label.style.top = corner.y + 2f;
            }
        }

        void Draw(MeshGenerationContext context)
        {
            UpdateTransform();
            if (m_Map == null || m_Scale <= 0f)
                return;

            var painter = context.painter2D;
            var bounds = m_Map.bounds;

            FillRect(painter, Rect.MinMaxRect(bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax), m_Floor);

            var focused = m_GetFocusedSection?.Invoke();
            foreach (var zone in m_Map.zones)
            {
                var isFocused = zone.id == focused;
                var fill = zone.accent;
                fill.a = isFocused ? 0.16f : 0.07f;
                FillRect(painter, zone.area, fill);

                var stroke = zone.accent;
                stroke.a = isFocused ? 0.9f : 0.35f;
                StrokeRect(painter, zone.area, stroke, isFocused ? 1.5f : 1f);
            }

            foreach (var obstacle in m_Map.obstacles)
                FillRect(painter, obstacle, m_Equipment);

            DrawTargets(painter, focused);

            var selected = m_GetSelected?.Invoke();
            var robots = TeleopRobot.all;
            for (var i = 0; i < robots.Count; i++)
                DrawRobot(painter, robots[i], robots[i] == selected);
        }

        void DrawTargets(Painter2D painter, string section)
        {
            if (m_WorkOrders == null || section == null)
                return;

            var active = m_WorkOrders.GetActive(section);
            if (active == null)
                return;

            foreach (var id in new[] { active.order.from, active.order.to })
            {
                var port = PortSocket.Find(id);
                if (port == null)
                    continue;

                var p = ToLocal(port.socket.position.x, port.socket.position.z);
                var radius = 3f + m_Pulse * 6f;
                var color = m_Active;
                color.a = 1f - m_Pulse;
                painter.strokeColor = color;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.Arc(p, radius, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Stroke();

                painter.fillColor = m_Active;
                painter.BeginPath();
                painter.MoveTo(p + new Vector2(0f, -3f));
                painter.LineTo(p + new Vector2(3f, 0f));
                painter.LineTo(p + new Vector2(0f, 3f));
                painter.LineTo(p + new Vector2(-3f, 0f));
                painter.ClosePath();
                painter.Fill();
            }
        }

        void DrawRobot(Painter2D painter, TeleopRobot robot, bool selected)
        {
            var position = robot.transform.position;
            var p = ToLocal(position.x, position.z);
            var color = StateColor(robot.state);

            // Navigation destination, if travelling.
            if (robot.mobileBase != null && robot.mobileBase.isNavigating)
            {
                var d = robot.mobileBase.destination;
                var q = ToLocal(d.x, d.z);
                painter.strokeColor = m_Active;
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(p);
                painter.LineTo(q);
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(q + new Vector2(-4f, -4f));
                painter.LineTo(q + new Vector2(4f, 4f));
                painter.MoveTo(q + new Vector2(4f, -4f));
                painter.LineTo(q + new Vector2(-4f, 4f));
                painter.Stroke();
            }

            if (selected)
            {
                painter.strokeColor = m_Selected;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.Arc(p, k_RobotRadius + 4f, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Stroke();
            }

            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(p, k_RobotRadius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();

            // Heading wedge. Map +Z is screen up, so a world forward of (x, z) is screen (x, -z).
            var forward = robot.transform.forward;
            var heading = new Vector2(forward.x, -forward.z).normalized;
            var side = new Vector2(-heading.y, heading.x);
            painter.fillColor = m_Floor;
            painter.BeginPath();
            painter.MoveTo(p + heading * (k_RobotRadius - 1f));
            painter.LineTo(p - heading * 2f + side * 3f);
            painter.LineTo(p - heading * 2f - side * 3f);
            painter.ClosePath();
            painter.Fill();
        }

        Color StateColor(RobotState state)
        {
            switch (state)
            {
                case RobotState.Idle: return m_Ok;
                case RobotState.Fault: return m_Warn;
                case RobotState.EStopped: return m_Danger;
                default: return m_Active;
            }
        }

        void FillRect(Painter2D painter, Rect world, Color color)
        {
            var a = ToLocal(world.xMin, world.yMax);
            var b = ToLocal(world.xMax, world.yMin);
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(new Vector2(b.x, a.y));
            painter.LineTo(b);
            painter.LineTo(new Vector2(a.x, b.y));
            painter.ClosePath();
            painter.Fill();
        }

        void StrokeRect(Painter2D painter, Rect world, Color color, float width)
        {
            var a = ToLocal(world.xMin, world.yMax);
            var b = ToLocal(world.xMax, world.yMin);
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(new Vector2(b.x, a.y));
            painter.LineTo(b);
            painter.LineTo(new Vector2(a.x, b.y));
            painter.ClosePath();
            painter.Stroke();
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (m_Map == null || m_Scale <= 0f || e.button != 0)
                return;

            var local = (Vector2)e.localPosition;
            var robots = TeleopRobot.all;
            for (var i = 0; i < robots.Count; i++)
            {
                var p = robots[i].transform.position;
                if (Vector2.Distance(local, ToLocal(p.x, p.z)) <= k_RobotRadius + 6f)
                {
                    robotClicked?.Invoke(robots[i]);
                    e.StopPropagation();
                    return;
                }
            }

            floorClicked?.Invoke(ToWorld(local));
            e.StopPropagation();
        }
    }
}
