using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NPoco;
using NPoco.FluentMappings;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Persistence.Sqlite.Services;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.History;

/// <summary>
/// A real Umbraco database on a temporary SQLite file, built the way Umbraco's database factory
/// builds one (SQLite syntax provider, NPoco poco data), so migrations and repositories run the SQL
/// a SQLite site runs. The file is deleted on dispose.
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"searchmanager-tests-{Guid.NewGuid():N}.sqlite"
    );

    public SqliteTestDatabase()
    {
        var syntax = new SqliteSyntaxProvider(
            Options.Create(new GlobalSettings()),
            NullLogger<SqliteSyntaxProvider>.Instance
        );
        var mappers = new MapperCollection();
        var pocoDataFactory = new FluentPocoDataFactory(
            (type, _) => new PocoDataBuilder(type, mappers).Init(),
            mappers
        );
        SqlContext = new SqlContext(syntax, DatabaseType.SQLite, pocoDataFactory);
        Database = new UmbracoDatabase(
            $"Data Source={_path};Pooling=False",
            SqlContext,
            SqliteFactory.Instance,
            NullLogger<UmbracoDatabase>.Instance,
            bulkSqlInsertProvider: null,
            databaseSchemaCreatorFactory: null!
        );
    }

    public ISqlContext SqlContext { get; }

    public UmbracoDatabase Database { get; }

    /// <summary>A scope provider whose every scope uses this database.</summary>
    public IScopeProvider ScopeProvider()
    {
        IScope scope = Substitute.For<IScope>();
        scope.Database.Returns(Database);
        scope.SqlContext.Returns(SqlContext);
        IScopeProvider provider = Substitute.For<IScopeProvider>();
        provider.CreateScope().ReturnsForAnyArgs(scope);
        return provider;
    }

    /// <summary>A migration context on this database.</summary>
    public IMigrationContext MigrationContext()
    {
        IMigrationContext context = Substitute.For<IMigrationContext>();
        context.Database.Returns(Database);
        context.SqlContext.Returns(SqlContext);
        context.Logger.Returns(NullLogger<IMigrationContext>.Instance);
        return context;
    }

    public void Dispose()
    {
        Database.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}
