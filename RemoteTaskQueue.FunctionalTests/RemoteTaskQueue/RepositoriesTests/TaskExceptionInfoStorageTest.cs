﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

using MoreLinq;

using NUnit.Framework;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories.BlobStorages;

using SKBKontur.Cassandra.CassandraClient.Abstractions;
using SKBKontur.Catalogue.NUnit.Extensions.EdiTestMachinery;
using SKBKontur.Catalogue.Objects;
using SKBKontur.Catalogue.Objects.TimeBasedUuid;
using SKBKontur.Catalogue.ServiceLib.Logging;

namespace RemoteTaskQueue.FunctionalTests.RemoteTaskQueue.RepositoriesTests
{
    public class TaskExceptionInfoStorageTest : BlobStorageFunctionalTestBase
    {
        [EdiSetUp]
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public void SetUp()
        {
            ResetCassandraState();
        }

        [Test]
        public void Read_NoExceptions()
        {
            var taskMetas = new[]
                {
                    TimeGuidMeta(),
                    TimeGuidMeta().With(x => x.TaskExceptionInfoIds = new List<TimeGuid>()),
                    TimeGuidMeta().With(x => x.TaskExceptionInfoIds = new List<TimeGuid> {TimeGuid.NowGuid()}),
                    LegacyMeta()
                };

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[0], new Exception[0]),
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[1], new Exception[0]),
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[2], new Exception[0]),
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[3], new Exception[0])
                });
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void TryAddDuplicate(MetaType metaType)
        {
            var exception = new Exception("Message");
            var duplicate = new Exception("Message");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception, out ids), Is.True);
            Assert.That(ids.Count, Is.EqualTo(1));

            List<TimeGuid> ids2;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta.With(x => x.TaskExceptionInfoIds = ids), duplicate, out ids2), Is.False);
            Assert.That(ids2, Is.Null);

            Check(new[] {new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception})});
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void TryAddDuplicate_OnlyLastExceptionConsidered(MetaType metaType)
        {
            var exception1 = new Exception("Message");
            var exception2 = new Exception("Message-2");
            var exception3 = new Exception("Message");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception1, out ids), Is.True);
            Assert.That(ids.Count, Is.EqualTo(1));

            List<TimeGuid> ids2;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta.With(x => x.TaskExceptionInfoIds = ids), exception2, out ids2), Is.True);
            Assert.That(ids2.Count, Is.EqualTo(2));

            List<TimeGuid> ids3;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta.With(x => x.TaskExceptionInfoIds = ids2), exception3, out ids3), Is.True);
            Assert.That(ids3.Count, Is.EqualTo(3));

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta.With(x => x.TaskExceptionInfoIds = ids3),
                                                                metaType == MetaType.TimeGuid ? new[] {exception1, exception2, exception3} : new[] {exception3})
                });
        }

        [Test]
        public void Read_Normal()
        {
            var random = new Random(Guid.NewGuid().GetHashCode());

            var metasWithExceptions = new List<Tuple<TaskMetaInformation, Exception[]>>();
            for(var i = 0; i < 100; i++)
            {
                var randomValue = random.Next(0, 2);
                var meta = randomValue == 0 ? TimeGuidMeta() : LegacyMeta();
                var exceptions = new List<Exception>();
                for(var j = 0; j < 20; j++)
                {
                    var e = new Exception("Message-" + Guid.NewGuid().ToString("N"));
                    List<TimeGuid> ids;
                    Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, e, out ids), Is.True);
                    exceptions.Add(e);
                    meta.TaskExceptionInfoIds = ids;
                }
                metasWithExceptions.Add(new Tuple<TaskMetaInformation, Exception[]>(meta, randomValue == 0 ? exceptions.ToArray() : new[] {exceptions.Last()}));
            }

            Check(metasWithExceptions.ToArray());
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void Read_DuplicateMetas(MetaType metaType)
        {
            var exception = new Exception("Message");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception}),
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception}),
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception}),
                });
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void Delete(MetaType metaType)
        {
            var exception1 = new Exception("Message");
            var exception2 = new Exception("Message-2");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception1, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception2, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, metaType == MetaType.TimeGuid ? new[] {exception1, exception2} : new[] {exception2})
                });

            taskExceptionInfoStorage.Delete(meta);

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new Exception[] {})
                });
        }

        [Test]
        public void Achieve_Limit()
        {
            var meta = TimeGuidMeta();
            var exceptions = new List<Exception>();

            for(var i = 0; i < 300; i++)
            {
                var e = new Exception("Message-" + Guid.NewGuid().ToString("N"));
                List<TimeGuid> ids;
                Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, e, out ids), Is.True);
                meta.TaskExceptionInfoIds = ids;
                exceptions.Add(e);
            }

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, exceptions.Take(100).Concat(exceptions.Skip(199)).ToArray()),
                });
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void Ttl(MetaType metaType)
        {
            var exception = new Exception(Guid.NewGuid().ToString());
            var metaTtl = TimeSpan.FromSeconds(5);
            var meta = NewMeta(metaType, metaTtl);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            Assert.That(taskExceptionInfoStorage.Read(new[] {meta})[meta.Id].Single().ExceptionMessageInfo, Is.StringContaining(exception.Message));
            Assert.That(() => taskExceptionInfoStorage.Read(new[] {meta})[meta.Id], Is.Empty.After((int)metaTtl.Multiply(2).TotalMilliseconds, 500));
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void Prolong_OneException(MetaType metaType)
        {
            var exception = new Exception(Guid.NewGuid().ToString());
            var metaTtl = TimeSpan.FromSeconds(5);
            var meta = NewMeta(metaType, metaTtl);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            meta.SetOrUpdateTtl(TimeSpan.FromHours(1));
            taskExceptionInfoStorage.ProlongExceptionInfosTtl(meta);

            Thread.Sleep(metaTtl.Multiply(2));
            Assert.That(taskExceptionInfoStorage.Read(new[] {meta})[meta.Id].Single().ExceptionMessageInfo, Is.StringContaining(exception.Message));
        }

        [Test]
        public void Prolong_SeveralExceptions()
        {
            var exception1 = new Exception(string.Format("Message1-{0}", Guid.NewGuid()));
            var exception2 = new Exception(string.Format("Message2-{0}", Guid.NewGuid()));
            var metaTtl = TimeSpan.FromSeconds(5);

            var sw = Stopwatch.StartNew();
            var meta = NewMeta(MetaType.TimeGuid, metaTtl);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception1, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception2, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            meta.SetOrUpdateTtl(TimeSpan.FromHours(1));
            taskExceptionInfoStorage.ProlongExceptionInfosTtl(meta);

            sw.Stop();
            Log.For(this).InfoFormat("sw.Elapsed: {0}", sw.Elapsed);

            Thread.Sleep(metaTtl.Multiply(2));
            var taskExceptionInfos = taskExceptionInfoStorage.Read(new[] {meta})[meta.Id].Select(x => x.ExceptionMessageInfo).ToArray();
            Assert.That(taskExceptionInfos, Is.EquivalentTo(new[] {exception1.ToString(), exception2.ToString()}), string.Format("sw.Elapsed: {0}", sw.Elapsed));
        }

        private void Check(Tuple<TaskMetaInformation, Exception[]>[] expected)
        {
            var taskExceptionInfos = taskExceptionInfoStorage.Read(expected.Select(x => x.Item1).ToArray());

            Assert.That(taskExceptionInfos.Count, Is.EqualTo(expected.DistinctBy(x => x.Item1.Id).Count()));
            foreach(var tuple in expected)
            {
                Assert.That(taskExceptionInfos[tuple.Item1.Id].Select(info => info.ExceptionMessageInfo).ToArray(),
                            Is.EqualTo(tuple.Item2.Select(exception => exception.ToString()).ToArray()));
            }
        }

        private static TaskMetaInformation TimeGuidMeta(TimeSpan? ttl = null)
        {
            return TaskMeta(TimeGuid.NowGuid().ToGuid().ToString(), ttl);
        }

        private static TaskMetaInformation LegacyMeta(TimeSpan? ttl = null)
        {
            return TaskMeta(Guid.NewGuid().ToString(), ttl);
        }

        private static TaskMetaInformation TaskMeta(string taskId, TimeSpan? ttl)
        {
            var taskMeta = new TaskMetaInformation(string.Format("Name-{0:N}", Guid.NewGuid()), taskId) {MinimalStartTicks = Timestamp.Now.Ticks};
            taskMeta.SetOrUpdateTtl(ttl ?? defaultTtl);
            return taskMeta;
        }

        private static TaskMetaInformation NewMeta(MetaType metaType, TimeSpan? ttl = null)
        {
            return metaType == MetaType.TimeGuid ? TimeGuidMeta(ttl) : LegacyMeta(ttl);
        }

        protected override ColumnFamily[] GetColumnFamilies()
        {
            return TaskExceptionInfoStorage.GetColumnFamilyNames().Select(x => new ColumnFamily {Name = x}).ToArray();
        }

        public enum MetaType
        {
            TimeGuid,
            Legacy
        }

        [Injected]
        private readonly TaskExceptionInfoStorage taskExceptionInfoStorage;

        private static readonly TimeSpan defaultTtl = TimeSpan.FromHours(1);
    }
}