using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GangaKiBagiya
{
    public enum Phase1AudioCue
    {
        WaterCollected,
        ObstacleHit,
        GoalSecured,
        Banking,
        Watering,
        Growth
    }

    public sealed class Phase1GameController : MonoBehaviour
    {
        private static readonly string[] ObstacleAssetNames =
        {
            "boat", "croc", "dirt", "diya", "dolphine", "fish 1", "fish 2", "flower", "plastic", "pollution"
        };
        [SerializeField] private Phase1Tuning tuning = new Phase1Tuning();
        [SerializeField] private Phase2PlantTuning plantTuning = new Phase2PlantTuning();
        [SerializeField] private Sprite[] backgroundSprites = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] riverLaneSprites = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] obstacleSprites = Array.Empty<Sprite>();
        [SerializeField] private GameObject[] backgroundPrefabs = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] riverLanePrefabs = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] obstaclePrefabs = Array.Empty<GameObject>();
        [SerializeField] private GameObject matkaPrefab;
        [SerializeField] private RuntimeAnimatorController[] backgroundControllers = Array.Empty<RuntimeAnimatorController>();
        [SerializeField] private RuntimeAnimatorController[] riverLaneControllers = Array.Empty<RuntimeAnimatorController>();
        [SerializeField] private RuntimeAnimatorController[] obstacleControllers = Array.Empty<RuntimeAnimatorController>();
        [SerializeField] private RuntimeAnimatorController matkaController;

        public event Action<Phase1AudioCue> AudioCue;

        private sealed class ObstacleView
        {
            public GameObject root;
            public BoxCollider2D trigger;
            public SpriteRenderer spriteRenderer;
            public GameObject modelInstance;
            public int currentPrefabIndex = -1;
        }

        private readonly Dictionary<int, ObstacleView> activeObstacleViews = new Dictionary<int, ObstacleView>();
        private readonly List<ObstacleView> obstaclePool = new List<ObstacleView>(16);
        private readonly HashSet<int> visibleObstacleIds = new HashSet<int>();
        private Phase1RunSimulation simulation;
        private Phase2PlantProgression plantProgression;
        private Phase3RewardCalendar rewardCalendar;
        private Phase3LocalSaveStore saveStore;
        private DateTime simulationDay;
        private DateTime lastFinalizedLocalDay;
        private DateTime profileCreatedDay;
        private TimeZoneInfo profileTimeZone;
        private string profileTimeZoneId;
        private string localProfileId;
        private long writeSequence;
        private long lastJournalSequence;
        private readonly List<Phase3JournalEvent> pendingJournalEvents = new List<Phase3JournalEvent>();
        private bool resultProcessed;
        private bool wateringApplied;
        private int growthPointsAwarded;
        private string runId;
        private Transform worldRoot;
        private GameObject collectorMarker;
        private SpriteRenderer collectorMarkerSprite;
        private int heldLane = -1;
        private bool pointerHeld;
        private bool bankRequested;
        private float tickAccumulator;
        private Camera gameCamera;
        private string setupWarning;
        private string persistenceWarning;
        private bool persistenceReadOnly;
        private bool applicationSuspended;
        private bool skipNextDeltaFrame;
        private int nextCheckpointTick;
        private Phase4Ui ui;
        private int stageBeforeWatering;

        private static readonly float[] LaneWorldY = { 1.25f, 0f, -1.25f };

        // Depth bands, far to near: sky 0.35..0.45, river, bank, hazards, collector. Models are
        // 0.05 deep, so each band is wider than any mesh; hazards further along draw in front.
        private const float RiverZ = 0.60f;
        private const float BankZ = 0.20f;
        private const float CollectorZ = -1.20f;
        private static float HazardZ(float progress) { return -0.10f - 0.60f * Mathf.Clamp01(progress); }

        // Returns true when a screen-space point (origin top-left) is over an interactive UI element.
        public Func<Vector2, bool> PointerBlocker { get; set; }

        public string SetupWarning => setupWarning;
        public string PersistenceWarning => persistenceWarning;
        public bool IsReady => simulation != null && string.IsNullOrEmpty(setupWarning);
        public Phase1RunStatus RunStatus => simulation != null ? simulation.Status : Phase1RunStatus.Ready;
        public bool HasResult => simulation != null && simulation.HasResult;
        public Phase1RunResult Result => simulation.Result;
        public bool GoalSecured => simulation != null && simulation.GoalSecured;
        public float PotWater => simulation != null ? simulation.CollectedMilliWater / (float)Phase1Tuning.WaterScale : 0f;
        public int DailyGoalUnits => simulation != null ? simulation.DailyGoalUnits : 0;
        public int PlantStage => plantProgression != null ? plantProgression.Stage : 1;
        public int PlantHealth => plantProgression != null ? plantProgression.Health : Phase2PlantTuning.MaximumHealth;
        public int GrowthProgressInStage => plantProgression != null ? plantProgression.GrowthProgressInStage : 0;
        public int GrowthPointsToNextStage => plantProgression != null ? plantProgression.GrowthPointsToNextStage : 0;
        public bool PlantFullyGrown => PlantStage == Phase2PlantTuning.StageCount;
        public bool WateringApplied => wateringApplied;
        public int GrowthPointsAwarded => growthPointsAwarded;
        public bool StageAdvancedByWatering => wateringApplied && plantProgression.Stage > stageBeforeWatering;
        public int LocalDayOfYear => ProfileNow().DayOfYear;

        public Phase4DailyState DailyState
        {
            get
            {
                if (simulation == null)
                    return Phase4DailyState.Unavailable;
                DateTime now = ProfileNow();
                if (simulation.Status == Phase1RunStatus.Running && simulationDay < now.Date)
                    return Phase4DailyState.PreviousRunGrace;
                if (plantProgression.HasWateredOn(now.Date))
                    return Phase4DailyState.Done;
                return plantProgression.CanWaterOn(now.Date) ? Phase4DailyState.Available : Phase4DailyState.Unavailable;
            }
        }

        public bool CanStartRun =>
            simulation != null && (simulation.Status == Phase1RunStatus.Ready || simulation.Status == Phase1RunStatus.Failed) &&
            ProfileNow().Date == simulationDay && plantProgression.CanWaterOn(simulationDay);

        public bool HasClaimableReward => rewardCalendar != null && TryGetClaimableRewardDay(ProfileNow(), out _);

        public int ClaimableRewardSlot =>
            rewardCalendar != null && TryGetClaimableRewardDay(ProfileNow(), out DateTime day) ? rewardCalendar.GetSlotNumber(day) : 0;

        public Phase4RewardSlot[] GetRewardSlots()
        {
            if (rewardCalendar == null)
                return Array.Empty<Phase4RewardSlot>();
            DateTime now = ProfileNow();
            DateTime[] days = rewardCalendar.GetCurrentCycle(now.Date);
            var slots = new Phase4RewardSlot[days.Length];
            for (int i = 0; i < days.Length; i++)
            {
                slots[i] = new Phase4RewardSlot
                {
                    slotNumber = rewardCalendar.GetSlotNumber(days[i]),
                    status = rewardCalendar.GetStatus(days[i], now, plantProgression),
                    isToday = days[i].Date == now.Date
                };
            }
            return slots;
        }

        public bool TryStartRun()
        {
            if (!CanStartRun)
                return false;
            StartRun();
            return simulation.Status == Phase1RunStatus.Running;
        }

        public void RequestBank()
        {
            if (simulation != null && simulation.Status == Phase1RunStatus.Running && simulation.GoalSecured)
                bankRequested = true;
        }

        public bool TryClaimReward()
        {
            return rewardCalendar != null && TryClaimAvailableReward();
        }

        private void Awake()
        {
            ui = gameObject.AddComponent<Phase4Ui>();
            ui.Initialize(this);

            if (tuning == null)
                tuning = new Phase1Tuning();
            gameCamera = Camera.main;
            if (gameCamera == null)
                gameCamera = FindAnyObjectByType<Camera>();
            if (gameCamera != null)
            {
                gameCamera.orthographic = true;
                // Fit the whole logical lane; taller-than-9:16 screens gain height instead of cropping.
                FitCamera();
                gameCamera.nearClipPlane = 0.1f;
                gameCamera.farClipPlane = 20f;
                gameCamera.backgroundColor = new Color(0.74f, 0.88f, 0.91f, 1f);
            }

            if (tuning == null || !tuning.IsUsable(out setupWarning))
            {
                if (string.IsNullOrEmpty(setupWarning))
                    setupWarning = "Phase 1 tuning is missing. Use Ganga Ki Bagiya > Phase 1 > Build Playable Scene.";
                enabled = false;
                return;
            }

            if (plantTuning == null)
                plantTuning = new Phase2PlantTuning();
            if (!plantTuning.IsUsable(out setupWarning))
            {
                enabled = false;
                return;
            }

            saveStore = new Phase3LocalSaveStore(Application.persistentDataPath);
            InitializeLocalProgress();
            BuildWorld();
        }

        private void Update()
        {
            if (simulation == null || applicationSuspended)
                return;

            DateTime now = ProfileNow();
            if (simulation.HasResult && !resultProcessed)
                ApplyRunProgression();
            ReconcileMissedDays(now);
            RefreshDailyChallenge(now);
            RefreshLayout();
            ReadPointer();
            if (simulation.Status == Phase1RunStatus.Running)
            {
                float stepSeconds = 1f / tuning.ticksPerSecond;
                float frameDelta = skipNextDeltaFrame ? 0f : Time.unscaledDeltaTime;
                skipNextDeltaFrame = false;
                tickAccumulator += Mathf.Min(frameDelta, 0.25f);
                while (tickAccumulator >= stepSeconds && simulation.Status == Phase1RunStatus.Running)
                {
                    Phase1TickEvents events = simulation.Step(heldLane, bankRequested);
                    bankRequested = false;
                    HandleEvents(events);
                    tickAccumulator -= stepSeconds;
                }
                if (simulation.Status == Phase1RunStatus.Running && simulation.ElapsedTicks >= nextCheckpointTick)
                {
                    PersistState();
                    nextCheckpointTick = simulation.ElapsedTicks + CheckpointIntervalTicks();
                }
            }

            if (simulation.HasResult && !resultProcessed)
                ApplyRunProgression();
            UpdateObstacleViews();
            UpdateCollectorMarker();
        }

        private void RefreshDailyChallenge(DateTime now)
        {
            DateTime today = now.Date;
            if (today <= simulationDay)
                return;

            DateTime previousDayDeadline = simulationDay.AddDays(1).AddMinutes(15);
            if (simulation.Status == Phase1RunStatus.Running && now < previousDayDeadline)
                return;

            CreateSimulationForDay(today);
            PersistState();
        }

        private void ReconcileMissedDays(DateTime now)
        {
            DateTime finalizedThrough = now.TimeOfDay >= TimeSpan.FromMinutes(15)
                ? now.Date.AddDays(-1)
                : now.Date.AddDays(-2);
            FinalizeMissedDaysThrough(finalizedThrough);
        }

        private void FinalizeMissedDaysThrough(DateTime day)
        {
            bool changed = false;
            while (lastFinalizedLocalDay < day.Date)
            {
                lastFinalizedLocalDay = lastFinalizedLocalDay.AddDays(1);
                plantProgression.ApplyMissedDay(lastFinalizedLocalDay);
                rewardCalendar.Forfeit(lastFinalizedLocalDay);
                QueueJournalEvent("day-finalized", lastFinalizedLocalDay, string.Empty);
                changed = true;
            }
            if (changed)
                PersistState();
        }

        private void CreateSimulationForDay(DateTime day)
        {
            simulationDay = day.Date;
            simulation = new Phase1RunSimulation(tuning, plantProgression.Stage);
            resultProcessed = false;
            wateringApplied = false;
            growthPointsAwarded = 0;
            runId = string.Empty;
            heldLane = -1;
            pointerHeld = false;
            bankRequested = false;
            tickAccumulator = 0f;
            nextCheckpointTick = CheckpointIntervalTicks();
            if (worldRoot != null)
                UpdateObstacleViews();
        }

        private void InitializeLocalProgress()
        {
            profileTimeZone = TimeZoneInfo.Local;
            profileTimeZoneId = profileTimeZone.Id;

            bool loaded = saveStore.TryLoad(out Phase3SaveData saved, out List<Phase3JournalEvent> journalEvents,
                out bool recoveredBackup, out bool foundInvalidFiles, out string loadWarning);
            persistenceWarning = loadWarning;
            if (!loaded)
            {
                CreateFreshProgress(ProfileNow().Date);
                persistenceReadOnly = foundInvalidFiles || saveStore.JournalDamaged;
                if (!persistenceReadOnly)
                    PersistState();
            }
            else
            {
                localProfileId = saved.localProfileId;
                profileTimeZoneId = saved.profileTimeZoneId;
                writeSequence = saved.writeSequence;
                lastJournalSequence = saved.lastJournalSequence;
                if (!TryResolveProfileTimeZone(profileTimeZoneId, out profileTimeZone))
                    persistenceWarning = "Saved timezone is unavailable; using the device timezone for display.";
                if (!Phase3SaveData.TryParseDay(saved.profileCreatedDay, out profileCreatedDay) ||
                    !Phase3SaveData.TryParseDay(saved.lastFinalizedDay, out lastFinalizedLocalDay) ||
                    !Phase3SaveData.TryParseDay(saved.simulationDay, out simulationDay))
                {
                    persistenceReadOnly = true;
                    persistenceWarning = "Saved dates are invalid; the original save was left untouched.";
                    CreateFreshProgress(ProfileNow().Date);
                }
                else
                {
                    plantProgression = new Phase2PlantProgression(plantTuning);
                    rewardCalendar = new Phase3RewardCalendar(profileCreatedDay);
                    bool restored = plantProgression.TryRestoreSnapshot(saved.progression) &&
                                    rewardCalendar.TryRestoreSnapshot(saved.rewards);
                    simulation = new Phase1RunSimulation(tuning, saved.run.stage);
                    restored = simulation.TryRestoreSnapshot(saved.run, out string runWarning) && restored;
                    if (!restored)
                    {
                        persistenceReadOnly = true;
                        persistenceWarning = "Saved progress failed validation; the original save was left untouched.";
                        CreateFreshProgress(ProfileNow().Date);
                    }
                    else
                    {
                        runId = saved.runId ?? string.Empty;
                        resultProcessed = saved.resultProcessed;
                        wateringApplied = saved.wateringApplied;
                        growthPointsAwarded = saved.growthPointsAwarded;
                        bankRequested = saved.pendingBankRequested;
                        tickAccumulator = Mathf.Clamp(saved.tickAccumulatorSeconds, 0f, 1f / tuning.ticksPerSecond);
                        nextCheckpointTick = simulation.ElapsedTicks + CheckpointIntervalTicks();
                        lastJournalSequence = ReplayJournalEvents(journalEvents, lastJournalSequence);
                        if (saveStore.JournalDamaged)
                            persistenceReadOnly = true;
                        if (recoveredBackup)
                            persistenceWarning = "Recovered the last good local save.";
                    }
                }
            }

            DateTime now = ProfileNow();
            ReconcileMissedDays(now);
            RefreshDailyChallenge(now);
        }

        private void CreateFreshProgress(DateTime today)
        {
            profileCreatedDay = today.Date;
            lastFinalizedLocalDay = profileCreatedDay.AddDays(-1);
            localProfileId = Guid.NewGuid().ToString("N");
            plantProgression = new Phase2PlantProgression(plantTuning);
            rewardCalendar = new Phase3RewardCalendar(profileCreatedDay);
            CreateSimulationForDay(today);
        }

        private long ReplayJournalEvents(List<Phase3JournalEvent> events, long sequence)
        {
            bool changed = false;
            for (int i = 0; i < events.Count; i++)
            {
                Phase3JournalEvent item = events[i];
                if (item.sequence <= sequence)
                    continue;
                if (!string.Equals(item.profileId, localProfileId, StringComparison.Ordinal) ||
                    !Phase3SaveData.TryParseDay(item.day, out DateTime day))
                {
                    persistenceReadOnly = true;
                    persistenceWarning = "A journal entry does not match this profile; it was not applied.";
                    break;
                }

                bool applied;
                if (item.eventType == "watering-completed")
                {
                    var result = new Phase1RunResult
                    {
                        reason = (Phase1RunEndReason)item.endReason,
                        collectedMilliWater = item.collectedMilliWater,
                        retainedMilliWater = item.retainedMilliWater,
                        spilledMilliWater = item.spilledMilliWater,
                        dailyGoalUnits = item.dailyGoalUnits,
                        bonusGrowthPoints = item.bonusGrowthPoints
                    };
                    applied = plantProgression.TryCompleteWatering(day, result) || plantProgression.HasWateredOn(day);
                }
                else if (item.eventType == "day-finalized")
                {
                    plantProgression.ApplyMissedDay(day);
                    rewardCalendar.Forfeit(day);
                    if (day > lastFinalizedLocalDay)
                        lastFinalizedLocalDay = day;
                    applied = true;
                }
                else if (item.eventType == "reward-claimed")
                {
                    applied = rewardCalendar.ApplyClaim(day, plantProgression) ||
                              rewardCalendar.GetStatus(day, ProfileNow(), plantProgression) == Phase3RewardStatus.Claimed;
                }
                else
                {
                    applied = false;
                }

                if (!applied)
                {
                    persistenceReadOnly = true;
                    persistenceWarning = "A journal entry could not be reconciled; it was not acknowledged.";
                    break;
                }
                sequence = item.sequence;
                changed = true;
            }

            if (changed && !persistenceReadOnly)
                PersistState();
            return sequence;
        }

        private void QueueJournalEvent(string eventType, DateTime day, string eventRunId, Phase1RunResult result = default)
        {
            string dayKey = Phase3SaveData.FormatDay(day);
            string eventId = localProfileId + ":" + eventType + ":" + dayKey;
            if (eventType == "watering-completed")
                eventId += ":" + eventRunId;
            for (int i = 0; i < pendingJournalEvents.Count; i++)
                if (pendingJournalEvents[i].eventId == eventId)
                    return;

            pendingJournalEvents.Add(new Phase3JournalEvent
            {
                eventId = eventId,
                profileId = localProfileId,
                eventType = eventType,
                day = dayKey,
                runId = eventRunId,
                endReason = (int)result.reason,
                collectedMilliWater = result.collectedMilliWater,
                retainedMilliWater = result.retainedMilliWater,
                spilledMilliWater = result.spilledMilliWater,
                dailyGoalUnits = result.dailyGoalUnits,
                bonusGrowthPoints = result.bonusGrowthPoints
            });
        }

        private void PersistState()
        {
            if (saveStore == null || persistenceReadOnly || simulation == null || plantProgression == null || rewardCalendar == null)
                return;

            var saved = new Phase3SaveData
            {
                writeSequence = writeSequence,
                lastJournalSequence = lastJournalSequence,
                localProfileId = localProfileId,
                profileTimeZoneId = profileTimeZoneId,
                profileCreatedDay = Phase3SaveData.FormatDay(profileCreatedDay),
                lastFinalizedDay = Phase3SaveData.FormatDay(lastFinalizedLocalDay),
                progression = plantProgression.CaptureSnapshot(),
                rewards = rewardCalendar.CaptureSnapshot(),
                simulationDay = Phase3SaveData.FormatDay(simulationDay),
                runId = runId ?? string.Empty,
                run = simulation.CaptureSnapshot(),
                resultProcessed = resultProcessed,
                wateringApplied = wateringApplied,
                growthPointsAwarded = growthPointsAwarded,
                tickAccumulatorSeconds = Mathf.Max(0f, tickAccumulator),
                pendingBankRequested = bankRequested
            };
            bool persisted = saveStore.Save(saved, pendingJournalEvents, out string error);
            writeSequence = saved.writeSequence;
            lastJournalSequence = saved.lastJournalSequence;
            if (persisted)
            {
                pendingJournalEvents.Clear();
                persistenceWarning = string.Empty;
            }
            else
            {
                persistenceWarning = "Progress could not be saved: " + error;
                Debug.LogWarning(persistenceWarning);
                if (saveStore.JournalDamaged)
                    persistenceReadOnly = true;
            }
        }

        private int CheckpointIntervalTicks()
        {
            return Mathf.Max(1, Mathf.RoundToInt(tuning.ticksPerSecond * 2f));
        }

        private bool TryResolveProfileTimeZone(string id, out TimeZoneInfo zone)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                zone = TimeZoneInfo.Local;
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                zone = TimeZoneInfo.Local;
                return false;
            }
        }

        private DateTime ProfileNow()
        {
            TimeZoneInfo zone = profileTimeZone ?? TimeZoneInfo.Local;
            // shortcut: Until Phase 4 provides server time, local dates trust the device UTC clock.
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        }

        private bool userPaused;

        public bool IsPaused => userPaused;

        /// <summary>Player-requested pause: same state-saving suspend as backgrounding, but focus changes cannot resume it.</summary>
        public void SetPaused(bool paused)
        {
            if (userPaused == paused)
                return;
            userPaused = paused;
            if (paused)
                SuspendSimulation();
            else
                ResumeSimulation();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                SuspendSimulation();
            else if (!userPaused)
                ResumeSimulation();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                if (!userPaused)
                    ResumeSimulation();
            }
            else
                SuspendSimulation();
        }

        private void OnApplicationQuit()
        {
            SuspendSimulation();
        }

        private void OnDisable()
        {
            if (saveStore != null)
                PersistState();
        }

        private void SuspendSimulation()
        {
            if (applicationSuspended)
                return;
            applicationSuspended = true;
            heldLane = -1;
            pointerHeld = false;
            simulation?.ReleaseInput();
            PersistState();
        }

        private void ResumeSimulation()
        {
            if (!applicationSuspended)
                return;
            applicationSuspended = false;
            skipNextDeltaFrame = true;
            heldLane = -1;
            pointerHeld = false;
            simulation?.ReleaseInput();
            DateTime now = ProfileNow();
            ReconcileMissedDays(now);
            RefreshDailyChallenge(now);
            PersistState();
        }

        private void ReadPointer()
        {
            bool pressed = false;
            bool down = false;
            bool released = false;
            Vector2 screenPosition = default;

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && (touchscreen.primaryTouch.press.isPressed || touchscreen.primaryTouch.press.wasReleasedThisFrame))
            {
                var touch = touchscreen.primaryTouch;
                pressed = touch.press.wasPressedThisFrame;
                down = touch.press.isPressed;
                released = touch.press.wasReleasedThisFrame;
                screenPosition = touch.position.ReadValue();
            }
            else if (Mouse.current != null)
            {
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                down = Mouse.current.leftButton.isPressed;
                released = Mouse.current.leftButton.wasReleasedThisFrame;
                screenPosition = Mouse.current.position.ReadValue();
            }

            Vector2 guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            if (pressed)
            {
                if (simulation.Status != Phase1RunStatus.Running || (PointerBlocker != null && PointerBlocker(guiPosition)))
                    return;

                pointerHeld = LaneAreaRect().Contains(guiPosition);
                heldLane = pointerHeld ? LaneAtScreenY(guiPosition.y) : -1;
            }

            if (down && pointerHeld)
                heldLane = LaneAreaRect().Contains(guiPosition) ? LaneAtScreenY(guiPosition.y) : -1;

            if (released)
            {
                pointerHeld = false;
                heldLane = -1;
            }
        }

        private void StartRun()
        {
            DateTime today = ProfileNow().Date;
            if (today != simulationDay)
                return;

            if (!plantProgression.CanWaterOn(simulationDay))
                return;

            simulation.Start();
            runId = Guid.NewGuid().ToString("N");
            heldLane = -1;
            pointerHeld = false;
            bankRequested = false;
            tickAccumulator = 0f;
            resultProcessed = false;
            wateringApplied = false;
            growthPointsAwarded = 0;
            nextCheckpointTick = simulation.ElapsedTicks + CheckpointIntervalTicks();
            UpdateObstacleViews();
            PersistState();
        }

        private void ApplyRunProgression()
        {
            resultProcessed = true;
            Phase1RunResult result = simulation.Result;
            if (result.reason == Phase1RunEndReason.HitBeforeGoal)
            {
                PersistState();
                return;
            }

            int previousGrowthPoints = plantProgression.GrowthPoints;
            int previousStage = plantProgression.Stage;
            stageBeforeWatering = previousStage;
            wateringApplied = plantProgression.TryCompleteWatering(simulationDay, result);
            if (!wateringApplied)
            {
                PersistState();
                return;
            }

            growthPointsAwarded = plantProgression.GrowthPoints - previousGrowthPoints;
            QueueJournalEvent("watering-completed", simulationDay, runId, result);
            AudioCue?.Invoke(Phase1AudioCue.Watering);
            if (plantProgression.Stage > previousStage)
                AudioCue?.Invoke(Phase1AudioCue.Growth);
            PersistState();
        }

        private bool TryClaimAvailableReward()
        {
            if (simulation.Status == Phase1RunStatus.Running || !TryGetClaimableRewardDay(ProfileNow(), out DateTime day) ||
                !rewardCalendar.TryClaim(day, ProfileNow(), plantProgression))
                return false;

            QueueJournalEvent("reward-claimed", day, string.Empty);
            PersistState();
            return true;
        }

        private bool TryGetClaimableRewardDay(DateTime now, out DateTime day)
        {
            DateTime[] candidates = { simulationDay.Date, now.Date, now.Date.AddDays(-1) };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (rewardCalendar.GetStatus(candidates[i], now, plantProgression) != Phase3RewardStatus.Available)
                    continue;
                day = candidates[i];
                return true;
            }
            day = default;
            return false;
        }

        private void HandleEvents(Phase1TickEvents events)
        {
            if ((events & Phase1TickEvents.WaterCollected) != 0)
            {
                AudioCue?.Invoke(Phase1AudioCue.WaterCollected);
                TriggerCollectorCollect();
            }
            if ((events & Phase1TickEvents.GoalSecured) != 0)
            {
                AudioCue?.Invoke(Phase1AudioCue.GoalSecured);
                TriggerCollectorCollect();
            }
            if ((events & Phase1TickEvents.ObstacleHit) != 0)
            {
                AudioCue?.Invoke(Phase1AudioCue.ObstacleHit);
                TriggerHitEffects();
            }
            if ((events & Phase1TickEvents.Banked) != 0)
            {
                AudioCue?.Invoke(Phase1AudioCue.Banking);
                TriggerCollectorCollect();
            }
        }

        private void BuildWorld()
        {
            if (gameCamera == null)
            {
                setupWarning = "A camera is required for the Phase 1 scene.";
                return;
            }

            GameObject root = new GameObject("Phase 1 World");
            worldRoot = root.transform;
            float width = gameCamera.orthographicSize * 2f * gameCamera.aspect;

            // Add soft directional light for 3D depth and paper-craft shading
            Light existingLight = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (existingLight == null)
            {
                GameObject lightGo = new GameObject("Phase 1 Directional Light");
                lightGo.transform.SetParent(worldRoot, false);
                lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.2f;
                l.color = new Color(1f, 0.98f, 0.94f);
            }

            // Positions and sizes are set by LayoutWorld once the HUD and screen are measured.
            AddSky("Sun", 0, -0.32f, 0.8f, 0f, 0);
            AddSky("Cloud Back 2", 1, -0.12f, 1.0f, 0.15f, 1);
            AddSky("Cloud Back 1", 5, 0.28f, 1.0f, 0.18f, 1);
            AddSky("Cloud 2", 3, -0.04f, 0.6f, 0.25f, 2);
            AddSky("Cloud 3", 4, 0.20f, 0.5f, 0.28f, 2);
            AddSky("Cloud 1", 2, 0.38f, 0.6f, 0.25f, 2);

            for (int lane = 0; lane < Phase1Tuning.LaneCount; lane++)
                AddRiverLane(lane, GetPrefab(riverLanePrefabs, lane), GetController(riverLaneControllers, lane), GetSprite(riverLaneSprites, lane), width);

            // Riverbank scenery sits in the strip below the lanes when the screen has room for it.
            int[] sceneryIndices = { 1, 2, 5, 6, 7 };
            for (int i = 0; i < sceneryIndices.Length; i++)
            {
                int sIdx = sceneryIndices[i];
                GameObject go = AddModelOrSprite("Riverbank " + ObstacleAssetNames[sIdx], GetPrefab(obstaclePrefabs, sIdx), GetController(obstacleControllers, sIdx), GetSprite(obstacleSprites, sIdx),
                    Vector2.zero, 0.45f, 12, 2);
                if (go != null)
                    bankItems.Add(go.transform);
            }

            SetupCollectorMarker();
            PrewarmObstaclePool(16);
            LayoutWorld(DefaultHudFraction);
        }

        private const float DefaultHudFraction = 0.17f;
        private const float BottomUiFraction = 0.15f;
        private const float LaneBlockHalf = 0.625f;

        private struct SkyItem
        {
            public Transform transform;
            public float baseWidth, baseHeight, x, heightFraction, drift;
        }

        private readonly List<SkyItem> skyItems = new List<SkyItem>();
        private readonly List<Transform> bankItems = new List<Transform>();
        private readonly List<Transform> riverItems = new List<Transform>();
        private float laidOutHud = -1f;
        private int laidOutWidth, laidOutHeight;

        private void AddSky(string name, int index, float x, float heightFraction, float drift, int order)
        {
            GameObject go = AddModelOrSprite(name, GetPrefab(backgroundPrefabs, index), GetController(backgroundControllers, index), GetSprite(backgroundSprites, index), Vector2.zero, 1f, order, 2);
            if (go == null)
                return;
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            skyItems.Add(new SkyItem { transform = go.transform, baseWidth = bounds.size.x / go.transform.localScale.x, baseHeight = bounds.size.y / go.transform.localScale.y, x = x, heightFraction = heightFraction, drift = drift });
        }

        private void FitCamera()
        {
            gameCamera.orthographicSize = Mathf.Max(3.2f, tuning.logicalLaneLength / (2f * Mathf.Max(0.1f, gameCamera.aspect)));
        }

        // Stacks HUD, sky band, three lanes, bank strip and bottom controls into the visible height.
        private void LayoutWorld(float hudFraction)
        {
            if (gameCamera == null || worldRoot == null)
                return;
            laidOutHud = hudFraction;
            laidOutWidth = Screen.width;
            laidOutHeight = Screen.height;
            float ortho = gameCamera.orthographicSize;
            float height = ortho * 2f;
            float width = height * gameCamera.aspect;
            float hudBottom = ortho - hudFraction * height;
            float lanesBlock = LaneWorldY.Length * 1.25f;
            float floor = -ortho + BottomUiFraction * height;
            float band = Mathf.Clamp(hudBottom - floor - lanesBlock, 0.5f, 1.2f);
            float laneTop = hudBottom - band;
            for (int i = 0; i < LaneWorldY.Length; i++)
                LaneWorldY[i] = laneTop - LaneBlockHalf - 1.25f * i;

            foreach (Transform river in riverItems)
            {
                int lane = riverItems.IndexOf(river);
                river.position = new Vector3(0f, LaneWorldY[lane], river.position.z);
            }

            float bandCentre = laneTop + band * 0.5f;
            foreach (SkyItem item in skyItems)
            {
                float scale = item.heightFraction * band / Mathf.Max(0.01f, item.baseHeight);
                float itemWidth = item.baseWidth * scale + item.drift * 2f;
                float limit = Mathf.Max(0f, (width - itemWidth) * 0.5f);
                Vector3 position = item.transform.position;
                position.x = Mathf.Clamp(item.x * width, -limit, limit);
                position.y = bandCentre;
                item.transform.localScale = Vector3.one * scale;
                item.transform.position = position;
            }

            float free = LaneWorldY[LaneWorldY.Length - 1] - LaneBlockHalf - floor;
            bool room = free >= 0.45f;
            for (int i = 0; i < bankItems.Count; i++)
            {
                bankItems[i].gameObject.SetActive(room);
                if (!room)
                    continue;
                float h = Mathf.Min(0.45f, free * 0.8f);
                bankItems[i].localScale = Vector3.one * h;
                bankItems[i].position = new Vector3((-0.4f + 0.2f * i) * width, floor + free * 0.5f, BankZ);
            }
        }

        // Re-lays out when the screen changes or the HUD card's measured height settles.
        private void RefreshLayout()
        {
            if (gameCamera == null)
                return;
            float hud = ui != null && ui.HudBottomFraction > 0f ? ui.HudBottomFraction : DefaultHudFraction;
            if (Screen.width != laidOutWidth || Screen.height != laidOutHeight)
                FitCamera();
            if (Mathf.Abs(hud - laidOutHud) > 0.004f || Screen.width != laidOutWidth || Screen.height != laidOutHeight)
                LayoutWorld(hud);
        }

        private void SetupCollectorMarker()
        {
            if (matkaPrefab != null)
            {
                collectorMarker = Instantiate(matkaPrefab, worldRoot);
                collectorMarker.name = "Matka Collector";
                collectorMarker.transform.position = new Vector3(0f, 0f, CollectorZ);
                collectorMarker.transform.localScale = Vector3.one * 0.65f;
                EnsureAnimator(collectorMarker, matkaController);
            }
            else
            {
                Sprite placeholder = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
                collectorMarkerSprite = AddSprite("Matka Placeholder", placeholder, Vector2.zero, 0.65f, 25, 0.4f);
                collectorMarkerSprite.color = new Color(0.93f, 0.56f, 0.23f, 1f);
                collectorMarker = collectorMarkerSprite.gameObject;
            }

            Rigidbody2D collectorBody = collectorMarker.AddComponent<Rigidbody2D>();
            collectorBody.bodyType = RigidbodyType2D.Kinematic;
            collectorBody.gravityScale = 0f;
            BoxCollider2D collectorTrigger = collectorMarker.AddComponent<BoxCollider2D>();
            collectorTrigger.isTrigger = true;
            collectorTrigger.size = new Vector2(0.65f, 0.65f);
        }

        private void AddRiverLane(int lane, GameObject prefab, RuntimeAnimatorController controller, Sprite sprite, float worldWidth)
        {
            float targetHeight = 1.30f;
            if (prefab != null)
            {
                GameObject riverGo = Instantiate(prefab, worldRoot);
                riverGo.name = "River Lane " + (lane + 1);
                riverGo.transform.position = new Vector3(0f, LaneWorldY[lane], RiverZ);
                riverGo.transform.localScale = new Vector3(worldWidth * 1.15f, targetHeight, 1f);
                EnsureAnimator(riverGo, controller);
                riverItems.Add(riverGo.transform);
                return;
            }

            SpriteRenderer renderer = AddSprite("River Lane " + (lane + 1), sprite,
                new Vector2(0f, LaneWorldY[lane]), 1f, 10, 2);
            if (renderer == null || sprite == null)
                return;
            Vector2 spriteSize = sprite.bounds.size;
            renderer.transform.localScale = new Vector3(worldWidth * 1.15f / spriteSize.x,
                targetHeight / spriteSize.y, 1f);
            riverItems.Add(renderer.transform);
        }

        private GameObject AddModelOrSprite(string name, GameObject prefab, RuntimeAnimatorController controller, Sprite sprite, Vector2 position, float desiredHeight, int order, float alpha)
        {
            if (prefab != null)
            {
                GameObject modelGo = Instantiate(prefab, worldRoot);
                modelGo.name = name;
                modelGo.transform.position = new Vector3(position.x, position.y, order >= 12 ? BankZ : 0.45f - 0.05f * order);
                modelGo.transform.localScale = Vector3.one * desiredHeight;
                EnsureAnimator(modelGo, controller);
                return modelGo;
            }

            SpriteRenderer sr = AddSprite(name, sprite, position, desiredHeight, order, alpha);
            return sr != null ? sr.gameObject : null;
        }

        private SpriteRenderer AddSprite(string name, Sprite sprite, Vector2 position, float desiredHeight, int order, float alpha)
        {
            if (sprite == null)
                return null;
            GameObject go = new GameObject(name);
            go.transform.SetParent(worldRoot, false);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            float scale = desiredHeight / Mathf.Max(0.01f, sprite.bounds.size.y);
            go.transform.localScale = Vector3.one * scale;
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, alpha);
            return renderer;
        }

        private void PrewarmObstaclePool(int count)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject go = new GameObject("Pooled River Obstacle");
                go.transform.SetParent(worldRoot, false);
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = 20;
                renderer.enabled = false;
                Rigidbody2D body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                BoxCollider2D trigger = go.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                go.SetActive(false);

                ObstacleView view = new ObstacleView
                {
                    root = go,
                    trigger = trigger,
                    spriteRenderer = renderer
                };
                obstaclePool.Add(view);
            }
        }

        private void UpdateObstacleViews()
        {
            if (simulation == null || worldRoot == null || gameCamera == null)
                return;

            visibleObstacleIds.Clear();
            float width = tuning.logicalLaneLength;
            IReadOnlyList<Phase1ObstacleState> obstacles = simulation.Obstacles;
            for (int i = 0; i < obstacles.Count; i++)
            {
                Phase1ObstacleState obstacle = obstacles[i];
                visibleObstacleIds.Add(obstacle.id);
                if (!activeObstacleViews.TryGetValue(obstacle.id, out ObstacleView view))
                {
                    view = AcquireObstacleView();
                    if (view == null)
                        continue;
                    activeObstacleViews.Add(obstacle.id, view);

                    GameObject prefab = GetPrefab(obstaclePrefabs, obstacle.spriteIndex);
                    if (prefab != null)
                    {
                        if (view.currentPrefabIndex != obstacle.spriteIndex || view.modelInstance == null)
                        {
                            if (view.modelInstance != null)
                                Destroy(view.modelInstance);
                            view.modelInstance = Instantiate(prefab, view.root.transform);
                            view.modelInstance.transform.localPosition = Vector3.zero;
                            float targetHeight = 0.85f;
                            view.modelInstance.transform.localScale = Vector3.one * targetHeight;
                            view.currentPrefabIndex = obstacle.spriteIndex;
                            EnsureAnimator(view.modelInstance, GetController(obstacleControllers, obstacle.spriteIndex));
                            Animator anim = view.modelInstance.GetComponentInChildren<Animator>();
                            if (anim != null && anim.isActiveAndEnabled)
                                anim.Play(0, -1, UnityEngine.Random.value);
                        }
                        if (view.spriteRenderer != null)
                            view.spriteRenderer.enabled = false;
                        if (view.trigger != null)
                            view.trigger.size = new Vector2(1.05f, 1.05f);
                    }
                    else
                    {
                        if (view.modelInstance != null)
                        {
                            Destroy(view.modelInstance);
                            view.modelInstance = null;
                            view.currentPrefabIndex = -1;
                        }
                        if (view.spriteRenderer != null)
                        {
                            view.spriteRenderer.enabled = true;
                            view.spriteRenderer.sprite = GetSprite(obstacleSprites, obstacle.spriteIndex);
                            if (view.trigger != null && view.spriteRenderer.sprite != null)
                                view.trigger.size = view.spriteRenderer.sprite.bounds.size;
                            float targetHeight = 0.85f;
                            float scale = view.spriteRenderer.sprite == null ? 1f : targetHeight / Mathf.Max(0.01f, view.spriteRenderer.sprite.bounds.size.y);
                            view.root.transform.localScale = Vector3.one * scale;
                        }
                    }
                    view.root.SetActive(true);
                }
                view.root.transform.position = new Vector3(width * (obstacle.progress - 0.5f), LaneWorldY[obstacle.lane], HazardZ(obstacle.progress));
            }

            List<int> expired = null;
            foreach (KeyValuePair<int, ObstacleView> pair in activeObstacleViews)
            {
                if (visibleObstacleIds.Contains(pair.Key))
                    continue;
                if (expired == null)
                    expired = new List<int>();
                expired.Add(pair.Key);
                pair.Value.root.SetActive(false);
                obstaclePool.Add(pair.Value);
            }
            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                    activeObstacleViews.Remove(expired[i]);
            }
        }

        private ObstacleView AcquireObstacleView()
        {
            if (obstaclePool.Count == 0)
                PrewarmObstaclePool(8);
            int last = obstaclePool.Count - 1;
            ObstacleView view = obstaclePool[last];
            obstaclePool.RemoveAt(last);
            return view;
        }

        private void UpdateCollectorMarker()
        {
            if (collectorMarker == null || gameCamera == null)
                return;
            float width = tuning.logicalLaneLength;
            int markerLane = heldLane >= 0 ? heldLane : 1;
            collectorMarker.transform.position = new Vector3(width * (tuning.collectorProgress - 0.5f), LaneWorldY[markerLane], CollectorZ);
            collectorMarker.SetActive(simulation.Status == Phase1RunStatus.Running);
        }

        private Sprite GetSprite(Sprite[] sprites, int index)
        {
            return sprites != null && index >= 0 && index < sprites.Length ? sprites[index] : null;
        }

        private GameObject GetPrefab(GameObject[] prefabs, int index)
        {
            return prefabs != null && index >= 0 && index < prefabs.Length ? prefabs[index] : null;
        }

        private RuntimeAnimatorController GetController(RuntimeAnimatorController[] controllers, int index)
        {
            return controllers != null && index >= 0 && index < controllers.Length ? controllers[index] : null;
        }

        private static void EnsureAnimator(GameObject go, RuntimeAnimatorController controller)
        {
            if (go == null) return;
            Animator animator = go.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = go.AddComponent<Animator>();
            if (controller != null)
                animator.runtimeAnimatorController = controller;
        }

        private void TriggerCollectorCollect()
        {
            if (collectorMarker != null)
            {
                Animator anim = collectorMarker.GetComponentInChildren<Animator>();
                if (anim != null)
                    anim.SetTrigger("Collect");
            }
        }

        private void TriggerHitEffects()
        {
            foreach (KeyValuePair<int, ObstacleView> pair in activeObstacleViews)
            {
                if (pair.Value.modelInstance != null && pair.Value.currentPrefabIndex == 1) // Croc
                {
                    Animator anim = pair.Value.modelInstance.GetComponentInChildren<Animator>();
                    if (anim != null)
                        anim.SetTrigger("Snap");
                }
            }
        }

        // Touch band = the three lanes' world extent projected to the screen (framing varies per device).
        private Rect LaneAreaRect()
        {
            float top = Screen.height - gameCamera.WorldToScreenPoint(new Vector3(0f, LaneWorldY[0] + LaneBlockHalf, 0f)).y;
            float bottom = Screen.height - gameCamera.WorldToScreenPoint(new Vector3(0f, LaneWorldY[LaneWorldY.Length - 1] - LaneBlockHalf, 0f)).y;
            return new Rect(0f, top, Screen.width, bottom - top);
        }

        private int LaneAtScreenY(float y)
        {
            float relative = Mathf.Clamp01((y - LaneAreaRect().y) / LaneAreaRect().height);
            return Mathf.Clamp(Mathf.FloorToInt(relative * Phase1Tuning.LaneCount), 0, Phase1Tuning.LaneCount - 1);
        }
    }
}
