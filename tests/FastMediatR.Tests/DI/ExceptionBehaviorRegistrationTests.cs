using FastMediatR;
using FastMediatR.Extensions.DI;
using FastMediatR.Tests.DI.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Tests.DI;

/// <summary>
/// Unit tests verifying that <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}"/> registers
/// the exception-handling pipeline behaviors and orders them according to the configured
/// <see cref="RequestExceptionActionProcessorStrategy"/>.
/// </summary>
public sealed class ExceptionBehaviorRegistrationTests
{
    private static IReadOnlyList<Type?> PipelineImplementationOrder(IServiceCollection services)
        => [.. services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)];

    /// <summary>
    /// Verifies that both exception behaviors are registered and, with the default strategy
    /// (<see cref="RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions"/>), the action
    /// behavior is registered before (outside) the processor behavior.
    /// </summary>
    [Fact]
    public void AddFastMediatR_DefaultStrategy_RegistersActionOutsideProcessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
#pragma warning disable IL2026, IL3050 // RequiresUnreferencedCode/RequiresDynamicCode — intentional in test context
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026, IL3050

        // Assert
        var order = PipelineImplementationOrder(services);
        int actionIndex = order.ToList().IndexOf(typeof(RequestExceptionActionProcessorBehavior<,>));
        int processorIndex = order.ToList().IndexOf(typeof(RequestExceptionProcessorBehavior<,>));

        actionIndex.Should().BeGreaterThanOrEqualTo(0);
        processorIndex.Should().BeGreaterThanOrEqualTo(0);
        actionIndex.Should().BeLessThan(processorIndex, "action behavior must wrap the processor for the default strategy");
    }

    /// <summary>
    /// Verifies that with <see cref="RequestExceptionActionProcessorStrategy.ApplyForAllExceptions"/> the
    /// processor behavior is registered before (outside) the action behavior, so actions always run.
    /// </summary>
    [Fact]
    public void AddFastMediatR_ApplyForAllExceptions_RegistersProcessorOutsideAction()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
#pragma warning disable IL2026, IL3050
        services.AddFastMediatR<TestMarker>(cfg =>
            cfg.RequestExceptionActionProcessorStrategy =
                RequestExceptionActionProcessorStrategy.ApplyForAllExceptions);
#pragma warning restore IL2026, IL3050

        // Assert
        var order = PipelineImplementationOrder(services);
        int actionIndex = order.ToList().IndexOf(typeof(RequestExceptionActionProcessorBehavior<,>));
        int processorIndex = order.ToList().IndexOf(typeof(RequestExceptionProcessorBehavior<,>));

        processorIndex.Should().BeLessThan(actionIndex, "processor behavior must wrap the action for ApplyForAllExceptions");
    }

    /// <summary>
    /// Verifies that the exception behaviors are registered after user open behaviors but before the
    /// pre/post-processor behaviors (so they catch exceptions from the handler and pre/post processors).
    /// </summary>
    [Fact]
    public void AddFastMediatR_ExceptionBehaviors_SitBetweenUserBehaviorsAndProcessors()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
#pragma warning disable IL2026, IL3050
        services.AddFastMediatR<TestMarker>(cfg =>
            cfg.AddOpenBehavior(typeof(PassThroughBehavior<,>)));
#pragma warning restore IL2026, IL3050

        // Assert
        var order = PipelineImplementationOrder(services).ToList();
        int userIndex = order.IndexOf(typeof(PassThroughBehavior<,>));
        int actionIndex = order.IndexOf(typeof(RequestExceptionActionProcessorBehavior<,>));
        int preIndex = order.IndexOf(typeof(RequestPreProcessorBehavior<,>));

        userIndex.Should().BeGreaterThanOrEqualTo(0);
        actionIndex.Should().BeGreaterThan(userIndex, "exception behaviors come after user behaviors");
        preIndex.Should().BeGreaterThan(actionIndex, "pre-processor behavior is innermost relative to exception behaviors");
    }

    /// <summary>
    /// Verifies that repeated <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}"/> calls
    /// (e.g. two marker types on one service collection) do not stack duplicate built-in behaviors —
    /// otherwise exception actions would run twice per failure.
    /// </summary>
    [Fact]
    public void AddFastMediatR_CalledTwice_RegistersBuiltInBehaviorsOnce()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
#pragma warning disable IL2026, IL3050
        services.AddFastMediatR<TestMarker>();
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026, IL3050

        // Assert — every built-in behavior appears exactly once.
        Type[] builtIns =
        [
            typeof(RequestExceptionActionProcessorBehavior<,>),
            typeof(RequestExceptionProcessorBehavior<,>),
            typeof(RequestPreProcessorBehavior<,>),
            typeof(RequestPostProcessorBehavior<,>),
        ];

        IReadOnlyList<Type?> order = PipelineImplementationOrder(services);
        foreach (Type behavior in builtIns)
        {
            order.Count(t => t == behavior).Should()
                .Be(1, "{0} must be registered exactly once even when AddFastMediatR runs twice", behavior.Name);
        }
    }

    /// <summary>
    /// Verifies that re-declaring the marker assembly via <c>RegisterHandlersFromAssembly</c> — and a second
    /// <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}"/> call — does not duplicate the
    /// closed handler/action interface mappings; a double scan would make every exception action (and
    /// pre/post-processor) run twice per request.
    /// </summary>
    [Fact]
    public void AddFastMediatR_SameAssemblyScannedRepeatedly_RegistersClosedMappingsOnce()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — marker assembly plus the same assembly re-declared, then the whole call repeated.
#pragma warning disable IL2026, IL3050
        services.AddFastMediatR<TestMarker>(cfg => cfg.RegisterHandlersFromAssemblyContaining<TestMarker>());
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026, IL3050

        // Assert — each discovered closed exception-handler/action mapping exists exactly once.
        services.Count(d => d.ServiceType == typeof(IRequestExceptionHandler<StrategyRequest, string, HandledStrategyException>))
            .Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IRequestExceptionAction<StrategyRequest, StrategyException>))
            .Should().Be(1);
    }
}

/// <summary>Open-generic pass-through behavior used to assert pipeline ordering.</summary>
internal sealed class PassThroughBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        return next(cancellationToken);
    }
}
