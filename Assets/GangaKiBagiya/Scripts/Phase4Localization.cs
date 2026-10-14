using System;
using System.Collections.Generic;
using UnityEngine;

namespace GangaKiBagiya
{
    public enum GameLanguage
    {
        Hindi,
        English
    }

    public static class Phase4Localization
    {
        private const string LanguagePreferenceKey = "gkb.language";
        private static readonly string[] HindiStageNames = { "बीज", "अंकुर", "पौधा", "छोटा पेड़", "बौर", "फल" };
        private static readonly string[] EnglishStageNames = { "Seed", "Sprout", "Seedling", "Sapling", "Blossom", "Fruiting" };

        private static GameLanguage? current;

        public static event Action LanguageChanged;

        public static GameLanguage Current
        {
            get
            {
                if (current == null)
                    current = PlayerPrefs.GetInt(LanguagePreferenceKey, (int)GameLanguage.Hindi) == (int)GameLanguage.English
                        ? GameLanguage.English
                        : GameLanguage.Hindi;
                return current.Value;
            }
        }

        public static void SetLanguage(GameLanguage language)
        {
            if (current == language)
                return;
            current = language;
            PlayerPrefs.SetInt(LanguagePreferenceKey, (int)language);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke();
        }

        public static void ToggleLanguage()
        {
            SetLanguage(Current == GameLanguage.Hindi ? GameLanguage.English : GameLanguage.Hindi);
        }

        public static string Get(string key)
        {
            return Get(key, Current);
        }

        public static string Get(string key, GameLanguage language)
        {
            if (!Table.TryGetValue(key, out string[] values))
                return key;
            return values[(int)language];
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        public static string StageName(int stage)
        {
            string[] names = Current == GameLanguage.Hindi ? HindiStageNames : EnglishStageNames;
            return names[Mathf.Clamp(stage, 1, names.Length) - 1];
        }

        public static IEnumerable<string> Keys => Table.Keys;

        public static IReadOnlyList<string> PlantFactKeys => PlantFacts;

        private static readonly string[] PlantFacts = { "fact.plant.0", "fact.plant.1", "fact.plant.2" };

        // Order of values: Hindi, English. Plant facts come from the GDD and still need expert review (Phase 6).
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            ["app.title"] = new[] { "गंगा की बगिया", "Ganga ki Bagiya" },
            ["app.tagline"] = new[] { "साफ़ पानी भरो। नदी बचाओ। रोज़ थोड़ा बढ़ो।", "Collect clean water. Protect the river. Grow a little every day." },
            ["lang.toggle"] = new[] { "English", "हिन्दी" },
            ["common.back"] = new[] { "वापस", "Back" },
            ["common.ok"] = new[] { "ठीक है", "OK" },

            ["home.howto.title"] = new[] { "कैसे खेलें", "How to play" },
            ["home.howto.1"] = new[] { "किसी धारा को छूकर पानी भरें।", "Touch a lane to collect water." },
            ["home.howto.2"] = new[] { "उँगली दबाए-दबाए सरकाकर धारा बदलें।", "Slide your finger to switch lanes." },
            ["home.howto.3"] = new[] { "उँगली उठाते ही पानी भरना रुक जाता है।", "Lift your finger to stop collecting." },
            ["home.howto.4"] = new[] { "दाईं ओर आती चीज़ों से बचें। टकराने पर पानी गिर जाता है।", "Dodge what drifts into the zone on the right. A hit spills water." },
            ["home.howto.5"] = new[] { "लक्ष्य पूरा होते ही आज की सिंचाई पक्की हो जाती है। घड़ा ले लें या और भरकर बोनस कमाएँ।", "Reach the goal and today's watering is secured. Take the pot, or keep going for bonus growth." },
            ["home.play"] = new[] { "खेलें", "Play" },
            ["home.playDone"] = new[] { "आज का पानी हो चुका", "Watered today" },
            ["home.thirsty"] = new[] { "आपका पौधा प्यासा है! आज उसे पानी दें।", "Your plant is thirsty! Water it today." },
            ["home.wilted"] = new[] { "पौधा मुरझा गया है, पर ज़िंदा है। पानी देकर उसे फिर हरा करें।", "The plant has wilted but is still alive. Water it to bring it back." },
            ["home.done"] = new[] { "आज पौधे को पानी मिल चुका है। कल फिर आइए!", "The plant has been watered today. Come back tomorrow!" },
            ["home.unavailable"] = new[] { "आज पानी देना संभव नहीं है।", "Watering is not available right now." },
            ["home.rewards"] = new[] { "सात दिन के इनाम", "7-day rewards" },
            ["home.claimReady"] = new[] { "इनाम तैयार है!", "A reward is ready!" },

            ["hud.stage"] = new[] { "आम का पेड़ · चरण {0} — {1}", "Mango · Stage {0} — {1}" },
            ["hud.growth"] = new[] { "विकास {0}/{1} अंक", "Growth {0}/{1} GP" },
            ["hud.growthComplete"] = new[] { "विकास पूरा", "Growth complete" },
            ["hud.pot"] = new[] { "घड़ा {0} / {1}", "Pot {0} / {1}" },
            ["hud.hint"] = new[] { "लेन छुएँ · सरकाकर बदलें · उठाकर रोकें", "Hold a lane · slide to switch · lift to stop" },
            ["hud.goalSecured"] = new[] { "लक्ष्य पूरा! घड़ा ले लें या और भरें।", "Goal secured! Take the pot or keep collecting." },
            ["hud.takePot"] = new[] { "घड़ा ले लें", "Take the pot" },
            ["hud.watering.available"] = new[] { "आज का पानी: बाकी", "Watering: available today" },
            ["hud.watering.done"] = new[] { "आज का पानी: हो गया", "Watering: done today" },
            ["hud.watering.unavailable"] = new[] { "आज का पानी: अभी नहीं", "Watering: unavailable" },
            ["hud.watering.grace"] = new[] { "पिछली दौड़ · 00:15 तक की छूट", "Previous run · 00:15 grace" },

            ["health.3"] = new[] { "स्वस्थ (3/3)", "Healthy (3/3)" },
            ["health.2"] = new[] { "देखभाल चाहिए (2/3)", "Needs care (2/3)" },
            ["health.1"] = new[] { "कमज़ोर (1/3)", "Weak (1/3)" },
            ["health.0"] = new[] { "मुरझाया पर ज़िंदा (0/3)", "Wilted but alive (0/3)" },

            ["result.title.fail"] = new[] { "पानी गिर गया", "The pot spilled" },
            ["result.title.goalHit"] = new[] { "लक्ष्य सुरक्षित रहा", "Goal kept safe" },
            ["result.title.banked"] = new[] { "घड़ा ले लिया", "Pot taken" },
            ["result.hit"] = new[] { "आप नदी में किसी चीज़ से टकरा गए।", "You ran into something in the river." },
            ["result.hit.before"] = new[] { "लक्ष्य से पहले टकराने पर सारा पानी गिर जाता है।", "A hit before the goal spills all the water." },
            ["result.hit.after"] = new[] { "लक्ष्य के बाद टकराने पर सिर्फ़ ज़्यादा पानी गिरा।", "A hit after the goal spills only the extra water." },
            ["result.banked"] = new[] { "आपने सारा पानी सुरक्षित रख लिया।", "You kept all of your water." },
            ["result.kept"] = new[] { "बचा पानी", "Kept" },
            ["result.spilled"] = new[] { "गिरा पानी", "Spilled" },
            ["result.bonus"] = new[] { "बोनस विकास अंक", "Bonus growth points" },
            ["result.noChange"] = new[] { "पौधे में कोई बदलाव नहीं हुआ। पौधे को कोई नुकसान नहीं हुआ। आप जितनी बार चाहें कोशिश कर सकते हैं।", "The plant is unchanged and not harmed. You can try as many times as you like." },
            ["result.retry"] = new[] { "फिर कोशिश करें", "Try again" },
            ["result.waterPlant"] = new[] { "पौधे को पानी दें", "Water the plant" },

            ["watering.title"] = new[] { "पौधे को पानी दिया जा रहा है…", "Watering the plant…" },
            ["watering.skip"] = new[] { "छोड़ें", "Skip" },

            ["done.title"] = new[] { "आज की सिंचाई पूरी!", "Today's watering is done!" },
            ["done.gained"] = new[] { "आज मिले विकास अंक: +{0}", "Growth points gained today: +{0}" },
            ["done.stageUp"] = new[] { "बधाई! पौधा अब \"{0}\" चरण में है।", "Well done! The plant is now at the \"{0}\" stage." },
            ["done.comeBack"] = new[] { "कल फिर आइए और पौधे को बढ़ते देखिए।", "Come back tomorrow and watch it grow." },
            ["done.factTitle"] = new[] { "क्या आप जानते हैं?", "Did you know?" },
            ["done.rewards"] = new[] { "इनाम देखें", "View rewards" },
            ["done.home"] = new[] { "होम", "Home" },

            ["rewards.title"] = new[] { "सात दिन के इनाम", "7-day rewards" },
            ["rewards.note"] = new[] { "छूटे हुए दिन का इनाम बाद में नहीं मिलता। इनाम की सामग्री जल्द आएगी।", "A missed day's reward cannot be claimed later. Reward contents are coming soon." },
            ["rewards.day"] = new[] { "दिन {0}", "Day {0}" },
            ["rewards.status.upcoming"] = new[] { "आगे", "Upcoming" },
            ["rewards.status.available"] = new[] { "उपलब्ध", "Available" },
            ["rewards.status.claimed"] = new[] { "मिल गया", "Claimed" },
            ["rewards.status.forfeited"] = new[] { "छूट गया", "Missed" },
            ["rewards.claim"] = new[] { "दिन {0} का इनाम लें", "Claim Day {0} reward" },
            ["rewards.nothing"] = new[] { "अभी कोई इनाम उपलब्ध नहीं है।", "No reward is available right now." },

            ["onboarding.welcome.title"] = new[] { "स्वागत है!", "Welcome!" },
            ["onboarding.welcome.body"] = new[] { "गंगा की बगिया में आपका स्वागत है। साफ़ पानी भरिए, नदी बचाइए और अपने आम के पौधे को रोज़ थोड़ा बड़ा कीजिए।", "Collect clean water, protect the river, and help your mango plant grow a little every day." },
            ["onboarding.create"] = new[] { "अपनी प्रोफ़ाइल बनाएँ", "Create your profile" },
            ["profile.title.form"] = new[] { "अपने बारे में बताएँ", "Tell us about you" },
            ["profile.name"] = new[] { "आपका नाम", "Your name" },
            ["profile.name.hint"] = new[] { "जैसे: आर्यन (Aryan)", "e.g. Aryan" },
            ["profile.gender"] = new[] { "लिंग (वैकल्पिक)", "Gender (optional)" },
            ["profile.gender.male"] = new[] { "पुरुष", "Male" },
            ["profile.gender.female"] = new[] { "महिला", "Female" },
            ["profile.gender.other"] = new[] { "अन्य", "Other" },
            ["profile.gender.skip"] = new[] { "बताना नहीं चाहते", "Prefer not to say" },
            ["profile.age"] = new[] { "उम्र (वैकल्पिक)", "Age (optional)" },
            ["profile.avatar"] = new[] { "अपना रंग चुनें", "Pick your colour" },
            ["profile.next"] = new[] { "आगे", "Continue" },
            ["profile.err.nameEmpty"] = new[] { "कृपया अपना नाम लिखें।", "Please enter your name." },
            ["profile.err.nameLength"] = new[] { "नाम 2 से 20 अक्षरों का होना चाहिए।", "The name must be 2 to 20 characters." },
            ["profile.err.nameChars"] = new[] { "नाम में केवल अक्षर, स्पेस और . ' - हो सकते हैं।", "Names can only use letters, spaces and . ' -" },
            ["profile.err.age"] = new[] { "उम्र 5 से 120 के बीच होनी चाहिए।", "Age must be between 5 and 120." },
            ["profile.preview.title"] = new[] { "आपकी प्रोफ़ाइल", "Your profile" },
            ["profile.preview.gender"] = new[] { "लिंग: {0}", "Gender: {0}" },
            ["profile.preview.age"] = new[] { "उम्र: {0}", "Age: {0}" },
            ["profile.edit"] = new[] { "प्रोफ़ाइल बदलें", "Edit profile" },
            ["profile.enter"] = new[] { "खेल शुरू करें", "Enter game" },
            ["profile.welcome"] = new[] { "नमस्ते, {0}! आपकी यात्रा शुरू होती है।", "Welcome, {0}. Your journey begins now." },
            ["profile.save"] = new[] { "सहेजें", "Save" },
            ["profile.saved"] = new[] { "प्रोफ़ाइल सहेजी गई।", "Profile saved." },
            ["profile.saveFailed"] = new[] { "प्रोफ़ाइल सहेजी नहीं जा सकी।", "The profile could not be saved." },
            ["profile.recovered"] = new[] { "सहेजी हुई प्रोफ़ाइल पढ़ी नहीं जा सकी। कृपया फिर से बनाएँ।", "Your saved profile could not be read. Please create it again." },
            ["menu.greeting"] = new[] { "नमस्ते, {0}", "Namaste, {0}" },
            ["menu.profile"] = new[] { "प्रोफ़ाइल", "Profile" },
            ["menu.settings"] = new[] { "सेटिंग्स", "Settings" },
            ["menu.howto"] = new[] { "कैसे खेलें", "How to play" },
            ["settings.title"] = new[] { "सेटिंग्स", "Settings" },
            ["settings.language"] = new[] { "भाषा", "Language" },
            ["settings.sounds"] = new[] { "इंटरफ़ेस की आवाज़ें", "Interface sounds" },
            ["settings.motion"] = new[] { "कम एनिमेशन", "Reduce motion" },
            ["settings.on"] = new[] { "चालू", "On" },
            ["settings.off"] = new[] { "बंद", "Off" },
            ["settings.profile"] = new[] { "खिलाड़ी प्रोफ़ाइल", "Player profile" },
            ["pause.title"] = new[] { "खेल रुका है", "Paused" },
            ["pause.resume"] = new[] { "जारी रखें", "Resume" },
            ["hud.pause"] = new[] { "रोकें", "Pause" },
            ["hud.toast.goal"] = new[] { "लक्ष्य पूरा!", "Goal reached!" },

            ["fact.plant.0"] = new[] { "आम की नई पत्तियाँ ताँबे जैसी लाल होती हैं।", "New mango leaves are coppery red." },
            ["fact.plant.1"] = new[] { "आम के फूलों के गुच्छों को बौर या मंजरी कहते हैं।", "Mango flower clusters are called bor or manjari." },
            ["fact.plant.2"] = new[] { "आम भारत का राष्ट्रीय फल है।", "The mango is India's national fruit." }
        };
    }
}
