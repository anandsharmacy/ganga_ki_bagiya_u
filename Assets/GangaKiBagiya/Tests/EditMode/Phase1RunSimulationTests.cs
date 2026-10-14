using GangaKiBagiya;
using NUnit.Framework;

namespace GangaKiBagiya.Tests
{
    public sealed class Phase1RunSimulationTests
    {
        private Phase1Tuning tuning;

        [SetUp]
        public void SetUp()
        {
            tuning = new Phase1Tuning();
        }

        [Test]
        public void StageOneGoalAndBonusThresholdMatchPhaseZeroBaseline()
        {
            Assert.That(tuning.DailyGoalForStage(1), Is.EqualTo(24));
            Assert.That(tuning.DailyGoalForStage(3), Is.EqualTo(40));
            Assert.That(tuning.BonusGrowthPoints(53 * Phase1Tuning.WaterScale, 24), Is.EqualTo(0));
            Assert.That(tuning.BonusGrowthPoints(54 * Phase1Tuning.WaterScale, 24), Is.EqualTo(1));
        }

        [Test]
        public void CollisionIsResolvedBeforeSameTickWaterCollection()
        {
            tuning.laneInitialSpawnDelaysSeconds = new[] { 0f, 100f, 100f };
            tuning.laneObstaclePixelsPerSecond = new[] { 128000f, 100f, 62f };
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();

            Phase1TickEvents events = simulation.Step(0);

            Assert.That(events, Is.EqualTo(Phase1TickEvents.ObstacleHit));
            Assert.That(simulation.Result.reason, Is.EqualTo(Phase1RunEndReason.HitBeforeGoal));
            Assert.That(simulation.Result.retainedMilliWater, Is.Zero);
            Assert.That(simulation.Result.spilledMilliWater, Is.Zero);
        }

        [Test]
        public void PreGoalHitSpillsAllCollectedWater()
        {
            tuning.laneInitialSpawnDelaysSeconds = new[] { 2.5f, 100f, 100f };
            tuning.laneObstaclePixelsPerSecond = new[] { 128000f, 100f, 62f };
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();
            for (int i = 0; i < 125 && simulation.Status == Phase1RunStatus.Running; i++)
                simulation.Step(0);

            Assert.That(simulation.Status, Is.EqualTo(Phase1RunStatus.Failed));
            Assert.That(simulation.Result.reason, Is.EqualTo(Phase1RunEndReason.HitBeforeGoal));
            Assert.That(simulation.Result.retainedMilliWater, Is.Zero);
            Assert.That(simulation.Result.spilledMilliWater, Is.EqualTo(simulation.Result.collectedMilliWater));
            Assert.That(simulation.Result.collectedMilliWater, Is.GreaterThan(0));
        }

        [Test]
        public void PostGoalHitKeepsExactlyGoalAndSpillsOnlySurplus()
        {
            tuning.laneInitialSpawnDelaysSeconds = new[] { 3.5f, 100f, 100f };
            tuning.laneObstaclePixelsPerSecond = new[] { 128000f, 100f, 62f };
            tuning.laneObstacleGapsSeconds = new[] { 20f, 20f, 20f };
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();
            for (int i = 0; i < 200 && simulation.Status == Phase1RunStatus.Running; i++)
                simulation.Step(0);

            Assert.That(simulation.Result.reason, Is.EqualTo(Phase1RunEndReason.HitAfterGoal));
            Assert.That(simulation.Result.retainedMilliWater, Is.EqualTo(24 * Phase1Tuning.WaterScale));
            Assert.That(simulation.Result.spilledMilliWater,
                Is.EqualTo(simulation.Result.collectedMilliWater - 24 * Phase1Tuning.WaterScale));
            Assert.That(simulation.Result.spilledMilliWater, Is.GreaterThan(0));
        }

        [Test]
        public void BankingKeepsAllWaterAndComputesBonusFromRetainedAmount()
        {
            tuning.laneInitialSpawnDelaysSeconds = new[] { 100f, 100f, 100f };
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();
            for (int i = 0; i < 400; i++)
                simulation.Step(0, i == 399);

            Assert.That(simulation.Status, Is.EqualTo(Phase1RunStatus.Completed));
            Assert.That(simulation.Result.reason, Is.EqualTo(Phase1RunEndReason.Banked));
            Assert.That(simulation.Result.retainedMilliWater, Is.EqualTo(simulation.Result.collectedMilliWater));
            Assert.That(simulation.Result.spilledMilliWater, Is.Zero);
            Assert.That(simulation.Result.bonusGrowthPoints, Is.EqualTo(1));
        }

        [Test]
        public void ReleasingLaneStopsWaterCollection()
        {
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();
            simulation.Step(1);
            int collected = simulation.CollectedMilliWater;
            simulation.Step(-1);
            Assert.That(simulation.CollectedMilliWater, Is.EqualTo(collected));
        }

        [Test]
        public void IdenticalRunsProduceIdenticalObstacleSchedules()
        {
            Phase1RunSimulation left = new Phase1RunSimulation(tuning);
            Phase1RunSimulation right = new Phase1RunSimulation(tuning);
            left.Start();
            right.Start();
            for (int i = 0; i < 90; i++)
            {
                int lane = i % 4 == 0 ? -1 : i % 3;
                left.Step(lane);
                right.Step(lane);
            }

            Assert.That(left.Obstacles.Count, Is.EqualTo(right.Obstacles.Count));
            for (int i = 0; i < left.Obstacles.Count; i++)
            {
                Assert.That(left.Obstacles[i].id, Is.EqualTo(right.Obstacles[i].id));
                Assert.That(left.Obstacles[i].lane, Is.EqualTo(right.Obstacles[i].lane));
                Assert.That(left.Obstacles[i].spriteIndex, Is.EqualTo(right.Obstacles[i].spriteIndex));
                Assert.That(left.Obstacles[i].progress, Is.EqualTo(right.Obstacles[i].progress));
            }
        }

        [Test]
        public void LaneNeighboursNeverOverlapAtMaximumDifficulty()
        {
            tuning.difficultyRampSeconds = 1f;
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();
            for (int i = 0; i < 120 * tuning.ticksPerSecond && simulation.Status == Phase1RunStatus.Running; i++)
            {
                simulation.Step(-1);
                var obstacles = simulation.Obstacles;
                for (int a = 0; a < obstacles.Count; a++)
                    for (int b = a + 1; b < obstacles.Count; b++)
                    {
                        if (obstacles[a].lane != obstacles[b].lane)
                            continue;
                        float distance = System.Math.Abs(obstacles[a].progress - obstacles[b].progress) * tuning.logicalLaneLength;
                        float needed = tuning.HalfWidth(obstacles[a].spriteIndex) + tuning.HalfWidth(obstacles[b].spriteIndex);
                        Assert.That(distance, Is.GreaterThanOrEqualTo(needed), "tick " + i);
                    }
            }
        }

        [Test]
        public void HitRegistersOnFirstVisibleOverlapNotAtCentreCrossing()
        {
            tuning.laneInitialSpawnDelaysSeconds = new[] { 0f, 100f, 100f };
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();
            Phase1TickEvents events = Phase1TickEvents.None;
            while (simulation.Status == Phase1RunStatus.Running && simulation.ElapsedTicks < 2000)
                events = simulation.Step(0);

            Assert.That(events, Is.EqualTo(Phase1TickEvents.ObstacleHit));
            float edge = (tuning.collectorProgress * tuning.logicalLaneLength - tuning.collectorHalfWidth) / tuning.logicalLaneLength;
            Assert.That(simulation.Obstacles[0].progress, Is.LessThan(tuning.collectorProgress));
            Assert.That(simulation.Obstacles[0].progress, Is.GreaterThan(edge - 0.2f));
        }

        [Test]
        public void HazardsInOtherLanesNeverHit()
        {
            Phase1RunSimulation simulation = new Phase1RunSimulation(tuning);
            simulation.Start();
            for (int i = 0; i < 60 * tuning.ticksPerSecond && simulation.Status == Phase1RunStatus.Running; i++)
                simulation.Step(-1);
            Assert.That(simulation.Status, Is.EqualTo(Phase1RunStatus.Running));
        }
    }
}
