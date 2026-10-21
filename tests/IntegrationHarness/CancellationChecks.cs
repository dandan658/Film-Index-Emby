using StrmAssistant.Common;

namespace FilmIndex.IntegrationTests;

internal static class CancellationChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        using var permit = new SemaphoreSlim(1, 1);
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        var work = ScheduledWork.Run(async pending =>
        {
            await permit.WaitAsync();
            pending.Add(Task.Run(async () => { try { await gate.Task; } finally { permit.Release(); } }, CancellationToken.None));
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }, () => completed = true);
        check(!work.IsCompleted && permit.CurrentCount == 0 && !completed,
            "Canceled task launch waits for already-owned work instead of reporting completion with a held permit");
        gate.SetResult();
        var canceled = false;
        try { await work; } catch (OperationCanceledException) { canceled = true; }
        check(canceled && completed && permit.CurrentCount == 1,
            "Cancellation propagates only after pending work releases its semaphore and resets the running flag");
        completed = false;
        var failed = false;
        try { await ScheduledWork.Run(pending => { pending.Add(Task.CompletedTask); throw new InvalidDataException("fixture"); }, () => completed = true); }
        catch (InvalidDataException) { failed = true; }
        check(failed && completed, "A failed launch still drains pending work and resets its running state");
    }
}
