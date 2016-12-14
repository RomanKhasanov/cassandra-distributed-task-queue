﻿using GroboContainer.Infection;

using RemoteQueue.Configuration;

using RemoteTaskQueue.FunctionalTests.Common.TaskDatas;
using RemoteTaskQueue.FunctionalTests.Common.TaskDatas.MonitoringTestTaskData;

namespace RemoteTaskQueue.FunctionalTests.Common
{
    [IgnoredImplementation]
    public class TestTaskDataRegistry : TaskDataRegistryBase
    {
        public TestTaskDataRegistry()
        {
            Register<FakeFailTaskData>();
            Register<FakePeriodicTaskData>();
            Register<FakeMixedPeriodicAndFailTaskData>();
            Register<SimpleTaskData>();
            Register<ByteArrayTaskData>();
            Register<ByteArrayAndNestedTaskData>();
            Register<FileIdTaskData>();

            Register<SlowTaskData>();
            Register<AlphaTaskData>();
            Register<BetaTaskData>();
            Register<DeltaTaskData>();
            Register<FailingTaskData>();

            Register<ChainTaskData>();
        }
    }
}