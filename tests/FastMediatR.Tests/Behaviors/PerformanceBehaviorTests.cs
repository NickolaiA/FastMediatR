using FastMediatR;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FastMediatR.Tests.Behaviors;

// ── Shared test type ──────────────────────────────────────────────────────────

/// <summary>Minimal request used across performance behavior tests.</summary>
internal sealed record PerformanceTestRequest : IRequest<string>;

/// <summary>
/// Unit tests for <see cref="PerformanceBehavior{TRequest,TResponse}"/>.
/// Uses <see cref="FakeTimeProvider"/> to control elapsed time and
/// <see cref="FakeLogger{T}"/> to observe warning log output.
/// </summary>
public sealed class PerformanceBehaviorTests
{
    /// <summary>Creates a <see cref="PerformanceBehavior{TRequest,TResponse}"/> configured with the given threshold.</summary>
    /// <param name="thresholdMs">The performance threshold in milliseconds.</param>
    /// <param name="timeProvider">The controllable time source.</param>
    /// <param name="logger">The fake logger that captures log records.</param>
    /// <returns>A configured <see cref="PerformanceBehavior{TRequest,TResponse}"/> instance.</returns>
    private static PerformanceBehavior<PerformanceTestRequest, string> CreateSut(
        int thresholdMs,
        FakeTimeProvider timeProvider,
        FakeLogger<PerformanceBehavior<PerformanceTestRequest, string>> logger)
    {
        IOptions<FastMediatROptions> options = Microsoft.Extensions.Options.Options.Create(
            new FastMediatROptions { PerformanceThresholdMs = thresholdMs });
        return new PerformanceBehavior<PerformanceTestRequest, string>(timeProvider, options, logger);
    }

    // ── Under threshold (no warning) ─────────────────────────────────────────

    /// <summary>
    /// Verifies that when the request completes within the configured threshold no
    /// warning log is produced.
    /// </summary>
    [Fact]
    public async Task Handle_DoesNotLog_WhenUnderThreshold()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<PerformanceBehavior<PerformanceTestRequest, string>>();
        var sut = CreateSut(thresholdMs: 500, timeProvider, logger);

        // Simulate 100 ms elapsed — well under the 500 ms threshold.
        RequestHandlerDelegate<string> next = _ =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            return Task.FromResult("ok");
        };

        // Act
        string result = await sut.Handle(new PerformanceTestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("ok");
        logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    // ── Over threshold (warning emitted) ─────────────────────────────────────

    /// <summary>
    /// Verifies that when the request takes longer than the threshold a <c>Warning</c>
    /// log entry is emitted containing the request type, elapsed time, and threshold.
    /// </summary>
    [Fact]
    public async Task Handle_LogsWarning_WhenOverThreshold()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<PerformanceBehavior<PerformanceTestRequest, string>>();
        var sut = CreateSut(thresholdMs: 500, timeProvider, logger);

        // Simulate 600 ms elapsed — 100 ms over the 500 ms threshold.
        RequestHandlerDelegate<string> next = _ =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(600));
            return Task.FromResult("slow");
        };

        // Act
        string result = await sut.Handle(new PerformanceTestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("slow");
        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning);

        var record = logger.Collector.GetSnapshot().Single(r => r.Level == LogLevel.Warning);
        record.Message.Should().Contain("PerformanceTestRequest");
        record.Message.Should().Contain("600");
        record.Message.Should().Contain("500");
    }

    // ── Measures on both success and failure ─────────────────────────────────

    /// <summary>
    /// Verifies that the behavior measures elapsed time and emits a warning even when the
    /// inner pipeline throws (the timer is in a <c>finally</c> block).
    /// </summary>
    [Fact]
    public async Task Handle_LogsWarning_WhenOverThreshold_AndNextThrows()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<PerformanceBehavior<PerformanceTestRequest, string>>();
        var sut = CreateSut(thresholdMs: 100, timeProvider, logger);

        // Simulate 200 ms elapsed before throwing.
        RequestHandlerDelegate<string> next = _ =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(200));
            return Task.FromException<string>(new InvalidOperationException("boom"));
        };

        // Act
        Func<Task> act = () => sut.Handle(new PerformanceTestRequest(), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning);
    }

    /// <summary>
    /// Verifies that when the inner pipeline throws but elapsed time is under the threshold,
    /// no warning is emitted even on the failure path.
    /// </summary>
    [Fact]
    public async Task Handle_DoesNotLog_WhenUnderThreshold_AndNextThrows()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<PerformanceBehavior<PerformanceTestRequest, string>>();
        var sut = CreateSut(thresholdMs: 500, timeProvider, logger);

        RequestHandlerDelegate<string> next = _ =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(50));
            return Task.FromException<string>(new InvalidOperationException("fast-fail"));
        };

        // Act
        Func<Task> act = () => sut.Handle(new PerformanceTestRequest(), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    // ── Null guards ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that passing a <see langword="null"/> <c>next</c> delegate throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_NullNext_ThrowsArgumentNullException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<PerformanceBehavior<PerformanceTestRequest, string>>();
        var sut = CreateSut(thresholdMs: 500, timeProvider, logger);

        // Act
        Func<Task> act = () => sut.Handle(new PerformanceTestRequest(), null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }
}
