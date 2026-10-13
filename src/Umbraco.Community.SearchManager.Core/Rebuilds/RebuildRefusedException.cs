namespace Umbraco.Community.SearchManager.Core.Rebuilds;

/// <summary>
/// Thrown by an index source when it refuses to start a rebuild (the index is missing, cannot be
/// rebuilt, or is already being rebuilt), before anything in the index has changed.
/// </summary>
/// <remarks>
/// The runner records a refused run as failed with no document count, which tells it apart from a
/// rebuild that failed part-way and left the index incomplete.
/// </remarks>
public sealed class RebuildRefusedException : InvalidOperationException
{
    /// <summary>Creates the exception with no message.</summary>
    public RebuildRefusedException() { }

    /// <summary>Creates the exception with the reason shown to the user.</summary>
    /// <param name="message">Why the rebuild was refused.</param>
    public RebuildRefusedException(string message)
        : base(message) { }

    /// <summary>Creates the exception with the reason and the exception behind it.</summary>
    /// <param name="message">Why the rebuild was refused.</param>
    /// <param name="innerException">What caused the refusal.</param>
    public RebuildRefusedException(string message, Exception innerException)
        : base(message, innerException) { }
}
