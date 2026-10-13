using Examine;
using Examine.Lucene;
using Examine.Lucene.Directories;
using Examine.Lucene.Providers;
using Lucene.Net.Analysis;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Explore;

/// <summary>
/// A real Examine <see cref="LuceneIndex"/> in memory, with Umbraco's field types for the fields
/// the tests query, so ranges, analysis and parsing behave as on a site.
/// </summary>
internal sealed class TestLuceneIndex : IDisposable
{
    private readonly KeptRamDirectory _directory;
    private bool _ownsDirectory = true;

    /// <summary>Creates the index and adds the documents synchronously.</summary>
    /// <param name="name">The index name.</param>
    /// <param name="documents">The documents.</param>
    public TestLuceneIndex(string name, params ValueSet[] documents)
        : this(name, (IValueSetValidator?)null, documents) { }

    /// <summary>Creates the index with a value set validator and adds the documents synchronously.</summary>
    /// <param name="name">The index name.</param>
    /// <param name="validator">The validator, as Umbraco's indexes have; null for none.</param>
    /// <param name="documents">The documents, which the validator must let in.</param>
    public TestLuceneIndex(string name, IValueSetValidator? validator, params ValueSet[] documents)
        : this(name, validator, null, documents) { }

    /// <summary>Creates the index with the analyzer an Umbraco index sets, and adds the documents synchronously.</summary>
    /// <param name="name">The index name.</param>
    /// <param name="analyzer">The index's analyzer, as Umbraco sets it per index; null for Examine's default.</param>
    /// <param name="documents">The documents.</param>
    public TestLuceneIndex(string name, Analyzer? analyzer, params ValueSet[] documents)
        : this(name, null, analyzer, documents) { }

    private TestLuceneIndex(
        string name,
        IValueSetValidator? validator,
        Analyzer? analyzer,
        ValueSet[] documents
    )
        : this(name, new KeptRamDirectory(), validator, analyzer)
    {
        using (Index.WithThreadingMode(IndexThreadingMode.Synchronous))
        {
            Index.IndexItems(documents);
        }
    }

    private TestLuceneIndex(
        string name,
        KeptRamDirectory directory,
        IValueSetValidator? validator = null,
        Analyzer? analyzer = null
    )
    {
        _directory = directory;
        var options = new LuceneDirectoryIndexOptions
        {
            DirectoryFactory = new GenericDirectoryFactory(_ => _directory),
            Validator = validator,
            Analyzer = analyzer,
            FieldDefinitions = new FieldDefinitionCollection(
                new FieldDefinition("level", FieldDefinitionTypes.Integer),
                new FieldDefinition("price", FieldDefinitionTypes.Double),
                new FieldDefinition("updateDate", FieldDefinitionTypes.DateTime),
                new FieldDefinition("__Published", FieldDefinitionTypes.Raw),
                new FieldDefinition("__VariesByCulture", FieldDefinitionTypes.Raw),
                new FieldDefinition("__Key", FieldDefinitionTypes.InvariantCultureIgnoreCase)
            ),
        };
        IOptionsMonitor<LuceneDirectoryIndexOptions> monitor = Substitute.For<
            IOptionsMonitor<LuceneDirectoryIndexOptions>
        >();
        monitor.Get(name).Returns(options);

        Index = new LuceneIndex(NullLoggerFactory.Instance, name, monitor);
    }

    /// <summary>
    /// Closes this index and opens a new one over the same files, which has written nothing
    /// itself, as after a site restart. Dispose the new one before this one.
    /// </summary>
    /// <returns>The reopened index.</returns>
    public TestLuceneIndex Reopen()
    {
        Index.Dispose();
        return new TestLuceneIndex(Index.Name, _directory) { _ownsDirectory = false };
    }

    /// <summary>The index.</summary>
    public LuceneIndex Index { get; }

    /// <summary>Builds a content document as Umbraco would index it.</summary>
    /// <param name="id">The node id.</param>
    /// <param name="type">The document type alias.</param>
    /// <param name="name">The node name.</param>
    /// <param name="level">The level.</param>
    /// <param name="updated">The update date.</param>
    /// <param name="extra">More fields.</param>
    /// <returns>The value set.</returns>
    public static ValueSet Content(
        int id,
        string type,
        string name,
        int level,
        DateTime updated,
        Dictionary<string, object>? extra = null
    )
    {
        var values = new Dictionary<string, object>
        {
            ["nodeName"] = name,
            ["level"] = level,
            ["updateDate"] = updated,
            ["__Published"] = "y",
            ["__VariesByCulture"] = "n",
            ["__Key"] = $"00000000-0000-0000-0000-{id:D12}",
        };
        foreach ((string key, object value) in extra ?? [])
        {
            values[key] = value;
        }

        return new ValueSet(
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "content",
            type,
            values
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Index.Dispose();
        if (_ownsDirectory)
        {
            _directory.Release();
        }
    }

    /// <summary>
    /// A RAM directory that outlives the index using it: <see cref="LuceneIndex"/> disposes its
    /// directory, and <see cref="Reopen"/> needs the files afterwards.
    /// </summary>
    private sealed class KeptRamDirectory : RAMDirectory
    {
        private bool _released;

        /// <summary>Frees the files; the index's own dispose does not.</summary>
        public void Release()
        {
            _released = true;
            Dispose();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            // Kept until Release.
            if (_released)
            {
                base.Dispose(disposing);
            }
        }
    }
}
