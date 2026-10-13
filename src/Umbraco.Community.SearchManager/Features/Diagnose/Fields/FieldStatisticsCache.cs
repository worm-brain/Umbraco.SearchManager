using System.Collections.Concurrent;
using System.Globalization;
using Examine.Lucene.Providers;
using Lucene.Net.Index;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Fields;

/// <summary>
/// Keeps the last field statistics read per target until the index changes (ADR 0018). The key is
/// the physical index and its reader's version and size: every commit Examine makes visible (a
/// save, a delete, a rebuild) opens a new reader with a new version, so a stale entry is never
/// served. Reads are single-flight: callers asking while a read runs wait for that read.
/// </summary>
/// <remarks>
/// Public so source packages (the Umbraco Search adapter) cache their physical indexes the same
/// way. Registered as a singleton; one entry per target, so memory stays bounded by the number
/// of indexes.
/// </remarks>
public sealed class FieldStatisticsCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Returns the target's statistics for the index as searches see it now, reading them when
    /// the index changed since the last read.
    /// </summary>
    /// <param name="target">The target the statistics are for, the cache key.</param>
    /// <param name="index">The Lucene index to read (for Umbraco Search, the active physical index).</param>
    /// <param name="read">Reads the statistics from a reader; runs on a pool thread.</param>
    /// <param name="cancellationToken">
    /// Stops waiting; a read already running carries on for the next caller.
    /// </param>
    /// <returns>The statistics.</returns>
    /// <exception cref="InvalidOperationException">The index has no Lucene searcher.</exception>
    public async Task<LuceneFieldReading> GetAsync(
        string target,
        LuceneIndex index,
        Func<IndexReader, CancellationToken, LuceneFieldReading> read,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(read);

        if (index.Searcher is not BaseLuceneSearcher searcher)
        {
            throw new InvalidOperationException($"{index.Name} has no Lucene searcher");
        }

        Task<LuceneFieldReading> reading;
        using (var reference = searcher.GetSearchContext().GetSearcher())
        {
            IndexReader reader = reference.IndexSearcher.IndexReader;
            if (reader is not DirectoryReader directory)
            {
                // Without a version there is nothing to key on, so every request reads.
                return read(reader, cancellationToken);
            }

            string version = string.Create(
                CultureInfo.InvariantCulture,
                $"{index.Name}|{directory.Version}|{directory.MaxDoc}"
            );
            Entry entry = _entries.AddOrUpdate(
                target,
                _ => Start(directory, version, read),
                (_, existing) =>
                    existing.Version == version && !existing.Reading.IsFaulted
                        ? existing
                        : Start(directory, version, read)
            );
            reading = entry.Reading;
        }

        return await reading.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a read on its own reference to the reader, so it can finish even when the caller
    /// that started it stops waiting and releases its own.
    /// </summary>
    /// <param name="reader">The reader, still referenced by the caller.</param>
    /// <param name="version">The cache key the reading is for.</param>
    /// <param name="read">The read.</param>
    /// <returns>The entry.</returns>
    private static Entry Start(
        DirectoryReader reader,
        string version,
        Func<IndexReader, CancellationToken, LuceneFieldReading> read
    )
    {
        // Lazy so the AddOrUpdate race that discards a factory result never starts a read. The
        // factory runs inside the creating caller's using block (GetAsync reads Reading before it
        // releases its reference), so the reader is open when the read takes its own reference.
        var lazy = new Lazy<Task<LuceneFieldReading>>(
            () =>
            {
                reader.IncRef();
                return Task.Run(() =>
                {
                    try
                    {
                        return read(reader, CancellationToken.None);
                    }
                    finally
                    {
                        reader.DecRef();
                    }
                });
            },
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        return new Entry(version, lazy);
    }

    /// <summary>One target's last read.</summary>
    /// <param name="Version">The physical index, reader version and size it was read at.</param>
    /// <param name="Lazy">The read, started on first use.</param>
    private sealed record Entry(string Version, Lazy<Task<LuceneFieldReading>> Lazy)
    {
        /// <summary>The read, started on first use.</summary>
        public Task<LuceneFieldReading> Reading => Lazy.Value;
    }
}
