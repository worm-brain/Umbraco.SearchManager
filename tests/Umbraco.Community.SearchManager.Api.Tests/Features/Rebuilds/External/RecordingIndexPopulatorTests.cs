using Examine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.External;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.External;

public class RecordingIndexPopulatorTests
{
    private readonly InMemoryRebuildHistory _history = new();
    private readonly CoreRebuildRecorder _recorder;

    public RecordingIndexPopulatorTests() =>
        _recorder = new CoreRebuildRecorder(
            _history,
            Substitute.For<IIndexDiagnosticsFactory>(),
            TimeProvider.System,
            NullLogger<CoreRebuildRecorder>.Instance
        );

    [Fact]
    public void Populate_InnerThrows_RethrowsTheSameException()
    {
        // Arrange
        var failure = new InvalidOperationException("Price missing");
        IIndexPopulator inner = Substitute.For<IIndexPopulator>();
        inner.When(populator => populator.Populate(Arg.Any<IIndex[]>())).Throw(failure);
        var populator = new RecordingIndexPopulator(inner, _recorder);

        // Act
        Exception thrown = Assert.Throws<InvalidOperationException>(() =>
            populator.Populate(Index("ProductIndex"))
        );

        // Assert
        Assert.Same(failure, thrown);
    }

    [Fact]
    public void Populate_InnerThrows_FailsOnlyTheTrackedRegisteredIndexes()
    {
        // Arrange
        IIndex products = Index("ProductIndex");
        IIndex external = Index("ExternalIndex");
        IIndex members = Index("MembersIndex");
        _recorder.Begin(products, RebuildTrigger.Startup);
        _recorder.Begin(members, RebuildTrigger.Startup);
        IIndexPopulator inner = Substitute.For<IIndexPopulator>();
        inner.IsRegistered(products).Returns(true);
        inner.IsRegistered(external).Returns(true);
        inner
            .When(populator => populator.Populate(Arg.Any<IIndex[]>()))
            .Throw(new InvalidOperationException("Price missing"));
        var populator = new RecordingIndexPopulator(inner, _recorder);

        // Act
        Assert.Throws<InvalidOperationException>(() =>
            populator.Populate(products, external, members)
        );
        _recorder.End("ProductIndex", null);
        _recorder.End("MembersIndex", null);

        // Assert
        Assert.Equal(
            [("ProductIndex", RebuildStatus.Failed), ("MembersIndex", RebuildStatus.Succeeded)],
            _history.Entries.Select(entry => (entry.IndexName, entry.Outcome))
        );
    }

    [Fact]
    public void Populate_InnerSucceeds_FailsNothing()
    {
        // Arrange
        IIndex products = Index("ProductIndex");
        _recorder.Begin(products, RebuildTrigger.Other);
        IIndexPopulator inner = Substitute.For<IIndexPopulator>();
        inner.IsRegistered(products).Returns(true);

        // Act
        new RecordingIndexPopulator(inner, _recorder).Populate(products);
        _recorder.End("ProductIndex", null);

        // Assert
        Assert.Equal(RebuildStatus.Succeeded, Assert.Single(_history.Entries).Outcome);
    }

    private static IIndex Index(string name)
    {
        IIndex index = Substitute.For<IIndex>();
        index.Name.Returns(name);
        return index;
    }
}
