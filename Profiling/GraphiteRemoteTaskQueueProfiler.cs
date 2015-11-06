﻿using System;

using GroboContainer.Infection;

using JetBrains.Annotations;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Handling.HandlerResults;
using RemoteQueue.Profiling;

using SKBKontur.Catalogue.Core.Graphite.Client.StatsD;
using SKBKontur.Catalogue.Core.Graphite.Client.StatsTimer;

namespace SKBKontur.Catalogue.RemoteTaskQueue.Profiling
{
    [IgnoredImplementation]
    public class GraphiteRemoteTaskQueueProfiler : IRemoteTaskQueueProfiler
    {
        public GraphiteRemoteTaskQueueProfiler(
            [NotNull] IGraphiteRemoteTaskQueueProfilerSettings settings,
            [NotNull] ICatalogueStatsDClient statsDClient,
            [NotNull] IStatsTimerClient statsTimerClient)
        {
            if(string.IsNullOrWhiteSpace(settings.KeyNamePrefix))
            {
                keyNamePrefix = "";
                this.statsDClient = EmptyStatsDClient.Instance;
                this.statsTimerClient = EmptyStatsTimerClient.Instance;
            }
            else
            {
                keyNamePrefix = settings.KeyNamePrefix;
                this.statsDClient = statsDClient
                    .WithScopes(new[]
                        {
                            string.Format("{0}.{1}", settings.KeyNamePrefix, Environment.MachineName),
                            string.Format("{0}.{1}", settings.KeyNamePrefix, "Total")
                        });
                this.statsTimerClient = statsTimerClient;
            }
        }

        public void ProcessTaskCreation<T>(TaskMetaInformation meta, T taskData)
        {
        }

        public void ProcessTaskCancel(TaskMetaInformation meta)
        {
        }

        public void ProcessTaskCreation([NotNull] TaskMetaInformation meta)
        {
            statsDClient.Increment("TasksQueued." + meta.Name);
        }

        public void ProcessTaskEnqueueing([NotNull] TaskMetaInformation meta)
        {
            statsTimerClient.TimingBegin(string.Format("{0}_{1}", meta.Id, meta.Attempts + 1), GetStatisticsKey(meta));
        }

        public void ProcessTaskDequeueing([NotNull] TaskMetaInformation meta)
        {
            statsTimerClient.TimingEnd(string.Format("{0}_{1}", meta.Id, meta.Attempts), GetStatisticsKey(meta));
        }

        public void ProcessTaskExecutionFinished(TaskMetaInformation meta, HandleResult handleResult, TimeSpan taskExecutionTime)
        {
            statsDClient.Timing("ExecutionTime." + meta.Name, (long)taskExecutionTime.TotalMilliseconds);
            statsDClient.Increment("TasksExecuted." + meta.Name + "." + handleResult.FinishAction);
        }

        public void ProcessTaskExecutionFailed(TaskMetaInformation meta, Exception e)
        {
        }

        [NotNull]
        private string GetStatisticsKey([NotNull] TaskMetaInformation meta)
        {
            return string.Format("{0}.Total.WaitingInQueue.{1}", keyNamePrefix, meta.Name);
        }

        private readonly ICatalogueStatsDClient statsDClient;
        private readonly IStatsTimerClient statsTimerClient;
        private readonly string keyNamePrefix;
    }
}