using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GangaKiBagiya
{
    // First-launch local profile: welcome -> details -> preview -> welcome-back message, plus the profile editor modal.
    public sealed partial class Phase4Ui
    {
        private static readonly PlayerGender[] GenderOptions =
            { PlayerGender.Male, PlayerGender.Female, PlayerGender.Other, PlayerGender.PreferNotToSay };
        private static readonly string[] GenderKeys =
            { "profile.gender.male", "profile.gender.female", "profile.gender.other", "profile.gender.skip" };

        private PlayerProfileStore profileStore;
        private PlayerProfile profile;
        private bool profileDamaged;
        private bool onboardingActive;
        private VisualElement onboardingScreen;

        private sealed class ProfileForm
        {
            public VisualElement Root;
            public TextField Name, Age;
            public Label NameLabel, NameError, GenderLabel, AgeLabel, AgeError, AvatarLabel;
            public string NameErrorKey, AgeErrorKey;
            public PlayerGender Gender;
            public int Avatar;
            public Action RefreshGender, RefreshAvatar;
        }

        private static string GenderText(PlayerGender gender)
        {
            int index = Array.IndexOf(GenderOptions, gender);
            return index < 0 ? string.Empty : Phase4Localization.Get(GenderKeys[index]);
        }

        private TextField MakeTextField(int maxLength, TouchScreenKeyboardType keyboard)
        {
            var field = new TextField { maxLength = maxLength, keyboardType = keyboard };
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            field.style.marginTop = 4f;
            VisualElement input = field.Q("unity-text-input") ?? field;
            input.style.backgroundColor = Color.white;
            input.style.color = Ink;
            input.style.fontSize = 18f;
            input.style.minHeight = 52f;
            input.style.paddingLeft = 12f;
            input.style.paddingRight = 12f;
            input.style.unityTextAlign = TextAnchor.MiddleLeft;
            UiTheme.Round(input, 12f);
            UiTheme.Border(input, 2f, Indigo);
            // Entered text is dark and bold; the placeholder stays muted so it never reads as input.
            void Tint()
            {
                foreach (TextElement text in input.Query<TextElement>().ToList())
                {
                    text.style.color = string.IsNullOrEmpty(field.value) ? UiTheme.Muted : Ink;
                    if (boldFont != null)
                        text.style.unityFontDefinition = FontDefinition.FromFont(string.IsNullOrEmpty(field.value) ? bodyFont : boldFont);
                }
            }
            Tint();
            field.RegisterValueChangedCallback(_ => Tint());
            input.RegisterCallback<FocusInEvent>(_ => UiTheme.Border(input, 3f, RaniPink));
            input.RegisterCallback<FocusOutEvent>(_ => UiTheme.Border(input, 2f, Indigo));
            return field;
        }

        private ProfileForm BuildProfileForm(PlayerProfile initial)
        {
            var f = new ProfileForm
            {
                Root = new VisualElement(),
                Gender = initial != null ? initial.gender : PlayerGender.Unspecified,
                Avatar = initial != null ? initial.avatarIndex : 0
            };

            f.NameLabel = MakeLabel(string.Empty, 16f, true, Ink);
            f.Name = MakeTextField(PlayerProfile.NameMaxLength, TouchScreenKeyboardType.Default);
            f.NameError = MakeLabel(string.Empty, 14f, false, UiTheme.Error);
            f.NameError.style.display = DisplayStyle.None;

            f.GenderLabel = MakeLabel(string.Empty, 16f, true, Ink);
            f.GenderLabel.style.marginTop = 12f;
            VisualElement gender = MakeSegmented(null, GenderKeys,
                () => Array.IndexOf(GenderOptions, f.Gender),
                i => f.Gender = f.Gender == GenderOptions[i] ? PlayerGender.Unspecified : GenderOptions[i], true, out f.RefreshGender);
            gender.style.marginTop = 4f;

            f.AgeLabel = MakeLabel(string.Empty, 16f, true, Ink);
            f.AgeLabel.style.marginTop = 8f;
            f.Age = MakeTextField(3, TouchScreenKeyboardType.NumberPad);
            f.AgeError = MakeLabel(string.Empty, 14f, false, UiTheme.Error);
            f.AgeError.style.display = DisplayStyle.None;

            f.AvatarLabel = MakeLabel(string.Empty, 16f, true, Ink);
            f.AvatarLabel.style.marginTop = 12f;
            var avatarRow = new VisualElement();
            avatarRow.style.flexDirection = FlexDirection.Row;
            avatarRow.style.justifyContent = Justify.SpaceBetween;
            avatarRow.style.marginTop = 4f;
            var rings = new List<VisualElement>();
            for (int i = 0; i < PlayerProfile.AvatarCount; i++)
            {
                int index = i;
                var ring = new VisualElement();
                ring.style.width = 48f;
                ring.style.height = 48f;
                ring.style.alignItems = Align.Center;
                ring.style.justifyContent = Justify.Center;
                UiTheme.Round(ring, 24f);
                Button swatch = NewButtonBase(14f, () =>
                {
                    f.Avatar = index;
                    f.RefreshAvatar();
                });
                UiTheme.StyleInteractive(swatch, UiTheme.Avatars[i], Color.white);
                swatch.style.width = 38f;
                swatch.style.height = 38f;
                swatch.style.minHeight = 38f;
                UiTheme.Round(swatch, 19f);
                ring.Add(swatch);
                rings.Add(ring);
                avatarRow.Add(ring);
            }
            f.RefreshAvatar = () =>
            {
                for (int i = 0; i < rings.Count; i++)
                    UiTheme.Border(rings[i], 3f, i == f.Avatar ? Ink : Color.clear);
            };

            f.Name.value = initial != null ? initial.name : string.Empty;
            f.Age.value = initial != null && initial.age > 0 ? initial.age.ToString() : string.Empty;
            f.Name.RegisterValueChangedCallback(_ => UpdateFormErrors(f, false));
            f.Age.RegisterValueChangedCallback(_ => UpdateFormErrors(f, false));

            f.Root.Add(f.NameLabel);
            f.Root.Add(f.Name);
            f.Root.Add(f.NameError);
            f.Root.Add(f.GenderLabel);
            f.Root.Add(gender);
            f.Root.Add(f.AgeLabel);
            f.Root.Add(f.Age);
            f.Root.Add(f.AgeError);
            f.Root.Add(f.AvatarLabel);
            f.Root.Add(avatarRow);
            f.RefreshAvatar();
            return f;
        }

        private void RefreshFormTexts(ProfileForm f)
        {
            f.NameLabel.text = Phase4Localization.Get("profile.name");
            f.GenderLabel.text = Phase4Localization.Get("profile.gender");
            f.AgeLabel.text = Phase4Localization.Get("profile.age");
            f.AvatarLabel.text = Phase4Localization.Get("profile.avatar");
            f.Name.textEdition.placeholder = Phase4Localization.Get("profile.name.hint");
            f.NameError.text = f.NameErrorKey != null ? Phase4Localization.Get(f.NameErrorKey) : string.Empty;
            f.AgeError.text = f.AgeErrorKey != null ? Phase4Localization.Get(f.AgeErrorKey) : string.Empty;
            f.RefreshGender();
        }

        // Live feedback only for input that is wrong; an untouched empty name is flagged on submit (force).
        private void UpdateFormErrors(ProfileForm f, bool force)
        {
            bool nameOk = PlayerProfileValidation.TryName(f.Name.value, out _, out string nameError);
            f.NameErrorKey = !nameOk && (force || !string.IsNullOrWhiteSpace(f.Name.value)) ? nameError : null;
            bool ageOk = PlayerProfileValidation.TryAge(f.Age.value, out _, out string ageError);
            f.AgeErrorKey = ageOk ? null : ageError;
            f.NameError.style.display = f.NameErrorKey != null ? DisplayStyle.Flex : DisplayStyle.None;
            f.AgeError.style.display = f.AgeErrorKey != null ? DisplayStyle.Flex : DisplayStyle.None;
            f.NameError.text = f.NameErrorKey != null ? Phase4Localization.Get(f.NameErrorKey) : string.Empty;
            f.AgeError.text = f.AgeErrorKey != null ? Phase4Localization.Get(f.AgeErrorKey) : string.Empty;
        }

        private bool TryReadForm(ProfileForm f, PlayerProfile existing, out PlayerProfile result)
        {
            result = null;
            bool nameOk = PlayerProfileValidation.TryName(f.Name.value, out string name, out _);
            bool ageOk = PlayerProfileValidation.TryAge(f.Age.value, out int age, out _);
            UpdateFormErrors(f, true);
            if (!nameOk || !ageOk)
            {
                UiAudio.Play(UiCue.Error);
                return false;
            }
            result = new PlayerProfile
            {
                name = name,
                gender = f.Gender,
                age = age,
                avatarIndex = f.Avatar,
                completed = true,
                createdUtc = existing != null && !string.IsNullOrEmpty(existing.createdUtc) ? existing.createdUtc : DateTime.UtcNow.ToString("o")
            };
            return true;
        }

        // ---- Edit from menu/settings ---------------------------------------------------------------------

        private void ShowProfileEditModal(Action back)
        {
            if (profile == null)
                return;
            VisualElement panel = UiTheme.Panel(Indigo);
            Label title = MakeLabel(string.Empty, 26f, false, Indigo, titleFont);
            ProfileForm form = BuildProfileForm(profile);
            Button save = MakeButton(RaniPink, Color.white, 20f, () =>
            {
                if (!TryReadForm(form, profile, out PlayerProfile updated))
                    return;
                if (!profileStore.Save(updated))
                {
                    ShowToast(Phase4Localization.Get("profile.saveFailed"), UiCue.Error);
                    return;
                }
                profile = updated;
                ShowToast(Phase4Localization.Get("profile.saved"));
                back();
            });
            save.style.marginTop = 14f;
            Button cancel = MakeSecondaryButton(17f, back);
            cancel.style.marginTop = 8f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 460f;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.Add(form.Root);
            panel.Add(title);
            panel.Add(scroll);
            panel.Add(save);
            panel.Add(cancel);
            OpenModal(panel, () =>
            {
                title.text = Phase4Localization.Get("menu.profile");
                save.text = Phase4Localization.Get("profile.save");
                cancel.text = Phase4Localization.Get("common.back");
                RefreshFormTexts(form);
            }, back);
        }

        // ---- First-launch onboarding ---------------------------------------------------------------------

        private VisualElement BuildOnboardingScreen()
        {
            VisualElement screen = NewSheet();
            screen.style.paddingTop = 28f;
            screen.style.paddingBottom = 20f;
            var host = new VisualElement();
            host.style.flexGrow = 1f;
            screen.Add(host);

            PlayerProfile pending = null;
            bool pageBusy = false, saving = false, finished = false;
            int currentPage = 0;
            Action finish = null;
            Action<int, bool> showPage = null;

            VisualElement NewPage()
            {
                var page = new VisualElement();
                page.style.position = Position.Absolute;
                page.style.left = 0f;
                page.style.right = 0f;
                page.style.top = 0f;
                page.style.bottom = 0f;
                page.style.justifyContent = Justify.Center;
                return page;
            }

            // Page 0: welcome
            VisualElement welcome = NewPage();
            Label brand = MakeLabel(string.Empty, 44f, false, Terracotta, titleFont);
            brand.style.unityTextAlign = TextAnchor.MiddleCenter;
            Label tagline = MakeLabel(string.Empty, 16f, false, Ink);
            tagline.style.unityTextAlign = TextAnchor.MiddleCenter;
            tagline.style.marginBottom = 14f;
            VisualElement welcomeCard = NewCard(Marigold);
            Label welcomeTitle = MakeLabel(string.Empty, 24f, true, Ink);
            Label welcomeBody = MakeLabel(string.Empty, 16f, false, Ink);
            welcomeBody.style.marginTop = 6f;
            Label damagedNote = MakeLabel(string.Empty, 14f, true, UiTheme.Error);
            damagedNote.style.marginTop = 8f;
            damagedNote.style.display = profileDamaged ? DisplayStyle.Flex : DisplayStyle.None;
            welcomeCard.Add(welcomeTitle);
            welcomeCard.Add(welcomeBody);
            welcomeCard.Add(damagedNote);
            Button create = MakeButton(RaniPink, Color.white, 22f, () => showPage(1, true));
            create.style.height = 64f;
            Button languageButton = MakeSecondaryButton(15f, Phase4Localization.ToggleLanguage);
            languageButton.style.marginTop = 10f;
            languageButton.style.alignSelf = Align.Center;
            languageButton.style.minWidth = 140f;
            welcome.Add(brand);
            welcome.Add(tagline);
            welcome.Add(welcomeCard);
            welcome.Add(create);
            welcome.Add(languageButton);

            // Page 1: details
            VisualElement details = NewPage();
            details.style.justifyContent = Justify.FlexStart;
            Label detailsTitle = MakeLabel(string.Empty, 28f, false, Terracotta, titleFont);
            detailsTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            detailsTitle.style.marginBottom = 8f;
            ProfileForm form = BuildProfileForm(null);
            VisualElement formCard = NewCard(Indigo);
            formCard.Add(form.Root);
            var detailsScroll = new ScrollView(ScrollViewMode.Vertical);
            detailsScroll.style.flexGrow = 1f;
            detailsScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            detailsScroll.Add(formCard);
            Button next = MakeButton(RaniPink, Color.white, 20f, () =>
            {
                if (pageBusy || !TryReadForm(form, null, out PlayerProfile read))
                    return;
                pending = read;
                showPage(2, true);
            });
            next.style.height = 56f;
            next.style.marginTop = 6f;
            Button detailsBack = MakeSecondaryButton(16f, () => showPage(0, false));
            detailsBack.style.marginTop = 6f;
            details.Add(detailsTitle);
            details.Add(detailsScroll);
            details.Add(next);
            details.Add(detailsBack);

            // Page 2: preview
            VisualElement preview = NewPage();
            Label previewTitle = MakeLabel(string.Empty, 28f, false, Terracotta, titleFont);
            previewTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            previewTitle.style.marginBottom = 10f;
            VisualElement previewCard = NewCard(Marigold);
            previewCard.style.alignItems = Align.Center;
            VisualElement previewBadge = MakeBadge(96f);
            previewBadge.style.marginTop = 6f;
            Label previewName = MakeLabel(string.Empty, 30f, false, Ink, titleFont);
            previewName.style.marginTop = 10f;
            previewName.style.unityTextAlign = TextAnchor.MiddleCenter;
            Label previewGender = MakeLabel(string.Empty, 17f, false, Ink);
            Label previewAge = MakeLabel(string.Empty, 17f, false, Ink);
            Label previewError = MakeLabel(string.Empty, 14f, true, UiTheme.Error);
            previewCard.Add(previewBadge);
            previewCard.Add(previewName);
            previewCard.Add(previewGender);
            previewCard.Add(previewAge);
            previewCard.Add(previewError);
            Button enter = MakeButton(RaniPink, Color.white, 22f, () =>
            {
                if (saving || pageBusy || pending == null)
                    return;
                saving = true;
                if (!profileStore.Save(pending))
                {
                    saving = false;
                    previewError.text = Phase4Localization.Get("profile.saveFailed");
                    UiAudio.Play(UiCue.Error);
                    return;
                }
                profile = pending;
                profileDamaged = false;
                showPage(3, true);
                UiAudio.Play(UiCue.Confirm);
                screen.schedule.Execute(() => finish()).StartingIn((long)(UiMotion.Duration(1.8f) * 1000f) + 300);
            });
            enter.style.height = 60f;
            Button edit = MakeSecondaryButton(17f, () => showPage(1, false));
            edit.style.marginTop = 8f;
            preview.Add(previewTitle);
            preview.Add(previewCard);
            preview.Add(enter);
            preview.Add(edit);

            // Page 3: welcome message
            VisualElement done = NewPage();
            done.style.alignItems = Align.Center;
            VisualElement doneBadge = MakeBadge(120f);
            Label doneMessage = MakeLabel(string.Empty, 25f, false, Terracotta, titleFont);
            doneMessage.style.paddingLeft = 8f;
            doneMessage.style.paddingRight = 8f;
            doneMessage.style.unityTextAlign = TextAnchor.MiddleCenter;
            doneMessage.style.marginTop = 18f;
            done.Add(doneBadge);
            done.Add(doneMessage);
            done.RegisterCallback<PointerDownEvent>(_ => finish());

            var pages = new[] { welcome, details, preview, done };
            foreach (VisualElement page in pages)
            {
                page.style.display = DisplayStyle.None;
                host.Add(page);
            }
            welcome.style.display = DisplayStyle.Flex;
            entrances[screen] = new List<VisualElement>();

            finish = () =>
            {
                if (finished || currentPage != 3)
                    return;
                finished = true;
                onboardingActive = false;
            };

            Action fillPreview = () =>
            {
                SetBadge(previewBadge, pending.avatarIndex, pending.name);
                previewName.text = pending.name;
                previewError.text = string.Empty;
            };

            showPage = (index, forward) =>
            {
                if (pageBusy || index == currentPage)
                    return;
                if (index == 2)
                    fillPreview();
                if (index == 3)
                    SetBadge(doneBadge, pending.avatarIndex, pending.name);
                pageBusy = true;
                VisualElement outgoing = pages[currentPage];
                VisualElement incoming = pages[index];
                currentPage = index;
                outgoing.SetEnabled(false);
                // Pages are hidden, never detached: UI Toolkit can leave a re-attached, previously faded element at opacity 0.
                UiMotion.Disappear(outgoing, UiMotion.Fast, () =>
                {
                    outgoing.style.display = DisplayStyle.None;
                    UiMotion.Rest(outgoing);
                    outgoing.SetEnabled(true);
                }, forward ? -14f : 14f);
                incoming.style.display = DisplayStyle.Flex;
                UiMotion.Appear(incoming, 0.08f, forward ? 18f : -18f, 1f, UiMotion.Normal);
                var parts = new List<VisualElement>();
                foreach (VisualElement child in incoming.Children())
                    parts.Add(child);
                UiMotion.Stagger(parts, 0.05f, 0.14f);
                if (index == 3)
                    UiMotion.Appear(doneBadge, 0.2f, 0f, 0.4f, 0.4f);
                screen.schedule.Execute(() => pageBusy = false).StartingIn((long)(UiMotion.Duration(0.4f) * 1000f) + 30);
            };

            refreshers[screen] = () =>
            {
                brand.text = Phase4Localization.Get("app.title");
                tagline.text = Phase4Localization.Get("app.tagline");
                welcomeTitle.text = Phase4Localization.Get("onboarding.welcome.title");
                welcomeBody.text = Phase4Localization.Get("onboarding.welcome.body");
                damagedNote.text = Phase4Localization.Get("profile.recovered");
                create.text = Phase4Localization.Get("onboarding.create");
                languageButton.text = Phase4Localization.Current == GameLanguage.Hindi ? "English" : "हिन्दी";
                detailsTitle.text = Phase4Localization.Get("profile.title.form");
                RefreshFormTexts(form);
                next.text = Phase4Localization.Get("profile.next");
                detailsBack.text = Phase4Localization.Get("common.back");
                previewTitle.text = Phase4Localization.Get("profile.preview.title");
                if (pending != null)
                {
                    previewGender.style.display = pending.gender == PlayerGender.Unspecified ? DisplayStyle.None : DisplayStyle.Flex;
                    previewGender.text = Phase4Localization.Format("profile.preview.gender", GenderText(pending.gender));
                    previewAge.style.display = pending.age == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                    previewAge.text = Phase4Localization.Format("profile.preview.age", pending.age);
                    doneMessage.text = Phase4Localization.Format("profile.welcome", pending.name);
                }
                enter.text = Phase4Localization.Get("profile.enter");
                edit.text = Phase4Localization.Get("profile.edit");
            };
            return screen;
        }
    }
}
