using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GangaKiBagiya
{
    public enum Phase4DailyState
    {
        Available,
        Done,
        Unavailable,
        PreviousRunGrace
    }

    public struct Phase4RewardSlot
    {
        public int slotNumber;
        public Phase3RewardStatus status;
        public bool isToday;
    }

    // Player-facing screens built with UI Toolkit so Devanagari is shaped by the advanced text generator.
    public sealed partial class Phase4Ui : MonoBehaviour
    {
        private enum Overlay
        {
            None,
            Rewards,
            Watering,
            Done
        }

        private const float ReferenceWidth = 430f;
        private const float ReferenceHeight = 932f;
        private const float WateringSeconds = 3.6f;
        private const float SteppedFramesPerSecond = 12f;

        private static readonly Color Kraft = UiTheme.Kraft;
        private static readonly Color Cream = UiTheme.Cream;
        private static readonly Color Ink = UiTheme.Ink;
        private static readonly Color Terracotta = UiTheme.Terracotta;
        private static readonly Color Marigold = UiTheme.Marigold;
        private static readonly Color RaniPink = UiTheme.RaniPink;
        private static readonly Color Indigo = UiTheme.Indigo;
        private static readonly Color RiverBlue = UiTheme.RiverBlue;
        private static readonly Color LeafGreen = UiTheme.LeafGreen;
        private static readonly Color Muted = UiTheme.Muted;

        private readonly Dictionary<VisualElement, Action> refreshers = new Dictionary<VisualElement, Action>();
        private Phase1GameController game;
        private VisualElement root;
        private VisualElement setupScreen;
        private VisualElement homeScreen;
        private VisualElement hudScreen;
        private VisualElement resultScreen;
        private VisualElement wateringScreen;
        private VisualElement doneScreen;
        private VisualElement rewardsScreen;
        private VisualElement activeScreen;
        private VisualElement hudCard;
        private Label persistenceLabel;

        /// <summary>HUD card's bottom edge as a fraction of the panel height, or 0 when not measurable.</summary>
        public float HudBottomFraction
        {
            get
            {
                if (hudCard == null || hudCard.panel == null || root == null || root.worldBound.height <= 0f || hudCard.worldBound.height <= 0f)
                    return 0f;
                return hudCard.worldBound.yMax / root.worldBound.height;
            }
        }
        private Font bodyFont;
        private Font boldFont;
        private Font titleFont;
        private Overlay overlay;
        private bool resultDismissed;
        private bool wasRunning;
        private float wateringStart;
        private int wateringStageBefore;

        private VisualElement wateringPot;
        private VisualElement wateringStem;
        private VisualElement wateringCanopy;
        private VisualElement wateringFruit;
        private readonly List<VisualElement> wateringDrops = new List<VisualElement>();

        public void Initialize(Phase1GameController controller)
        {
            game = controller;
        }

        private void Start()
        {
            bodyFont = Resources.Load<Font>("Fonts/Hind-Regular");
            boldFont = Resources.Load<Font>("Fonts/Hind-Bold");
            titleFont = Resources.Load<Font>("Fonts/YatraOne-Regular");

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int((int)ReferenceWidth, (int)ReferenceHeight);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 10;

            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1f;
            root.style.unityTextGenerator = TextGeneratorType.Advanced;
            if (bodyFont != null)
                root.style.unityFontDefinition = FontDefinition.FromFont(bodyFont);
            root.style.color = Ink;

            ProgressFills.Clear();
            profileStore = new PlayerProfileStore(Application.persistentDataPath);
            profile = profileStore.Load(out profileDamaged);
            onboardingActive = profile == null;

            setupScreen = BuildSetupScreen();
            homeScreen = BuildHomeScreen();
            hudScreen = BuildHudScreen();
            resultScreen = BuildResultScreen();
            wateringScreen = BuildWateringScreen();
            doneScreen = BuildDoneScreen();
            rewardsScreen = BuildRewardsScreen();
            onboardingScreen = BuildOnboardingScreen();
            persistenceLabel = MakeLabel(string.Empty, 11f, false, Muted);
            persistenceLabel.style.position = Position.Absolute;
            persistenceLabel.style.left = 12f;
            persistenceLabel.style.right = 12f;
            persistenceLabel.style.bottom = 2f;
            persistenceLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(persistenceLabel);
            BuildModalLayer();

            game.PointerBlocker = guiPosition => modalOpen || IsOverButton(guiPosition);
            game.AudioCue += OnGameCue;
            UiSettings.Changed += ApplyMotionSettings;
            Phase4Localization.LanguageChanged += RefreshActive;
            ShowScreen(null);
        }

        private void OnDestroy()
        {
            Phase4Localization.LanguageChanged -= RefreshActive;
            UiSettings.Changed -= ApplyMotionSettings;
            if (game != null)
            {
                game.AudioCue -= OnGameCue;
                game.PointerBlocker = null;
            }
        }

        private void Update()
        {
            if (root == null)
                return;

            VisualElement target = ResolveScreen();
            if (target != activeScreen)
                ShowScreen(target);
            RefreshActive();

            ApplySafeArea();
            HandleBack();

            string warning = game.PersistenceWarning ?? string.Empty;
            if (persistenceLabel.text != warning)
                persistenceLabel.text = warning;
        }

        private Rect appliedSafeArea;

        // Pads the UI by the display's safe area (cutouts, system bars).
        private void ApplySafeArea()
        {
            Rect area = Screen.safeArea;
            if (area == appliedSafeArea || root.worldBound.height <= 0f || Screen.height <= 0)
                return;
            appliedSafeArea = area;
            float scale = root.worldBound.height / Screen.height;
            float limit = root.worldBound.height * 0.1f;
            root.style.paddingTop = Mathf.Clamp((Screen.height - area.yMax) * scale, 0f, limit);
            root.style.paddingBottom = Mathf.Clamp(area.yMin * scale, 0f, limit);
        }

        private VisualElement ResolveScreen()
        {
            if (!string.IsNullOrEmpty(game.SetupWarning))
                return setupScreen;
            if (onboardingActive)
                return onboardingScreen;
            if (!game.IsReady)
                return null;

            bool running = game.RunStatus == Phase1RunStatus.Running;
            if (running && !wasRunning)
            {
                overlay = Overlay.None;
                resultDismissed = false;
            }
            wasRunning = running;
            if (running)
                return hudScreen;

            switch (overlay)
            {
                case Overlay.Rewards:
                    return rewardsScreen;
                case Overlay.Watering:
                    if (Time.unscaledTime - wateringStart >= WateringSeconds)
                        overlay = Overlay.Done;
                    return overlay == Overlay.Watering ? wateringScreen : doneScreen;
                case Overlay.Done:
                    return doneScreen;
            }

            return game.HasResult && !resultDismissed ? resultScreen : homeScreen;
        }

        private readonly Dictionary<VisualElement, List<VisualElement>> entrances = new Dictionary<VisualElement, List<VisualElement>>();
        private readonly HashSet<VisualElement> entered = new HashSet<VisualElement>();

        // Crossfades between screens; the incoming screen is non-interactive until its entrance finishes,
        // so a double tap can never act on a half-shown screen.
        private void ShowScreen(VisualElement target)
        {
            VisualElement outgoing = activeScreen;
            activeScreen = target;
            if (outgoing != null && outgoing != target)
            {
                outgoing.SetEnabled(false);
                UiMotion.Disappear(outgoing, UiMotion.Fast, () =>
                {
                    // Hidden, never detached (see Phase4UiOnboarding): re-attached faded elements can stay at opacity 0.
                    if (outgoing != activeScreen)
                        outgoing.style.display = DisplayStyle.None;
                    UiMotion.Rest(outgoing);
                    outgoing.SetEnabled(true);
                }, 8f);
            }

            if (target != null)
            {
                if (target.parent == null)
                    root.Insert(0, target);
                target.style.display = DisplayStyle.Flex;
                float seconds = UiMotion.Duration(UiMotion.Normal);
                if (seconds > 0f)
                {
                    target.SetEnabled(false);
                    target.schedule.Execute(() => target.SetEnabled(true)).StartingIn((long)(seconds * 1000f) + 40);
                }
                else
                    target.SetEnabled(true);
                UiMotion.Appear(target, 0f, 10f, 1f, UiMotion.Normal);
                if (entrances.TryGetValue(target, out List<VisualElement> parts) && entered.Add(target))
                    UiMotion.Stagger(parts, 0.06f, 0.08f);
            }
            RefreshActive();
        }

        private void RefreshActive()
        {
            if (activeScreen != null && refreshers.TryGetValue(activeScreen, out Action refresh))
                refresh();
            modalRefresh?.Invoke();
        }

        private bool IsOverButton(Vector2 guiPosition)
        {
            if (root == null || root.panel == null)
                return false;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, guiPosition);
            for (VisualElement element = root.panel.Pick(panelPosition); element != null; element = element.parent)
            {
                if (element is Button)
                    return true;
            }
            return false;
        }

        private VisualElement BuildSetupScreen()
        {
            VisualElement screen = NewScreen();
            VisualElement card = NewCard(Terracotta);
            card.style.marginTop = 80f;
            Label message = MakeLabel(string.Empty, 17f, false, Ink);
            card.Add(MakeLabel(Phase4Localization.Get("app.title"), 28f, false, Terracotta, titleFont));
            card.Add(message);
            screen.Add(card);
            refreshers[screen] = () => message.text = game.SetupWarning ?? string.Empty;
            return screen;
        }

        private VisualElement BuildHomeScreen()
        {
            VisualElement screen = NewSheet();
            screen.style.paddingTop = 24f;
            screen.style.paddingBottom = 20f;

            VisualElement greetingRow = new VisualElement();
            greetingRow.style.flexDirection = FlexDirection.Row;
            greetingRow.style.alignItems = Align.Center;
            greetingRow.style.marginBottom = 6f;
            VisualElement badge = MakeBadge(40f);
            Label greeting = MakeLabel(string.Empty, 20f, true, Ink);
            greeting.style.marginLeft = 10f;
            greetingRow.Add(badge);
            greetingRow.Add(greeting);
            screen.Add(greetingRow);

            Label title = MakeLabel(string.Empty, 40f, false, Terracotta, titleFont);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            Label tagline = MakeLabel(string.Empty, 16f, false, Ink);
            tagline.style.unityTextAlign = TextAnchor.MiddleCenter;
            tagline.style.marginBottom = 12f;
            screen.Add(title);
            screen.Add(tagline);

            VisualElement plantCard = NewCard(Marigold);
            Label stageLabel = MakeLabel(string.Empty, 19f, true, Ink);
            Label growthLabel = MakeLabel(string.Empty, 15f, false, Ink);
            VisualElement growthBar = NewProgressBar(out VisualElement growthFill, LeafGreen);
            Label statusLabel = MakeLabel(string.Empty, 16f, true, Terracotta);
            statusLabel.style.marginTop = 6f;
            plantCard.Add(stageLabel);
            plantCard.Add(growthLabel);
            plantCard.Add(growthBar);
            plantCard.Add(statusLabel);
            screen.Add(plantCard);

            Button play = MakeButton(RaniPink, Color.white, 24f, () =>
            {
                if (game.TryStartRun())
                    resultDismissed = false;
            });
            play.style.height = 68f;
            play.style.marginTop = 4f;
            screen.Add(play);

            Button rewards = MakeSecondaryButton(16f, () => overlay = Overlay.Rewards);
            rewards.style.marginTop = 10f;
            screen.Add(rewards);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 8f;
            Button profileButton = MakeSecondaryButton(15f, () => ShowProfileEditModal(CloseModalOnly));
            Button settingsButton = MakeSecondaryButton(15f, () => ShowSettingsModal(CloseModalOnly));
            Button howTo = MakeSecondaryButton(15f, () => ShowHowToModal(CloseModalOnly));
            foreach (Button b in new[] { profileButton, settingsButton, howTo })
            {
                b.style.flexGrow = 1f;
                b.style.flexBasis = 0f;
                b.style.marginLeft = 3f;
                b.style.marginRight = 3f;
                row.Add(b);
            }
            screen.Add(row);

            entrances[screen] = new List<VisualElement> { greetingRow, title, tagline, plantCard, play, rewards, row };

            refreshers[screen] = () =>
            {
                greetingRow.style.display = profile != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (profile != null)
                {
                    SetBadge(badge, profile.avatarIndex, profile.name);
                    greeting.text = Phase4Localization.Format("menu.greeting", profile.name);
                }
                title.text = Phase4Localization.Get("app.title");
                tagline.text = Phase4Localization.Get("app.tagline");
                stageLabel.text = Phase4Localization.Format("hud.stage", game.PlantStage, Phase4Localization.StageName(game.PlantStage));
                growthLabel.text = GrowthText();
                SetFill(growthFill, GrowthFraction());
                statusLabel.text = HomeStatusText();
                play.text = Phase4Localization.Get(game.DailyState == Phase4DailyState.Done ? "home.playDone" : "home.play");
                UiTheme.SetEnabled(play, game.CanStartRun);
                rewards.text = Phase4Localization.Get("home.rewards") + (game.HasClaimableReward ? "  ●" : string.Empty);
                profileButton.text = Phase4Localization.Get("menu.profile");
                settingsButton.text = Phase4Localization.Get("menu.settings");
                howTo.text = Phase4Localization.Get("menu.howto");
            };
            return screen;
        }

        private VisualElement BuildHudScreen()
        {
            VisualElement screen = new VisualElement();
            screen.pickingMode = PickingMode.Ignore;
            screen.style.position = Position.Absolute;
            screen.style.left = 0f;
            screen.style.right = 0f;
            screen.style.top = 0f;
            screen.style.bottom = 0f;

            VisualElement top = NewCard(Terracotta);
            hudCard = top;
            top.pickingMode = PickingMode.Ignore;
            top.style.marginTop = 8f;
            top.style.paddingTop = 6f;
            top.style.paddingBottom = 6f;
            Label stage = MakeLabel(string.Empty, 15f, true, Ink);
            Label health = MakeLabel(string.Empty, 14f, false, Ink);
            Label watering = MakeLabel(string.Empty, 14f, false, Ink);
            Label pot = MakeLabel(string.Empty, 18f, true, Indigo);
            hudPot = pot;
            pot.style.marginTop = 2f;
            VisualElement meter = NewProgressBar(out VisualElement meterFill, RiverBlue);
            VisualElement headRow = new VisualElement();
            headRow.pickingMode = PickingMode.Ignore;
            headRow.style.flexDirection = FlexDirection.Row;
            headRow.style.alignItems = Align.Center;
            stage.style.flexGrow = 1f;
            stage.style.flexShrink = 1f;
            Button pause = MakeSecondaryButton(16f, () => { if (!modalOpen) ShowPauseModal(); });
            pause.text = "II";
            pause.tooltip = Phase4Localization.Get("hud.pause");
            pause.style.width = 44f;
            pause.style.minHeight = 40f;
            pause.style.height = 40f;
            pause.style.marginLeft = 8f;
            headRow.Add(stage);
            headRow.Add(pause);
            top.Add(headRow);
            top.Add(health);
            top.Add(watering);
            top.Add(pot);
            top.Add(meter);
            screen.Add(top);

            VisualElement bottom = new VisualElement();
            bottom.pickingMode = PickingMode.Ignore;
            bottom.style.position = Position.Absolute;
            bottom.style.left = 12f;
            bottom.style.right = 12f;
            bottom.style.bottom = 22f;
            Label hint = MakeLabel(string.Empty, 15f, true, Ink);
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.backgroundColor = new Color(Cream.r, Cream.g, Cream.b, 0.9f);
            Round(hint, 10f);
            hint.style.paddingTop = 6f;
            hint.style.paddingBottom = 6f;
            Button bank = MakeButton(RaniPink, Color.white, 22f, game.RequestBank);
            bank.style.height = 60f;
            bank.style.marginTop = 8f;
            bottom.Add(hint);
            bottom.Add(bank);
            screen.Add(bottom);

            hitFlash = new VisualElement();
            hitFlash.pickingMode = PickingMode.Ignore;
            hitFlash.style.position = Position.Absolute;
            hitFlash.style.left = 0f;
            hitFlash.style.right = 0f;
            hitFlash.style.top = 0f;
            hitFlash.style.bottom = 0f;
            hitFlash.style.backgroundColor = new Color(UiTheme.Error.r, UiTheme.Error.g, UiTheme.Error.b, 0.35f);
            hitFlash.style.opacity = 0f;
            screen.Insert(0, hitFlash);

            refreshers[screen] = () =>
            {
                stage.text = Phase4Localization.Format("hud.stage", game.PlantStage, Phase4Localization.StageName(game.PlantStage));
                health.text = HealthText() + " · " + GrowthText();
                watering.text = WateringStateText();
                pot.text = Phase4Localization.Format("hud.pot", game.PotWater.ToString("0.0"), game.DailyGoalUnits);
                SetFill(meterFill, game.DailyGoalUnits > 0 ? game.PotWater / game.DailyGoalUnits : 0f);
                meterFill.style.backgroundColor = game.GoalSecured ? LeafGreen : RiverBlue;
                hint.text = Phase4Localization.Get(game.GoalSecured ? "hud.goalSecured" : "hud.hint");
                bank.text = Phase4Localization.Get("hud.takePot");
                bank.style.display = game.GoalSecured ? DisplayStyle.Flex : DisplayStyle.None;
                pause.tooltip = Phase4Localization.Get("hud.pause");
            };
            return screen;
        }

        private VisualElement BuildResultScreen()
        {
            VisualElement screen = NewScreen();
            screen.style.justifyContent = Justify.Center;
            VisualElement card = NewCard(Terracotta);
            Label title = MakeLabel(string.Empty, 28f, false, Terracotta, titleFont);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            Label message = MakeLabel(string.Empty, 16f, false, Ink);
            Label numbers = MakeLabel(string.Empty, 18f, true, Indigo);
            numbers.style.marginTop = 8f;
            Label outcome = MakeLabel(string.Empty, 16f, false, Ink);
            outcome.style.marginTop = 8f;
            Button action = MakeButton(RaniPink, Color.white, 22f, OnResultAction);
            action.style.height = 60f;
            action.style.marginTop = 14f;
            card.Add(title);
            card.Add(message);
            card.Add(numbers);
            card.Add(outcome);
            card.Add(action);
            screen.Add(card);

            refreshers[screen] = () =>
            {
                if (!game.HasResult)
                    return;
                Phase1RunResult result = game.Result;
                title.text = Phase4Localization.Get(result.reason == Phase1RunEndReason.HitBeforeGoal ? "result.title.fail" :
                    result.reason == Phase1RunEndReason.HitAfterGoal ? "result.title.goalHit" : "result.title.banked");
                message.text = result.reason == Phase1RunEndReason.Banked
                    ? Phase4Localization.Get("result.banked")
                    : Phase4Localization.Get("result.hit") + "\n" + Phase4Localization.Get(
                        result.reason == Phase1RunEndReason.HitBeforeGoal ? "result.hit.before" : "result.hit.after");
                numbers.text = Phase4Localization.Get("result.kept") + ": " + FormatWater(result.retainedMilliWater) +
                               "   " + Phase4Localization.Get("result.spilled") + ": " + FormatWater(result.spilledMilliWater) +
                               "\n" + Phase4Localization.Get("result.bonus") + ": " + result.bonusGrowthPoints;
                outcome.text = game.WateringApplied ? Phase4Localization.Get("done.comeBack") : Phase4Localization.Get("result.noChange");
                action.text = Phase4Localization.Get(game.WateringApplied ? "result.waterPlant" :
                    game.CanStartRun ? "result.retry" : "common.back");
            };
            return screen;
        }

        private void OnResultAction()
        {
            if (game.WateringApplied)
            {
                resultDismissed = true;
                wateringStart = Time.unscaledTime;
                wateringStageBefore = game.StageAdvancedByWatering ? game.PlantStage - 1 : game.PlantStage;
                overlay = Overlay.Watering;
                return;
            }

            if (!game.TryStartRun())
                resultDismissed = true;
        }

        private VisualElement BuildWateringScreen()
        {
            VisualElement screen = NewScreen();
            Label title = MakeLabel(string.Empty, 24f, false, Terracotta, titleFont);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginTop = 40f;
            screen.Add(title);

            VisualElement stage = new VisualElement();
            stage.pickingMode = PickingMode.Ignore;
            stage.style.height = 420f;
            stage.style.marginTop = 20f;
            screen.Add(stage);

            VisualElement soil = NewBox(Hex("#6B4A2F"), 300f, 36f);
            soil.style.left = 45f;
            soil.style.top = 360f;
            Round(soil, 18f);
            wateringStem = NewBox(LeafGreen, 12f, 20f);
            wateringStem.style.left = 189f;
            wateringCanopy = NewBox(LeafGreen, 20f, 20f);
            Round(wateringCanopy, 60f);
            wateringFruit = NewBox(Marigold, 18f, 18f);
            Round(wateringFruit, 9f);
            wateringPot = NewBox(Terracotta, 110f, 92f);
            Round(wateringPot, 22f);
            wateringPot.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(0f));
            stage.Add(soil);
            stage.Add(wateringStem);
            stage.Add(wateringCanopy);
            stage.Add(wateringFruit);
            stage.Add(wateringPot);
            for (int i = 0; i < 3; i++)
            {
                VisualElement drop = NewBox(RiverBlue, 10f, 16f);
                Round(drop, 6f);
                wateringDrops.Add(drop);
                stage.Add(drop);
            }

            Button skip = MakeButton(Muted, Color.white, 16f, () => overlay = Overlay.Done);
            skip.style.width = 120f;
            skip.style.alignSelf = Align.Center;
            screen.Add(skip);

            refreshers[screen] = () =>
            {
                title.text = Phase4Localization.Get("watering.title");
                skip.text = Phase4Localization.Get("watering.skip");
                AnimateWatering();
            };
            return screen;
        }

        // Stepped to ~12 fps to match the stop-motion look.
        private void AnimateWatering()
        {
            float elapsed = Mathf.Max(0f, Time.unscaledTime - wateringStart);
            float t = Mathf.Floor(elapsed * SteppedFramesPerSecond) / SteppedFramesPerSecond;
            float slide = Mathf.Clamp01(t / 1.0f);
            float pour = Mathf.Clamp01((t - 1.0f) / 0.4f) * (t < 2.4f ? 1f : 0f);
            float potX = Mathf.Lerp(-140f, 165f, slide);
            float tilt = -38f * pour;
            wateringPot.style.left = potX;
            wateringPot.style.top = 40f;
            wateringPot.style.rotate = new Rotate(new Angle(tilt, AngleUnit.Degree));

            bool pouring = pour > 0f;
            for (int i = 0; i < wateringDrops.Count; i++)
            {
                VisualElement drop = wateringDrops[i];
                drop.style.display = pouring ? DisplayStyle.Flex : DisplayStyle.None;
                float cycle = (t * 2f + i * 0.34f) % 1f;
                drop.style.left = 188f + (i - 1) * 8f;
                drop.style.top = 120f + cycle * 220f;
            }

            int visualStage = game.PlantStage;
            if (game.StageAdvancedByWatering && t < 2.8f)
                visualStage = wateringStageBefore;
            float bounce = t >= 2.4f && t < 3.0f ? 1.12f : 1f;
            float stemHeight = (14f + 34f * (visualStage - 1)) * bounce;
            wateringStem.style.height = stemHeight;
            wateringStem.style.top = 360f - stemHeight;
            float canopy = visualStage >= 3 ? (50f + 22f * (visualStage - 3)) * bounce : 0f;
            wateringCanopy.style.display = canopy > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            wateringCanopy.style.width = canopy;
            wateringCanopy.style.height = canopy * 0.8f;
            wateringCanopy.style.left = 195f - canopy * 0.5f;
            wateringCanopy.style.top = 360f - stemHeight - canopy * 0.55f;
            wateringCanopy.style.backgroundColor = visualStage == 5 ? Hex("#E58FB0") : LeafGreen;
            wateringFruit.style.display = visualStage >= 6 ? DisplayStyle.Flex : DisplayStyle.None;
            wateringFruit.style.left = 205f;
            wateringFruit.style.top = 360f - stemHeight + 6f;
        }

        private VisualElement BuildDoneScreen()
        {
            VisualElement screen = NewScreen();
            screen.style.justifyContent = Justify.Center;
            VisualElement card = NewCard(LeafGreen);
            Label title = MakeLabel(string.Empty, 28f, false, LeafGreen, titleFont);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            Label stage = MakeLabel(string.Empty, 18f, true, Ink);
            stage.style.marginTop = 8f;
            Label gained = MakeLabel(string.Empty, 16f, false, Ink);
            Label growth = MakeLabel(string.Empty, 15f, false, Ink);
            VisualElement bar = NewProgressBar(out VisualElement fill, LeafGreen);
            Label stageUp = MakeLabel(string.Empty, 16f, true, RaniPink);
            stageUp.style.marginTop = 6f;
            Label factTitle = MakeLabel(string.Empty, 16f, true, Indigo);
            factTitle.style.marginTop = 12f;
            Label fact = MakeLabel(string.Empty, 16f, false, Ink);
            Label comeBack = MakeLabel(string.Empty, 15f, false, Muted);
            comeBack.style.marginTop = 10f;
            Button rewards = MakeButton(Indigo, Color.white, 17f, () => overlay = Overlay.Rewards);
            rewards.style.marginTop = 12f;
            Button home = MakeButton(RaniPink, Color.white, 20f, () =>
            {
                overlay = Overlay.None;
                resultDismissed = true;
            });
            home.style.marginTop = 8f;
            foreach (VisualElement element in new VisualElement[]
                     { title, stage, gained, growth, bar, stageUp, factTitle, fact, comeBack, rewards, home })
                card.Add(element);
            screen.Add(card);

            refreshers[screen] = () =>
            {
                title.text = Phase4Localization.Get("done.title");
                stage.text = Phase4Localization.Format("hud.stage", game.PlantStage, Phase4Localization.StageName(game.PlantStage));
                gained.text = Phase4Localization.Format("done.gained", game.GrowthPointsAwarded);
                growth.text = GrowthText();
                SetFill(fill, GrowthFraction());
                stageUp.text = game.StageAdvancedByWatering
                    ? Phase4Localization.Format("done.stageUp", Phase4Localization.StageName(game.PlantStage))
                    : string.Empty;
                stageUp.style.display = game.StageAdvancedByWatering ? DisplayStyle.Flex : DisplayStyle.None;
                factTitle.text = Phase4Localization.Get("done.factTitle");
                IReadOnlyList<string> facts = Phase4Localization.PlantFactKeys;
                fact.text = Phase4Localization.Get(facts[game.LocalDayOfYear % facts.Count]);
                comeBack.text = Phase4Localization.Get("done.comeBack");
                rewards.text = Phase4Localization.Get("done.rewards") + (game.HasClaimableReward ? "  ●" : string.Empty);
                home.text = Phase4Localization.Get("done.home");
            };
            return screen;
        }

        private VisualElement BuildRewardsScreen()
        {
            VisualElement screen = NewScreen();
            screen.style.paddingTop = 36f;
            Label title = MakeLabel(string.Empty, 30f, false, Terracotta, titleFont);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            screen.Add(title);

            VisualElement grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.Center;
            grid.style.marginTop = 14f;
            var tiles = new List<(VisualElement tile, Label day, Label status)>();
            for (int i = 0; i < 7; i++)
            {
                VisualElement tile = NewCard(Muted);
                tile.style.width = 92f;
                tile.style.height = 96f;
                tile.style.marginLeft = 4f;
                tile.style.marginRight = 4f;
                tile.style.marginBottom = 8f;
                tile.style.alignItems = Align.Center;
                tile.style.justifyContent = Justify.Center;
                Label day = MakeLabel(string.Empty, 17f, true, Ink);
                Label status = MakeLabel(string.Empty, 14f, false, Ink);
                tile.Add(day);
                tile.Add(status);
                tiles.Add((tile, day, status));
                grid.Add(tile);
            }
            screen.Add(grid);

            Label note = MakeLabel(string.Empty, 14f, false, Muted);
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            note.style.marginTop = 6f;
            screen.Add(note);
            Button claim = MakeButton(RaniPink, Color.white, 20f, () => game.TryClaimReward());
            claim.style.marginTop = 14f;
            claim.style.height = 60f;
            Button back = MakeButton(Indigo, Color.white, 18f, () => overlay = Overlay.None);
            back.style.marginTop = 10f;
            screen.Add(claim);
            screen.Add(back);

            refreshers[screen] = () =>
            {
                title.text = Phase4Localization.Get("rewards.title");
                note.text = Phase4Localization.Get("rewards.note");
                Phase4RewardSlot[] slots = game.GetRewardSlots();
                for (int i = 0; i < tiles.Count; i++)
                {
                    bool has = i < slots.Length;
                    tiles[i].tile.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                    if (!has)
                        continue;
                    Phase4RewardSlot slot = slots[i];
                    tiles[i].day.text = Phase4Localization.Format("rewards.day", slot.slotNumber);
                    tiles[i].status.text = Phase4Localization.Get(RewardKey(slot.status));
                    Color accent = slot.status == Phase3RewardStatus.Claimed ? LeafGreen :
                        slot.status == Phase3RewardStatus.Available ? RaniPink :
                        slot.status == Phase3RewardStatus.Forfeited ? Muted : Indigo;
                    SetBorder(tiles[i].tile, slot.isToday ? 5f : 3f, accent);
                    tiles[i].tile.style.backgroundColor = slot.status == Phase3RewardStatus.Forfeited ? Hex("#E4DAC4") : Cream;
                }
                bool canClaim = game.HasClaimableReward;
                claim.text = canClaim
                    ? Phase4Localization.Format("rewards.claim", game.ClaimableRewardSlot)
                    : Phase4Localization.Get("rewards.nothing");
                UiTheme.SetEnabled(claim, canClaim);
                back.text = Phase4Localization.Get("common.back");
            };
            return screen;
        }

        private static string RewardKey(Phase3RewardStatus status)
        {
            switch (status)
            {
                case Phase3RewardStatus.Claimed:
                    return "rewards.status.claimed";
                case Phase3RewardStatus.Available:
                    return "rewards.status.available";
                case Phase3RewardStatus.Forfeited:
                    return "rewards.status.forfeited";
                default:
                    return "rewards.status.upcoming";
            }
        }

        private string HealthText()
        {
            return Phase4Localization.Get("health." + Mathf.Clamp(game.PlantHealth, 0, 3));
        }

        private string GrowthText()
        {
            return game.PlantFullyGrown
                ? Phase4Localization.Get("hud.growthComplete")
                : Phase4Localization.Format("hud.growth", game.GrowthProgressInStage, game.GrowthPointsToNextStage);
        }

        private float GrowthFraction()
        {
            if (game.PlantFullyGrown)
                return 1f;
            return game.GrowthPointsToNextStage > 0 ? (float)game.GrowthProgressInStage / game.GrowthPointsToNextStage : 0f;
        }

        private string WateringStateText()
        {
            switch (game.DailyState)
            {
                case Phase4DailyState.Done:
                    return Phase4Localization.Get("hud.watering.done");
                case Phase4DailyState.Unavailable:
                    return Phase4Localization.Get("hud.watering.unavailable");
                case Phase4DailyState.PreviousRunGrace:
                    return Phase4Localization.Get("hud.watering.grace");
                default:
                    return Phase4Localization.Get("hud.watering.available");
            }
        }

        private string HomeStatusText()
        {
            string health = HealthText();
            switch (game.DailyState)
            {
                case Phase4DailyState.Done:
                    return Phase4Localization.Get("home.done");
                case Phase4DailyState.Unavailable:
                    return Phase4Localization.Get("home.unavailable");
            }

            if (game.PlantHealth == 0)
                return Phase4Localization.Get("home.wilted");
            if (game.PlantHealth < Phase2PlantTuning.MaximumHealth)
                return Phase4Localization.Get("home.thirsty") + "\n" + health;
            return health;
        }

        private static string FormatWater(int milliWater)
        {
            return (milliWater / (float)Phase1Tuning.WaterScale).ToString("0.0");
        }

        // Translucent sheet: the animated river world shows through the top, solid kraft at the bottom.
        private VisualElement NewSheet()
        {
            VisualElement screen = NewScreen();
            screen.style.backgroundColor = Color.clear;
            screen.style.backgroundImage = UiTheme.ScrimBackground();
            return screen;
        }

        private VisualElement NewScreen()
        {
            var screen = new VisualElement();
            screen.pickingMode = PickingMode.Ignore;
            screen.style.position = Position.Absolute;
            screen.style.left = 0f;
            screen.style.right = 0f;
            screen.style.top = 0f;
            screen.style.bottom = 0f;
            screen.style.paddingLeft = 16f;
            screen.style.paddingRight = 16f;
            screen.style.backgroundColor = new Color(Kraft.r, Kraft.g, Kraft.b, 0.97f);
            return screen;
        }

        private static VisualElement NewCard(Color accent)
        {
            return UiTheme.Panel(accent);
        }

        private static VisualElement NewBox(Color color, float width, float height)
        {
            var box = new VisualElement();
            box.pickingMode = PickingMode.Ignore;
            box.style.position = Position.Absolute;
            box.style.width = width;
            box.style.height = height;
            box.style.backgroundColor = color;
            return box;
        }

        private static VisualElement NewProgressBar(out VisualElement fill, Color fillColor)
        {
            var bar = new VisualElement();
            bar.pickingMode = PickingMode.Ignore;
            bar.style.height = 16f;
            bar.style.marginTop = 4f;
            bar.style.backgroundColor = Hex("#D9CDB2");
            Round(bar, 8f);
            fill = new VisualElement();
            fill.pickingMode = PickingMode.Ignore;
            fill.style.height = Length.Percent(100f);
            fill.style.backgroundColor = fillColor;
            Round(fill, 8f);
            UiMotion.SetTransition(fill, UiMotion.Normal, "width", "background-color");
            ProgressFills.Add(fill);
            bar.Add(fill);
            return bar;
        }

        private static void SetFill(VisualElement fill, float fraction)
        {
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        }

        private Label MakeLabel(string text, float size, bool bold, Color color, Font font = null)
        {
            var label = new Label(text);
            label.pickingMode = PickingMode.Ignore;
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            Font chosen = font ?? (bold ? boldFont : null);
            if (chosen != null)
                label.style.unityFontDefinition = FontDefinition.FromFont(chosen);
            return label;
        }

        private Button MakeButton(Color background, Color textColor, float size, Action onClick)
        {
            Button button = NewButtonBase(size, onClick);
            UiTheme.StyleInteractive(button, background, textColor);
            return button;
        }

        // Quiet secondary action: cream fill, indigo outline.
        private Button MakeSecondaryButton(float size, Action onClick)
        {
            Button button = NewButtonBase(size, onClick);
            UiTheme.StyleInteractive(button, () => Cream, () => Indigo, 2f, () => Indigo);
            return button;
        }

        private Button NewButtonBase(float size, Action onClick)
        {
            Button button = onClick != null ? new Button(onClick) : new Button();
            button.style.fontSize = size;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (boldFont != null)
                button.style.unityFontDefinition = FontDefinition.FromFont(boldFont);
            return button;
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement element, float width, Color color)
        {
            element.style.borderTopWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderTopColor = color;
            element.style.borderRightColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
        }

        private static Color Hex(string html)
        {
            ColorUtility.TryParseHtmlString(html, out Color color);
            return color;
        }
    }
}
