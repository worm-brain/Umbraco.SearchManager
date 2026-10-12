using Microsoft.Extensions.Hosting;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>
/// The hosted service that drains the rebuild queue on the server, so rebuilds keep running when
/// the browser leaves. Each wake-up starts whatever <see cref="IRebuildQueue.StartNext"/> allows and
/// runs it in the background; a run finishing wakes it again for the next one.
/// </summary>
internal sealed class RebuildWorker(IRebuildQueue queue, RebuildRunner runner) : BackgroundService
{
    private readonly List<Task> _running = [];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (RebuildRun run in queue.StartNext())
                {
                    _running.Add(
                        Task.Run(() => runner.RunAsync(run, stoppingToken), CancellationToken.None)
                    );
                }

                _running.RemoveAll(task => task.IsCompleted);
                await queue.WaitForWorkAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: let running runs record that they were stopped.
        }

        await Task.WhenAll(_running);
    }
}
