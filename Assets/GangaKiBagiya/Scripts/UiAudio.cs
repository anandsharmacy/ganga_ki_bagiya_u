using UnityEngine;

namespace GangaKiBagiya
{
    public enum UiCue
    {
        Tap,
        Confirm,
        Error,
        Toast
    }

    /// <summary>Tiny procedural interface sounds: no assets, built once, one AudioSource.</summary>
    public static class UiAudio
    {
        private const int SampleRate = 22050;
        private static AudioSource source;
        private static AudioClip[] clips;

        public static void Play(UiCue cue)
        {
            if (!UiSettings.UiSoundsEnabled)
                return;
            if (source == null && !Create())
                return;
            source.PlayOneShot(clips[(int)cue], 0.5f);
        }

        private static bool Create()
        {
            try
            {
                var go = new GameObject("UiAudio") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                clips = new[]
                {
                    Tone("tap", 0.05f, 880f, 880f, 0.5f),
                    Tone("confirm", 0.18f, 660f, 990f, 0.55f),
                    Tone("error", 0.16f, 220f, 180f, 0.6f),
                    Tone("toast", 0.1f, 740f, 740f, 0.45f)
                };
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // Sine sweep with a short attack and exponential decay so it never clicks.
        private static AudioClip Tone(string name, float seconds, float from, float to, float volume)
        {
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                phase += 2f * Mathf.PI * Mathf.Lerp(from, to, t) / SampleRate;
                float envelope = Mathf.Min(1f, i / (SampleRate * 0.004f)) * Mathf.Exp(-4f * t);
                data[i] = Mathf.Sin(phase) * envelope * volume;
            }
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
