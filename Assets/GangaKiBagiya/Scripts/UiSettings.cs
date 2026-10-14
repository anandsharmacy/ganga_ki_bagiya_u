using System;
using UnityEngine;

namespace GangaKiBagiya
{
    /// <summary>Player interface preferences (PlayerPrefs). Language lives in Phase4Localization.</summary>
    public static class UiSettings
    {
        private const string SoundsKey = "gkb.ui.sounds";
        private const string ReducedMotionKey = "gkb.ui.reducedMotion";
        private static bool? sounds;
        private static bool? reducedMotion;

        public static event Action Changed;

        public static bool UiSoundsEnabled
        {
            get
            {
                if (sounds == null)
                    sounds = PlayerPrefs.GetInt(SoundsKey, 1) == 1;
                return sounds.Value;
            }
            set
            {
                if (sounds == value)
                    return;
                sounds = value;
                PlayerPrefs.SetInt(SoundsKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool ReducedMotion
        {
            get
            {
                if (reducedMotion == null)
                    reducedMotion = PlayerPrefs.GetInt(ReducedMotionKey, 0) == 1;
                return reducedMotion.Value;
            }
            set
            {
                if (reducedMotion == value)
                    return;
                reducedMotion = value;
                PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }
    }
}
