using Examine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Runtime;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Models;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.External;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.External;

public class CoreRebuildRecordingComposerTests
{
    [Fact]
    public void Decorate_SingletonRebuilder_ResolvesTheDecoratorOnceWithWrappedPopulators()
    {
        // Arrange
        ServiceCollection services = Services();
        services.AddSingleton<IIndexRebuilder, PopulatorRebuilder>();

        // Act
        CoreRebuildRecordingComposer.Decorate(services);

        // Assert
        using ServiceProvider provider = services.BuildServiceProvider();
        var decorator = (RecordingIndexRebuilder)provider.GetRequiredService<IIndexRebuilder>();
        Assert.Multiple(
            () => Assert.Same(decorator, provider.GetRequiredService<IIndexRebuilder>()),
            () => Assert.True(decorator.PopulatorsWrapped),
            () =>
                Assert.All(
                    ((PopulatorRebuilder)decorator.Inner).Populators,
                    populator => Assert.IsType<RecordingIndexPopulator>(populator)
                )
        );
    }

    [Fact]
    public void Decorate_TransientRebuilder_StaysTransient()
    {
        // Arrange
        ServiceCollection services = Services();
        services.AddTransient<IIndexRebuilder, PopulatorRebuilder>();

        // Act
        CoreRebuildRecordingComposer.Decorate(services);

        // Assert
        Assert.Equal(
            ServiceLifetime.Transient,
            services.Last(service => service.ServiceType == typeof(IIndexRebuilder)).Lifetime
        );
    }

    [Fact]
    public void Decorate_SeveralRegistrations_WrapsTheLastOne()
    {
        // Arrange
        ServiceCollection services = Services();
        services.AddSingleton<IIndexRebuilder, PopulatorRebuilder>();
        services.AddSingleton<IIndexRebuilder, PlainRebuilder>();

        // Act
        CoreRebuildRecordingComposer.Decorate(services);

        // Assert
        using ServiceProvider provider = services.BuildServiceProvider();
        var decorator = (RecordingIndexRebuilder)provider.GetRequiredService<IIndexRebuilder>();
        Assert.Equal(
            (typeof(PlainRebuilder), false),
            (decorator.Inner.GetType(), decorator.PopulatorsWrapped)
        );
    }

    [Fact]
    public void Decorate_NoopRebuilder_IsLeftAlone()
    {
        // Arrange
        ServiceCollection services = Services();
        services.AddSingleton<IIndexRebuilder, NoopIndexRebuilder>();

        // Act
        CoreRebuildRecordingComposer.Decorate(services);

        // Assert
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<NoopIndexRebuilder>(provider.GetRequiredService<IIndexRebuilder>());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IRebuildHistory>(new InMemoryRebuildHistory());
        services.AddSingleton(Substitute.For<IIndexDiagnosticsFactory>());
        services.AddSingleton(Substitute.For<IExamineManager>());
        services.AddSingleton(Substitute.For<IMainDom>());
        services.AddSingleton(Substitute.For<IRuntimeState>());
        services.AddSingleton(Substitute.For<IHostApplicationLifetime>());
        services.AddSingleton<CoreRebuildRecorder>();
        services.AddSingleton(Substitute.For<IIndexPopulator>());
        services.AddSingleton(Substitute.For<IIndexPopulator>());
        return services;
    }

    /// <summary>Takes the populators, as Umbraco's ExamineIndexRebuilder does.</summary>
    private sealed class PopulatorRebuilder(IEnumerable<IIndexPopulator> populators)
        : PlainRebuilder
    {
        public IReadOnlyList<IIndexPopulator> Populators { get; } = [.. populators];
    }

    /// <summary>Named like Umbraco's stand-in for sites without Examine.</summary>
    private sealed class NoopIndexRebuilder : PlainRebuilder;

    /// <summary>A rebuilder that does nothing.</summary>
    private class PlainRebuilder : IIndexRebuilder
    {
        public bool CanRebuild(string indexName) => false;

        [Obsolete("Test double.")]
        public void RebuildIndex(
            string indexName,
            TimeSpan? delay = null,
            bool useBackgroundThread = true
        ) { }

        [Obsolete("Test double.")]
        public void RebuildIndexes(
            bool onlyEmptyIndexes,
            TimeSpan? delay = null,
            bool useBackgroundThread = true
        ) { }

        public Task<Attempt<IndexRebuildResult>> RebuildIndexAsync(
            string indexName,
            TimeSpan? delay = null,
            bool useBackgroundThread = true
        ) => Task.FromResult(Attempt.Succeed(IndexRebuildResult.Success));

        public Task<Attempt<IndexRebuildResult>> RebuildIndexesAsync(
            bool onlyEmptyIndexes,
            TimeSpan? delay = null,
            bool useBackgroundThread = true
        ) => Task.FromResult(Attempt.Succeed(IndexRebuildResult.Success));

        public Task<bool> IsRebuildingAsync(string indexName) => Task.FromResult(false);
    }
}
