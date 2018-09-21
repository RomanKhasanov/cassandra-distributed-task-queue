﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using GroBuf;

using RemoteQueue.Handling;
using RemoteQueue.LocalTasks.TaskQueue;
using RemoteQueue.Profiling;
using RemoteQueue.Settings;

using SkbKontur.Graphite.Client;

using SKBKontur.Cassandra.CassandraClient.Clusters;
using SKBKontur.Catalogue.ServiceLib.Logging;
using SKBKontur.Catalogue.ServiceLib.Scheduling;

namespace RemoteQueue.Configuration
{
    public class ExchangeSchedulableRunner : IExchangeSchedulableRunner, IDisposable
    {
        public ExchangeSchedulableRunner(
            IExchangeSchedulableRunnerSettings runnerSettings,
            IPeriodicTaskRunner periodicTaskRunner,
            IGraphiteClient graphiteClient,
            ITaskDataRegistry taskDataRegistry,
            ITaskHandlerRegistry taskHandlerRegistry,
            ISerializer serializer,
            ICassandraCluster cassandraCluster,
            IRemoteTaskQueueSettings taskQueueSettings,
            IRemoteTaskQueueProfiler remoteTaskQueueProfiler)
        {
            this.runnerSettings = runnerSettings;
            this.periodicTaskRunner = periodicTaskRunner;
            var taskCounter = new TaskCounter(runnerSettings.MaxRunningTasksCount, runnerSettings.MaxRunningContinuationsCount);
            var remoteTaskQueue = new RemoteTaskQueue(serializer, cassandraCluster, taskQueueSettings, taskDataRegistry, remoteTaskQueueProfiler);
            localTaskQueue = new LocalTaskQueue(taskCounter, taskHandlerRegistry, remoteTaskQueue);
            foreach (var taskTopic in taskHandlerRegistry.GetAllTaskTopicsToHandle())
                handlerManagers.Add(new HandlerManager(taskTopic, runnerSettings.MaxRunningTasksCount, localTaskQueue, remoteTaskQueue.HandleTasksMetaStorage, remoteTaskQueue.GlobalTime));
            reportConsumerStateToGraphiteTask = new ReportConsumerStateToGraphiteTask(graphiteClient, handlerManagers);
            RemoteTaskQueueBackdoor = remoteTaskQueue;
        }

        public void Dispose()
        {
            Stop();
        }

        public void Start()
        {
            if (!started)
            {
                lock (lockObject)
                {
                    if (!started)
                    {
                        RemoteTaskQueueBackdoor.ResetTicksHolderInMemoryState();
                        localTaskQueue.Start();
                        foreach (var handlerManager in handlerManagers)
                            periodicTaskRunner.Register(handlerManager, runnerSettings.PeriodicInterval);
                        periodicTaskRunner.Register(reportConsumerStateToGraphiteTask, TimeSpan.FromMinutes(1));
                        started = true;
                        var handlerManagerIds = string.Join("\r\n", handlerManagers.Select(x => x.Id));
                        Log.For(this).Info($"Start ExchangeSchedulableRunner: schedule handlerManagers[{handlerManagers.Count}] with period {runnerSettings.PeriodicInterval}:\r\n{handlerManagerIds}");
                    }
                }
            }
        }

        public void Stop()
        {
            if (started)
            {
                lock (lockObject)
                {
                    if (started)
                    {
                        Log.For(this).Info("Stopping ExchangeSchedulableRunner");
                        periodicTaskRunner.Unregister(reportConsumerStateToGraphiteTask.Id, 15000);
                        Task.WaitAll(handlerManagers.Select(theHandlerManager => Task.Factory.StartNew(() => { periodicTaskRunner.Unregister(theHandlerManager.Id, 15000); })).ToArray());
                        localTaskQueue.StopAndWait(TimeSpan.FromSeconds(100));
                        RemoteTaskQueueBackdoor.ResetTicksHolderInMemoryState();
                        started = false;
                        Log.For(this).Info("ExchangeSchedulableRunner stopped");
                    }
                }
            }
        }

#pragma warning disable 618
        public IRemoteTaskQueueBackdoor RemoteTaskQueueBackdoor { get; }
#pragma warning restore 618
        private volatile bool started;
        private readonly IExchangeSchedulableRunnerSettings runnerSettings;
        private readonly IPeriodicTaskRunner periodicTaskRunner;
        private readonly LocalTaskQueue localTaskQueue;
        private readonly ReportConsumerStateToGraphiteTask reportConsumerStateToGraphiteTask;
        private readonly object lockObject = new object();
        private readonly List<IHandlerManager> handlerManagers = new List<IHandlerManager>();
    }
}