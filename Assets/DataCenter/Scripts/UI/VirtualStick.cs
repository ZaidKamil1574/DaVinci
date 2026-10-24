using UnityEngine;
using UnityEngine.UIElements;

namespace DataCenter.UI
{
    /// <summary>
    /// An on-screen joystick for Console mode on the desktop: drag the knob, read -1 to 1 on each
    /// axis, release to spring back to centre. The screen counterpart of the VR console levers.
    /// </summary>
    public class VirtualStick : VisualElement
    {
        readonly VisualElement m_Knob;
        int m_PointerId = -1;
        Vector2 m_Value;

        /// <summary>Current deflection. X right, Y up, each -1 to 1.</summary>
        public Vector2 value => m_Value;

        public VirtualStick(string caption)
        {
            AddToClassList("dc-stick");
            m_Knob = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Knob.AddToClassList("dc-stick__knob");
            Add(m_Knob);

            var label = new Label(caption) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("dc-stick__label");
            Add(label);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => Release());
        }

        void OnDown(PointerDownEvent e)
        {
            m_PointerId = e.pointerId;
            this.CapturePointer(e.pointerId);
            Track(e.localPosition);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId == m_PointerId)
                Track(e.localPosition);
        }

        void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != m_PointerId)
                return;

            this.ReleasePointer(e.pointerId);
            Release();
        }

        void Track(Vector2 local)
        {
            var rect = contentRect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f)
                return;

            var offset = Vector2.ClampMagnitude((local - rect.center) / radius, 1f);
            m_Value = new Vector2(offset.x, -offset.y);
            m_Knob.style.translate = new Translate(offset.x * radius * 0.7f, offset.y * radius * 0.7f);
        }

        void Release()
        {
            m_PointerId = -1;
            m_Value = Vector2.zero;
            m_Knob.style.translate = new Translate(0f, 0f);
        }
    }
}
