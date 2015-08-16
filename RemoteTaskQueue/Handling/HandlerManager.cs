﻿using System;
using System.Linq;

using log4net;

using MoreLinq;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories;
using RemoteQueue.Cassandra.Repositories.Indexes;
using RemoteQueue.LocalTasks.TaskQueue;

namespace RemoteQueue.Handling
{
    public class HandlerManager : IHandlerManager
    {
        public HandlerManager(
            ITaskQueue taskQueue,
            ITaskCounter taskCounter,
            Func<Tuple<string, ColumnInfo>, TaskMetaInformation, long, HandlerTask> createHandlerTask,
            TaskHandlerCollection taskHandlerCollection,
            IHandleTasksMetaStorage handleTasksMetaStorage)
        {
            this.taskQueue = taskQueue;
            this.taskCounter = taskCounter;
            this.createHandlerTask = createHandlerTask;
            this.taskHandlerCollection = taskHandlerCollection;
            this.handleTasksMetaStorage = handleTasksMetaStorage;
        }

        public void Run()
        {
            lock(lockObject)
            {
                var nowTicks = DateTime.UtcNow.Ticks;
                var taskInfoBatches = handleTasksMetaStorage
                    .GetAllTasksInStates(nowTicks, TaskState.New, TaskState.WaitingForRerun, TaskState.InProcess, TaskState.WaitingForRerunAfterError)
                    .Batch(100, Enumerable.ToArray);
                if(logger.IsDebugEnabled)
                    logger.DebugFormat("Начали обработку очереди.");
                foreach(var taskInfoBatch in taskInfoBatches)
                {
                    var metas = handleTasksMetaStorage.GetMetasQuiet(taskInfoBatch.Select(x => x.Item1).ToArray());
                    for(var i = 0; i < taskInfoBatch.Length; i++)
                    {
                        var meta = metas[i];
                        var taskInfo = taskInfoBatch[i];
                        if(!taskCounter.CanQueueTask(TaskQueueReason.PullFromQueue))
                            return;
                        QueueTask(taskInfo, meta, nowTicks, TaskQueueReason.PullFromQueue);
                    }
                }
            }
        }

        public string Id { get { return "HandlerManager"; } }

        public void Start()
        {
            taskQueue.Start();
        }

        public void Stop()
        {
            if(!started)
                return;
            started = false;
            taskQueue.StopAndWait(100 * 1000);
        }

        public long GetQueueLength()
        {
            return taskQueue.GetQueueLength();
        }

        public Tuple<long, long> GetCassandraQueueLength()
        {
            var allTasksInStates = handleTasksMetaStorage.GetAllTasksInStates(DateTime.UtcNow.Ticks, TaskState.New, TaskState.WaitingForRerun, TaskState.InProcess, TaskState.WaitingForRerunAfterError);
            long all = allTasksInStates.Count();
            return new Tuple<long, long>(all, all);
        }

        private void QueueTask(Tuple<string, ColumnInfo> taskInfo, TaskMetaInformation meta, long nowTicks, TaskQueueReason reason)
        {
            if(meta != null && !taskHandlerCollection.ContainsHandlerFor(meta.Name))
                return;
            var handlerTask = createHandlerTask(taskInfo, meta, nowTicks);
            handlerTask.Reason = reason;
            if(meta == null || reason == TaskQueueReason.PullFromQueue || (reason == TaskQueueReason.TaskContinuation && meta.State == TaskState.New))
                taskQueue.QueueTask(handlerTask);
        }

        private static readonly ILog logger = LogManager.GetLogger(typeof(HandlerManager));
        private readonly Func<Tuple<string, ColumnInfo>, TaskMetaInformation, long, HandlerTask> createHandlerTask;
        private readonly TaskHandlerCollection taskHandlerCollection;
        private readonly IHandleTasksMetaStorage handleTasksMetaStorage;
        private readonly object lockObject = new object();
        private readonly ITaskQueue taskQueue;
        private readonly ITaskCounter taskCounter;
        private volatile bool started;
    }
}