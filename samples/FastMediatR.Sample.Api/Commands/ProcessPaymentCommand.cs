using FastMediatR;
using Microsoft.Extensions.Logging;

namespace FastMediatR.Sample.Api.Commands;

/// <summary>
/// Demonstrates the exception-handling pipeline. The handler throws a
/// <see cref="PaymentDeclinedException"/> when <paramref name="Amount"/> exceeds the demo credit
/// limit; a registered <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> then
/// translates that into a graceful <see cref="ProcessPaymentResult"/> instead of letting the
/// exception bubble out to a 500 response.
/// </summary>
/// <param name="OrderId">The order being paid for.</param>
/// <param name="Amount">The payment amount; values above 10,000 are declined.</param>
public sealed record ProcessPaymentCommand(Guid OrderId, decimal Amount) : IRequest<ProcessPaymentResult>;

/// <summary>Outcome of a <see cref="ProcessPaymentCommand"/>.</summary>
/// <param name="Approved">Whether the payment was approved.</param>
/// <param name="Reason">A human-readable status reason.</param>
public sealed record ProcessPaymentResult(bool Approved, string Reason);

/// <summary>Domain exception signalling that a payment was declined.</summary>
public sealed class PaymentDeclinedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PaymentDeclinedException"/> class.</summary>
    /// <param name="message">The decline reason.</param>
    public PaymentDeclinedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PaymentDeclinedException"/> class.</summary>
    public PaymentDeclinedException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PaymentDeclinedException"/> class.</summary>
    /// <param name="message">The decline reason.</param>
    /// <param name="innerException">The inner exception.</param>
    public PaymentDeclinedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Handles <see cref="ProcessPaymentCommand"/>. Throws <see cref="PaymentDeclinedException"/> for
/// amounts above the demo limit to exercise the exception-handling pipeline.
/// </summary>
public sealed class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand, ProcessPaymentResult>
{
    /// <summary>The demo credit limit above which payments are declined.</summary>
    public const decimal CreditLimit = 10_000m;

    /// <summary>Handles the command.</summary>
    /// <param name="request">The command instance.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    /// <returns>The approved payment result.</returns>
    /// <exception cref="PaymentDeclinedException">Thrown when the amount exceeds <see cref="CreditLimit"/>.</exception>
    public Task<ProcessPaymentResult> Handle(ProcessPaymentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Amount > CreditLimit)
        {
            // Culture-invariant on purpose: this message flows verbatim into
            // ProcessPaymentResult.Reason — i.e. the HTTP response body — so its formatting
            // must never depend on the server's culture. The currency is named explicitly
            // because a unit-less number would be ambiguous.
            throw new PaymentDeclinedException(
                FormattableString.Invariant($"Amount {request.Amount:N2} USD exceeds the credit limit of {CreditLimit:N2} USD."));
        }

        return Task.FromResult(new ProcessPaymentResult(true, "Payment approved."));
    }
}

/// <summary>
/// Translates a <see cref="PaymentDeclinedException"/> into a non-faulted
/// <see cref="ProcessPaymentResult"/>, demonstrating how an exception handler can supply a fallback
/// response and suppress the exception.
/// </summary>
public sealed class PaymentDeclinedExceptionHandler
    : IRequestExceptionHandler<ProcessPaymentCommand, ProcessPaymentResult, PaymentDeclinedException>
{
    /// <inheritdoc/>
    public Task Handle(
        ProcessPaymentCommand request,
        PaymentDeclinedException exception,
        RequestExceptionHandlerState<ProcessPaymentResult> state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(state);

        state.SetHandled(new ProcessPaymentResult(false, exception.Message));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Logs every <see cref="PaymentDeclinedException"/> as a side-effect, demonstrating an
/// <see cref="IRequestExceptionAction{TRequest,TException}"/> that observes but does not suppress.
/// </summary>
public sealed class PaymentDeclinedExceptionAction(ILogger<PaymentDeclinedExceptionAction> logger)
    : IRequestExceptionAction<ProcessPaymentCommand, PaymentDeclinedException>
{
    private readonly ILogger<PaymentDeclinedExceptionAction> _logger = logger;

    /// <inheritdoc/>
    public Task Execute(
        ProcessPaymentCommand request,
        PaymentDeclinedException exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exception);

        PaymentLog.Declined(_logger, request.OrderId, exception.Message);
        return Task.CompletedTask;
    }
}

/// <summary>Well-known logging event ids for the payment use case.</summary>
public static class PaymentLogEvents
{
    /// <summary>EventId of the payment-declined warning emitted by <see cref="PaymentLog.Declined"/>.</summary>
    public const int Decline = 4001;
}

/// <summary>High-performance logging messages for payment exception actions.</summary>
internal static partial class PaymentLog
{
    [LoggerMessage(
        EventId = PaymentLogEvents.Decline,
        Level = LogLevel.Warning,
        Message = "Payment declined for order {OrderId}: {Reason}")]
    public static partial void Declined(ILogger logger, Guid orderId, string reason);
}
