using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Extensions;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.External;

/// <summary>
/// Decorates Umbraco's <see cref="IIndexRebuilder"/> with <see cref="RecordingIndexRebuilder"/>,
/// and its populators with <see cref="RecordingIndexPopulator"/>, so core Examine rebuilds started
/// outside Search Manager are recorded in the history (ADR 0022).
/// </summary>
/// <remarks>
/// Runs after <see cref="AddExamineComposer"/>, which registers the rebuilder on every Umbraco 17
/// and 18 version. The registration is replaced in place, keeping its lifetime: Umbraco 17.0 ends
/// with a transient registration, later versions with a singleton; the recorder that holds the
/// state is a singleton either way.
/// </remarks>
[ComposeAfter(typeof(AddExamineComposer))]
public sealed class CoreRebuildRecordingComposer : IComposer
{
    /// <summary>Umbraco's stand-in when Examine is not installed; there is nothing to record then.</summary>
    internal const string NoopRebuilderName = "NoopIndexRebuilder";

    /// <summary>Adds the recorder and decorates the rebuilder.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<CoreRebuildRecorder>();
        Decorate(builder.Services);
        builder.AddNotificationHandler<
            UmbracoApplicationStartingNotification,
            CoreRebuildRecordingCheck
        >();
    }

    /// <summary>
    /// Replaces the last <see cref="IIndexRebuilder"/> registration (the one Umbraco resolves) with
    /// the decorator around it. Does nothing when there is none, or it is Umbraco's no-op rebuilder.
    /// </summary>
    /// <param name="services">The services.</param>
    internal static void Decorate(IServiceCollection services)
    {
        ServiceDescriptor? descriptor = services.LastOrDefault(service =>
            service.ServiceType == typeof(IIndexRebuilder) && !service.IsKeyedService
        );
        if (descriptor is null || descriptor.ImplementationType?.Name == NoopRebuilderName)
        {
            return;
        }

        services.Remove(descriptor);
        services.Add(
            ServiceDescriptor.Describe(
                typeof(IIndexRebuilder),
                provider => CreateDecorator(provider, descriptor),
                descriptor.Lifetime
            )
        );
    }

    /// <summary>Builds the inner rebuilder from its original registration and wraps it.</summary>
    /// <param name="provider">The services.</param>
    /// <param name="descriptor">The original registration.</param>
    /// <returns>The decorator.</returns>
    private static RecordingIndexRebuilder CreateDecorator(
        IServiceProvider provider,
        ServiceDescriptor descriptor
    )
    {
        CoreRebuildRecorder recorder = provider.GetRequiredService<CoreRebuildRecorder>();
        bool populatorsWrapped = false;
        IIndexRebuilder inner;
        if (descriptor.ImplementationInstance is IIndexRebuilder instance)
        {
            inner = instance;
        }
        else if (descriptor.ImplementationFactory is { } factory)
        {
            inner = (IIndexRebuilder)factory(provider);
        }
        else if (TakesPopulators(descriptor.ImplementationType!))
        {
            // Umbraco's ExamineIndexRebuilder takes IEnumerable<IIndexPopulator> (17.0 to 18.2.1):
            // hand it every registered populator, wrapped, so failures reach the recorder.
            IIndexPopulator[] populators =
            [
                .. provider
                    .GetServices<IIndexPopulator>()
                    .Select(populator => new RecordingIndexPopulator(populator, recorder)),
            ];
            inner = (IIndexRebuilder)
                ActivatorUtilities.CreateInstance(
                    provider,
                    descriptor.ImplementationType!,
                    // One argument: passed bare, the array would spread into params object[].
                    [populators]
                );
            populatorsWrapped = true;
        }
        else
        {
            inner = (IIndexRebuilder)
                ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
        }

        RecordingIndexRebuilder decorator =
            ActivatorUtilities.CreateInstance<RecordingIndexRebuilder>(provider, inner, recorder);
        decorator.PopulatorsWrapped = populatorsWrapped;
        return decorator;
    }

    /// <summary>Whether a public constructor of the type takes the populators.</summary>
    /// <param name="type">The rebuilder type.</param>
    /// <returns><see langword="true"/> when the populators can be handed over wrapped.</returns>
    private static bool TakesPopulators(Type type) =>
        type.GetConstructors()
            .Any(constructor =>
                constructor
                    .GetParameters()
                    .Any(parameter =>
                        parameter.ParameterType == typeof(IEnumerable<IIndexPopulator>)
                    )
            );
}

/// <summary>
/// Logs at startup when core Examine rebuilds started outside Search Manager cannot be recorded
/// (fully or in part), so a silent gap in the history has an explanation in the log.
/// </summary>
/// <param name="rebuilder">The rebuilder Umbraco resolves.</param>
/// <param name="logger">The logger.</param>
internal sealed class CoreRebuildRecordingCheck(
    IIndexRebuilder rebuilder,
    ILogger<CoreRebuildRecordingCheck> logger
) : INotificationHandler<UmbracoApplicationStartingNotification>
{
    /// <inheritdoc />
    public void Handle(UmbracoApplicationStartingNotification notification)
    {
        if (rebuilder is not RecordingIndexRebuilder decorator)
        {
            // Umbraco's no-op rebuilder (no Examine) has nothing to record.
            if (rebuilder.GetType().Name != CoreRebuildRecordingComposer.NoopRebuilderName)
            {
                logger.RebuilderNotDecorated(
                    rebuilder.GetType().FullName ?? rebuilder.GetType().Name
                );
            }

            return;
        }

        if (!decorator.PopulatorsWrapped)
        {
            logger.PopulatorsNotWrapped(
                decorator.Inner.GetType().FullName ?? decorator.Inner.GetType().Name
            );
        }
    }
}
