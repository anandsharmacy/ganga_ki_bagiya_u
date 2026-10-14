using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GangaKiBagiya.Tests
{
    public sealed class Phase3PersistenceTests
    {
        private string testDirectory;

        [SetUp]
        public void SetUp()
        {
            testDirectory = Path.Combine(Application.temporaryCachePath, "GangaKiBagiya-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, true);
        }

        [Test]
        public void SaveLoadsAndCorruptPrimaryRecoversLastGoodBackup()
        {
            var store = new Phase3LocalSaveStore(testDirectory);
            Phase3SaveData data = NewSave(new DateTime(2026, 10, 12));

            Assert.That(store.Save(data, null, out string error), Is.True, error);
            Assert.That(store.Save(data, null, out error), Is.True, error);
            File.WriteAllText(Path.Combine(testDirectory, "progress-v1.json"), "not-json");

            Assert.That(store.TryLoad(out Phase3SaveData loaded, out _, out bool recovered, out bool invalid, out string warning), Is.True, warning);
            Assert.That(recovered, Is.True);
            Assert.That(invalid, Is.True);
            Assert.That(loaded.writeSequence, Is.EqualTo(1));
        }

        [Test]
        public void SaveStoreReturnsUncommittedJournalEventsForReplay()
        {
            var store = new Phase3LocalSaveStore(testDirectory);
            Phase3SaveData data = NewSave(new DateTime(2026, 10, 12));
            Assert.That(store.Save(data, null, out string error), Is.True, error);
            var journalEvent = new Phase3JournalEvent
            {
                eventId = "test-profile:day-finalized:2026-10-12",
                profileId = data.localProfileId,
                eventType = "day-finalized",
                day = "2026-10-12"
            };
            Assert.That(store.Save(data, new[] { journalEvent }, out error), Is.True, error);
            File.WriteAllText(Path.Combine(testDirectory, "progress-v1.json"), "broken-after-commit");

            Assert.That(store.TryLoad(out _, out var pending, out bool recovered, out _, out string warning), Is.True, warning);
            Assert.That(recovered, Is.True);
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(pending[0].eventId, Is.EqualTo(journalEvent.eventId));
        }

        [Test]
        public void ProgressionSnapshotKeepsWateringAndMissedDayIdempotency()
        {
            DateTime day = new DateTime(2026, 10, 12);
            var original = new Phase2PlantProgression(new Phase2PlantTuning());
            Assert.That(original.TryCompleteWatering(day, SuccessfulRun()), Is.True);
            original.ApplyMissedDay(day.AddDays(1));
            var restored = new Phase2PlantProgression(new Phase2PlantTuning());

            Assert.That(restored.TryRestoreSnapshot(original.CaptureSnapshot()), Is.True);
            Assert.That(restored.TryCompleteWatering(day, SuccessfulRun()), Is.False);
            Assert.That(restored.ApplyMissedDay(day.AddDays(1)), Is.False);
            Assert.That(restored.Health, Is.EqualTo(original.Health));
            Assert.That(restored.GrowthPoints, Is.EqualTo(original.GrowthPoints));
        }

        [Test]
        public void RewardClaimsAreIdempotentAndExpireAtTheGraceDeadline()
        {
            DateTime anchor = new DateTime(2026, 10, 12);
            var plant = new Phase2PlantProgression(new Phase2PlantTuning());
            var calendar = new Phase3RewardCalendar(anchor);
            Assert.That(plant.TryCompleteWatering(anchor, SuccessfulRun()), Is.True);
            DateTime beforeDeadline = anchor.AddDays(1).AddMinutes(14);

            Assert.That(calendar.GetStatus(anchor, beforeDeadline, plant), Is.EqualTo(Phase3RewardStatus.Available));
            Assert.That(calendar.TryClaim(anchor, beforeDeadline, plant), Is.True);
            Assert.That(calendar.TryClaim(anchor, beforeDeadline, plant), Is.False);
            Assert.That(calendar.GetStatus(anchor, beforeDeadline, plant), Is.EqualTo(Phase3RewardStatus.Claimed));
            Assert.That(calendar.GetStatus(anchor, anchor.AddDays(1).AddMinutes(15), plant), Is.EqualTo(Phase3RewardStatus.Claimed));
        }

        [Test]
        public void RewardCycleRepeatsDayOneAfterDaySevenAndMissesForfeit()
        {
            DateTime anchor = new DateTime(2026, 10, 12);
            var plant = new Phase2PlantProgression(new Phase2PlantTuning());
            var calendar = new Phase3RewardCalendar(anchor);
            DateTime dayEight = anchor.AddDays(7);
            plant.ApplyMissedDay(anchor);

            Assert.That(calendar.GetSlotNumber(dayEight), Is.EqualTo(1));
            Assert.That(calendar.GetCurrentCycle(dayEight)[0], Is.EqualTo(dayEight));
            Assert.That(calendar.Forfeit(anchor), Is.True);
            Assert.That(calendar.GetStatus(anchor, anchor.AddHours(1), plant), Is.EqualTo(Phase3RewardStatus.Forfeited));
        }

        [Test]
        public void RestoredRunContinuesFromTheSameLogicalState()
        {
            var tuning = new Phase1Tuning();
            var original = new Phase1RunSimulation(tuning, 1);
            original.Start();
            for (int i = 0; i < 70; i++)
                original.Step(-1);
            Phase1RunSnapshot snapshot = JsonUtility.FromJson<Phase1RunSnapshot>(JsonUtility.ToJson(original.CaptureSnapshot()));
            var restored = new Phase1RunSimulation(tuning, snapshot.stage);

            Assert.That(restored.TryRestoreSnapshot(snapshot, out string reason), Is.True, reason);
            for (int i = 0; i < 40; i++)
            {
                Assert.That(restored.Step(-1), Is.EqualTo(original.Step(-1)));
                Assert.That(restored.ElapsedTicks, Is.EqualTo(original.ElapsedTicks));
                Assert.That(restored.CollectedMilliWater, Is.EqualTo(original.CollectedMilliWater));
                Assert.That(restored.Obstacles.Count, Is.EqualTo(original.Obstacles.Count));
                for (int j = 0; j < restored.Obstacles.Count; j++)
                {
                    Assert.That(restored.Obstacles[j].id, Is.EqualTo(original.Obstacles[j].id));
                    Assert.That(restored.Obstacles[j].lane, Is.EqualTo(original.Obstacles[j].lane));
                    Assert.That(restored.Obstacles[j].spriteIndex, Is.EqualTo(original.Obstacles[j].spriteIndex));
                    Assert.That(restored.Obstacles[j].progress, Is.EqualTo(original.Obstacles[j].progress).Within(0.00001f));
                }
            }
        }

        private static Phase3SaveData NewSave(DateTime day)
        {
            var plant = new Phase2PlantProgression(new Phase2PlantTuning());
            var rewards = new Phase3RewardCalendar(day);
            var simulation = new Phase1RunSimulation(new Phase1Tuning(), plant.Stage);
            return new Phase3SaveData
            {
                localProfileId = "test-profile",
                profileTimeZoneId = "UTC",
                profileCreatedDay = Phase3SaveData.FormatDay(day),
                lastFinalizedDay = Phase3SaveData.FormatDay(day.AddDays(-1)),
                progression = plant.CaptureSnapshot(),
                rewards = rewards.CaptureSnapshot(),
                simulationDay = Phase3SaveData.FormatDay(day),
                runId = string.Empty,
                run = simulation.CaptureSnapshot()
            };
        }

        private static Phase1RunResult SuccessfulRun()
        {
            return new Phase1RunResult
            {
                reason = Phase1RunEndReason.Banked,
                dailyGoalUnits = 24,
                retainedMilliWater = 24 * Phase1Tuning.WaterScale
            };
        }
    }
}
