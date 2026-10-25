using System.Collections.Generic;
using UnityEngine;

namespace DataCenter
{
    /// <summary>
    /// Plays <see cref="FeedbackEvent"/>s with tones synthesised at startup.
    /// </summary>
    /// <remarks>
    /// Generated rather than imported so the web build carries no audio files and every cue is
    /// tunable in code. Short PCM clips created with <see cref="AudioClip.Create(string,int,int,int,bool)"/>
    /// play on WebGL as well as Android.
    /// </remarks>
    public class FeedbackAudio : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)]
        float m_Volume = 0.6f;

        [SerializeField, Tooltip("Spatialise cues at their source. Off plays everything flat, which suits the desktop view.")]
        bool m_Spatial;

        const int k_SampleRate = 22050;

        readonly Dictionary<FeedbackCue, AudioClip> m_Clips = new Dictionary<FeedbackCue, AudioClip>();
        AudioSource m_Source;
        float m_LastPlayed;

        void Awake()
        {
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f;

            m_Clips[FeedbackCue.LinkUp] = Tones("LinkUp", (880f, 0.07f), (1320f, 0.11f));
            m_Clips[FeedbackCue.Reject] = Buzz("Reject", 150f, 0.28f);
            m_Clips[FeedbackCue.Grip] = Tones("Grip", (520f, 0.05f));
            m_Clips[FeedbackCue.Release] = Tones("Release", (390f, 0.05f));
            m_Clips[FeedbackCue.EStop] = Alarm("EStop", 0.9f);
            m_Clips[FeedbackCue.OrderComplete] = Tones("Complete", (660f, 0.09f), (880f, 0.09f), (1320f, 0.18f));
            m_Clips[FeedbackCue.SectionComplete] = Tones("Section", (523f, 0.12f), (659f, 0.12f), (784f, 0.12f), (1046f, 0.3f));
            m_Clips[FeedbackCue.Warning] = Tones("Warning", (700f, 0.08f), (500f, 0.12f));

            // "Grab now" and "plug in now": bright and rising, unlike the lower grip click, so they
            // read as an invitation rather than a confirmation.
            m_Clips[FeedbackCue.GrabReady] = Tones("GrabReady", (1047f, 0.06f), (1568f, 0.1f));
            m_Clips[FeedbackCue.PlugReady] = Tones("PlugReady", (784f, 0.05f), (988f, 0.05f), (1319f, 0.1f));
            m_Clips[FeedbackCue.Bump] = Thud("Bump", 0.14f);
            m_Clips[FeedbackCue.BumpMetal] = Clank("BumpMetal", 0.4f);
            m_Clips[FeedbackCue.BumpCart] = Rattle("BumpCart");
            m_Clips[FeedbackCue.PlugTap] = Click("PlugTap");
        }

        void OnEnable() => EventBus.Subscribe<FeedbackEvent>(OnFeedback);

        void OnDisable() => EventBus.Unsubscribe<FeedbackEvent>(OnFeedback);

        void OnFeedback(FeedbackEvent e)
        {
            if (!m_Clips.TryGetValue(e.cue, out var clip))
                return;

            // Impacts carry how hard they were; everything else plays at full volume.
            var volume = m_Volume * (e.strength > 0f ? Mathf.Lerp(0.25f, 1f, e.strength) : 1f);
            if (m_Spatial)
            {
                AudioSource.PlayClipAtPoint(clip, e.position, volume);
                return;
            }

            // Grip and release can fire every frame in a fumble; do not machine-gun them.
            if ((e.cue == FeedbackCue.Grip || e.cue == FeedbackCue.Release) && Time.unscaledTime - m_LastPlayed < 0.08f)
                return;

            m_LastPlayed = Time.unscaledTime;
            m_Source.PlayOneShot(clip, volume);
        }

        /// <summary>Sequential sine beeps with short fades, so they click less.</summary>
        static AudioClip Tones(string name, params (float frequency, float seconds)[] notes)
        {
            var total = 0;
            foreach (var note in notes)
                total += Mathf.CeilToInt(note.seconds * k_SampleRate);

            var data = new float[total];
            var offset = 0;
            foreach (var note in notes)
            {
                var count = Mathf.CeilToInt(note.seconds * k_SampleRate);
                for (var i = 0; i < count; i++)
                {
                    var t = i / (float)k_SampleRate;
                    var envelope = Mathf.Min(1f, i / 200f) * Mathf.Min(1f, (count - i) / 400f);
                    data[offset + i] = Mathf.Sin(2f * Mathf.PI * note.frequency * t) * 0.5f * envelope;
                }

                offset += count;
            }

            return Build(name, data);
        }

        static AudioClip Buzz(string name, float frequency, float seconds)
        {
            var count = Mathf.CeilToInt(seconds * k_SampleRate);
            var data = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)k_SampleRate;
                var square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * t));
                var envelope = Mathf.Min(1f, i / 100f) * Mathf.Min(1f, (count - i) / 300f);
                data[i] = square * 0.25f * envelope;
            }

            return Build(name, data);
        }

        /// <summary>A short low knock with a falling pitch, for running into something.</summary>
        static AudioClip Thud(string name, float seconds)
        {
            var count = Mathf.CeilToInt(seconds * k_SampleRate);
            var data = new float[count];
            var phase = 0f;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)k_SampleRate;
                var frequency = Mathf.Lerp(140f, 60f, t / seconds);
                phase += 2f * Mathf.PI * frequency / k_SampleRate;
                var envelope = Mathf.Min(1f, i / 60f) * Mathf.Exp(-t * 28f);
                data[i] = Mathf.Sin(phase) * 0.7f * envelope;
            }

            return Build(name, data);
        }

        /// <summary>
        /// Struck sheet metal: a short noise hit, then inharmonic partials that ring and die away,
        /// the high ones first.
        /// </summary>
        static AudioClip Clank(string name, float seconds)
        {
            var partials = new[] { (330f, 9f, 0.5f), (857f, 14f, 0.35f), (1490f, 20f, 0.25f), (2330f, 30f, 0.18f) };
            var count = Mathf.CeilToInt(seconds * k_SampleRate);
            var data = new float[count];
            var noise = new System.Random(7);
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)k_SampleRate;
                var value = 0f;
                foreach (var (frequency, decay, level) in partials)
                    value += Mathf.Sin(2f * Mathf.PI * frequency * t) * level * Mathf.Exp(-t * decay);
                value += ((float)noise.NextDouble() * 2f - 1f) * 0.5f * Mathf.Exp(-t * 90f);
                data[i] = value * 0.55f * Mathf.Min(1f, i / 20f);
            }

            return Build(name, data);
        }

        /// <summary>A light cart shaking: three quick metallic ticks, each quieter.</summary>
        static AudioClip Rattle(string name)
        {
            var count = Mathf.CeilToInt(0.22f * k_SampleRate);
            var data = new float[count];
            var noise = new System.Random(11);
            for (var hit = 0; hit < 3; hit++)
            {
                var start = Mathf.CeilToInt((hit * 0.055f + hit * hit * 0.008f) * k_SampleRate);
                var level = 0.5f * Mathf.Pow(0.6f, hit);
                for (var i = start; i < count; i++)
                {
                    var t = (i - start) / (float)k_SampleRate;
                    var ring = Mathf.Sin(2f * Mathf.PI * 1830f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 2960f * t) * 0.4f;
                    var tick = ((float)noise.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 300f);
                    data[i] += (ring * Mathf.Exp(-t * 45f) + tick) * level;
                }
            }

            return Build(name, data);
        }

        /// <summary>A small hard plastic part knocking on something: a very short bright click.</summary>
        static AudioClip Click(string name)
        {
            var count = Mathf.CeilToInt(0.04f * k_SampleRate);
            var data = new float[count];
            var noise = new System.Random(3);
            var previous = 0f;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)k_SampleRate;
                // Differencing white noise tilts it bright, like a hard tap rather than a thud.
                var white = (float)noise.NextDouble() * 2f - 1f;
                var bright = white - previous;
                previous = white;
                data[i] = (bright * 0.35f + Mathf.Sin(2f * Mathf.PI * 3200f * t) * 0.25f) * Mathf.Exp(-t * 160f);
            }

            return Build(name, data);
        }

        static AudioClip Alarm(string name, float seconds)
        {
            var count = Mathf.CeilToInt(seconds * k_SampleRate);
            var data = new float[count];
            var phase = 0f;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)k_SampleRate;
                // Accumulate phase: a sweeping frequency in sin(f * t) would warble.
                var frequency = Mathf.Lerp(600f, 1100f, Mathf.PingPong(t * 6f, 1f));
                phase += 2f * Mathf.PI * frequency / k_SampleRate;
                var envelope = Mathf.Min(1f, i / 200f) * Mathf.Min(1f, (count - i) / 800f);
                data[i] = Mathf.Sin(phase) * 0.45f * envelope;
            }

            return Build(name, data);
        }

        static AudioClip Build(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, k_SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
