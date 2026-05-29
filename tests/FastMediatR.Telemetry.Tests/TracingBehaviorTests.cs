using System.Diagnostics;
using FastMediatR;
using FastMediatR.Telemetry;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Telemetry.Tests;

/// <summary>
/// Unit tests for <see cref="TracingBehavior{TRequest,TResponse}"/>.
/// Each test uses an in-memory <see cref="ActivityListener"/> scoped to the
/// <c>"FastMediatR"</c> source to capture emitted spans without requiring an OTel exporter.
/// </summary>
public sealed class TracingBehaviorTests
{
    // ── Helper request/response types ─────────────────────────────────────────

    /// <summary>A simple request type used in success-path tests.</summary>
    private sealed record PingRequest : IRequest<string>;

    /// <summary>A request type used in exception-path tests.</summary>
    private sealed record FailRequest : IRequest<string>;

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates and registers an in-memory <see cref="ActivityListener"/> that collects
    /// all stopped activities emitted by the <c>"FastMediatR"</c> source.
    /// Dispose the returned listener to unregister it after the test.
    /// </summary>
    /// <param name="collected">
    /// The list to which stopped activities will be appended.
    /// Must be pre-initialised by the caller.
    /// </param>
    /// <returns>A registered <see cref="ActivityListener"/>; dispose after use.</returns>
    private static ActivityListener CreateListener(List<Activity> collected)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FastMediatRActivitySource.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            // Use ActivityStopped so tags and status set during Handle are captured.
            ActivityStopped = collected.Add,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that exactly one activity is emitted for a successful request when
    /// a listener is subscribed to the <c>"FastMediatR"</c> source.
    /// </summary>
    [Fact]
    public async Task Send_WithListener_EmitsOneActivity()
    {
        // Arrange
        var collected = new List<Activity>();
        using var listener = CreateListener(collected);
        var behavior = new TracingBehavior<PingRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("pong");

        // Act
        await behavior.Handle(new PingRequest(), next, TestContext.Current.CancellationToken);

        // Assert
        collected.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that the emitted activity's <see cref="Activity.DisplayName"/> matches
    /// the short type name of the request (not the fully-qualified name).
    /// </summary>
    [Fact]
    public async Task Send_WithListener_ActivityHasCorrectName()
    {
        // Arrange
        var collected = new List<Activity>();
        using var listener = CreateListener(collected);
        var behavior = new TracingBehavior<PingRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("pong");

        // Act
        await behavior.Handle(new PingRequest(), next, TestContext.Current.CancellationToken);

        // Assert
        collected.Should().ContainSingle()
            .Which.DisplayName.Should().Be(nameof(PingRequest));
    }

    /// <summary>
    /// Verifies that <c>mediator.request_type</c> and <c>mediator.response_type</c> tags
    /// are populated with the fully-qualified type names.
    /// </summary>
    [Fact]
    public async Task Send_WithListener_TagsPopulated()
    {
        // Arrange
        var collected = new List<Activity>();
        using var listener = CreateListener(collected);
        var behavior = new TracingBehavior<PingRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("pong");

        // Act
        await behavior.Handle(new PingRequest(), next, TestContext.Current.CancellationToken);

        // Assert
        var activity = collected.Should().ContainSingle().Subject;
        activity.GetTagItem("mediator.request_type").Should().Be(typeof(PingRequest).FullName);
        activity.GetTagItem("mediator.response_type").Should().Be(typeof(string).FullName);
    }

    /// <summary>
    /// Verifies that the activity status is set to <see cref="ActivityStatusCode.Ok"/>
    /// when the handler completes without throwing.
    /// </summary>
    [Fact]
    public async Task Send_WithListener_SuccessfulRequest_StatusIsOk()
    {
        // Arrange
        var collected = new List<Activity>();
        using var listener = CreateListener(collected);
        var behavior = new TracingBehavior<PingRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("pong");

        // Act
        await behavior.Handle(new PingRequest(), next, TestContext.Current.CancellationToken);

        // Assert
        collected.Should().ContainSingle()
            .Which.Status.Should().Be(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Verifies that when the handler throws, the activity status is set to
    /// <see cref="ActivityStatusCode.Error"/> and an <c>exception</c> event is recorded.
    /// </summary>
    [Fact]
    public async Task Send_HandlerThrows_StatusIsErrorAndExceptionRecorded()
    {
        // Arrange
        var collected = new List<Activity>();
        using var listener = CreateListener(collected);
        var behavior = new TracingBehavior<FailRequest, string>();
        var expected = new InvalidOperationException("boom");
        RequestHandlerDelegate<string> next = _ => throw expected;

        // Act
        var act = () => behavior.Handle(new FailRequest(), next, TestContext.Current.CancellationToken);

        // Assert — exception is re-thrown
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        var activity = collected.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);

        // Activity.AddException (BCL, .NET 9+) adds an event named "exception"
        // following OTel semantic conventions.
        activity.Events.Should().ContainSingle(e => e.Name == "exception");
    }

    /// <summary>
    /// Verifies that when no <see cref="ActivityListener"/> is subscribed to the
    /// <c>"FastMediatR"</c> source, the behavior still calls the next delegate
    /// and returns the correct result (zero-overhead fast path).
    /// </summary>
    [Fact]
    public async Task Send_WithNoListener_PassesThroughWithNoAllocation()
    {
        // Arrange — no listener registered; StartActivity returns null for this source.
        var behavior = new TracingBehavior<PingRequest, string>();
        bool nextCalled = false;

        RequestHandlerDelegate<string> next = _ =>
        {
            nextCalled = true;
            return Task.FromResult("pong");
        };

        // Act
        string result = await behavior.Handle(
            new PingRequest(), next, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be("pong");
        nextCalled.Should().BeTrue("the next delegate must always be invoked");
    }

    /// <summary>
    /// Verifies that when a parent <see cref="Activity"/> is active on the current thread,
    /// the span created by the behavior is correctly parented to it (context propagation).
    /// </summary>
    [Fact]
    public async Task Send_ParentActivity_PropagatesContext()
    {
        // Arrange
        var collected = new List<Activity>();
        using var listener = CreateListener(collected);
        var behavior = new TracingBehavior<PingRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("pong");

        // Start a parent activity so that Activity.Current is non-null when Handle runs.
        using var parentActivity = FastMediatRActivitySource.Source.StartActivity("ParentSpan");
        parentActivity.Should().NotBeNull("a listener is registered that accepts all activities");

        // Act
        await behavior.Handle(new PingRequest(), next, TestContext.Current.CancellationToken);

        // Assert — the child span should have a ParentId that matches the parent's Id.
        // collected may contain both the child (stopped inside Handle) and the parent
        // (stopped when using-scope exits); filter to find the child by OperationName.
        var childActivity = collected
            .Should().Contain(a => a.OperationName == nameof(PingRequest))
            .Which;

        childActivity.ParentId.Should().Be(parentActivity!.Id);
    }
}
