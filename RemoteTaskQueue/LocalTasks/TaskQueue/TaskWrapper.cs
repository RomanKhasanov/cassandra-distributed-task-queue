using System;

using JetBrains.Annotations;

using log4net;

using RemoteQueue.Handling;

namespace RemoteQueue.LocalTasks.TaskQueue
{
    public class TaskWrapper
    {
        public TaskWrapper([NotNull] string taskId, [NotNull] HandlerTask handlerTask, [NotNull] LocalTaskQueue localTaskQueue)
        {
            this.taskId = taskId;
            this.handlerTask = handlerTask;
            this.localTaskQueue = localTaskQueue;
            finished = false;
        }

        public bool Finished { get { return finished; } }

        public void Run()
        {
            try
            {
                handlerTask.RunTask();
            }
            catch(Exception e)
            {
                logger.Error("������ �� ����� ��������� ����������� ������.", e);
            }
            try
            {
                finished = true;
                localTaskQueue.TaskFinished(taskId);
            }
            catch(Exception e)
            {
                logger.Warn("������ �� ����� ��������� ������.", e);
            }
        }

        private readonly string taskId;
        private readonly HandlerTask handlerTask;
        private readonly LocalTaskQueue localTaskQueue;
        private volatile bool finished;
        private readonly ILog logger = LogManager.GetLogger(typeof(LocalTaskQueue));
    }
}