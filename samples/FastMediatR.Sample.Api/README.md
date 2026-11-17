# FastMediatR.Sample.Api

Canonical reference Web API demonstrating every public feature of FastMediatR.

This project is updated at the end of every implementation phase. See [docs/260524_1230-fastmediatr-task-breakdown.md](../../docs/260524_1230-fastmediatr-task-breakdown.md) gate **S** for the scope of each phase, and [docs/260524_1300-fastmediatr-usage-guide.md](../../docs/260524_1300-fastmediatr-usage-guide.md) for explanations of the code shown here.

## Current scope (through Phase 6)

| Folder            | Demonstrates                                                          |
|-------------------|-----------------------------------------------------------------------|
| `Commands/`       | `IRequest<TResponse>` (`CreateUserCommand`), `IRequest` → `Unit` (`DeactivateUserCommand`), and the exception-handling pipeline (`ProcessPaymentCommand` + `PaymentDeclinedExceptionHandler`/`PaymentDeclinedExceptionAction`) |
| `Queries/`        | `IRequest<TResponse>` used as a read-only query (`GetUserByIdQuery`)  |
| `Notifications/`  | `INotification` with multiple handlers (`UserCreatedNotification`)    |
| `Program.cs`      | DI wiring, built-in behaviors, exception handling, OpenTelemetry tracing, and the `/telemetry/demo` endpoint |

> **Note (Phase 6).** The sample is now fully wired: `AddFastMediatR<Program>()`, built-in behaviors, the Phase 5 exception handler, and Phase 6 OpenTelemetry tracing via `AddFastMediatRTelemetry()` and `AddFastMediatRInstrumentation()` are all active.

## Running

```powershell
dotnet run --project samples/FastMediatR.Sample.Api
# then
curl http://localhost:5100/health
```

> **Expected log noise.** The invalid `POST /users` demos in `FastMediatR.Sample.Api.http`
> deliberately throw `ValidationException`, so the console prints ERROR-level stack traces for
> them — the 400 problem+json response is the correct outcome, not a pipeline bug.

## Layout

```
samples/FastMediatR.Sample.Api/
├── Commands/
│   ├── CreateUserCommand.cs
│   ├── DeactivateUserCommand.cs
│   └── ProcessPaymentCommand.cs
├── Queries/
│   └── GetUserByIdQuery.cs
├── Notifications/
│   └── UserCreatedNotification.cs
├── Program.cs
├── appsettings.json
└── FastMediatR.Sample.Api.csproj
```

## Future phases

- Phase 8 — switch from project references to the published `FastMediatR` NuGet package
