﻿using JetBrains.Annotations;

namespace SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Entities
{
    public class Task
    {
        public Task([NotNull] TaskMetaInformation meta, [NotNull] byte[] data)
        {
            Meta = meta;
            Data = data;
        }

        [NotNull]
        public TaskMetaInformation Meta { get; private set; }

        [NotNull]
        public byte[] Data { get; private set; }
    }
}