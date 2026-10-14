using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GangaKiBagiya
{
    // Modal system, settings, pause, how-to, toasts and HUD feedback.
    public sealed partial class Phase4Ui
    {
        private static readonly List<VisualElement> ProgressFills = new List<VisualElement>();

        private VisualElement modalLayer;
        private VisualElement modalHolder;
        private VisualElement hudPot;
        private VisualElement hitFlash;
        private Label toast;
        private Action modalRefresh;
        private Action modalBack;
        private bool modalOpen;
        private bool modalBusy;
        private int toastId;

        private void BuildModalLayer()
        {
            modalLayer = new VisualElement();
            modalLayer.style.position = Position.Absolute;
            modalLayer.style.left = 0f;
            modalLayer.style.right = 0f;
            modalLayer.style.top = 0f;
            modalLayer.style.bottom = 0f;
            modalLayer.style.backgroundColor = new Color(Ink.r, Ink.g, Ink.b, 0.6f);
            modalLayer.style.justifyContent = Justify.Center;
            modalLayer.style.paddingLeft = 20f;
            modalLayer.style.paddingRight = 20f;
            modalHolder = new VisualElement();
            modalLayer.Add(modalHolder);
            modalLayer.style.display = DisplayStyle.None;
            root.Add(modalLayer);

            toast = MakeLabel(string.Empty, 18f, true, Ink);
            toast.pickingMode = PickingMode.Ignore;
            toast.style.position = Position.Absolute;
            toast.style.left = 40f;
            toast.style.right = 40f;
            toast.style.top = 190f;
            toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            toast.style.backgroundColor = Cream;
            toast.style.paddingTop = 10f;
            toast.style.paddingBottom = 10f;
            toast.style.paddingLeft = 18f;
            toast.style.paddingRight = 18f;
            UiTheme.Round(toast, 16f);
            UiTheme.Border(toast, 3f, Indigo);
            toast.style.display = DisplayStyle.None;
            root.Add(toast);
        }

        // Opens (or swaps the content of) the single modal. Input to everything underneath is blocked while it is open.
        private void OpenModal(VisualElement content, Action refresh, Action back)
        {
            if (modalBusy)
                return;
            modalHolder.Clear();
            modalHolder.Add(content);
            modalRefresh = refresh;
            modalBack = back;
            modalBusy = true;
            modalLayer.schedule.Execute(() => modalBusy = false).StartingIn((long)(UiMotion.Duration(0.24f) * 1000f) + 20);
            if (!modalOpen)
            {
                modalOpen = true;
                modalLayer.style.display = DisplayStyle.Flex;
                modalLayer.BringToFront();
                toast.BringToFront();
                UiMotion.Appear(modalLayer, 0f, 0f, 1f, 0.18f);
            }
            UiMotion.Appear(content, 0f, 12f, 0.94f, 0.22f);
            refresh?.Invoke();
            content.schedule.Execute(() => content.Q<Button>()?.Focus()).StartingIn(60);
        }

        private void CloseModalOnly()
        {
            CloseModal(null);
        }

        private void CloseModal(Action after)
        {
            if (!modalOpen || modalBusy)
                return;
            modalBusy = true;
            UiMotion.Disappear(modalLayer, 0.16f, () =>
            {
                modalLayer.style.display = DisplayStyle.None;
                UiMotion.Rest(modalLayer);
                modalHolder.Clear();
                modalRefresh = null;
                modalBack = null;
                modalOpen = false;
                modalBusy = false;
                after?.Invoke();
            });
        }

        // Android's back button arrives as Escape.
        private void HandleBack()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;
            if (modalOpen)
                modalBack?.Invoke();
            else if (activeScreen == hudScreen)
                ShowPauseModal();
        }

        private void ApplyMotionSettings()
        {
            foreach (VisualElement fill in ProgressFills)
                UiMotion.SetTransition(fill, UiMotion.Normal, "width", "background-color");
        }

        private void ShowToast(string text, UiCue cue = UiCue.Toast)
        {
            int id = ++toastId;
            toast.text = text;
            toast.style.display = DisplayStyle.Flex;
            toast.BringToFront();
            UiAudio.Play(cue);
            UiMotion.Appear(toast, 0f, -10f, 0.96f, 0.2f);
            toast.schedule.Execute(() =>
            {
                if (id != toastId)
                    return;
                UiMotion.Disappear(toast, 0.2f, () =>
                {
                    if (id == toastId)
                        toast.style.display = DisplayStyle.None;
                }, -6f);
            }).StartingIn(1700);
        }

        private void OnGameCue(Phase1AudioCue cue)
        {
            switch (cue)
            {
                case Phase1AudioCue.WaterCollected:
                    if (hudPot != null && activeScreen == hudScreen)
                        UiMotion.Pop(hudPot);
                    break;
                case Phase1AudioCue.GoalSecured:
                    ShowToast(Phase4Localization.Get("hud.toast.goal"));
                    break;
                case Phase1AudioCue.ObstacleHit:
                    UiAudio.Play(UiCue.Error);
                    FlashHit();
                    break;
            }
        }

        private void FlashHit()
        {
            if (hitFlash == null || UiSettings.ReducedMotion)
                return;
            UiMotion.SetTransition(hitFlash, 0f, "opacity");
            hitFlash.style.opacity = 1f;
            hitFlash.schedule.Execute(() =>
            {
                UiMotion.SetTransition(hitFlash, 0.45f, "opacity");
                hitFlash.style.opacity = 0f;
            }).StartingIn(40);
        }

        // ---- Shared controls -------------------------------------------------------------------------------

        private VisualElement MakeBadge(float size)
        {
            var badge = new VisualElement();
            badge.pickingMode = PickingMode.Ignore;
            badge.style.width = size;
            badge.style.height = size;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.flexShrink = 0f;
            UiTheme.Round(badge, size * 0.5f);
            UiTheme.Border(badge, 2f, Color.white);
            Label letter = MakeLabel(string.Empty, size * 0.5f, true, Color.white);
            letter.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.Add(letter);
            return badge;
        }

        private void SetBadge(VisualElement badge, int avatar, string name)
        {
            Color color = UiTheme.Avatars[Mathf.Clamp(avatar, 0, UiTheme.Avatars.Length - 1)];
            badge.style.backgroundColor = color;
            Label letter = (Label)badge[0];
            letter.style.color = color.grayscale > 0.55f ? Ink : Color.white;
            string initial = string.Empty;
            if (!string.IsNullOrEmpty(name))
                initial = new StringInfo(name).SubstringByTextElements(0, 1).ToUpperInvariant();
            letter.text = initial;
        }

        // Row of mutually exclusive choices; labels come from localization keys or fixed text.
        private VisualElement MakeSegmented(string[] fixedLabels, string[] keys, Func<int> selected, Action<int> choose,
            bool wrap, out Action refresh)
        {
            int count = keys != null ? keys.Length : fixedLabels.Length;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = wrap ? Wrap.Wrap : Wrap.NoWrap;
            var buttons = new List<Button>();
            var restyles = new List<Action>();
            for (int i = 0; i < count; i++)
            {
                int index = i;
                Button button = NewButtonBase(15f, () => choose(index));
                button.style.flexGrow = 1f;
                button.style.flexBasis = wrap ? Length.Percent(46f) : 0f;
                button.style.marginLeft = 2f;
                button.style.marginRight = 2f;
                button.style.marginBottom = 4f;
                restyles.Add(UiTheme.StyleInteractive(button, () => selected() == index ? Indigo : Cream,
                    () => selected() == index ? Color.white : Indigo, 2f, () => Indigo));
                buttons.Add(button);
                row.Add(button);
            }
            Action update = () =>
            {
                for (int i = 0; i < count; i++)
                {
                    buttons[i].text = keys != null ? Phase4Localization.Get(keys[i]) : fixedLabels[i];
                    restyles[i]();
                }
            };
            foreach (Button button in buttons)
                button.clicked += update;
            refresh = update;
            return row;
        }

        private VisualElement SettingRow(Label label, VisualElement control)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 10f;
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            row.Add(label);
            row.Add(control);
            return row;
        }

        // ---- Modals ----------------------------------------------------------------------------------------

        private void ShowPauseModal()
        {
            game.SetPaused(true);
            BuildPauseModal();
        }

        private void BuildPauseModal()
        {
            VisualElement panel = UiTheme.Panel(Terracotta);
            Label title = MakeLabel(string.Empty, 30f, false, Terracotta, titleFont);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 10f;
            Action resume = () => CloseModal(() => game.SetPaused(false));
            Button resumeButton = MakeButton(RaniPink, Color.white, 22f, resume);
            resumeButton.style.height = 60f;
            Button howTo = MakeSecondaryButton(17f, () => ShowHowToModal(BuildPauseModal));
            howTo.style.marginTop = 10f;
            Button settings = MakeSecondaryButton(17f, () => ShowSettingsModal(BuildPauseModal));
            settings.style.marginTop = 8f;
            panel.Add(title);
            panel.Add(resumeButton);
            panel.Add(howTo);
            panel.Add(settings);
            OpenModal(panel, () =>
            {
                title.text = Phase4Localization.Get("pause.title");
                resumeButton.text = Phase4Localization.Get("pause.resume");
                howTo.text = Phase4Localization.Get("menu.howto");
                settings.text = Phase4Localization.Get("menu.settings");
            }, resume);
        }

        private void ShowHowToModal(Action back)
        {
            VisualElement panel = UiTheme.Panel(Indigo);
            Label title = MakeLabel(string.Empty, 22f, true, Indigo);
            panel.Add(title);
            var lines = new List<Label>();
            for (int i = 0; i < 5; i++)
            {
                Label line = MakeLabel(string.Empty, 15f, false, Ink);
                line.style.marginTop = 4f;
                lines.Add(line);
                panel.Add(line);
            }
            Button done = MakeSecondaryButton(17f, back);
            done.style.marginTop = 12f;
            panel.Add(done);
            OpenModal(panel, () =>
            {
                title.text = Phase4Localization.Get("home.howto.title");
                for (int i = 0; i < lines.Count; i++)
                    lines[i].text = "• " + Phase4Localization.Get("home.howto." + (i + 1));
                done.text = Phase4Localization.Get("common.back");
            }, back);
        }

        private void ShowSettingsModal(Action back)
        {
            VisualElement panel = UiTheme.Panel(Indigo);
            Label title = MakeLabel(string.Empty, 26f, false, Indigo, titleFont);
            title.style.marginBottom = 4f;
            panel.Add(title);

            Label languageLabel = MakeLabel(string.Empty, 16f, true, Ink);
            VisualElement language = MakeSegmented(new[] { "हिन्दी", "English" }, null,
                () => Phase4Localization.Current == GameLanguage.Hindi ? 0 : 1,
                i => Phase4Localization.SetLanguage(i == 0 ? GameLanguage.Hindi : GameLanguage.English), false, out Action refreshLanguage);
            language.style.width = 170f;
            panel.Add(SettingRow(languageLabel, language));

            Label soundsLabel = MakeLabel(string.Empty, 16f, true, Ink);
            Button sounds = MakeToggle(() => UiSettings.UiSoundsEnabled, () => UiSettings.UiSoundsEnabled = !UiSettings.UiSoundsEnabled, out Action refreshSounds);
            panel.Add(SettingRow(soundsLabel, sounds));

            Label motionLabel = MakeLabel(string.Empty, 16f, true, Ink);
            Button motion = MakeToggle(() => UiSettings.ReducedMotion, () => UiSettings.ReducedMotion = !UiSettings.ReducedMotion, out Action refreshMotion);
            panel.Add(SettingRow(motionLabel, motion));

            Label profileLabel = MakeLabel(string.Empty, 16f, true, Ink);
            profileLabel.style.marginTop = 14f;
            panel.Add(profileLabel);
            var profileRow = new VisualElement();
            profileRow.style.flexDirection = FlexDirection.Row;
            profileRow.style.alignItems = Align.Center;
            profileRow.style.marginTop = 6f;
            VisualElement badge = MakeBadge(40f);
            Label profileName = MakeLabel(string.Empty, 17f, false, Ink);
            profileName.style.marginLeft = 10f;
            profileName.style.flexGrow = 1f;
            Button edit = MakeSecondaryButton(15f, () => ShowProfileEditModal(() => ShowSettingsModal(back)));
            profileRow.Add(badge);
            profileRow.Add(profileName);
            profileRow.Add(edit);
            panel.Add(profileRow);

            Button done = MakeButton(Indigo, Color.white, 18f, back);
            done.style.marginTop = 14f;
            panel.Add(done);

            OpenModal(panel, () =>
            {
                title.text = Phase4Localization.Get("settings.title");
                languageLabel.text = Phase4Localization.Get("settings.language");
                soundsLabel.text = Phase4Localization.Get("settings.sounds");
                motionLabel.text = Phase4Localization.Get("settings.motion");
                profileLabel.text = Phase4Localization.Get("settings.profile");
                edit.text = Phase4Localization.Get("profile.edit");
                done.text = Phase4Localization.Get("common.back");
                if (profile != null)
                {
                    SetBadge(badge, profile.avatarIndex, profile.name);
                    profileName.text = profile.name;
                }
                refreshLanguage();
                refreshSounds();
                refreshMotion();
            }, back);
        }

        private Button MakeToggle(Func<bool> isOn, Action toggle, out Action refresh)
        {
            Button button = NewButtonBase(15f, null);
            button.style.width = 96f;
            Action restyle = UiTheme.StyleInteractive(button, () => isOn() ? LeafGreen : Cream, () => isOn() ? Color.white : Indigo,
                2f, () => isOn() ? LeafGreen : Indigo);
            Action update = () =>
            {
                button.text = Phase4Localization.Get(isOn() ? "settings.on" : "settings.off");
                restyle();
            };
            button.clicked += () =>
            {
                toggle();
                update();
            };
            refresh = update;
            return button;
        }
    }
}
