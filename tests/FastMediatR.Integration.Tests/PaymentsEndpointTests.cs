using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FastMediatR.Sample.Api.Commands;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FastMediatR.Integration.Tests;

/// <summary>
/// Integration tests for the sample API's <c>POST /payments</c> endpoint — the flagship exception-handling
/// pipeline scenario: an over-limit amount makes the handler throw <c>PaymentDeclinedException</c>, the
/// registered <c>IRequestExceptionHandler</c> suppresses it into a graceful HTTP 200 decline, and the
/// registered <c>IRequestExceptionAction</c> logs the decline as a side-effect. Both halves are asserted
/// end-to-end through the real host.
/// </summary>
public sealed class PaymentsEndpointTests : IClassFixture<PaymentsWebApplicationFactory>
{
    /// <summary>EventId of the sample's decline log message (<see cref="PaymentLogEvents.Decline"/>).</summary>
    private const int DeclineEventId = PaymentLogEvents.Decline;

    private readonly HttpClient _client;
    private readonly PaymentsWebApplicationFactory _factory;

    /// <summary>Initialises the test class using the shared factory fixture.</summary>
    /// <param name="factory">The shared <see cref="PaymentsWebApplicationFactory"/>.</param>
    public PaymentsEndpointTests(PaymentsWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>POST /payments with an amount under the credit limit returns 200 with approved: true.</summary>
    [Fact]
    public async Task PostPayments_AmountUnderCreditLimit_ReturnsApproved()
    {
        // Arrange
        var payload = new { orderId = Guid.NewGuid(), amount = 42.00m };

        // Act
        var response = await _client.PostAsJsonAsync("/payments", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("approved").GetBoolean().Should().BeTrue();
        body.GetProperty("reason").GetString().Should().Be("Payment approved.");
    }

    /// <summary>
    /// POST /payments with the amount exactly at the credit limit: only <em>over</em>-limit payments
    /// decline (the handler compares <c>Amount &gt; CreditLimit</c>), so the boundary amount is approved.
    /// </summary>
    [Fact]
    public async Task PostPayments_AmountExactlyAtCreditLimit_ReturnsApproved()
    {
        // Arrange
        var payload = new { orderId = Guid.NewGuid(), amount = ProcessPaymentCommandHandler.CreditLimit };

        // Act
        var response = await _client.PostAsJsonAsync("/payments", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("approved").GetBoolean().Should().BeTrue();
        body.GetProperty("reason").GetString().Should().Be("Payment approved.");
    }

    /// <summary>
    /// POST /payments with an amount over the credit limit: the exception handler suppresses the throw into
    /// a graceful 200 decline, and the exception action records the decline — the full pipeline proven
    /// through HTTP.
    /// </summary>
    [Fact]
    public async Task PostPayments_AmountOverCreditLimit_ReturnsDeclinedAndLogs()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var payload = new { orderId, amount = 99_000.00m };

        // Act
        var response = await _client.PostAsJsonAsync("/payments", payload, TestContext.Current.CancellationToken);

        // Assert — the handler converted the domain exception into a graceful decline (no 500)…
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("approved").GetBoolean().Should().BeFalse();
        // Exact contract, not just a substring: culture-invariant N2 formatting with the explicit USD
        // unit (RV-07), matching ProcessPaymentCommandHandler's FormattableString.Invariant message.
        body.GetProperty("reason").GetString().Should()
            .Be("Amount 99,000.00 USD exceeds the credit limit of 10,000.00 USD.");

        // …and the exception action ran as a side-effect, logging the decline exactly once.
        LogRecord decline = _factory.LoggerProvider.Entries.Should()
            .ContainSingle(e => e.EventId.Id == DeclineEventId, "the PaymentDeclinedExceptionAction must log the decline")
            .Subject;
        decline.Level.Should().Be(LogLevel.Warning);
        decline.Category.Should().EndWith("PaymentDeclinedExceptionAction");
        decline.Message.Should().Contain(orderId.ToString());
    }
}

/// <summary>
/// <see cref="CustomWebApplicationFactory"/> plus a recording <see cref="ILoggerProvider"/>, so the
/// exception-action side-effect can be asserted. Environment and FastMediatR configuration overrides
/// live in the base class — this fixture only layers the provider on top.
/// </summary>
public sealed class PaymentsWebApplicationFactory : CustomWebApplicationFactory
{
    /// <summary>The recording logger provider installed into the test host.</summary>
    public RecordingLoggerProvider LoggerProvider { get; } = new();

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(LoggerProvider));
    }
}

/// <summary>A single log entry captured by <see cref="RecordingLoggerProvider"/>.</summary>
/// <param name="Category">Logger category name.</param>
/// <param name="Level">Severity level the entry was logged at.</param>
/// <param name="EventId">Logging event id.</param>
/// <param name="Message">Formatted message.</param>
public sealed record LogRecord(string Category, LogLevel Level, EventId EventId, string Message);

/// <summary>Minimal thread-safe in-memory <see cref="ILoggerProvider"/> that records every entry.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogRecord> _entries = new();

    /// <summary>Snapshot of all entries recorded so far.</summary>
    public IReadOnlyList<LogRecord> Entries => [.. _entries];

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing to release — the queue is in-memory only.
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<LogRecord> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogRecord(category, logLevel, eventId, formatter(state, exception)));
    }
}
