using System;
using System.Linq;

using GroBuf;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories.BlobStorages;
using RemoteQueue.Handling;

using SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.TaskIndexedStorage.Writing;

namespace SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.Core.Implementation
{
    public class TaskMetaProcessor : ITaskMetaProcessor
    {
        public TaskMetaProcessor(ITaskDataTypeToNameMapper taskDataTypeToNameMapper, ITaskDataBlobStorage taskDataStorage, TaskWriter writer, ISerializer serializer)
        {
            this.taskDataTypeToNameMapper = taskDataTypeToNameMapper;
            this.taskDataStorage = taskDataStorage;
            this.writer = writer;
            this.serializer = serializer;
        }

        public void IndexMetas(TaskMetaInformation[] batch)
        {
            var taskDatas = taskDataStorage.ReadQuiet(batch.Select(m => m.Id).ToArray());
            var taskDataObjects = new object[taskDatas.Length];
            for(var i = 0; i < batch.Length; i++)
            {
                var taskData = taskDatas[i];
                Type taskType;
                object taskDataObj = null;
                if(taskDataTypeToNameMapper.TryGetTaskType(batch[i].Name, out taskType))
                    taskDataObj = serializer.Deserialize(taskType, taskData);
                taskDataObjects[i] = taskDataObj;
            }
            if(batch.Length > 0)
                writer.IndexBatch(batch, taskDataObjects);
        }

        private readonly ITaskDataTypeToNameMapper taskDataTypeToNameMapper;

        private readonly ITaskDataBlobStorage taskDataStorage;
        private readonly TaskWriter writer;
        private readonly ISerializer serializer;
    }
}