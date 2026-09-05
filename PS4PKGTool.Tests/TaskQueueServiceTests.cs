using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.TaskQueue;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class TaskQueueServiceTests
{
    [TestMethod]
    public async Task AutoStart_RunsTasksSequentially()
    {
        using var queue = new TaskQueueService();
        var order = new List<string>();
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = new QueuedPackageTask("Test", "First", "", null, async (_, ct) =>
        {
            lock (order) order.Add("first-start");
            await firstMayFinish.Task.WaitAsync(ct);
            lock (order) order.Add("first-end");
            return new QueueTaskExecutionResult(true);
        });
        var second = new QueuedPackageTask("Test", "Second", "", null, (_, _) =>
        {
            lock (order) order.Add("second");
            completed.TrySetResult();
            return Task.FromResult(new QueueTaskExecutionResult(true));
        });

        queue.Enqueue(first);
        queue.Enqueue(second);
        await WaitForAsync(() => first.Status == QueueTaskStatus.Running);
        Assert.AreEqual(QueueTaskStatus.Queued, second.Status);

        firstMayFinish.SetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        CollectionAssert.AreEqual(new[] { "first-start", "first-end", "second" }, order);
        Assert.AreEqual(QueueTaskStatus.Completed, second.Status);
    }

    [TestMethod]
    public async Task ManualMode_WaitsUntilStartNext()
    {
        using var queue = new TaskQueueService { AutoStart = false };
        var task = new QueuedPackageTask("Test", "Manual", "", null, (_, _) => Task.FromResult(new QueueTaskExecutionResult(true)));
        queue.Enqueue(task);
        await Task.Delay(75);
        Assert.AreEqual(QueueTaskStatus.Queued, task.Status);

        queue.StartNext();
        await WaitForAsync(() => task.Status == QueueTaskStatus.Completed);
    }

    [TestMethod]
    public void Cancel_QueuedTask_DoesNotExecute()
    {
        using var queue = new TaskQueueService { AutoStart = false };
        var task = new QueuedPackageTask("Test", "Cancelled", "", null, (_, _) => throw new AssertFailedException("Should not run"));
        queue.Enqueue(task);
        queue.Cancel(task);
        Assert.AreEqual(QueueTaskStatus.Cancelled, task.Status);
    }

    [TestMethod]
    public async Task Retry_InManualMode_RequeuesUntilStartNext()
    {
        using var queue = new TaskQueueService { AutoStart = false };
        var task = new QueuedPackageTask("Test", "Retry", "", null, (_, _) => Task.FromResult(new QueueTaskExecutionResult(true)));
        queue.Enqueue(task);
        queue.Cancel(task);

        queue.Retry(task);

        await Task.Delay(75);
        Assert.AreEqual(QueueTaskStatus.Queued, task.Status);

        queue.StartNext();
        await WaitForAsync(() => task.Status == QueueTaskStatus.Completed);
    }

    [TestMethod]
    public async Task ShutdownAsync_WaitsForCancellationAndPersistsInterruptedState()
    {
        var queue = new TaskQueueService();
        var task = new QueuedPackageTask("Test", "Shutdown", "", null, async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new QueueTaskExecutionResult(true);
        });

        queue.Enqueue(task);
        await WaitForAsync(() => task.Status == QueueTaskStatus.Running);

        await queue.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(QueueTaskStatus.Interrupted, task.Status);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) Assert.Fail("Timed out waiting for queue state.");
            await Task.Delay(15);
        }
    }
}
