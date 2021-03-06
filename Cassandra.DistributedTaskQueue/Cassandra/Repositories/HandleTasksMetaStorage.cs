﻿using System;
using System.Collections.Generic;
using System.Linq;

using JetBrains.Annotations;

using SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Entities;
using SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Repositories.BlobStorages;
using SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Repositories.Indexes;
using SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Repositories.Indexes.ChildTaskIndex;
using SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Repositories.Indexes.StartTicksIndexes;
using SkbKontur.Cassandra.DistributedTaskQueue.Configuration;
using SkbKontur.Cassandra.DistributedTaskQueue.Profiling;
using SkbKontur.Cassandra.GlobalTimestamp;
using SkbKontur.Cassandra.TimeBasedUuid;

using Vostok.Logging.Abstractions;

namespace SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Repositories
{
    public class HandleTasksMetaStorage : IHandleTasksMetaStorage
    {
        public HandleTasksMetaStorage(
            ITaskMetaStorage taskMetaStorage,
            ITaskMinimalStartTicksIndex minimalStartTicksIndex,
            IEventLogRepository eventLogRepository,
            IGlobalTime globalTime,
            IChildTaskIndex childTaskIndex,
            IRtqTaskDataRegistry taskDataRegistry,
            ILog logger)
        {
            this.taskMetaStorage = taskMetaStorage;
            this.minimalStartTicksIndex = minimalStartTicksIndex;
            this.eventLogRepository = eventLogRepository;
            this.globalTime = globalTime;
            this.childTaskIndex = childTaskIndex;
            this.taskDataRegistry = taskDataRegistry;
            this.logger = logger.ForContext(nameof(HandleTasksMetaStorage));
        }

        [CanBeNull]
        public LiveRecordTicksMarkerState TryGetCurrentLiveRecordTicksMarker([NotNull] TaskIndexShardKey taskIndexShardKey)
        {
            return minimalStartTicksIndex.TryGetCurrentLiveRecordTicksMarker(taskIndexShardKey);
        }

        [NotNull]
        public TaskIndexRecord[] GetIndexRecords(long toTicks, [NotNull] TaskIndexShardKey[] taskIndexShardKeys)
        {
            var liveRecordsByKey = new Dictionary<TaskIndexShardKey, TaskIndexRecord[]>();
            foreach (var taskIndexShardKey in taskIndexShardKeys)
            {
                var liveRecords = minimalStartTicksIndex.GetRecords(taskIndexShardKey, toTicks, batchSize : 2000).Take(10000).ToArray();
                liveRecordsByKey.Add(taskIndexShardKey, liveRecords);
                if (liveRecords.Any())
                    logger.Info("Got {LiveRecordsCount} live minimalStartTicksIndex records for taskIndexShardKey: {TaskIndexShardKey}; Oldest live record: {OldestLiveRecord}",
                                new {LiveRecordsCount = liveRecords.Length, TaskIndexShardKey = taskIndexShardKey, OldestLiveRecord = liveRecords.First()});
            }
            return Shuffle(liveRecordsByKey.SelectMany(x => x.Value).ToArray());
        }

        [NotNull]
        public TaskIndexRecord AddMeta([NotNull] TaskMetaInformation taskMeta, [CanBeNull] TaskIndexRecord oldTaskIndexRecord)
        {
            var metricsContext = MetricsContext.For(taskMeta).SubContext("HandleTasksMetaStorage.AddMeta");
            var globalNowTicks = globalTime.UpdateNowTimestamp().Ticks;
            var nowTicks = Math.Max((taskMeta.LastModificationTicks ?? 0) + PreciseTimestampGenerator.TicksPerMicrosecond, globalNowTicks);
            taskMeta.LastModificationTicks = nowTicks;
            using (metricsContext.Timer("EventLogRepository_AddEvent").NewContext())
                eventLogRepository.AddEvent(taskMeta, eventTimestamp : new Timestamp(nowTicks), eventId : Guid.NewGuid());
            var newIndexRecord = FormatIndexRecord(taskMeta);
            using (metricsContext.Timer("MinimalStartTicksIndex_AddRecord").NewContext())
                minimalStartTicksIndex.AddRecord(newIndexRecord, globalNowTicks, taskMeta.GetTtl());
            if (taskMeta.State == TaskState.New)
            {
                using (metricsContext.Timer("ChildTaskIndex_WriteIndexRecord").NewContext())
                    childTaskIndex.WriteIndexRecord(taskMeta, globalNowTicks);
            }
            using (metricsContext.Timer("TaskMetaStorage_Write").NewContext())
                taskMetaStorage.Write(taskMeta, globalNowTicks);
            if (oldTaskIndexRecord != null)
            {
                using (metricsContext.Timer("MinimalStartTicksIndex_RemoveRecord").NewContext())
                    minimalStartTicksIndex.RemoveRecord(oldTaskIndexRecord, globalNowTicks);
            }
            return newIndexRecord;
        }

        public void ProlongMetaTtl([NotNull] TaskMetaInformation taskMeta)
        {
            var globalNowTicks = globalTime.UpdateNowTimestamp().Ticks;
            minimalStartTicksIndex.WriteRecord(FormatIndexRecord(taskMeta), globalNowTicks, taskMeta.GetTtl());
            childTaskIndex.WriteIndexRecord(taskMeta, globalNowTicks);
            taskMetaStorage.Write(taskMeta, globalNowTicks);
        }

        [NotNull]
        public TaskIndexRecord FormatIndexRecord([NotNull] TaskMetaInformation taskMeta)
        {
            var taskTopic = taskDataRegistry.GetTaskTopic(taskMeta.Name);
            var taskIndexShardKey = new TaskIndexShardKey(taskTopic, taskMeta.State);
            return new TaskIndexRecord(taskMeta.Id, taskMeta.MinimalStartTicks, taskIndexShardKey);
        }

        [NotNull]
        public TaskMetaInformation GetMeta([NotNull] string taskId)
        {
            var meta = taskMetaStorage.Read(taskId);
            if (meta == null)
                throw new InvalidOperationException($"TaskMeta not found for: {taskId}");
            return meta;
        }

        [NotNull]
        public Dictionary<string, TaskMetaInformation> GetMetas([NotNull] string[] taskIds)
        {
            return taskMetaStorage.Read(taskIds);
        }

        [NotNull]
        private static T[] Shuffle<T>([NotNull] T[] array)
        {
            for (var i = 0; i < array.Length; i++)
            {
                var r = i + (int)(ThreadLocalRandom.Instance.NextDouble() * (array.Length - i));
                var t = array[r];
                array[r] = array[i];
                array[i] = t;
            }
            return array;
        }

        private readonly ITaskMetaStorage taskMetaStorage;
        private readonly ITaskMinimalStartTicksIndex minimalStartTicksIndex;
        private readonly IEventLogRepository eventLogRepository;
        private readonly IGlobalTime globalTime;
        private readonly IChildTaskIndex childTaskIndex;
        private readonly IRtqTaskDataRegistry taskDataRegistry;
        private readonly ILog logger;
    }
}