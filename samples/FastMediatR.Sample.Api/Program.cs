// FastMediatR sample Web API — see README.md and ../../docs/260524_1300-fastmediatr-usage-guide.md
// Phase 4: ValidationBehavior, LoggingBehavior, PerformanceBehavior, pre/post processors, fanout strategies.
// Phase 5: Global exception handler (ValidationException → 400, all others → 500).
// Phase 6: OpenTelemetry tracing via TracingBehavior + AddFastMediatRTelemetry.

using FastMediatR;
using FastMediatR.Extensions.DI;
using FastMediatR.Sample;
using FastMediatR.Sample.Api.Commands;
using FastMediatR.Sample.Api.Notifications;
using FastMediatR.Sample.Api.Queries;
using FastMediatR.Telemetry;
using FluentValidation;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── FluentValidation ────────────────────────────────────────────────────────
// Register all AbstractValidator<T> implementations found in this assembly so
// ValidationBehavior<,> can resolve them from DI.
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ── OpenAPI / Scalar ───────────────────────────────────────────────────────
// Generates the OpenAPI document and hosts a Scalar UI for interactive endpoint calls.
builder.Services.AddOpenApi();

// ── Telemetry ────────────────────────────────────────────────────────────────
// Phase 6: Register TracingBehavior BEFORE AddFastMediatR so it sits outermost in
// the pipeline and its span covers the full dispatch latency.
builder.Services.AddFastMediatRTelemetry();

// ── OpenTelemetry SDK ────────────────────────────────────────────────────────
// Phase 6: Wire the "FastMediatR" ActivitySource into the OTel SDK and export to
// stdout so spans are visible during local development.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddFastMediatRInstrumentation()
        .AddConsoleExporter());

// ── FastMediatR ─────────────────────────────────────────────────────────────
// Phase 4: LoggingBehavior → PerformanceBehavior → ValidationBehavior are added as
// open behaviors (outermost first → innermost last in pipeline order).
// Pre/post processor behaviors are registered automatically by AddFastMediatR.
#pragma warning disable IL2026 // Sample is not AOT-compiled (IsAotCompatible=false)
builder.Services.AddFastMediatR<Program>(cfg =>
{
    // The decline demo suppresses PaymentDeclinedException via a handler AND logs it via an exception
    // action — the action only runs for handled exceptions under ApplyForAllExceptions (the default
    // ApplyForUnhandledExceptions would skip it, since the handler suppresses the exception first).
    cfg.RequestExceptionActionProcessorStrategy =
        RequestExceptionActionProcessorStrategy.ApplyForAllExceptions;

    cfg.RegisterHandlersFromAssemblyContaining<Program>()
       // Outermost: logs every request entry/exit/failure.
       .AddOpenBehavior(typeof(LoggingBehavior<,>))
       // Middle: measures elapsed time and warns on slow requests.
       .AddOpenBehavior(typeof(PerformanceBehavior<,>))
       // Innermost (before handler): validates input via FluentValidation.
       .AddOpenBehavior(typeof(ValidationBehavior<,>))
       // Example custom no-op behavior to show extensibility.
       .AddOpenBehavior(typeof(NoOpSampleBehavior<,>));
});
#pragma warning restore IL2026

var app = builder.Build();

// ── OpenAPI / Scalar endpoints ─────────────────────────────────────────────
app.MapOpenApi();
app.MapScalarApiReference();

// ── Global exception handler ─────────────────────────────────────────────────
// Converts FluentValidation.ValidationException → HTTP 400 Problem Details.
// All other unhandled exceptions → HTTP 500.
// Must be registered early in the pipeline so it wraps all endpoint middleware.
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var exception = feature?.Error;

        if (exception is ValidationException validationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var errors = validationException.Errors
                .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.Ordinal);

            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title = "Validation failed.",
                status = 400,
                errors,
            }).ConfigureAwait(false);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = "An unexpected error occurred.",
            status = 500,
        }).ConfigureAwait(false);
    });
});

// ── Health ──────────────────────────────────────────────────────────────────

// Liveness probe — also used by the CI smoke check (task breakdown 0.S).
app.MapGet("/health", () => Results.Ok(new { status = "ok", phase = "Phase 6" }));

// ── Commands ────────────────────────────────────────────────────────────────

// POST /users  →  CreateUserCommand  →  returns new user id (Guid)
app.MapPost("/users", async (CreateUserCommand command, IMediator mediator, CancellationToken ct) =>
{
    var id = await mediator.Send(command, ct).ConfigureAwait(false);
    return Results.Created($"/users/{id}", new { id });
});

// DELETE /users/{id}  →  DeactivateUserCommand  →  void (Unit)
// DeactivateUserCommandHandler uses [HandlerLifetime(Transient)] — demonstrates per-handler lifetime override.
app.MapDelete("/users/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    await mediator.Send(new DeactivateUserCommand(id), ct).ConfigureAwait(false);
    return Results.NoContent();
});

// POST /payments  →  ProcessPaymentCommand  →  ProcessPaymentResult
// Demonstrates the exception-handling pipeline: amounts over the credit limit make the handler throw
// PaymentDeclinedException, which PaymentDeclinedExceptionHandler converts into a graceful
// "declined" result (HTTP 200) while PaymentDeclinedExceptionAction logs the decline as a side-effect.
app.MapPost("/payments", async (ProcessPaymentCommand command, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(command, ct).ConfigureAwait(false);
    return Results.Ok(result);
});

// ── Queries ─────────────────────────────────────────────────────────────────

// GET /users/{id}  →  GetUserByIdQuery  →  returns UserDto or 404
app.MapGet("/users/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    var user = await mediator.Send(new GetUserByIdQuery(id), ct).ConfigureAwait(false);
    return user is null ? Results.NotFound() : Results.Ok(user);
});

// ── Notifications ───────────────────────────────────────────────────────────

// POST /users/{id}/events/created  →  UserCreatedNotification  →  fan-out (no return)
// Uses the default TaskWhenAllPublisher (parallel fan-out).
app.MapPost("/users/{id:guid}/events/created", async (Guid id, string email, IMediator mediator, CancellationToken ct) =>
{
    await mediator.Publish(new UserCreatedNotification(id, email), ct).ConfigureAwait(false);
    return Results.Accepted();
});

// ── Telemetry demo ───────────────────────────────────────────────────────────

// GET /telemetry/demo  →  dispatches a GetUserByIdQuery and a UserCreatedNotification.
// The Send call emits a span via TracingBehavior; Publish demonstrates fan-out remains functional.
app.MapGet("/telemetry/demo", async (IMediator mediator, CancellationToken ct) =>
{
    // Send a query so TracingBehavior emits a "GetUserByIdQuery" span.
    var result = await mediator.Send(new GetUserByIdQuery(Guid.Empty), ct).ConfigureAwait(false);

    // Publish remains intentionally untraced by Phase 6; this call demonstrates
    // that notifications still execute alongside traced Send operations.
    await mediator.Publish(
        new UserCreatedNotification(Guid.NewGuid(), "telemetry-demo@example.com"),
        ct).ConfigureAwait(false);

    return Results.Ok(new
    {
        span = "GetUserByIdQuery",
        found = result is not null,
        notificationPublished = true,
    });
});

await app.RunAsync().ConfigureAwait(false);

/// <summary>Public partial program type so <c>WebApplicationFactory&lt;Program&gt;</c> can target it from integration tests.</summary>
public partial class Program;

namespace FastMediatR.Sample
{
    /// <summary>
    /// Sample open-generic no-op pipeline behavior demonstrating <see cref="FastMediatR.Extensions.DI.FastMediatRConfig.AddOpenBehavior"/>.
    /// In a real application this would be replaced by logging, validation, or performance-monitoring behaviors.
    /// </summary>
    /// <typeparam name="TRequest">The request type flowing through this behavior.</typeparam>
    /// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
    public sealed class NoOpSampleBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        /// <summary>Passes the request to the next pipeline step without any cross-cutting logic.</summary>
        /// <param name="request">The request instance.</param>
        /// <param name="next">The next step in the pipeline.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to the next step.</param>
        /// <returns>The response produced by the inner pipeline.</returns>
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(next);
            return next(cancellationToken);
        }
    }
}

