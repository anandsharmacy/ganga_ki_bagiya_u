using System.Text.RegularExpressions;
using NUnit.Framework;

namespace GangaKiBagiya.Tests
{
    public sealed class Phase4LocalizationTests
    {
        private static readonly Regex Devanagari = new Regex("[\u0900-\u097F]");

        [Test]
        public void EveryKeyHasNonEmptyHindiAndEnglishText()
        {
            foreach (string key in Phase4Localization.Keys)
            {
                Assert.IsNotEmpty(Phase4Localization.Get(key, GameLanguage.Hindi), key + " (Hindi)");
                Assert.IsNotEmpty(Phase4Localization.Get(key, GameLanguage.English), key + " (English)");
            }
        }

        [Test]
        public void HindiTextContainsDevanagariWhereEnglishDoesNot()
        {
            foreach (string key in Phase4Localization.Keys)
            {
                if (key == "lang.toggle")
                    continue;
                Assert.IsTrue(Devanagari.IsMatch(Phase4Localization.Get(key, GameLanguage.Hindi)), key + " should be Hindi");
                Assert.IsFalse(Devanagari.IsMatch(Phase4Localization.Get(key, GameLanguage.English)), key + " should be English");
            }
        }

        [Test]
        public void FormatPlaceholdersMatchBetweenLanguages()
        {
            foreach (string key in Phase4Localization.Keys)
            {
                Assert.AreEqual(
                    Regex.Matches(Phase4Localization.Get(key, GameLanguage.Hindi), @"\{\d+\}").Count,
                    Regex.Matches(Phase4Localization.Get(key, GameLanguage.English), @"\{\d+\}").Count,
                    key);
            }
        }

        [Test]
        public void ToggleSwitchesLanguageAndNotifiesListeners()
        {
            GameLanguage original = Phase4Localization.Current;
            int notifications = 0;
            void Handler() => notifications++;
            Phase4Localization.LanguageChanged += Handler;
            try
            {
                Phase4Localization.ToggleLanguage();
                Assert.AreNotEqual(original, Phase4Localization.Current);
                Assert.AreEqual(1, notifications);
            }
            finally
            {
                Phase4Localization.LanguageChanged -= Handler;
                Phase4Localization.SetLanguage(original);
            }
        }

        [Test]
        public void EveryMangoStageHasABilingualName()
        {
            for (int stage = 1; stage <= Phase2PlantTuning.StageCount; stage++)
            {
                Phase4Localization.SetLanguage(GameLanguage.Hindi);
                Assert.IsTrue(Devanagari.IsMatch(Phase4Localization.StageName(stage)));
                Phase4Localization.SetLanguage(GameLanguage.English);
                Assert.IsFalse(Devanagari.IsMatch(Phase4Localization.StageName(stage)));
            }
        }
    }
}
