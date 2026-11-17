using FastMediatR;
using FastMediatR.Extensions.DI;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Tests.Behaviors;

/// <summary>
/// Delegate-backed <see cref="IRequestExceptionAction{TRequest,TException}"/> that records invocations.
/// </summary>
internal sealed class DelegateExceptionAction<TException>(Action<TException> body)
    : IRequestExceptionAction<ExcTestRequest, TException>
    where TException : Exception
{
    /// <summary>Number of times <see cref="Execute"/> was invoked.</summary>
    public int InvocationCount { get; private set; }

    /// <inheritdoc/>
    public Task Execute(ExcTestRequest request, TException exception, CancellationToken cancellationToken)
    {
        InvocationCount++;
        body(exception);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Exception action that records the <see cref="CancellationToken"/> it is invoked with, to verify
/// actions run with a non-cancellable token.
/// </summary>
internal sealed class TokenRecordingAction : IRequestExceptionAction<ExcTestRequest, FooException>
{
    /// <summary>Number of times <see cref="Execute"/> was invoked.</summary>
    public int InvocationCount { get; private set; }

    /// <summary>The token the action was last invoked with.</summary>
    public CancellationToken ObservedToken { get; private set; }

    /// <inheritdoc/>
    public Task Execute(ExcTestRequest request, FooException exception, CancellationToken cancellationToken)
    {
        InvocationCount++;
        ObservedToken = cancellationToken;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Unit tests for <see cref="RequestExceptionActionProcessorBehavior{TRequest,TResponse}"/>.
/// </summary>
public sealed class RequestExceptionActionProcessorBehaviorTests
{
    private static RequestHandlerDelegate<string> Throwing(Exception exception)
        => _ => Task.FromException<string>(exception);

    private static RequestExceptionActionProcessorBehavior<ExcTestRequest, string> Build(IServiceProvider provider)
        => new(provider);

    // ── Happy-path: no exception ─────────────────────────────────────────────

    /// <summary>Verifies that when <c>next</c> succeeds the response is returned and no action runs.</summary>
    [Fact]
    public async Task Handle_WhenNoException_ReturnsResponseAndRunsNoActions()
    {
        // Arrange
        var action = new DelegateExceptionAction<FooException>(_ => { });
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionAction<ExcTestRequest, FooException>>(action);
        var sut = Build(services.BuildServiceProvider());

        // Act
        string result = await sut.Handle(new ExcTestRequest("x"), _ => Task.FromResult("ok"), CancellationToken.None);

        // Assert
        result.Should().Be("ok");
        action.InvocationCount.Should().Be(0);
    }

    // ── Action runs then rethrows ────────────────────────────────────────────

    /// <summary>
    /// Verifies that on exception the matching action runs and the original exception is always rethrown
    /// (actions cannot suppress).
    /// </summary>
    [Fact]
    public async Task Handle_WhenException_RunsActionAndRethrows()
    {
        // Arrange
        var action = new DelegateExceptionAction<FooException>(_ => { });
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionAction<ExcTestRequest, FooException>>(action);
        var sut = Build(services.BuildServiceProvider());

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FooException>().WithMessage("boom");
        action.InvocationCount.Should().Be(1);
    }

    // ── All matching actions run ─────────────────────────────────────────────

    /// <summary>
    /// Verifies that every action matching the exception hierarchy runs (a base-type action runs for a
    /// derived exception) and the exception still propagates.
    /// </summary>
    [Fact]
    public async Task Handle_RunsAllMatchingActionsAcrossHierarchy()
    {
        // Arrange
        var derivedAction = new DelegateExceptionAction<DerivedFooException>(_ => { });
        var baseAction = new DelegateExceptionAction<FooException>(_ => { });
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionAction<ExcTestRequest, DerivedFooException>>(derivedAction);
        services.AddSingleton<IRequestExceptionAction<ExcTestRequest, FooException>>(baseAction);
        var sut = Build(services.BuildServiceProvider());

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(new DerivedFooException("boom")), CancellationToken.None);

        // Assert — both run; exception still propagates.
        await act.Should().ThrowAsync<DerivedFooException>();
        derivedAction.InvocationCount.Should().Be(1);
        baseAction.InvocationCount.Should().Be(1);
    }

    // ── Cancellation-insensitive actions ─────────────────────────────────────

    /// <summary>
    /// Verifies that actions run even when the request's <see cref="CancellationToken"/> is already
    /// cancelled, and are handed a non-cancellable token instead — guaranteeing side-effect telemetry.
    /// </summary>
    [Fact]
    public async Task Action_WhenRequestTokenAlreadyCancelled_StillRunsWithNonCancellableToken()
    {
        // Arrange
        var action = new TokenRecordingAction();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionAction<ExcTestRequest, FooException>>(action);
        var sut = Build(services.BuildServiceProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("cancelled")), cts.Token);

        // Assert — the action ran and never saw the cancelled request token.
        await act.Should().ThrowAsync<FooException>().WithMessage("cancelled");
        action.InvocationCount.Should().Be(1);
        action.ObservedToken.CanBeCanceled.Should().BeFalse();
    }

    // ── Faulty action propagation ────────────────────────────────────────────

    /// <summary>
    /// Verifies that when an action itself throws, the fault carries the original request exception
    /// under the <c>"FastMediatR.OriginalException"</c> <see cref="Exception.Data"/> key.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActionThrows_OriginalExceptionStoredInFaultData()
    {
        // Arrange
        var action = new DelegateExceptionAction<FooException>(_ => throw new InvalidOperationException("action-fault"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionAction<ExcTestRequest, FooException>>(action);
        var sut = Build(services.BuildServiceProvider());
        var original = new FooException("boom");

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(original), CancellationToken.None);

        // Assert — the action's fault propagates and carries the original exception it replaced.
        var fault = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        fault.Message.Should().Be("action-fault");
        fault.Data["FastMediatR.OriginalException"].Should().BeSameAs(original);
    }

    // ── Null guards ──────────────────────────────────────────────────────────

    /// <summary>Verifies that a <see langword="null"/> service provider throws.</summary>
    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        // Act
        Action act = () => _ = new RequestExceptionActionProcessorBehavior<ExcTestRequest, string>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    /// <summary>Verifies that a <see langword="null"/> <c>next</c> delegate throws.</summary>
    [Fact]
    public async Task Handle_NullNext_Throws()
    {
        // Arrange
        var sut = Build(new ServiceCollection().BuildServiceProvider());

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }
}
