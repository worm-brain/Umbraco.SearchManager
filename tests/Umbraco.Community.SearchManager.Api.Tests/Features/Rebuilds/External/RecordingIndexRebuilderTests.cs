using Examine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Runtime;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Models;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.External;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.External;

public class RecordingIndexRebuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly IIndexRebuilder _inner = Substitute.For<IIndexRebuilder>();
    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly IIndexDiagnosticsFactory _diagnostics =
        Substitute.For<IIndexDiagnosticsFactory>();
    private readonly IMainDom _mainDom = Substitute.For<IMainDom>();
    private readonly IRuntimeState _runtimeState = Substitute.For<IRuntimeState>();
    private readonly InMemoryRebuildHistory _history = new();
    private readonly CoreRebuildRecorder _recorder;

    public RecordingIndexRebuilderTests()
    {
        _mainDom.IsMainDom.Returns(true);
        _runtimeState.Level.Returns(RuntimeLevel.Run);
        _inner
            .RebuildIndexAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>())
            .Returns(Attempt.Succeed(IndexRebuildResult.Success));
        _inner
            .RebuildIndexesAsync(Arg.Any<bool>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>())
            .Returns(Attempt.Succeed(IndexRebuildResult.Success));
        _recorder = new CoreRebuildRecorder(
            _history,
            _diagnostics,
            new FakeTimeProvider(Now),
            NullLogger<CoreRebuildRecorder>.Instance
        );
    }

    [Fact]
    public async Task RebuildIndexAsync_ForegroundRebuildStartedElsewhere_RecordsItAsOther()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);

        // Act
        await Rebuilder().RebuildIndexAsync("ExternalIndex", useBackgroundThread: false);

        // Assert
        Assert.Equal(
            new RebuildHistoryEntry(
                "ExternalIndex",
                "examine",
                Environment.MachineName,
                RebuildTrigger.Other,
                null,
                Now,
                Now,
                RebuildStatus.Succeeded,
                10,
                12,
                null,
                false
            ),
            Assert.Single(_history.Entries)
        );
    }

    [Fact]
    public async Task RebuildIndexAsync_InnerThrowsWrapped_RecordsTheInnerMessageAndRethrows()
    {
        // Arrange
        AddIndex("ProductIndex", 10, 4);
        _inner
            .RebuildIndexAsync("ProductIndex", Arg.Any<TimeSpan?>(), false)
            .ThrowsAsync(new AggregateException(new InvalidOperationException("Price missing")));

        // Act
        await Assert.ThrowsAsync<AggregateException>(() =>
            Rebuilder().RebuildIndexAsync("ProductIndex", useBackgroundThread: false)
        );

        // Assert
        RebuildHistoryEntry entry = Assert.Single(_history.Entries);
        Assert.Equal(
            (RebuildStatus.Failed, "Price missing", (long?)4),
            (entry.Outcome, entry.Error, entry.DocumentsAfter)
        );
    }

    [Fact]
    public async Task RebuildIndexAsync_SearchManagerRebuild_PassesThroughUnrecorded()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);

        // Act
        using (SearchManagerRebuildScope.Begin())
        {
            await Rebuilder().RebuildIndexAsync("ExternalIndex", useBackgroundThread: false);
        }

        // Assert
        Assert.Empty(_history.Entries);
    }

    [Fact]
    public async Task RebuildIndexAsync_UmbracoRefuses_RecordsNothingAndReturnsTheRefusal()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);
        _inner
            .RebuildIndexAsync("ExternalIndex", Arg.Any<TimeSpan?>(), false)
            .Returns(Attempt.Fail(IndexRebuildResult.AlreadyRebuilding));

        // Act
        Attempt<IndexRebuildResult> attempt = await Rebuilder()
            .RebuildIndexAsync("ExternalIndex", useBackgroundThread: false);

        // Assert
        Assert.Equal(
            (false, IndexRebuildResult.AlreadyRebuilding, 0),
            (attempt.Success, attempt.Result, _history.Entries.Count)
        );
    }

    [Fact]
    public async Task RebuildIndexAsync_AlreadyRebuilding_PassesThroughUnrecorded()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);
        _inner.IsRebuildingAsync("ExternalIndex").Returns(true);

        // Act
        await Rebuilder().RebuildIndexAsync("ExternalIndex", useBackgroundThread: true);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(_history.Entries),
            () => _inner.Received(1).RebuildIndexAsync("ExternalIndex", null, true)
        );
    }

    [Fact]
    public async Task RebuildIndexAsync_NotMainDom_PassesThroughUnrecorded()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);
        _mainDom.IsMainDom.Returns(false);

        // Act
        await Rebuilder().RebuildIndexAsync("ExternalIndex", useBackgroundThread: false);

        // Assert
        Assert.Empty(_history.Entries);
    }

    [Fact]
    public async Task RebuildIndexAsync_Background_ReturnsAtOnceAndRecordsWhenUmbracoFinishes()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);
        var finish = new TaskCompletionSource<Attempt<IndexRebuildResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _inner.RebuildIndexAsync("ExternalIndex", Arg.Any<TimeSpan?>(), false).Returns(finish.Task);
        RecordingIndexRebuilder rebuilder = Rebuilder();

        // Act
        Attempt<IndexRebuildResult> attempt = await rebuilder.RebuildIndexAsync(
            "ExternalIndex",
            useBackgroundThread: true
        );
        int recordedBeforeFinish = _history.Entries.Count;
        finish.SetResult(Attempt.Succeed(IndexRebuildResult.Success));
        await rebuilder.WhenBackgroundIdleAsync();

        // Assert
        Assert.Equal((true, 0, 1), (attempt.Success, recordedBeforeFinish, _history.Entries.Count));
    }

    [Fact]
    public async Task IsRebuildingAsync_WhileATrackedRunIsGoing_IsTrue()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);
        var finish = new TaskCompletionSource<Attempt<IndexRebuildResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _inner.RebuildIndexAsync("ExternalIndex", Arg.Any<TimeSpan?>(), false).Returns(finish.Task);
        RecordingIndexRebuilder rebuilder = Rebuilder();
        Task<Attempt<IndexRebuildResult>> rebuild = rebuilder.RebuildIndexAsync(
            "ExternalIndex",
            useBackgroundThread: false
        );

        // Act
        bool rebuilding = await rebuilder.IsRebuildingAsync("ExternalIndex");
        finish.SetResult(Attempt.Succeed(IndexRebuildResult.Success));
        await rebuild;

        // Assert
        Assert.True(rebuilding);
    }

    [Fact]
    public async Task RebuildIndexesAsync_FirstDelayedCall_RecordsStartupForEmptyRebuildableIndexesOnly()
    {
        // Arrange
        AddIndex("ExternalIndex", 0, 50, exists: true);
        AddIndex("InternalIndex", 70, 70, exists: true);
        AddIndex("ProductIndex", 0, 5, exists: false);
        AddIndex("CustomIndex", 0, 0, exists: false, canRebuild: false);

        // Act
        await Rebuilder()
            .RebuildIndexesAsync(true, TimeSpan.FromMilliseconds(1), useBackgroundThread: false);

        // Assert
        Assert.Equal(
            [("ExternalIndex", RebuildTrigger.Startup), ("ProductIndex", RebuildTrigger.Startup)],
            _history.Entries.Select(entry => (entry.IndexName, entry.Trigger))
        );
    }

    [Fact]
    public async Task RebuildIndexesAsync_StartupRunOfAnIndexWithNothingToIndex_IsNotRecorded()
    {
        // Arrange
        AddIndex("MembersIndex", 0, 0, exists: true);
        AddIndex("ProductIndex", 0, 60, exists: false);

        // Act
        await Rebuilder()
            .RebuildIndexesAsync(true, TimeSpan.FromMilliseconds(1), useBackgroundThread: false);

        // Assert
        Assert.Equal(["ProductIndex"], _history.Entries.Select(entry => entry.IndexName));
    }

    [Fact]
    public async Task RebuildIndexesAsync_FailedStartupRunThatWroteNothing_IsRecorded()
    {
        // Arrange
        IIndex index = AddIndex("ProductIndex", 0, 0, exists: false);
        _inner
            .RebuildIndexesAsync(Arg.Any<bool>(), Arg.Any<TimeSpan?>(), false)
            .Returns(_ =>
            {
                _recorder.Fail(index.Name, "Price missing");
                return Attempt.Succeed(IndexRebuildResult.Success);
            });

        // Act
        await Rebuilder()
            .RebuildIndexesAsync(true, TimeSpan.FromMilliseconds(1), useBackgroundThread: false);

        // Assert
        RebuildHistoryEntry entry = Assert.Single(_history.Entries);
        Assert.Equal(
            (RebuildTrigger.Startup, RebuildStatus.Failed, (long?)0),
            (entry.Trigger, entry.Outcome, entry.DocumentsAfter)
        );
    }

    [Fact]
    public async Task RebuildIndexAsync_OtherRunThatWroteNothing_IsRecorded()
    {
        // Arrange
        AddIndex("MembersIndex", 0, 0);

        // Act
        await Rebuilder().RebuildIndexAsync("MembersIndex", useBackgroundThread: false);

        // Assert
        Assert.Equal(RebuildTrigger.Other, Assert.Single(_history.Entries).Trigger);
    }

    [Fact]
    public async Task RebuildIndexesAsync_SecondDelayedCall_RecordsOther()
    {
        // Arrange
        AddIndex("ExternalIndex", 0, 50, exists: true);
        RecordingIndexRebuilder rebuilder = Rebuilder();
        await rebuilder.RebuildIndexesAsync(
            false,
            TimeSpan.FromMilliseconds(1),
            useBackgroundThread: false
        );

        // Act
        await rebuilder.RebuildIndexesAsync(
            false,
            TimeSpan.FromMilliseconds(1),
            useBackgroundThread: false
        );

        // Assert
        Assert.Equal(RebuildTrigger.Other, _history.Entries[^1].Trigger);
    }

    [Fact]
    public async Task RebuildIndexAsync_IndexingErrorDuringRebuild_RecordsFailed()
    {
        // Arrange
        IIndex index = AddIndex("ProductIndex", 10, 9);
        _inner
            .RebuildIndexAsync("ProductIndex", Arg.Any<TimeSpan?>(), false)
            .Returns(_ =>
            {
                index.IndexingError += Raise.EventWith(
                    new IndexingErrorEventArgs(index, "Value cannot be null", "1234", null)
                );
                return Attempt.Succeed(IndexRebuildResult.Success);
            });

        // Act
        await Rebuilder().RebuildIndexAsync("ProductIndex", useBackgroundThread: false);

        // Assert
        RebuildHistoryEntry entry = Assert.Single(_history.Entries);
        Assert.Equal((RebuildStatus.Failed, "Value cannot be null"), (entry.Outcome, entry.Error));
    }

    [Fact]
    public async Task RebuildIndexAsync_HistoryCannotBeWritten_StillReturnsUmbracosAnswer()
    {
        // Arrange
        AddIndex("ExternalIndex", 10, 12);
        _history.AddThrows = new InvalidOperationException("Database is locked");

        // Act
        Attempt<IndexRebuildResult> attempt = await Rebuilder()
            .RebuildIndexAsync("ExternalIndex", useBackgroundThread: false);

        // Assert
        Assert.True(attempt.Success);
    }

    private RecordingIndexRebuilder Rebuilder() =>
        new(
            _inner,
            _recorder,
            _examineManager,
            _mainDom,
            _runtimeState,
            Substitute.For<IHostApplicationLifetime>(),
            NullLogger<RecordingIndexRebuilder>.Instance
        );

    /// <summary>Registers an index whose diagnostics count <paramref name="before"/> then <paramref name="after"/>.</summary>
    private IIndex AddIndex(
        string name,
        long before,
        long after,
        bool exists = true,
        bool canRebuild = true
    )
    {
        IIndex index = Substitute.For<IIndex, IIndexStats>();
        index.Name.Returns(name);
        index.IndexExists().Returns(exists);
        ((IIndexStats)index).GetDocumentCount().Returns(before);

        IIndexDiagnostics diagnostics = Substitute.For<IIndexDiagnostics>();
        diagnostics.GetDocumentCount().Returns(before, after);
        _diagnostics.Create(index).Returns(diagnostics);

        _inner.CanRebuild(name).Returns(canRebuild);
        _examineManager
            .TryGetIndex(name, out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = index;
                return true;
            });
        IIndex[] existing = [.. _examineManager.Indexes ?? []];
        _examineManager.Indexes.Returns([.. existing, index]);
        return index;
    }
}
