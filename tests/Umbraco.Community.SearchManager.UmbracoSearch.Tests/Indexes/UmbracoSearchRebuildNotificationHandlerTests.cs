using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Notifications;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Indexes;

public class UmbracoSearchRebuildNotificationHandlerTests
{
    private readonly UmbracoSearchRebuildTracker _tracker = new();
    private readonly IRebuildHistory _history = Substitute.For<IRebuildHistory>();
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero)
    );
    private readonly UmbracoSearchRebuildNotificationHandler _handler;

    public UmbracoSearchRebuildNotificationHandlerTests()
    {
        var options = new IndexOptions();
        options.RegisterContentIndex<
            IExamineIndexer,
            IExamineSearcher,
            IDraftContentChangeStrategy
        >("Umb_Members", UmbracoObjectTypes.Member);
        options.RegisterContentIndex<
            IExamineIndexer,
            IExamineSearcher,
            IDraftContentChangeStrategy
        >("Umb_Content", UmbracoObjectTypes.Document);
        _handler = new UmbracoSearchRebuildNotificationHandler(
            _tracker,
            new UmbracoSearchExternalRebuildRecorder(
                _history,
                Options.Create(options),
                _time,
                NullLogger<UmbracoSearchExternalRebuildRecorder>.Instance
            )
        );
    }

    [Fact]
    public void Handle_RebuildStartedElsewhere_IsRecordedAsOtherWithItsTimes()
    {
        // Act
        _handler.Handle(new IndexRebuildStartingNotification("Umb_Content"));
        _time.Advance(TimeSpan.FromSeconds(42));
        _handler.Handle(new IndexRebuildCompletedNotification("Umb_Content"));

        // Assert
        _history
            .Received(1)
            .Add(
                Arg.Is<RebuildHistoryEntry>(entry =>
                    entry.IndexName == "Umb_Content"
                    && entry.Engine == UmbracoSearchIndexSource.EngineId
                    && entry.Trigger == RebuildTrigger.Other
                    && entry.TriggeredBy == null
                    && entry.EndedAt - entry.StartedAt == TimeSpan.FromSeconds(42)
                    && entry.Outcome == RebuildStatus.Succeeded
                    && !entry.IsMemberIndex
                )
            );
    }

    [Fact]
    public void Handle_MemberIndexRebuiltElsewhere_IsRecordedAsAMemberIndex()
    {
        // Act
        _handler.Handle(new IndexRebuildStartingNotification("Umb_Members"));
        _handler.Handle(new IndexRebuildCompletedNotification("Umb_Members"));

        // Assert
        _history.Received(1).Add(Arg.Is<RebuildHistoryEntry>(entry => entry.IsMemberIndex));
    }

    [Fact]
    public void Handle_RebuildSearchManagerIsWatching_IsLeftToSearchManager()
    {
        // Arrange
        using UmbracoSearchRebuildTracker.RebuildWatch watch = _tracker.Watch("Umb_Content");

        // Act
        _handler.Handle(new IndexRebuildStartingNotification("Umb_Content"));
        _handler.Handle(new IndexRebuildCompletedNotification("Umb_Content"));

        // Assert
        _history.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public void Handle_CompletionWithoutAStartSeen_IsNotRecorded()
    {
        // Act
        _handler.Handle(new IndexRebuildCompletedNotification("Umb_Content"));

        // Assert
        _history.DidNotReceiveWithAnyArgs().Add(default!);
    }
}
