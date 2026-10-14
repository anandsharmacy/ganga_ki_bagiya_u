using System;
using System.Collections.Generic;
using UnityEngine;

namespace GangaKiBagiya
{
    [Serializable]
    public sealed class Phase2PlantTuning
    {
        public const int StageCount = 6;
        public const int MaximumHealth = 3;
        public const int WateringHealthRecovery = 2;

        [Header("Mango growth")]
        [Tooltip("Growth points needed to advance from stages 1 through 5.")]
        [Min(1)] public int[] growthPointsToAdvance = { 2, 2, 2, 3, 3 };

        public bool IsUsable(out string reason)
        {
            if (growthPointsToAdvance == null || growthPointsToAdvance.Length != StageCount - 1)
            {
                reason = "Plant tuning must define five mango-stage growth thresholds.";
                return false;
            }

            long total = 0;
            foreach (int threshold in growthPointsToAdvance)
            {
                if (threshold <= 0)
                {
                    reason = "Every mango-stage growth threshold must be positive.";
                    return false;
                }
                total += threshold;
            }

            if (total > int.MaxValue)
            {
                reason = "The total mango growth-point cap is too large.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public int MaximumGrowthPoints
        {
            get
            {
                int total = 0;
                if (growthPointsToAdvance != null)
                    foreach (int threshold in growthPointsToAdvance)
                        total = (int)Math.Min(int.MaxValue, (long)total + threshold);
                return total;
            }
        }

        public int GrowthPointsBeforeStage(int stage)
        {
            int total = 0;
            int count = Math.Min(Math.Max(0, stage - 1), growthPointsToAdvance == null ? 0 : growthPointsToAdvance.Length);
            for (int i = 0; i < count; i++)
                total += growthPointsToAdvance[i];
            return total;
        }

        public int GrowthPointsToAdvanceFrom(int stage)
        {
            return growthPointsToAdvance != null && stage >= 1 && stage < StageCount && stage <= growthPointsToAdvance.Length
                ? growthPointsToAdvance[stage - 1]
                : 0;
        }
    }

    [Serializable]
    public sealed class Phase2ProgressionSnapshot
    {
        public int version = 1;
        public int stage = 1;
        public int growthPoints;
        public int health = Phase2PlantTuning.MaximumHealth;
        public long[] wateredDays = Array.Empty<long>();
        public long[] missedDays = Array.Empty<long>();
    }

    public sealed class Phase2PlantProgression
    {
        private readonly Phase2PlantTuning tuning;
        private readonly HashSet<long> wateredDays = new HashSet<long>();
        private readonly HashSet<long> missedDays = new HashSet<long>();

        public int Stage { get; private set; } = 1;
        public int GrowthPoints { get; private set; }
        public int Health { get; private set; } = Phase2PlantTuning.MaximumHealth;
        public int GrowthProgressInStage => Math.Max(0, GrowthPoints - tuning.GrowthPointsBeforeStage(Stage));
        public int GrowthPointsToNextStage => tuning.GrowthPointsToAdvanceFrom(Stage);

        public Phase2PlantProgression(Phase2PlantTuning tuning)
        {
            if (tuning == null)
                throw new ArgumentNullException(nameof(tuning));
            if (!tuning.IsUsable(out string reason))
                throw new ArgumentException(reason, nameof(tuning));
            this.tuning = tuning;
        }

        public bool HasWateredOn(DateTime day)
        {
            return wateredDays.Contains(day.Date.Ticks);
        }

        public bool CanWaterOn(DateTime day)
        {
            long key = day.Date.Ticks;
            return !wateredDays.Contains(key) && !missedDays.Contains(key);
        }

        public bool TryCompleteWatering(DateTime day, Phase1RunResult result)
        {
            bool secured = result.reason == Phase1RunEndReason.Banked || result.reason == Phase1RunEndReason.HitAfterGoal;
            if (!secured || result.dailyGoalUnits <= 0 ||
                (long)result.retainedMilliWater < (long)result.dailyGoalUnits * Phase1Tuning.WaterScale ||
                !CanWaterOn(day))
                return false;

            wateredDays.Add(day.Date.Ticks);
            Health = Math.Min(Phase2PlantTuning.MaximumHealth, Health + Phase2PlantTuning.WateringHealthRecovery);
            if (Stage < Phase2PlantTuning.StageCount)
            {
                long updatedPoints = (long)GrowthPoints + 1 + Math.Max(0, result.bonusGrowthPoints);
                GrowthPoints = (int)Math.Min(tuning.MaximumGrowthPoints, updatedPoints);
                while (Stage < Phase2PlantTuning.StageCount && GrowthPoints >= tuning.GrowthPointsBeforeStage(Stage + 1))
                    Stage++;
            }
            return true;
        }

        public bool ApplyMissedDay(DateTime day)
        {
            long key = day.Date.Ticks;
            if (wateredDays.Contains(key) || !missedDays.Add(key))
                return false;

            Health = Math.Max(0, Health - 1);
            return true;
        }

        public Phase2ProgressionSnapshot CaptureSnapshot()
        {
            long[] watered = new long[wateredDays.Count];
            long[] missed = new long[missedDays.Count];
            wateredDays.CopyTo(watered);
            missedDays.CopyTo(missed);
            Array.Sort(watered);
            Array.Sort(missed);
            return new Phase2ProgressionSnapshot
            {
                stage = Stage,
                growthPoints = GrowthPoints,
                health = Health,
                wateredDays = watered,
                missedDays = missed
            };
        }

        public bool TryRestoreSnapshot(Phase2ProgressionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.version != 1 || snapshot.stage < 1 || snapshot.stage > Phase2PlantTuning.StageCount ||
                snapshot.growthPoints < 0 || snapshot.growthPoints > tuning.MaximumGrowthPoints ||
                snapshot.health < 0 || snapshot.health > Phase2PlantTuning.MaximumHealth ||
                snapshot.wateredDays == null || snapshot.missedDays == null ||
                snapshot.stage != StageForGrowthPoints(snapshot.growthPoints))
                return false;

            var watered = new HashSet<long>();
            var missed = new HashSet<long>();
            foreach (long day in snapshot.wateredDays)
            {
                if (!IsDayKey(day) || !watered.Add(day))
                    return false;
            }
            foreach (long day in snapshot.missedDays)
            {
                if (!IsDayKey(day) || watered.Contains(day) || !missed.Add(day))
                    return false;
            }

            Stage = snapshot.stage;
            GrowthPoints = snapshot.growthPoints;
            Health = snapshot.health;
            wateredDays.Clear();
            wateredDays.UnionWith(watered);
            missedDays.Clear();
            missedDays.UnionWith(missed);
            return true;
        }

        private int StageForGrowthPoints(int growthPoints)
        {
            int stage = 1;
            while (stage < Phase2PlantTuning.StageCount && growthPoints >= tuning.GrowthPointsBeforeStage(stage + 1))
                stage++;
            return stage;
        }

        private static bool IsDayKey(long ticks)
        {
            return ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks &&
                   new DateTime(ticks).Date.Ticks == ticks;
        }
    }
}
