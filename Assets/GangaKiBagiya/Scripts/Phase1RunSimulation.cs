using System;
using System.Collections.Generic;
using UnityEngine;

namespace GangaKiBagiya
{
    public enum Phase1RunStatus
    {
        Ready,
        Running,
        Failed,
        Completed
    }

    public enum Phase1RunEndReason
    {
        HitBeforeGoal,
        HitAfterGoal,
        Banked
    }

    [Flags]
    public enum Phase1TickEvents
    {
        None = 0,
        WaterCollected = 1,
        GoalSecured = 2,
        ObstacleHit = 4,
        Banked = 8
    }

    [Serializable]
    public struct Phase1ObstacleState
    {
        public int id;
        public int lane;
        public int spriteIndex;
        public float progress;
        public bool isHazard;
    }

    [Serializable]
    public struct Phase1RunResult
    {
        public Phase1RunEndReason reason;
        public int collectedMilliWater;
        public int retainedMilliWater;
        public int spilledMilliWater;
        public int dailyGoalUnits;
        public int bonusGrowthPoints;
    }

    [Serializable]
    public sealed class Phase1RunSnapshot
    {
        public int version = 1;
        public int stage = 1;
        public int dailyGoalUnits;
        public Phase1RunStatus status;
        public int elapsedTicks;
        public int collectedMilliWater;
        public bool goalSecured;
        public int[] nextSpawnTicks = Array.Empty<int>();
        public int nextObstacleId;
        public int nextHazardSprite;
        public int heldLane = -1;
        public Phase1ObstacleState[] obstacles = Array.Empty<Phase1ObstacleState>();
        public bool hasResult;
        public Phase1RunResult result;
    }

    /// <summary>Deterministic, fixed-step run rules. Rendering and input live in Phase1GameController.</summary>
    public sealed class Phase1RunSimulation
    {
        private readonly Phase1Tuning tuning;
        private readonly int stage;
        private readonly List<Phase1ObstacleState> obstacles = new List<Phase1ObstacleState>(16);
        private readonly int[] nextSpawnTick = new int[Phase1Tuning.LaneCount];
        private int nextObstacleId;
        private int nextHazardSprite;
        private int heldLane = -1;
        private bool hasResult;
        private Phase1RunResult result;

        public Phase1RunStatus Status { get; private set; } = Phase1RunStatus.Ready;
        public bool GoalSecured { get; private set; }
        public int CollectedMilliWater { get; private set; }
        public int DailyGoalUnits { get; }
        public int ElapsedTicks { get; private set; }
        public float ElapsedSeconds => ElapsedTicks / (float)tuning.ticksPerSecond;
        public IReadOnlyList<Phase1ObstacleState> Obstacles => obstacles;
        public bool HasResult => hasResult;
        public Phase1RunResult Result => result;
        public int Stage => stage;

        public Phase1RunSimulation(Phase1Tuning tuning, int stage = 1)
        {
            if (tuning == null)
                throw new ArgumentNullException(nameof(tuning));
            if (!tuning.IsUsable(out string reason))
                throw new ArgumentException(reason, nameof(tuning));

            this.tuning = tuning;
            this.stage = Mathf.Max(1, stage);
            DailyGoalUnits = tuning.DailyGoalForStage(this.stage);
        }

        public void Start()
        {
            obstacles.Clear();
            Array.Clear(nextSpawnTick, 0, nextSpawnTick.Length);
            for (int lane = 0; lane < Phase1Tuning.LaneCount; lane++)
                nextSpawnTick[lane] = Mathf.RoundToInt(tuning.laneInitialSpawnDelaysSeconds[lane] * tuning.ticksPerSecond);

            nextObstacleId = 0;
            nextHazardSprite = 0;
            heldLane = -1;
            CollectedMilliWater = 0;
            ElapsedTicks = 0;
            GoalSecured = false;
            hasResult = false;
            result = default;
            Status = Phase1RunStatus.Running;
        }

        public Phase1RunSnapshot CaptureSnapshot()
        {
            return new Phase1RunSnapshot
            {
                stage = stage,
                dailyGoalUnits = DailyGoalUnits,
                status = Status,
                elapsedTicks = ElapsedTicks,
                collectedMilliWater = CollectedMilliWater,
                goalSecured = GoalSecured,
                nextSpawnTicks = (int[])nextSpawnTick.Clone(),
                nextObstacleId = nextObstacleId,
                nextHazardSprite = nextHazardSprite,
                heldLane = heldLane,
                obstacles = obstacles.ToArray(),
                hasResult = hasResult,
                result = result
            };
        }

        public bool TryRestoreSnapshot(Phase1RunSnapshot snapshot, out string reason)
        {
            if (snapshot == null || snapshot.version != 1 || snapshot.stage != stage ||
                snapshot.dailyGoalUnits != DailyGoalUnits || !Enum.IsDefined(typeof(Phase1RunStatus), snapshot.status) ||
                snapshot.elapsedTicks < 0 || snapshot.collectedMilliWater < 0 ||
                snapshot.nextSpawnTicks == null || snapshot.nextSpawnTicks.Length != Phase1Tuning.LaneCount ||
                snapshot.obstacles == null || snapshot.nextObstacleId < 0 || snapshot.nextHazardSprite < 0 ||
                snapshot.heldLane < -1 || snapshot.heldLane >= Phase1Tuning.LaneCount)
            {
                reason = "The saved run header is invalid.";
                return false;
            }

            int goalMilliWater = DailyGoalUnits * Phase1Tuning.WaterScale;
            if (snapshot.goalSecured && snapshot.collectedMilliWater < goalMilliWater)
            {
                reason = "The saved run has an inconsistent secured goal.";
                return false;
            }
            bool resultStatus = snapshot.status == Phase1RunStatus.Failed || snapshot.status == Phase1RunStatus.Completed;
            if (snapshot.hasResult != resultStatus || (snapshot.status == Phase1RunStatus.Ready &&
                (snapshot.elapsedTicks != 0 || snapshot.collectedMilliWater != 0 || snapshot.obstacles.Length != 0)) ||
                (snapshot.status == Phase1RunStatus.Running && snapshot.elapsedTicks == 0) ||
                (snapshot.status == Phase1RunStatus.Failed && snapshot.goalSecured) ||
                (snapshot.status == Phase1RunStatus.Completed && !snapshot.goalSecured))
            {
                reason = "The saved run status is inconsistent.";
                return false;
            }

            for (int lane = 0; lane < Phase1Tuning.LaneCount; lane++)
            {
                if (snapshot.nextSpawnTicks[lane] < 0 ||
                    (snapshot.status == Phase1RunStatus.Running && snapshot.nextSpawnTicks[lane] <= snapshot.elapsedTicks))
                {
                    reason = "The saved obstacle schedule is invalid.";
                    return false;
                }
            }

            var obstacleIds = new HashSet<int>();
            int[] hazardIndices = tuning.hazardObstacleSpriteIndices;
            for (int i = 0; i < snapshot.obstacles.Length; i++)
            {
                Phase1ObstacleState obstacle = snapshot.obstacles[i];
                if (obstacle.id < 0 || obstacle.id >= snapshot.nextObstacleId || !obstacleIds.Add(obstacle.id) ||
                    obstacle.lane < 0 || obstacle.lane >= Phase1Tuning.LaneCount ||
                    Array.IndexOf(hazardIndices, obstacle.spriteIndex) < 0 ||
                    float.IsNaN(obstacle.progress) || float.IsInfinity(obstacle.progress) ||
                    obstacle.progress < -0.4f || obstacle.progress > 1.4f)
                {
                    reason = "The saved obstacle state is invalid.";
                    return false;
                }
            }

            if (snapshot.hasResult && (!IsValidResult(snapshot.result, snapshot.status) ||
                                       snapshot.result.collectedMilliWater != snapshot.collectedMilliWater ||
                                       snapshot.result.dailyGoalUnits != DailyGoalUnits))
            {
                reason = "The saved run result is invalid.";
                return false;
            }

            Array.Copy(snapshot.nextSpawnTicks, nextSpawnTick, nextSpawnTick.Length);
            obstacles.Clear();
            obstacles.AddRange(snapshot.obstacles);
            nextObstacleId = snapshot.nextObstacleId;
            nextHazardSprite = snapshot.nextHazardSprite;
            ElapsedTicks = snapshot.elapsedTicks;
            CollectedMilliWater = snapshot.collectedMilliWater;
            GoalSecured = snapshot.goalSecured;
            Status = snapshot.status;
            hasResult = snapshot.hasResult;
            result = snapshot.result;
            heldLane = -1;
            reason = string.Empty;
            return true;
        }

        public void ReleaseInput()
        {
            heldLane = -1;
        }

        /// <param name="lane">The currently held lane, or -1 when no lane is held.</param>
        /// <param name="bankRequested">Banking is accepted only after the goal is secured.</param>
        public Phase1TickEvents Step(int lane, bool bankRequested = false)
        {
            if (Status != Phase1RunStatus.Running)
                return Phase1TickEvents.None;

            heldLane = lane >= 0 && lane < Phase1Tuning.LaneCount ? lane : -1;
            ElapsedTicks++;
            float multiplier = tuning.SpeedMultiplier(ElapsedSeconds, stage);
            SpawnDueObstacles(multiplier);

            for (int i = 0; i < obstacles.Count; i++)
            {
                Phase1ObstacleState obstacle = obstacles[i];
                float previousProgress = obstacle.progress;
                obstacle.progress += tuning.laneObstaclePixelsPerSecond[obstacle.lane] /
                                     tuning.referenceScreenWidthPixels / tuning.ticksPerSecond * multiplier;
                obstacles[i] = obstacle;

                // Swept overlap of the hazard's hit span with the matka's, so fast obstacles cannot tunnel.
                float reach = tuning.HalfWidth(obstacle.spriteIndex) * tuning.hitForgiveness;
                float collectorX = tuning.collectorProgress * tuning.logicalLaneLength;
                if (obstacle.isHazard && heldLane == obstacle.lane &&
                    obstacle.progress * tuning.logicalLaneLength + reach >= collectorX - tuning.collectorHalfWidth &&
                    previousProgress * tuning.logicalLaneLength - reach <= collectorX + tuning.collectorHalfWidth)
                {
                    bool secured = GoalSecured;
                    Finish(secured ? Phase1RunEndReason.HitAfterGoal : Phase1RunEndReason.HitBeforeGoal);
                    return Phase1TickEvents.ObstacleHit;
                }
            }

            Phase1TickEvents events = Phase1TickEvents.None;
            if (heldLane >= 0)
            {
                int oldWholeUnits = CollectedMilliWater / Phase1Tuning.WaterScale;
                CollectedMilliWater += tuning.laneWaterMilliUnitsPerSecond[heldLane] / tuning.ticksPerSecond;
                int newWholeUnits = CollectedMilliWater / Phase1Tuning.WaterScale;
                if (newWholeUnits > oldWholeUnits)
                {
                    events |= Phase1TickEvents.WaterCollected;
                }
            }

            if (!GoalSecured && CollectedMilliWater >= DailyGoalUnits * Phase1Tuning.WaterScale)
            {
                GoalSecured = true;
                events |= Phase1TickEvents.GoalSecured;
            }

            if (bankRequested && GoalSecured)
            {
                Finish(Phase1RunEndReason.Banked);
                events |= Phase1TickEvents.Banked;
            }

            for (int i = obstacles.Count - 1; i >= 0; i--)
            {
                if (obstacles[i].progress > 1f + (tuning.HalfWidth(obstacles[i].spriteIndex) + 0.1f) / tuning.logicalLaneLength)
                    obstacles.RemoveAt(i);
            }
            return events;
        }

        private void SpawnDueObstacles(float difficultyMultiplier)
        {
            for (int lane = 0; lane < Phase1Tuning.LaneCount; lane++)
            {
                if (ElapsedTicks < nextSpawnTick[lane])
                    continue;

                int[] hazardIndices = tuning.hazardObstacleSpriteIndices;
                int spriteIndex = hazardIndices[nextHazardSprite % hazardIndices.Length];
                float length = tuning.logicalLaneLength;
                float spawnProgress = -(tuning.HalfWidth(spriteIndex) + 0.1f) / length;

                // Keep neighbours clear of each other; lane speed is uniform so spacing set here holds.
                bool blocked = false;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    Phase1ObstacleState other = obstacles[i];
                    float needed = tuning.HalfWidth(other.spriteIndex) + tuning.HalfWidth(spriteIndex) + tuning.minClearGap;
                    if (other.lane == lane && (other.progress - spawnProgress) * length < needed)
                        blocked = true;
                }
                if (blocked)
                {
                    nextSpawnTick[lane] = ElapsedTicks + 1;
                    continue;
                }

                nextHazardSprite++;
                obstacles.Add(new Phase1ObstacleState
                {
                    id = nextObstacleId++,
                    lane = lane,
                    spriteIndex = spriteIndex,
                    progress = spawnProgress,
                    isHazard = true
                });

                int gapTicks = Mathf.Max(1, Mathf.RoundToInt(tuning.laneObstacleGapsSeconds[lane] /
                                                              difficultyMultiplier * tuning.ticksPerSecond));
                nextSpawnTick[lane] = ElapsedTicks + gapTicks;
            }
        }

        private void Finish(Phase1RunEndReason reason)
        {
            int retained = reason switch
            {
                Phase1RunEndReason.HitBeforeGoal => 0,
                Phase1RunEndReason.HitAfterGoal => DailyGoalUnits * Phase1Tuning.WaterScale,
                _ => CollectedMilliWater
            };
            int spilled = Mathf.Max(0, CollectedMilliWater - retained);
            result = new Phase1RunResult
            {
                reason = reason,
                collectedMilliWater = CollectedMilliWater,
                retainedMilliWater = retained,
                spilledMilliWater = spilled,
                dailyGoalUnits = DailyGoalUnits,
                bonusGrowthPoints = tuning.BonusGrowthPoints(retained, DailyGoalUnits)
            };
            hasResult = true;
            Status = reason == Phase1RunEndReason.HitBeforeGoal ? Phase1RunStatus.Failed : Phase1RunStatus.Completed;
        }

        private bool IsValidResult(Phase1RunResult runResult, Phase1RunStatus status)
        {
            bool failed = runResult.reason == Phase1RunEndReason.HitBeforeGoal;
            int expectedRetained = runResult.reason == Phase1RunEndReason.HitBeforeGoal ? 0 :
                runResult.reason == Phase1RunEndReason.HitAfterGoal ? DailyGoalUnits * Phase1Tuning.WaterScale :
                runResult.collectedMilliWater;
            return Enum.IsDefined(typeof(Phase1RunEndReason), runResult.reason) &&
                   (status == Phase1RunStatus.Failed) == failed &&
                   runResult.collectedMilliWater >= 0 && runResult.retainedMilliWater >= 0 &&
                   runResult.retainedMilliWater <= runResult.collectedMilliWater &&
                   runResult.retainedMilliWater == expectedRetained &&
                   runResult.spilledMilliWater == runResult.collectedMilliWater - runResult.retainedMilliWater &&
                   runResult.dailyGoalUnits == DailyGoalUnits && runResult.bonusGrowthPoints == tuning.BonusGrowthPoints(
                       runResult.retainedMilliWater, DailyGoalUnits) &&
                   (failed ? runResult.retainedMilliWater == 0 : runResult.retainedMilliWater >= DailyGoalUnits * Phase1Tuning.WaterScale);
        }
    }
}
