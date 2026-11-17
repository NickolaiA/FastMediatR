using FastMediatR;
using FastMediatR.Extensions.DI;
using FastMediatR.Tests.DI.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Tests.DI;

/// <summary>
/// End-to-end pipeline tests that combine the real <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}"/>
/// wiring (descriptors → DI → behavior chain) with exception handlers/actions and assert the
/// <em>runtime</em> semantics of each <see cref="RequestExceptionActionProcessorStrategy"/>.
/// The descriptor-order tests in <see cref="ExceptionBehaviorRegistrationTests"/> prove <em>what</em> is
/// registered; these prove the resolved <c>IEnumerable&lt;IPipelineBehavior&lt;TRequest,TResponse&gt;&gt;</c>
/// chain actually nests as documented (first-registered = outermost).
/// </summary>
public sealed class ExceptionPipelineStrategyTests
{
    /// <summary>
    /// Builds the pipeline exactly the way the mediator composes it: resolve the behaviors from a scope
    /// (MS.DI preserves registration order, outermost first) and wrap the terminal delegate — standing in
    /// for the handler and throwing <paramref name="exception"/> — in each behavior.
    /// </summary>
    private static RequestHandlerDelegate<string> BuildPipeline(
        IServiceProvider scopeProvider,
        StrategyRequest request,
        Exception exception)
    {
        var behaviors = scopeProvider.GetServices<IPipelineBehavior<StrategyRequest, string>>().ToList();
        behaviors.Should().HaveCountGreaterThanOrEqualTo(2, "both exception behaviors must be registered");

        RequestHandlerDelegate<string> chain = _ => Task.FromException<string>(exception);
        for (int i = behaviors.Count - 1; i >= 0; i--)
        {
            IPipelineBehavior<StrategyRequest, string> behavior = behaviors[i];
            RequestHandlerDelegate<string> inner = chain;
            chain = ct => behavior.Handle(request, inner, ct);
        }

        return chain;
    }

    private static ServiceProvider BuildProvider(Action<FastMediatRConfig>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StrategyActionRecorder());
#pragma warning disable IL2026, IL3050 // RequiresUnreferencedCode/RequiresDynamicCode — intentional in test context
        services.AddFastMediatR<TestMarker>(configure);
#pragma warning restore IL2026, IL3050
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Verifies the default strategy end-to-end: when a handler suppresses the exception, the action
    /// behavior (registered outside the processor) never sees it, so actions do <em>not</em> run and the
    /// fallback response is returned.
    /// </summary>
    [Fact]
    public async Task Pipeline_DefaultStrategy_SuppressedException_DoesNotRunActions()
    {
        // Arrange
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<StrategyActionRecorder>();
        var request = new StrategyRequest("order-1");
        var chain = BuildPipeline(scope.ServiceProvider, request, new HandledStrategyException("declined"));

        // Act
        string response = await chain(CancellationToken.None);

        // Assert — the handler suppressed the exception before the outer action behavior could observe it.
        response.Should().Be(StrategySuppressionHandler.Fallback);
        recorder.ExecuteCount.Should().Be(0, "actions must not run for suppressed exceptions by default");
    }

    /// <summary>
    /// Verifies the default strategy for unhandled exceptions: the outer action behavior runs every
    /// matching action exactly once and the original exception is rethrown unchanged.
    /// </summary>
    [Fact]
    public async Task Pipeline_DefaultStrategy_UnhandledException_RunsActionsAndRethrowsOriginal()
    {
        // Arrange
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<StrategyActionRecorder>();
        var request = new StrategyRequest("order-2");
        var original = new UnhandledStrategyException("boom");
        var chain = BuildPipeline(scope.ServiceProvider, request, original);

        // Act
        Func<Task> act = () => chain(CancellationToken.None);

        // Assert — same instance rethrown, action observed it once.
        (await act.Should().ThrowAsync<UnhandledStrategyException>()).Which.Should().BeSameAs(original);
        recorder.ExecuteCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies <see cref="RequestExceptionActionProcessorStrategy.ApplyForAllExceptions"/> end-to-end:
    /// the action behavior is inside the processor, so actions run even though a handler still suppresses
    /// the exception and the fallback response wins.
    /// </summary>
    [Fact]
    public async Task Pipeline_ApplyForAllExceptions_SuppressedException_StillRunsActions()
    {
        // Arrange
        using ServiceProvider provider = BuildProvider(cfg =>
            cfg.RequestExceptionActionProcessorStrategy =
                RequestExceptionActionProcessorStrategy.ApplyForAllExceptions);
        using IServiceScope scope = provider.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<StrategyActionRecorder>();
        var request = new StrategyRequest("order-3");
        var chain = BuildPipeline(scope.ServiceProvider, request, new HandledStrategyException("declined"));

        // Act
        string response = await chain(CancellationToken.None);

        // Assert — actions observed the failure, the handler still supplied the fallback.
        response.Should().Be(StrategySuppressionHandler.Fallback);
        recorder.ExecuteCount.Should().Be(1, "ApplyForAllExceptions runs actions even when a handler suppresses");
    }
}

// ── Fixtures ──────────────────────────────────────────────────────────────────
//
// The request type is unique to these tests: AddFastMediatR<TestMarker> scans the whole test assembly,
// so the handler/action classes below are auto-registered only for StrategyRequest and can never
// interfere with exception-handling tests built on other request types.
//
// WARNING — the flip side: these concrete fixture classes are scanned into EVERY test collection in
// this assembly that calls AddFastMediatR<TestMarker>(). Anything there that resolves ALL registered
// IRequestExceptionAction<,> services (or turns on container ValidateOnBuild) must also register a
// StrategyActionRecorder, or resolving StrategyRecordingAction fails with a missing dependency that
// looks unrelated to this file.

/// <summary>Request used exclusively by the pipeline-strategy tests.</summary>
internal sealed record StrategyRequest(string Id) : IRequest<string>;

/// <summary>Base exception the recording action is registered for, so both derived exceptions match.</summary>
internal class StrategyException : Exception
{
    public StrategyException(string message)
        : base(message)
    {
    }
}

/// <summary>Exception with a registered suppressing handler.</summary>
internal sealed class HandledStrategyException(string message) : StrategyException(message);

/// <summary>Exception without any handler — always rethrown.</summary>
internal sealed class UnhandledStrategyException(string message) : StrategyException(message);

/// <summary>Singleton side-effect recorder injected into <see cref="StrategyRecordingAction"/>.</summary>
internal sealed class StrategyActionRecorder
{
    /// <summary>Number of times the recording action executed.</summary>
    public int ExecuteCount { get; set; }
}

/// <summary>
/// Suppressing handler auto-discovered by the scanner for <see cref="HandledStrategyException"/>.
/// </summary>
internal sealed class StrategySuppressionHandler : IRequestExceptionHandler<StrategyRequest, string, HandledStrategyException>
{
    /// <summary>Fallback response returned when the exception is suppressed.</summary>
    public const string Fallback = "handled-fallback";

    /// <inheritdoc/>
    public Task Handle(
        StrategyRequest request,
        HandledStrategyException exception,
        RequestExceptionHandlerState<string> state,
        CancellationToken cancellationToken)
    {
        state.SetHandled(Fallback);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Side-effect action auto-discovered by the scanner, registered for the base <see cref="StrategyException"/>
/// so it matches both derived exceptions.
/// </summary>
internal sealed class StrategyRecordingAction(StrategyActionRecorder recorder)
    : IRequestExceptionAction<StrategyRequest, StrategyException>
{
    /// <inheritdoc/>
    public Task Execute(StrategyRequest request, StrategyException exception, CancellationToken cancellationToken)
    {
        recorder.ExecuteCount++;
        return Task.CompletedTask;
    }
}
