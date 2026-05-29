using FastMediatR;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FastMediatR.Tests.Behaviors;

// ── Shared test type ──────────────────────────────────────────────────────────

/// <summary>Minimal request used across logging behavior tests.</summary>
internal sealed record LoggingTestRequest(string Value) : IRequest<string>;

/// <summary>
/// Unit tests for <see cref="LoggingBehavior{TRequest,TResponse}"/>.
/// Uses <see cref="FakeLogger{T}"/> from <c>Microsoft.Extensions.Logging.Testing</c> and
/// <see cref="FakeTimeProvider"/> from <c>Microsoft.Extensions.TimeProvider.Testing</c> so
/// log output and timing can be observed without a real logging infrastructure.
/// </summary>
public sealed class LoggingBehaviorTests
{
    /// <summary>Helper that creates a <see cref="LoggingBehavior{TRequest,TResponse}"/> with the given options.</summary>
    /// <param name="options">Options controlling logging behavior.</param>
    /// <param name="timeProvider">Controllable time source.</param>
    /// <param name="logger">Fake logger that captures log records.</param>
    /// <returns>A configured <see cref="LoggingBehavior{TRequest,TResponse}"/> instance.</returns>
    private static LoggingBehavior<LoggingTestRequest, string> CreateSut(
        FastMediatROptions options,
        FakeTimeProvider timeProvider,
        FakeLogger<LoggingBehavior<LoggingTestRequest, string>> logger)
    {
        IOptions<FastMediatROptions> wrapped = Microsoft.Extensions.Options.Options.Create(options);
        return new LoggingBehavior<LoggingTestRequest, string>(wrapped, timeProvider, logger);
    }

    // ── Logging enabled ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when <see cref="FastMediatROptions.EnableLogging"/> is <see langword="true"/>
    /// (default), the behavior logs "Handling" at Information level on entry and
    /// "Handled" at Information level on completion.
    /// </summary>
    [Fact]
    public async Task Handle_LogsHandlingAndHandled_WhenEnableLoggingTrue()
    {
        // Arrange
        var options = new FastMediatROptions { EnableLogging = true };
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<LoggingBehavior<LoggingTestRequest, string>>();
        var sut = CreateSut(options, timeProvider, logger);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("result");

        // Act
        string result = await sut.Handle(new LoggingTestRequest("x"), next, CancellationToken.None);

        // Assert
        result.Should().Be("result");
        logger.Collector.GetSnapshot().Should().Contain(r =>
            r.Level == LogLevel.Information && r.Message.Contains("Handling"));
        logger.Collector.GetSnapshot().Should().Contain(r =>
            r.Level == LogLevel.Information && r.Message.Contains("Handled"));
    }

    /// <summary>
    /// Verifies that when <see cref="FastMediatROptions.EnableLogging"/> is <see langword="false"/>
    /// the behavior produces no log records and still returns the handler response.
    /// </summary>
    [Fact]
    public async Task Handle_SkipsLogging_WhenEnableLoggingFalse()
    {
        // Arrange
        var options = new FastMediatROptions { EnableLogging = false };
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<LoggingBehavior<LoggingTestRequest, string>>();
        var sut = CreateSut(options, timeProvider, logger);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("pass");

        // Act
        string result = await sut.Handle(new LoggingTestRequest("x"), next, CancellationToken.None);

        // Assert
        result.Should().Be("pass");
        logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that when <see cref="FastMediatROptions.EnablePayloadLogging"/> is
    /// <see langword="true"/> the "Handling" log entry includes the structured destructuring
    /// key <c>@Request</c> in its state (the <c>{@Request}</c> message template parameter).
    /// </summary>
    [Fact]
    public async Task Handle_LogsWithPayload_WhenEnablePayloadLoggingTrue()
    {
        // Arrange
        var options = new FastMediatROptions { EnableLogging = true, EnablePayloadLogging = true };
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<LoggingBehavior<LoggingTestRequest, string>>();
        var sut = CreateSut(options, timeProvider, logger);

        var request = new LoggingTestRequest("hello");
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        // Act
        await sut.Handle(request, next, CancellationToken.None);

        // Assert — the "Handling" record must include a structured state containing {@Request}.
        var handlingRecord = logger.Collector.GetSnapshot()
            .FirstOrDefault(r => r.Message.Contains("Handling"));

        handlingRecord.Should().NotBeNull();
        // [LoggerMessage] preserves the "@" prefix from "{@Request}" in StructuredState keys.
        handlingRecord!.StructuredState.Should().NotBeNull();
        handlingRecord.StructuredState!.Select(kv => kv.Key)
            .Should().Contain("@Request");
    }

    // ── Failure path ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when the inner pipeline throws, the behavior logs a "Failed" entry
    /// at Error level (including the exception) and re-throws the original exception.
    /// </summary>
    [Fact]
    public async Task Handle_LogsFailure_WhenNextThrows_AndRethrows()
    {
        // Arrange
        var options = new FastMediatROptions { EnableLogging = true };
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<LoggingBehavior<LoggingTestRequest, string>>();
        var sut = CreateSut(options, timeProvider, logger);

        var expectedException = new InvalidOperationException("inner error");
        RequestHandlerDelegate<string> next = _ => Task.FromException<string>(expectedException);

        // Act
        Func<Task> act = () => sut.Handle(new LoggingTestRequest("x"), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner error");
        logger.Collector.GetSnapshot().Should().Contain(r =>
            r.Level == LogLevel.Error &&
            r.Message.Contains("Failed") &&
            r.Exception == expectedException);
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
        var options = new FastMediatROptions { EnableLogging = true };
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogger<LoggingBehavior<LoggingTestRequest, string>>();
        var sut = CreateSut(options, timeProvider, logger);

        // Act
        Func<Task> act = () => sut.Handle(new LoggingTestRequest("x"), null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }
}
