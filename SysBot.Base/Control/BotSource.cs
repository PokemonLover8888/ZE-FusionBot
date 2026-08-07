using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Base;

public class BotSource<T>(RoutineExecutor<T> Bot)
    where T : class, IConsoleBotConfig
{
    public readonly RoutineExecutor<T> Bot = Bot;

    private CancellationTokenSource Source = new();

    public bool IsPaused { get; private set; }

    public bool IsRunning { get; private set; }

    public bool IsStopping { get; private set; }

    public void Pause()
    {
        if (!IsRunning || IsStopping)
            return;

        IsPaused = true;
        Task.Run(Bot.SoftStop)
            .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
            .ContinueWith(_ => IsPaused = false, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void RebootAndStop()
    {
        Stop();

        Task.Run(() => Bot.RebootAndStopAsync(Source.Token)
            .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
            .ContinueWith(_ => IsRunning = false));

        IsRunning = true;
    }

    public void Restart()
    {
        bool ok = true;
        Task.Run(Bot.Connection.Reset).ContinueWith(task =>
        {
            ok = false;
            ReportFailure(task);
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
        .ContinueWith(_ =>
        {
            if (ok)
                Start();
        }, TaskContinuationOptions.RunContinuationsAsynchronously | TaskContinuationOptions.NotOnFaulted);
    }

    public void Resume()
    {
        Start();
    }

    public void Start()
    {
        if (IsPaused)
            Stop(); // can't soft-resume; just re-launch

        if (IsRunning || IsStopping)
            return;

        IsRunning = true;
        Task.Run(RunWithAutoRestartAsync);
    }

    // Runs the bot routine and AUTO-RESTARTS it if it crashes (e.g. the Switch dropped off WiFi),
    // with a short backoff, until the Switch comes back or the bot is deliberately stopped.
    // Connect() re-inits its socket on failure, so simply re-running the routine is a clean
    // reconnect. Previously a single connect failure logged "Bot has crashed!" and the bot sat
    // dead until someone manually restarted it.
    private async System.Threading.Tasks.Task RunWithAutoRestartAsync()
    {
        int failures = 0;
        while (!Source.IsCancellationRequested && !IsStopping)
        {
            try
            {
                await Bot.RunAsync(Source.Token).ConfigureAwait(false);
                LogUtil.LogError("Bot has stopped without error.", Bot.Connection.Name);
                break; // clean return — not a crash
            }
            catch (System.OperationCanceledException)
            {
                break; // deliberate stop
            }
            catch (System.Exception ex)
            {
                if (Source.IsCancellationRequested || IsStopping)
                    break;

                failures++;
                var ident = Bot.Connection.Name;
                LogUtil.LogError("Bot has crashed!", ident);
                if (!string.IsNullOrEmpty(ex.Message))
                    LogUtil.LogError("Aggregate message: " + ex.Message, ident);

                int delaySec = System.Math.Min(60, 10 * failures); // 10s, 20s … capped at 60s
                LogUtil.LogError($"Auto-reconnecting in {delaySec}s (attempt {failures})…", ident);
                try { await System.Threading.Tasks.Task.Delay(delaySec * 1000, Source.Token).ConfigureAwait(false); }
                catch (System.OperationCanceledException) { break; }
            }
        }
        IsRunning = false;
    }

    public void Stop()
    {
        if (!IsRunning || IsStopping)
            return;

        IsStopping = true;
        Source.Cancel();
        Source = new CancellationTokenSource();

        Task.Run(async () => await Bot.HardStop()
            .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
            .ContinueWith(_ => IsPaused = IsRunning = IsStopping = false));
    }

    private void ReportFailure(Task finishedTask)
    {
        var ident = Bot.Connection.Name;
        var ae = finishedTask.Exception;
        if (ae == null)
        {
            LogUtil.LogError("Bot has stopped without error.", ident);
            return;
        }

        LogUtil.LogError("Bot has crashed!", ident);

        if (!string.IsNullOrEmpty(ae.Message))
            LogUtil.LogError("Aggregate message: " + ae.Message, ident);

        var st = ae.StackTrace;
        if (!string.IsNullOrEmpty(st))
            LogUtil.LogError("Aggregate stacktrace: " + st, ident);

        foreach (var e in ae.InnerExceptions)
        {
            if (!string.IsNullOrEmpty(e.Message))
                LogUtil.LogError("Inner message: " + e.Message, ident);
            LogUtil.LogError("Inner stacktrace: " + e.StackTrace, ident);
        }
    }
}
