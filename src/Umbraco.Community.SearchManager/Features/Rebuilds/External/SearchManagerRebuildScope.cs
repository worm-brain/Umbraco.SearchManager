namespace Umbraco.Community.SearchManager.Features.Rebuilds.External;

/// <summary>
/// Marks the async flow of a rebuild Search Manager itself runs, so <see cref="RecordingIndexRebuilder"/>
/// passes it straight through: the queue records it already, with the user who asked.
/// </summary>
/// <remarks>
/// Uses <see cref="AsyncLocal{T}"/>, so the mark follows awaits but cannot leak into rebuilds
/// started elsewhere: Umbraco's background queue and long-running operations suppress
/// execution-context flow.
/// </remarks>
internal static class SearchManagerRebuildScope
{
    private static readonly AsyncLocal<bool> Active = new();

    /// <summary>Whether the current flow is inside a Search Manager rebuild.</summary>
    public static bool IsActive => Active.Value;

    /// <summary>Marks the current flow until the result is disposed.</summary>
    /// <returns>Restores the previous mark when disposed.</returns>
    public static IDisposable Begin()
    {
        bool previous = Active.Value;
        Active.Value = true;
        return new Restore(previous);
    }

    /// <summary>Puts the previous mark back.</summary>
    /// <param name="previous">The mark before <see cref="Begin"/>.</param>
    private sealed class Restore(bool previous) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => Active.Value = previous;
    }
}
