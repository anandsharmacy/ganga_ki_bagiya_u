using System;
using UnityEngine;

namespace GangaKiBagiya
{
    [Serializable]
    public sealed class Phase1Tuning
    {
        public const int LaneCount = 3;
        public const int WaterScale = 1000;

        [Header("Daily goal")]
        [Min(1)] public int startingDailyGoal = 24;
        [Min(0)] public int dailyGoalIncreasePerStage = 8;

        [Header("Fixed-step simulation")]
        [Min(1)] public int ticksPerSecond = 50;
        [Tooltip("Water units per second, stored as thousandths to avoid frame-rate drift.")]
        public int[] laneWaterMilliUnitsPerSecond = { 7500, 4200, 2200 };

        [Header("Obstacle tuning")]
        [Tooltip("Reference speeds in pixels per second, ordered top/middle/bottom.")]
        public float[] laneObstaclePixelsPerSecond = { 150f, 100f, 62f };
        [Tooltip("Seconds between obstacle spawns in each lane, ordered top/middle/bottom.")]
        public float[] laneObstacleGapsSeconds = { 1.8f, 2.5f, 3.2f };
        [Tooltip("Initial spawn delays in seconds, ordered top/middle/bottom.")]
        public float[] laneInitialSpawnDelaysSeconds = { 1.1f, 1.7f, 2.3f };
        public float referenceScreenWidthPixels = 1280f;
        [Range(0.5f, 0.95f)] public float collectorProgress = 0.82f;
        [Tooltip("Indices in the obstacle sprite list used as gameplay hazards; all others remain scenery until their role is approved.")]
        public int[] hazardObstacleSpriteIndices = { 0, 3, 4, 8, 9 };

        [Header("Logical lane (world units, identical on every screen)")]
        [Tooltip("Visible lane width; obstacle x = (progress - 0.5) * this.")]
        public float logicalLaneLength = 3.6f;
        [Tooltip("Hazard half-widths by obstacle sprite index (boat, croc, dirt, diya, dolphine, fish 1, fish 2, flower, plastic, pollution) at the 0.85 runtime height.")]
        public float[] hazardHalfWidths = { 0.44f, 0.94f, 1.58f, 0.48f, 0.57f, 0.61f, 0.64f, 0.87f, 0.71f, 0.55f };
        [Min(0f)] public float minClearGap = 0.25f;
        [Tooltip("Half-width of the matka's hit zone.")]
        public float collectorHalfWidth = 0.28f;
        [Range(0.1f, 1f)] public float hitForgiveness = 0.8f;

        public float HalfWidth(int spriteIndex)
        {
            return hazardHalfWidths != null && spriteIndex >= 0 && spriteIndex < hazardHalfWidths.Length ? hazardHalfWidths[spriteIndex] : 0.6f;
        }

        [Header("Difficulty")]
        [Min(1f)] public float difficultyRampSeconds = 80f;
        [Min(1f)] public float maximumSpeedMultiplier = 1.9f;
        [Min(0f)] public float speedBonusPerAdditionalStage = 0.05f;

        public int DailyGoalForStage(int stage)
        {
            return startingDailyGoal + dailyGoalIncreasePerStage * Mathf.Max(0, stage - 1);
        }

        public float SpeedMultiplier(float elapsedSeconds, int stage)
        {
            float ramp = Mathf.Clamp01(elapsedSeconds / Mathf.Max(1f, difficultyRampSeconds));
            float stageBonus = Mathf.Max(0, stage - 1) * speedBonusPerAdditionalStage;
            return Mathf.Min(maximumSpeedMultiplier, 1f + (maximumSpeedMultiplier - 1f) * ramp + stageBonus);
        }

        public int BonusGrowthPoints(int retainedMilliWater, int goalUnits)
        {
            int bonusMilliWater = Mathf.Max(0, retainedMilliWater - goalUnits * WaterScale);
            return bonusMilliWater / (30 * WaterScale);
        }

        public bool IsUsable(out string reason)
        {
            if (ticksPerSecond <= 0 || laneWaterMilliUnitsPerSecond == null || laneWaterMilliUnitsPerSecond.Length != LaneCount)
            {
                reason = "Tuning must define three lane water rates and a positive tick rate.";
                return false;
            }
            if (laneObstaclePixelsPerSecond == null || laneObstaclePixelsPerSecond.Length != LaneCount ||
                laneObstacleGapsSeconds == null || laneObstacleGapsSeconds.Length != LaneCount ||
                laneInitialSpawnDelaysSeconds == null || laneInitialSpawnDelaysSeconds.Length != LaneCount ||
                referenceScreenWidthPixels <= 0f || logicalLaneLength <= 0f || hazardObstacleSpriteIndices == null || hazardObstacleSpriteIndices.Length == 0)
            {
                reason = "Tuning must define three obstacle lanes, a reference width, and at least one hazard sprite.";
                return false;
            }
            for (int lane = 0; lane < LaneCount; lane++)
            {
                if (laneWaterMilliUnitsPerSecond[lane] < 0 || laneObstaclePixelsPerSecond[lane] <= 0f ||
                    laneObstacleGapsSeconds[lane] <= 0f || laneInitialSpawnDelaysSeconds[lane] < 0f)
                {
                    reason = "Lane rates and spawn timing must be non-negative, with positive speeds and gaps.";
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }
    }
}
