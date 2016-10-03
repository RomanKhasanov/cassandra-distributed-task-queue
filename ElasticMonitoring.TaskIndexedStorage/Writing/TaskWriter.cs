﻿using System;
using System.Linq;

using Elasticsearch.Net;

using JetBrains.Annotations;

using log4net;

using RemoteQueue.Cassandra.Entities;

using SKBKontur.Catalogue.Core.ElasticsearchClientExtensions;
using SKBKontur.Catalogue.Core.ElasticsearchClientExtensions.Responses.Bulk;
using SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.TaskIndexedStorage.Utils;
using SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.TaskIndexedStorage.Writing.Contracts;

namespace SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.TaskIndexedStorage.Writing
{
    public class TaskWriter
    {
        public TaskWriter(
            IElasticsearchClientFactory elasticsearchClientFactory,
            IWriteIndexNameFactory indexNameFactory,
            TaskDataService taskDataService)
        {
            this.indexNameFactory = indexNameFactory;
            this.taskDataService = taskDataService;
            elasticsearchClient = elasticsearchClientFactory.GetClient(TaskWriterJsonSettings.GetSerializerSettings());
        }

        public void IndexBatch([NotNull] Tuple<TaskMetaInformation, TaskExceptionInfo[], object>[] batch)
        {
            logger.LogInfoFormat("IndexBatch: {0} tasks", batch.Length);
            var body = new object[batch.Length * 2];
            for(var i = 0; i < batch.Length; i++)
            {
                var meta = batch[i].Item1;
                var taskData = batch[i].Item3;
                var indexName = indexNameFactory.GetIndexForTask(meta);
                body[2 * i] = new
                    {
                        index = new
                            {
                                _index = indexName,
                                _type = meta.Name,
                                _id = meta.Id
                            }
                    };
                body[2 * i + 1] = BuildSavedData(meta, batch[i].Item2, taskData);
            }
            elasticsearchClient.Bulk<BulkResponse>(body).DieIfErros();
        }

        private object BuildSavedData([NotNull] TaskMetaInformation meta, [NotNull] TaskExceptionInfo[] exceptionInfos, [CanBeNull] object taskData)
        {
            var metaIndexedInfo = new MetaIndexedInfo
                {
                    Id = meta.Id,
                    Name = meta.Name,
                    State = meta.State.ToString(),
                    Attempts = meta.Attempts,
                    ParentTaskId = meta.ParentTaskId,
                    TaskGroupLock = meta.TaskGroupLock,
                    EnqueueTime = meta.Ticks,
                    FinishExecutingTime = meta.FinishExecutingTicks,
                    LastModificationTime = meta.LastModificationTicks.Value, //todo hack
                    MinimalStartTime = meta.MinimalStartTicks,
                    StartExecutingTime = meta.StartExecutingTicks,
                    ExpirationTime = meta.ExpirationTimestampTicks ?? 0
                };
            var exceptionInfo = string.Join("\r\n", exceptionInfos.Reverse().Select(x => x.ExceptionMessageInfo));
            return taskDataService.CreateTaskIndexedInfo(metaIndexedInfo, exceptionInfo, taskData);
        }

        private readonly IWriteIndexNameFactory indexNameFactory;
        private readonly TaskDataService taskDataService;

        private readonly IElasticsearchClient elasticsearchClient;

        private static readonly ILog logger = LogManager.GetLogger("TaskWriter");
    }
}