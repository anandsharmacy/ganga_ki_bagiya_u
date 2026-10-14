using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GangaKiBagiya
{
    public enum Phase3RewardStatus
    {
        Upcoming,
        Available,
        Claimed,
        Forfeited
    }

    [Serializable]
    public sealed class Phase3RewardSnapshot
    {
        public int version = 1;
        public long anchorDayTicks;
        public long[] claimedDayTicks = Array.Empty<long>();
        public long[] forfeitedDayTicks = Array.Empty<long>();
    }

    public sealed class Phase3RewardCalendar
    {
        private readonly DateTime anchorDay;
        private readonly HashSet<long> claimedDays = new HashSet<long>();
        private readonly HashSet<long> forfeitedDays = new HashSet<long>();

        public DateTime AnchorDay => anchorDay;

        public Phase3RewardCalendar(DateTime anchorDay)
        {
            this.anchorDay = anchorDay.Date;
        }

        public Phase3RewardStatus GetStatus(DateTime day, DateTime now, Phase2PlantProgression plant)
        {
            long key = day.Date.Ticks;
            if (claimedDays.Contains(key))
                return Phase3RewardStatus.Claimed;
            if (forfeitedDays.Contains(key) || day.Date < anchorDay ||
                now >= day.Date.AddDays(1).AddMinutes(15) || !plant.CanWaterOn(day) && !plant.HasWateredOn(day))
                return Phase3RewardStatus.Forfeited;
            if (day.Date > now.Date || !plant.HasWateredOn(day))
                return Phase3RewardStatus.Upcoming;
            return Phase3RewardStatus.Available;
        }

        public bool TryClaim(DateTime day, DateTime now, Phase2PlantProgression plant)
        {
            if (GetStatus(day, now, plant) != Phase3RewardStatus.Available)
                return false;
            return claimedDays.Add(day.Date.Ticks);
        }

        public bool ApplyClaim(DateTime day, Phase2PlantProgression plant)
        {
            long key = day.Date.Ticks;
            if (!plant.HasWateredOn(day) || forfeitedDays.Contains(key))
                return false;
            return claimedDays.Add(key);
        }

        public bool Forfeit(DateTime day)
        {
            long key = day.Date.Ticks;
            if (claimedDays.Contains(key))
                return false;
            return forfeitedDays.Add(key);
        }

        public DateTime[] GetCurrentCycle(DateTime day)
        {
            if (day.Date < anchorDay)
            {
                DateTime[] future = new DateTime[7];
                for (int i = 0; i < future.Length; i++)
                    future[i] = anchorDay.AddDays(i);
                return future;
            }
            int daysFromAnchor = (day.Date - anchorDay).Days;
            int slot = ((daysFromAnchor % 7) + 7) % 7;
            DateTime firstDay = day.Date.AddDays(-slot);
            DateTime[] dates = new DateTime[7];
            for (int i = 0; i < dates.Length; i++)
                dates[i] = firstDay.AddDays(i);
            return dates;
        }

        public int GetSlotNumber(DateTime day)
        {
            int daysFromAnchor = (day.Date - anchorDay).Days;
            return ((daysFromAnchor % 7) + 7) % 7 + 1;
        }

        public Phase3RewardSnapshot CaptureSnapshot()
        {
            long[] claimed = new long[claimedDays.Count];
            long[] forfeited = new long[forfeitedDays.Count];
            claimedDays.CopyTo(claimed);
            forfeitedDays.CopyTo(forfeited);
            Array.Sort(claimed);
            Array.Sort(forfeited);
            return new Phase3RewardSnapshot
            {
                anchorDayTicks = anchorDay.Ticks,
                claimedDayTicks = claimed,
                forfeitedDayTicks = forfeited
            };
        }

        public bool TryRestoreSnapshot(Phase3RewardSnapshot snapshot)
        {
            if (snapshot == null || snapshot.version != 1 || !IsDayKey(snapshot.anchorDayTicks) ||
                snapshot.anchorDayTicks != anchorDay.Ticks || snapshot.claimedDayTicks == null || snapshot.forfeitedDayTicks == null)
                return false;

            var claimed = new HashSet<long>();
            var forfeited = new HashSet<long>();
            foreach (long day in snapshot.claimedDayTicks)
            {
                if (!IsDayKey(day) || day < anchorDay.Ticks || !claimed.Add(day))
                    return false;
            }
            foreach (long day in snapshot.forfeitedDayTicks)
            {
                if (!IsDayKey(day) || day < anchorDay.Ticks || claimed.Contains(day) || !forfeited.Add(day))
                    return false;
            }

            claimedDays.Clear();
            claimedDays.UnionWith(claimed);
            forfeitedDays.Clear();
            forfeitedDays.UnionWith(forfeited);
            return true;
        }

        private static bool IsDayKey(long ticks)
        {
            return ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks &&
                   new DateTime(ticks).Date.Ticks == ticks;
        }
    }

    [Serializable]
    public sealed class Phase3SaveData
    {
        public int schemaVersion = 1;
        public long writeSequence;
        public long lastJournalSequence;
        public string localProfileId;
        public string profileTimeZoneId;
        public string profileCreatedDay;
        public string lastFinalizedDay;
        public Phase2ProgressionSnapshot progression;
        public Phase3RewardSnapshot rewards;
        public string simulationDay;
        public string runId;
        public Phase1RunSnapshot run;
        public bool resultProcessed;
        public bool wateringApplied;
        public int growthPointsAwarded;
        public float tickAccumulatorSeconds;
        public bool pendingBankRequested;

        public bool IsValid(out string reason)
        {
            if (schemaVersion != 1 || writeSequence < 0 || lastJournalSequence < 0 ||
                string.IsNullOrEmpty(localProfileId) || localProfileId.Length > 64 ||
                string.IsNullOrEmpty(profileTimeZoneId) || profileTimeZoneId.Length > 128 ||
                !TryParseDay(profileCreatedDay, out DateTime created) ||
                !TryParseDay(lastFinalizedDay, out _) ||
                !TryParseDay(simulationDay, out DateTime runDay) ||
                progression == null || progression.version != 1 || progression.stage < 1 || progression.stage > Phase2PlantTuning.StageCount ||
                progression.growthPoints < 0 || progression.health < 0 || progression.health > Phase2PlantTuning.MaximumHealth ||
                progression.wateredDays == null || progression.missedDays == null ||
                rewards == null || rewards.version != 1 || rewards.anchorDayTicks != created.Ticks ||
                rewards.claimedDayTicks == null || rewards.forfeitedDayTicks == null ||
                run == null || run.version != 1 || run.stage < 1 || run.stage > Phase2PlantTuning.StageCount ||
                growthPointsAwarded < 0 || float.IsNaN(tickAccumulatorSeconds) || float.IsInfinity(tickAccumulatorSeconds) ||
                tickAccumulatorSeconds < 0f || tickAccumulatorSeconds > 1f)
            {
                reason = "The local save header or progress fields are invalid.";
                return false;
            }

            if (runDay.Ticks < created.Ticks || run.dailyGoalUnits <= 0 ||
                (run.hasResult && !resultProcessed) ||
                (wateringApplied && (!ProgressionDayIsWatered(runDay.Ticks) || !run.hasResult || run.result.reason == Phase1RunEndReason.HitBeforeGoal)) ||
                (pendingBankRequested && (run.status != Phase1RunStatus.Running || !run.goalSecured)))
            {
                reason = "The saved run does not match the profile progress.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool ProgressionDayIsWatered(long ticks)
        {
            foreach (long wateredDay in progression.wateredDays)
                if (wateredDay == ticks)
                    return true;
            return false;
        }

        public static string FormatDay(DateTime day)
        {
            return day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static bool TryParseDay(string value, out DateTime day)
        {
            bool parsed = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime result);
            day = parsed ? DateTime.SpecifyKind(result.Date, DateTimeKind.Unspecified) : default;
            return parsed;
        }
    }

    [Serializable]
    public sealed class Phase3JournalEvent
    {
        public long sequence;
        public string eventId;
        public string profileId;
        public string eventType;
        public string day;
        public string runId;
        public int endReason;
        public int collectedMilliWater;
        public int retainedMilliWater;
        public int spilledMilliWater;
        public int dailyGoalUnits;
        public int bonusGrowthPoints;

        public bool IsValid()
        {
            if (sequence <= 0 || string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(profileId) ||
                !Phase3SaveData.TryParseDay(day, out _) || string.IsNullOrEmpty(eventType))
                return false;
            if (eventType == "day-finalized" || eventType == "reward-claimed")
                return true;
            return eventType == "watering-completed" && !string.IsNullOrEmpty(runId) &&
                   Enum.IsDefined(typeof(Phase1RunEndReason), endReason) &&
                   (Phase1RunEndReason)endReason != Phase1RunEndReason.HitBeforeGoal &&
                   collectedMilliWater >= 0 && (long)retainedMilliWater >= (long)dailyGoalUnits * Phase1Tuning.WaterScale &&
                   retainedMilliWater <= collectedMilliWater && spilledMilliWater == collectedMilliWater - retainedMilliWater &&
                   dailyGoalUnits > 0 && bonusGrowthPoints >= 0;
        }
    }

    public sealed class Phase3LocalSaveStore
    {
        private const string SnapshotName = "progress-v1.json";
        private const string JournalName = "progress-events-v1.jsonl";
        private readonly string directory;
        private readonly string snapshotPath;
        private readonly string backupPath;
        private readonly string journalPath;
        private bool journalDamaged;

        public bool JournalDamaged => journalDamaged;

        public Phase3LocalSaveStore(string directory)
        {
            this.directory = directory;
            snapshotPath = Path.Combine(directory, SnapshotName);
            backupPath = snapshotPath + ".bak";
            journalPath = Path.Combine(directory, JournalName);
        }

        public bool TryLoad(out Phase3SaveData data, out List<Phase3JournalEvent> pendingEvents,
            out bool recoveredBackup, out bool foundInvalidFiles, out string warning)
        {
            data = null;
            pendingEvents = new List<Phase3JournalEvent>();
            recoveredBackup = false;
            foundInvalidFiles = false;
            warning = string.Empty;

            bool primaryExists = File.Exists(snapshotPath);
            if (primaryExists && TryReadSnapshot(snapshotPath, out data))
            {
                // The primary snapshot is the newest committed state.
            }
            else
            {
                foundInvalidFiles = primaryExists;
                if (File.Exists(backupPath) && TryReadSnapshot(backupPath, out data))
                {
                    recoveredBackup = true;
                    warning = "Recovered the last good local save.";
                }
                else if (File.Exists(backupPath))
                {
                    foundInvalidFiles = true;
                }
            }

            if (data == null)
            {
                if (foundInvalidFiles)
                    warning = "The local save and its backup are invalid; the originals were left untouched.";
                return false;
            }

            List<Phase3JournalEvent> journal = ReadJournal(out bool damaged);
            journalDamaged = damaged;
            if (damaged)
                warning = "The progress journal is damaged; new progress will not overwrite it.";
            for (int i = 0; i < journal.Count; i++)
                if (journal[i].sequence > data.lastJournalSequence)
                    pendingEvents.Add(journal[i]);
            return true;
        }

        public bool Save(Phase3SaveData data, IList<Phase3JournalEvent> events, out string error)
        {
            error = string.Empty;
            if (journalDamaged)
            {
                error = "The progress journal is damaged.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);
                List<Phase3JournalEvent> journal = ReadJournal(out bool damaged);
                if (damaged)
                {
                    journalDamaged = true;
                    error = "The progress journal is damaged.";
                    return false;
                }

                long nextSequence = journal.Count == 0 ? 0 : journal[journal.Count - 1].sequence;
                long snapshotJournalSequence = data.lastJournalSequence;
                var eventsToAppend = new List<Phase3JournalEvent>();
                if (events != null)
                {
                    for (int i = 0; i < events.Count; i++)
                    {
                        Phase3JournalEvent progressEvent = events[i];
                        if (progressEvent == null || !IsValidUnsequencedEvent(progressEvent))
                        {
                            error = "A pending progress event is invalid.";
                            return false;
                        }

                        Phase3JournalEvent existing = FindEvent(journal, progressEvent.eventId);
                        if (existing == null)
                        {
                            progressEvent.sequence = ++nextSequence;
                            journal.Add(CloneEvent(progressEvent));
                            eventsToAppend.Add(progressEvent);
                            existing = progressEvent;
                        }
                        snapshotJournalSequence = Math.Max(snapshotJournalSequence, existing.sequence);
                    }
                }

                if (eventsToAppend.Count > 0 && !AppendJournal(eventsToAppend, out error))
                    return false;
                data.lastJournalSequence = snapshotJournalSequence;

                data.writeSequence++;
                if (!data.IsValid(out error))
                    return false;

                string payload = JsonUtility.ToJson(data);
                var envelope = new SnapshotEnvelope
                {
                    version = 1,
                    payload = payload,
                    checksum = Checksum(payload)
                };
                string tempPath = snapshotPath + ".tmp";
                WriteFlushed(tempPath, Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope)));
                if (File.Exists(snapshotPath))
                {
                    bool currentIsValid = TryReadSnapshot(snapshotPath, out _);
                    File.Replace(tempPath, snapshotPath, currentIsValid ? backupPath : null);
                }
                else
                {
                    File.Move(tempPath, snapshotPath);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private List<Phase3JournalEvent> ReadJournal(out bool damaged)
        {
            var events = new List<Phase3JournalEvent>();
            damaged = false;
            if (!File.Exists(journalPath))
                return events;

            try
            {
                string content = File.ReadAllText(journalPath, Encoding.UTF8);
                string[] lines = content.Split('\n');
                var ids = new HashSet<string>();
                long lastSequence = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Length == 0)
                        continue;
                    Phase3JournalEvent item = null;
                    try
                    {
                        item = JsonUtility.FromJson<Phase3JournalEvent>(line);
                    }
                    catch
                    {
                        // A partial final line is an uncommitted append and can be discarded on the next write.
                    }
                    if (item != null && item.IsValid() && item.sequence == lastSequence + 1 && ids.Add(item.eventId))
                    {
                        events.Add(item);
                        lastSequence = item.sequence;
                        continue;
                    }

                    bool trailingPartialLine = i == lines.Length - 1 && !content.EndsWith("\n", StringComparison.Ordinal);
                    if (!trailingPartialLine)
                        damaged = true;
                    break;
                }
            }
            catch
            {
                damaged = true;
            }
            return events;
        }

        private bool AppendJournal(IList<Phase3JournalEvent> progressEvents, out string error)
        {
            error = string.Empty;
            try
            {
                var content = new StringBuilder();
                for (int i = 0; i < progressEvents.Count; i++)
                    content.Append(JsonUtility.ToJson(progressEvents[i])).Append('\n');
                byte[] lines = Encoding.UTF8.GetBytes(content.ToString());
                using (var stream = new FileStream(journalPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    if (stream.Length > 0)
                    {
                        stream.Position = stream.Length - 1;
                        if (stream.ReadByte() != '\n')
                        {
                            long lastNewline = -1;
                            for (long position = stream.Length - 1; position >= 0; position--)
                            {
                                stream.Position = position;
                                if (stream.ReadByte() == '\n')
                                {
                                    lastNewline = position;
                                    break;
                                }
                            }
                            long lineStart = lastNewline + 1;
                            long lineLength = stream.Length - lineStart;
                            byte[] trailing = new byte[checked((int)lineLength)];
                            stream.Position = lineStart;
                            stream.Read(trailing, 0, trailing.Length);
                            Phase3JournalEvent finalEvent = null;
                            try
                            {
                                finalEvent = JsonUtility.FromJson<Phase3JournalEvent>(Encoding.UTF8.GetString(trailing));
                            }
                            catch
                            {
                                // Discard only the incomplete, never-committed tail before retrying the append.
                            }
                            if (finalEvent != null && finalEvent.IsValid())
                            {
                                stream.Position = stream.Length;
                                stream.WriteByte((byte)'\n');
                            }
                            else
                            {
                                stream.SetLength(lineStart);
                            }
                        }
                    }
                    stream.Position = stream.Length;
                    stream.Write(lines, 0, lines.Length);
                    stream.Flush(true);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool TryReadSnapshot(string path, out Phase3SaveData data)
        {
            data = null;
            try
            {
                SnapshotEnvelope envelope = JsonUtility.FromJson<SnapshotEnvelope>(File.ReadAllText(path, Encoding.UTF8));
                if (envelope == null || envelope.version != 1 || string.IsNullOrEmpty(envelope.payload) ||
                    !string.Equals(envelope.checksum, Checksum(envelope.payload), StringComparison.Ordinal))
                    return false;
                Phase3SaveData parsed = JsonUtility.FromJson<Phase3SaveData>(envelope.payload);
                if (parsed == null || !parsed.IsValid(out _))
                    return false;
                data = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteFlushed(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string Checksum(string payload)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        private static bool IsValidUnsequencedEvent(Phase3JournalEvent item)
        {
            long sequence = item.sequence;
            item.sequence = 1;
            bool valid = item.IsValid();
            item.sequence = sequence;
            return valid;
        }

        private static Phase3JournalEvent FindEvent(List<Phase3JournalEvent> events, string eventId)
        {
            for (int i = 0; i < events.Count; i++)
                if (string.Equals(events[i].eventId, eventId, StringComparison.Ordinal))
                    return events[i];
            return null;
        }

        private static Phase3JournalEvent CloneEvent(Phase3JournalEvent source)
        {
            return JsonUtility.FromJson<Phase3JournalEvent>(JsonUtility.ToJson(source));
        }

        [Serializable]
        private sealed class SnapshotEnvelope
        {
            public int version;
            public string payload;
            public string checksum;
        }
    }
}
