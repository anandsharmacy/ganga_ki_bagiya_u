using System;
using NUnit.Framework;

namespace GangaKiBagiya.Tests
{
    public sealed class Phase2PlantProgressionTests
    {
        private Phase2PlantProgression plant;

        [SetUp]
        public void SetUp()
        {
            plant = new Phase2PlantProgression(new Phase2PlantTuning());
        }

        [Test]
        public void NewPlantStartsAsSeedWithFullHealthAndConfiguredProgress()
        {
            Assert.That(plant.Stage, Is.EqualTo(1));
            Assert.That(plant.GrowthPoints, Is.Zero);
            Assert.That(plant.GrowthProgressInStage, Is.Zero);
            Assert.That(plant.GrowthPointsToNextStage, Is.EqualTo(2));
            Assert.That(plant.Health, Is.EqualTo(3));
        }

        [Test]
        public void WateringCarriesSurplusAcrossEveryStageThreshold()
        {
            DateTime day = new DateTime(2026, 10, 12);

            Assert.That(plant.TryCompleteWatering(day, SuccessfulRun(bonusGrowthPoints: 11)), Is.True);

            Assert.That(plant.Stage, Is.EqualTo(6));
            Assert.That(plant.GrowthPoints, Is.EqualTo(12));
            Assert.That(plant.GrowthPointsToNextStage, Is.Zero);
        }

        [Test]
        public void StageThresholdsCanBeConfigured()
        {
            Phase2PlantTuning customTuning = new Phase2PlantTuning { growthPointsToAdvance = new[] { 1, 3, 1, 2, 1 } };
            Phase2PlantProgression customPlant = new Phase2PlantProgression(customTuning);

            Assert.That(customPlant.TryCompleteWatering(new DateTime(2026, 10, 12), SuccessfulRun()), Is.True);

            Assert.That(customPlant.Stage, Is.EqualTo(2));
            Assert.That(customPlant.GrowthPoints, Is.EqualTo(1));
            Assert.That(customPlant.GrowthPointsToNextStage, Is.EqualTo(3));
        }

        [Test]
        public void FailedRunLeavesWateringAvailableAndPlantUnchanged()
        {
            DateTime day = new DateTime(2026, 10, 12);
            plant.ApplyMissedDay(day.AddDays(-1));
            int healthBefore = plant.Health;

            Assert.That(plant.TryCompleteWatering(day, FailedRun()), Is.False);

            Assert.That(plant.CanWaterOn(day), Is.True);
            Assert.That(plant.GrowthPoints, Is.Zero);
            Assert.That(plant.Health, Is.EqualTo(healthBefore));
        }

        [Test]
        public void WateringIsAppliedOnlyOnceForTheSameDay()
        {
            DateTime day = new DateTime(2026, 10, 12);

            Assert.That(plant.TryCompleteWatering(day, SuccessfulRun()), Is.True);
            Assert.That(plant.TryCompleteWatering(day, SuccessfulRun(bonusGrowthPoints: 20)), Is.False);

            Assert.That(plant.GrowthPoints, Is.EqualTo(1));
            Assert.That(plant.Health, Is.EqualTo(3));
        }

        [Test]
        public void MissedDaysReduceHealthOnceAndNeverRemoveGrowth()
        {
            DateTime day = new DateTime(2026, 10, 12);
            plant.TryCompleteWatering(day, SuccessfulRun());

            Assert.That(plant.ApplyMissedDay(day), Is.False);
            Assert.That(plant.ApplyMissedDay(day.AddDays(1)), Is.True);
            Assert.That(plant.ApplyMissedDay(day.AddDays(1)), Is.False);
            Assert.That(plant.ApplyMissedDay(day.AddDays(2)), Is.True);
            Assert.That(plant.ApplyMissedDay(day.AddDays(3)), Is.True);

            Assert.That(plant.Health, Is.Zero);
            Assert.That(plant.Stage, Is.EqualTo(1));
            Assert.That(plant.GrowthPoints, Is.EqualTo(1));
        }

        [Test]
        public void StageSixStopsGrowthButWateringStillRestoresHealth()
        {
            DateTime day = new DateTime(2026, 10, 12);
            plant.TryCompleteWatering(day, SuccessfulRun(bonusGrowthPoints: 11));
            plant.ApplyMissedDay(day.AddDays(1));

            Assert.That(plant.TryCompleteWatering(day.AddDays(2), SuccessfulRun(bonusGrowthPoints: 20)), Is.True);

            Assert.That(plant.Stage, Is.EqualTo(6));
            Assert.That(plant.GrowthPoints, Is.EqualTo(12));
            Assert.That(plant.Health, Is.EqualTo(3));
        }

        private static Phase1RunResult SuccessfulRun(int bonusGrowthPoints = 0)
        {
            return new Phase1RunResult
            {
                reason = Phase1RunEndReason.Banked,
                dailyGoalUnits = 24,
                retainedMilliWater = 24 * Phase1Tuning.WaterScale,
                bonusGrowthPoints = bonusGrowthPoints
            };
        }

        private static Phase1RunResult FailedRun()
        {
            return new Phase1RunResult
            {
                reason = Phase1RunEndReason.HitBeforeGoal,
                dailyGoalUnits = 24,
                retainedMilliWater = 0
            };
        }
    }
}
