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
        }

        void OnEnable() => EventBus.Subscribe<FeedbackEvent>(OnFeedback);

        void OnDisable() => EventBus.Unsubscribe<FeedbackEvent>(OnFeedback);

        void OnFeedback(FeedbackEvent e)
        {
            if (!m_Clips.TryGetValue(e.cue, out var clip))
                return;

            if (m_Spatial)
            {
                AudioSource.PlayClipAtPoint(clip, e.position, m_Volume);
                return;
            }

            // Grip and release can fire every frame in a fumble; do not machine-gun them.
            if ((e.cue == FeedbackCue.Grip || e.cue == FeedbackCue.Release) && Time.unscaledTime - m_LastPlayed < 0.08f)
                return;

            m_LastPlayed = Time.unscaledTime;
            m_Source.PlayOneShot(clip, m_Volume);
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
