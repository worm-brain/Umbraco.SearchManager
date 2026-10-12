namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>
/// Reports a rebuilding index's document count once a second, for the run's progress estimate.
/// Public so source packages (the Umbraco Search adapter) report progress the same way.
/// </summary>
public static class DocumentCountPoller
{
    /// <summary>How often the count is read.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Starts polling in the background until <paramref name="stop"/> is cancelled. A count that
    /// cannot be read (null, or the reader throws) is skipped; polling never fails the rebuild.
    /// </summary>
    /// <param name="readCount">Reads the current count, or null when unknown.</param>
    /// <param name="progress">Receives each count.</param>
    /// <param name="timeProvider">Drives the interval.</param>
    /// <param name="stop">Stops polling.</param>
    /// <returns>The polling task, which completes once stopped.</returns>
    public static Task StartAsync(
        Func<long?> readCount,
        IProgress<long> progress,
        TimeProvider timeProvider,
        CancellationToken stop
    )
    {
        ArgumentNullException.ThrowIfNull(readCount);
        ArgumentNullException.ThrowIfNull(progress);

        return Task.Run(
            async () =>
            {
                using var timer = new PeriodicTimer(Interval, timeProvider);
                try
                {
                    while (await timer.WaitForNextTickAsync(stop))
                    {
                        long? count = TryRead(readCount);
                        if (count is not null)
                        {
                            progress.Report(count.Value);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stopped: the rebuild finished.
                }
            },
            CancellationToken.None
        );
    }

    /// <summary>Reads the count, treating any failure as unknown.</summary>
    /// <param name="readCount">The reader.</param>
    /// <returns>The count, or null.</returns>
    private static long? TryRead(Func<long?> readCount)
    {
        try
        {
            return readCount();
        }
#pragma warning disable CA1031 // A count the index cannot give right now is simply skipped.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
