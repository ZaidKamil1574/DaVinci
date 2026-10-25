using UnityEngine;
using UnityEngine.UIElements;

namespace DataCenter.UI
{
    /// <summary>
    /// A small line graph of the last few seconds of one value, such as link latency or frame rate,
    /// drawn with <see cref="Painter2D"/>.
    /// </summary>
    /// <remarks>
    /// The vertical scale fits the larger of the samples and <see cref="guide"/>, so a steady value
    /// reads as a flat line rather than noise, and the dashed guide (a budget or target) is always
    /// in view. Colours come from USS custom properties, like <see cref="MinimapElement"/>.
    /// </remarks>
    public class SparklineElement : VisualElement
    {
        static readonly CustomStyleProperty<Color> k_Line = new CustomStyleProperty<Color>("--sparkline-line");
        static readonly CustomStyleProperty<Color> k_Guide = new CustomStyleProperty<Color>("--sparkline-guide");

        readonly float[] m_Samples;
        int m_Count;
        int m_Head;
        Color m_Line = new Color(0.1f, 0.83f, 1f);
        Color m_Guide = new Color(0.51f, 0.59f, 0.67f, 0.45f);

        /// <summary>Reference value drawn as a dashed line, such as a latency budget or the target frame rate.</summary>
        public float guide { get; set; }

        public SparklineElement() : this(60) { }

        public SparklineElement(int capacity)
        {
            m_Samples = new float[Mathf.Max(2, capacity)];
            AddToClassList("dc-sparkline");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        /// <summary>Adds a sample, dropping the oldest once full, and redraws.</summary>
        public void Push(float value)
        {
            m_Samples[m_Head] = value;
            m_Head = (m_Head + 1) % m_Samples.Length;
            m_Count = Mathf.Min(m_Count + 1, m_Samples.Length);
            MarkDirtyRepaint();
        }

        void OnStyleResolved(CustomStyleResolvedEvent e)
        {
            if (e.customStyle.TryGetValue(k_Line, out var c)) m_Line = c;
            if (e.customStyle.TryGetValue(k_Guide, out c)) m_Guide = c;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width <= 2f || rect.height <= 2f)
                return;

            var max = guide;
            for (var i = 0; i < m_Count; i++)
                max = Mathf.Max(max, m_Samples[i]);
            max = Mathf.Max(max * 1.15f, 1f);

            var painter = context.painter2D;
            painter.lineWidth = 1f;
            painter.strokeColor = m_Guide;
            if (guide > 0f)
            {
                var y = Y(guide, max, rect);
                for (var x = rect.xMin; x < rect.xMax; x += 6f)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, y));
                    painter.LineTo(new Vector2(Mathf.Min(x + 3f, rect.xMax), y));
                    painter.Stroke();
                }
            }

            if (m_Count < 2)
                return;

            // Oldest on the left, newest on the right edge.
            var step = rect.width / (m_Samples.Length - 1);
            var start = m_Samples.Length - m_Count;
            painter.lineWidth = 1.5f;
            painter.lineJoin = LineJoin.Round;
            painter.strokeColor = m_Line;
            painter.BeginPath();
            for (var i = 0; i < m_Count; i++)
            {
                var sample = m_Samples[(m_Head - m_Count + i + m_Samples.Length) % m_Samples.Length];
                var point = new Vector2(rect.xMin + (start + i) * step, Y(sample, max, rect));
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }

            painter.Stroke();
        }

        static float Y(float value, float max, Rect rect) => rect.yMax - Mathf.Clamp01(value / max) * rect.height;
    }
}
