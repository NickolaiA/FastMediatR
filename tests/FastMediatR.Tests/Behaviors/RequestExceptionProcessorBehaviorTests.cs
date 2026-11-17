using System.Collections;
using FastMediatR;
using FastMediatR.Extensions.DI;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Tests.Behaviors;

// ── Shared test types ─────────────────────────────────────────────────────────

/// <summary>Minimal request used across exception-handling behavior tests.</summary>
internal sealed record ExcTestRequest(string Value) : IRequest<string>;

/// <summary>Minimal request with a value-type response, used to test <c>SetHandled(default)</c>.</summary>
internal sealed record ExcValueRequest(string Value) : IRequest<int>;

/// <summary>Base domain exception used to test exception-hierarchy matching.</summary>
internal class FooException : Exception
{
    public FooException(string message)
        : base(message)
    {
    }
}

/// <summary>Derived exception used to verify base-type handler matching.</summary>
internal sealed class DerivedFooException : FooException
{
    public DerivedFooException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Exception whose <see cref="Exception.Data"/> override returns <see langword="null"/>, simulating a
/// hostile custom implementation that cannot carry the attached original exception.
/// </summary>
internal sealed class NullDataException(string message)
    : Exception(message)
{
    public override IDictionary Data => null!;
}

/// <summary>
/// Delegate-backed <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> that records
/// invocations and lets each test decide whether to suppress the exception.
/// </summary>
internal sealed class DelegateExceptionHandler<TException>(Action<RequestExceptionHandlerState<string>, TException> body)
    : IRequestExceptionHandler<ExcTestRequest, string, TException>
    where TException : Exception
{
    /// <summary>Number of times <see cref="Handle"/> was invoked.</summary>
    public int InvocationCount { get; private set; }

    /// <inheritdoc/>
    public Task Handle(
        ExcTestRequest request,
        TException exception,
        RequestExceptionHandlerState<string> state,
        CancellationToken cancellationToken)
    {
        InvocationCount++;
        body(state, exception);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Delegate-backed <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> for a value-type
/// response, used to verify <c>SetHandled(default)</c> suppresses the exception.
/// </summary>
internal sealed class DelegateValueExceptionHandler(Action<RequestExceptionHandlerState<int>, FooException> body)
    : IRequestExceptionHandler<ExcValueRequest, int, FooException>
{
    /// <inheritdoc/>
    public Task Handle(
        ExcValueRequest request,
        FooException exception,
        RequestExceptionHandlerState<int> state,
        CancellationToken cancellationToken)
    {
        body(state, exception);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Exception handler that records the <see cref="CancellationToken"/> it is invoked with, to verify
/// handlers observe the <em>request's</em> token — even when it is already cancelled.
/// </summary>
internal sealed class TokenRecordingHandler : IRequestExceptionHandler<ExcTestRequest, string, FooException>
{
    /// <summary>The token the handler was last invoked with.</summary>
    public CancellationToken ObservedToken { get; private set; }

    /// <inheritdoc/>
    public Task Handle(
        ExcTestRequest request,
        FooException exception,
        RequestExceptionHandlerState<string> state,
        CancellationToken cancellationToken)
    {
        ObservedToken = cancellationToken;
        state.SetHandled("handled");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Exception handler that faults <em>asynchronously</em> (after the first await), used to verify the
/// async fault path propagates and still carries the original exception.
/// </summary>
internal sealed class AsyncFaultingHandler : IRequestExceptionHandler<ExcTestRequest, string, FooException>
{
    /// <inheritdoc/>
    public async Task Handle(
        ExcTestRequest request,
        FooException exception,
        RequestExceptionHandlerState<string> state,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("async-handler-fault");
    }
}

/// <summary>
/// Unit tests for <see cref="RequestExceptionProcessorBehavior{TRequest,TResponse}"/>.
/// </summary>
public sealed class RequestExceptionProcessorBehaviorTests
{
    private static RequestHandlerDelegate<string> Throwing(Exception exception)
        => _ => Task.FromException<string>(exception);

    private static RequestExceptionProcessorBehavior<ExcTestRequest, string> Build(IServiceProvider provider)
        => new(provider);

    // ── Happy-path: no exception ─────────────────────────────────────────────

    /// <summary>Verifies that when <c>next</c> succeeds the response is returned unchanged.</summary>
    [Fact]
    public async Task Handle_WhenNoException_ReturnsResponse()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var sut = Build(provider);
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        // Act
        string result = await sut.Handle(new ExcTestRequest("x"), next, CancellationToken.None);

        // Assert
        result.Should().Be("ok");
    }

    // ── Handled exception → fallback response ─────────────────────────────────

    /// <summary>
    /// Verifies that a matching handler which calls <c>SetHandled</c> suppresses the exception and the
    /// behavior returns the fallback response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerSuppresses_ReturnsFallbackResponse()
    {
        // Arrange
        var handler = new DelegateExceptionHandler<FooException>((state, _) => state.SetHandled("recovered"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());

        // Act
        string result = await sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), CancellationToken.None);

        // Assert
        result.Should().Be("recovered");
        handler.InvocationCount.Should().Be(1);
    }

    // ── No handler → rethrow ─────────────────────────────────────────────────

    /// <summary>Verifies that with no registered handler the original exception is rethrown.</summary>
    [Fact]
    public async Task Handle_WhenNoHandlerRegistered_Rethrows()
    {
        // Arrange
        var sut = Build(new ServiceCollection().BuildServiceProvider());

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FooException>().WithMessage("boom");
    }

    /// <summary>Verifies that a handler which does not call <c>SetHandled</c> results in a rethrow.</summary>
    [Fact]
    public async Task Handle_WhenHandlerDoesNotSuppress_Rethrows()
    {
        // Arrange
        var handler = new DelegateExceptionHandler<FooException>((_, _) => { /* observe only */ });
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FooException>().WithMessage("boom");
        handler.InvocationCount.Should().Be(1);
    }

    // ── Exception-hierarchy matching ─────────────────────────────────────────

    /// <summary>
    /// Verifies that a handler registered for a base exception type handles a derived exception
    /// (the behavior walks the exception type hierarchy).
    /// </summary>
    [Fact]
    public async Task Handle_MatchesHandlerForBaseExceptionType()
    {
        // Arrange — handler registered for FooException, but a DerivedFooException is thrown.
        var handler = new DelegateExceptionHandler<FooException>((state, _) => state.SetHandled("base-handled"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());

        // Act
        string result = await sut.Handle(new ExcTestRequest("x"), Throwing(new DerivedFooException("boom")), CancellationToken.None);

        // Assert
        result.Should().Be("base-handled");
        handler.InvocationCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that the most-derived matching handler is tried first and that once one handler
    /// suppresses the exception, no further handlers run.
    /// </summary>
    [Fact]
    public async Task Handle_StopsAtFirstSuppressingHandler()
    {
        // Arrange
        var derivedHandler = new DelegateExceptionHandler<DerivedFooException>((state, _) => state.SetHandled("derived"));
        var baseHandler = new DelegateExceptionHandler<FooException>((state, _) => state.SetHandled("base"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, DerivedFooException>>(derivedHandler);
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(baseHandler);
        var sut = Build(services.BuildServiceProvider());

        // Act
        string result = await sut.Handle(new ExcTestRequest("x"), Throwing(new DerivedFooException("boom")), CancellationToken.None);

        // Assert — most-derived handler wins; base handler never runs.
        result.Should().Be("derived");
        derivedHandler.InvocationCount.Should().Be(1);
        baseHandler.InvocationCount.Should().Be(0);
    }

    // ── Faulty handler propagation ───────────────────────────────────────────

    /// <summary>
    /// Verifies that when an exception handler itself throws <em>synchronously</em>, the original
    /// handler exception propagates unchanged — not a reflection <see cref="System.Reflection.TargetInvocationException"/>
    /// wrapper.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerThrowsSynchronously_PropagatesOriginalException()
    {
        // Arrange — handler throws before any await (mirrors a non-async handler guard clause).
        var handler = new DelegateExceptionHandler<FooException>((_, _) => throw new InvalidOperationException("handler-fault"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), CancellationToken.None);

        // Assert — the handler's own exception surfaces directly, unwrapped.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler-fault");
    }

    /// <summary>
    /// Verifies that when a handler itself throws, the fault carries the original request exception
    /// under the <c>"FastMediatR.OriginalException"</c> <see cref="Exception.Data"/> key, so the
    /// replaced exception remains discoverable.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerFaults_OriginalExceptionStoredInFaultData()
    {
        // Arrange
        var handler = new DelegateExceptionHandler<FooException>((_, _) => throw new InvalidOperationException("handler-fault"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());
        var original = new FooException("boom");

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(original), CancellationToken.None);

        // Assert — the handler's fault propagates and carries the original exception it replaced.
        var fault = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        fault.Message.Should().Be("handler-fault");
        fault.Data["FastMediatR.OriginalException"].Should().BeSameAs(original);
    }

    /// <summary>
    /// Verifies that the original-exception <c>Data</c> attachment is best-effort: when the faulting
    /// handler's exception has an unwritable <see cref="Exception.Data"/> (hostile override), the fault
    /// still propagates unchanged — the diagnostic attach must never replace the fault with its own error.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerFaultHasUnwritableData_FaultStillPropagates()
    {
        // Arrange
        var handler = new DelegateExceptionHandler<FooException>((_, _) => throw new NullDataException("handler-fault"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), CancellationToken.None);

        // Assert — the handler's fault surfaces intact (NOT the NullReferenceException an unguarded
        // Data attachment would raise). Losing the original is acceptable; masking the fault is not.
        var fault = (await act.Should().ThrowAsync<NullDataException>()).Which;
        fault.Message.Should().Be("handler-fault");
    }

    /// <summary>
    /// Verifies that a handler which <em>rethrows the original exception instance</em> is left untouched:
    /// the fault <em>is</em> the original exception, so no self-referencing
    /// <c>"FastMediatR.OriginalException"</c> entry is attached — the exception just propagates.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerRethrowsSameInstance_DoesNotAttachOriginalToItself()
    {
        // Arrange
        var handler = new DelegateExceptionHandler<FooException>((_, exception) => throw exception);
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());
        var original = new FooException("boom");

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(original), CancellationToken.None);

        // Assert — same instance, no Data entry attached (the ReferenceEquals filter prevented self-attachment).
        var fault = (await act.Should().ThrowAsync<FooException>()).Which;
        fault.Should().BeSameAs(original);
        fault.Data.Contains("FastMediatR.OriginalException").Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a handler faulting <em>asynchronously</em> (after the first await) still propagates
    /// the fault — unwrapped — and the fault carries the original request exception.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerFaultsAsynchronously_FaultCarriesOriginal()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(new AsyncFaultingHandler());
        var sut = Build(services.BuildServiceProvider());
        var original = new FooException("boom");

        // Act
        Func<Task> act = () => sut.Handle(new ExcTestRequest("x"), Throwing(original), CancellationToken.None);

        // Assert — the awaited fault surfaces (not an AggregateException wrapper) and carries the original.
        var fault = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        fault.Message.Should().Be("async-handler-fault");
        fault.Data["FastMediatR.OriginalException"].Should().BeSameAs(original);
    }

    // ── Handler token semantics ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that — unlike actions — handlers observe the <em>request's</em> own token, even when it
    /// is already cancelled, as documented on
    /// <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>. (The action counterpart —
    /// a guaranteed non-cancellable token — is pinned in
    /// <see cref="RequestExceptionActionProcessorBehaviorTests"/>.)
    /// </summary>
    [Fact]
    public async Task Handle_ForwardsRequestTokenToHandler_EvenWhenAlreadyCancelled()
    {
        // Arrange
        var handler = new TokenRecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, FooException>>(handler);
        var sut = Build(services.BuildServiceProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        string result = await sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), cts.Token);

        // Assert — the handler saw the cancelled request token (asymmetric with the action contract by design).
        result.Should().Be("handled");
        handler.ObservedToken.CanBeCanceled.Should().BeTrue();
        handler.ObservedToken.IsCancellationRequested.Should().BeTrue();
    }

    // ── Hierarchy terminus & value-type responses ─────────────────────────────

    /// <summary>
    /// Verifies that a handler registered for <see cref="Exception"/> — the end of the hierarchy walk —
    /// catches any thrown exception and can suppress it.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerRegisteredForExceptionBase_CatchesAnyException()
    {
        // Arrange
        var handler = new DelegateExceptionHandler<Exception>((state, _) => state.SetHandled("caught"));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcTestRequest, string, Exception>>(handler);
        var sut = Build(services.BuildServiceProvider());

        // Act
        string result = await sut.Handle(new ExcTestRequest("x"), Throwing(new FooException("boom")), CancellationToken.None);

        // Assert — the walk reached Exception and the catch-all handler suppressed the failure.
        result.Should().Be("caught");
        handler.InvocationCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that <c>SetHandled(default)</c> with a value-type <c>TResponse</c> suppresses the exception
    /// and returns the default value — suppression is tracked by the <c>Handled</c> flag, never by
    /// null-ness of the response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenValueTypeResponseSuppressedWithDefault_ReturnsDefaultWithoutRethrowing()
    {
        // Arrange
        var handler = new DelegateValueExceptionHandler((state, _) => state.SetHandled(default));
        var services = new ServiceCollection();
        services.AddSingleton<IRequestExceptionHandler<ExcValueRequest, int, FooException>>(handler);
        var sut = new RequestExceptionProcessorBehavior<ExcValueRequest, int>(services.BuildServiceProvider());

        // Act
        int result = await sut.Handle(
            new ExcValueRequest("x"),
            _ => Task.FromException<int>(new FooException("boom")),
            CancellationToken.None);

        // Assert — suppressed, default returned, no rethrow.
        result.Should().Be(default);
    }

    // ── Null guards ──────────────────────────────────────────────────────────
    /// <summary>Verifies that a <see langword="null"/> service provider throws.</summary>
    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        // Act
        Action act = () => _ = new RequestExceptionProcessorBehavior<ExcTestRequest, string>(null!);

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
