﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using GroBuf;

using log4net;

using RemoteQueue.Handling;
using RemoteQueue.LocalTasks.TaskQueue;
using RemoteQueue.Profiling;
using RemoteQueue.Settings;
using RemoteQueue.UserClasses;

using SKBKontur.Cassandra.CassandraClient.Clusters;
using SKBKontur.Catalogue.ServiceLib.Scheduling;

namespace RemoteQueue.Configuration
{
    public class ExchangeSchedulableRunner : IExchangeSchedulableRunner, IDisposable
    {
        public ExchangeSchedulableRunner(
            IExchangeSchedulableRunnerSettings runnerSettings,
            IPeriodicTaskRunner periodicTaskRunner,
            TaskHandlerRegistryBase taskHandlerRegistry,
            ISerializer serializer,
            ICassandraCluster cassandraCluster,
            ICassandraSettings cassandraSettings,
            IRemoteTaskQueueSettings taskQueueSettings,
            ITaskDataTypeToNameMapper taskDataTypeToNameMapper,
            ITaskTopicResolver taskTopicResolver,
            IRemoteTaskQueueProfiler remoteTaskQueueProfiler)
        {
            this.runnerSettings = runnerSettings;
            this.periodicTaskRunner = periodicTaskRunner;
            var taskCounter = new TaskCounter(runnerSettings.MaxRunningTasksCount, runnerSettings.MaxRunningContinuationsCount);
            var taskHandlerCollection = new TaskHandlerCollection(taskDataTypeToNameMapper, taskHandlerRegistry);
            var remoteTaskQueue = new RemoteTaskQueue(serializer, cassandraCluster, cassandraSettings, taskQueueSettings, taskDataTypeToNameMapper, taskTopicResolver, remoteTaskQueueProfiler);
            var localTaskQueue = new LocalTaskQueue(taskCounter, taskHandlerCollection, remoteTaskQueue);
            handlerManagers.Add(new HandlerManager(string.Empty, runnerSettings.MaxRunningTasksCount, localTaskQueue, remoteTaskQueue.HandleTasksMetaStorage, remoteTaskQueue.GlobalTime));
            foreach(var taskTopic in taskTopicResolver.GetAllTaskTopics())
                handlerManagers.Add(new HandlerManager(taskTopic, runnerSettings.MaxRunningTasksCount, localTaskQueue, remoteTaskQueue.HandleTasksMetaStorage, remoteTaskQueue.GlobalTime));
        }

        public void Dispose()
        {
            Stop();
        }

        public void Stop()
        {
            if(started)
            {
                lock(lockObject)
                {
                    if(started)
                    {
                        Task.WaitAll(handlerManagers.Select(theHandlerManager => Task.Factory.StartNew(() =>
                            {
                                periodicTaskRunner.Unregister(theHandlerManager.Id, 15000);
                                theHandlerManager.Stop();
                            })).ToArray());
                        started = false;
                        logger.Info("Stop ExchangeSchedulableRunner.");
                    }
                }
            }
        }

        public void Start()
        {
            if(!started)
            {
                lock(lockObject)
                {
                    if(!started)
                    {
                        foreach(var handlerManager in handlerManagers)
                        {
                            handlerManager.Start();
                            periodicTaskRunner.Register(handlerManager, runnerSettings.PeriodicInterval);
                        }
                        started = true;
                        logger.InfoFormat("Start ExchangeSchedulableRunner: schedule handlerManagers[{0}] with period {1}:\r\n{2}", handlerManagers.Count, runnerSettings.PeriodicInterval, string.Join("\r\n", handlerManagers.Select(x => x.Id)));
                    }
                }
            }
        }

        private volatile bool started;
        private readonly IExchangeSchedulableRunnerSettings runnerSettings;
        private readonly IPeriodicTaskRunner periodicTaskRunner;
        private readonly object lockObject = new object();
        private readonly List<IHandlerManager> handlerManagers = new List<IHandlerManager>();
        private static readonly ILog logger = LogManager.GetLogger(typeof(ExchangeSchedulableRunner));
    }
}