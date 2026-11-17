# FastMediatR

> High-performance, AOT-friendly, MIT-licensed in-process mediator for **.NET 10** with a public API that is a near drop-in replacement for MediatR.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

FastMediatR uses a Roslyn `IIncrementalGenerator` to emit zero-reflection dispatch code at compile time. The result: faster `Send`/`Publish`, fewer allocations, and full Native-AOT/trimming compatibility.

> **Status:** Pre-1.0 — actively under construction. Public APIs may shift until v1.0.0 is tagged.

## Features

- **Zero-reflection dispatch** — a source generator emits a `switch`-based dispatcher at compile time; no `MethodInfo`, no `dynamic`, no runtime type scanning on the hot path.
- **Native AOT & trimming friendly** — ships `IsAotCompatible=true` and `IsTrimmable=true`; the only reflection touchpoint is a one-time startup lookup that has a fully trim-safe alternative.
- **Near drop-in for MediatR** — `IRequest<T>`, `IRequestHandler<,>`, `INotification`, `INotificationHandler<>`, `ISender`, `IPublisher`, `IMediator`, and `IPipelineBehavior<,>` mirror the MediatR surface (see the migration guide).
- **Build-time wiring validation** — diagnostics `FM001` (no handler) and `FM002` (duplicate handler) fail the build before you run; `FM003` flags non-default handler lifetimes.
- **Built-in pipeline behaviors** — `ValidationBehavior` (FluentValidation), `LoggingBehavior`, and `PerformanceBehavior`, plus pre-/post-processors.
- **Exception handling pipeline** — `IRequestExceptionHandler<,,>` (translate exceptions to fallback responses) and `IRequestExceptionAction<,>` (side-effect-only observers), with type-hierarchy matching and a configurable action strategy.
- **Configurable notification publishing** — `TaskWhenAllPublisher` (default, concurrent) and `SequentialAwaitPublisher` (ordered, fail-fast).
- **Optional OpenTelemetry tracing** via the `FastMediatR.Telemetry` package.

## Quick start

Define requests and handlers:

```csharp
using FastMediatR;

public sealed record CreateUserCommand(string Email, string DisplayName) : IRequest<Guid>;

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
{
    public Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
}
```

Register in `Program.cs` and dispatch:

```csharp
using FastMediatR.Extensions.DI;

builder.Services.AddFastMediatR<Program>();

app.MapPost("/users", async (CreateUserCommand command, IMediator mediator, CancellationToken ct) =>
{
    var id = await mediator.Send(command, ct);
    return Results.Created($"/users/{id}", new { id });
});
```

See the [usage guide](docs/260524_1300-fastmediatr-usage-guide.md) for DI options, pipeline behaviors, AOT guidance, and MediatR migration.

## Project layout

| Project | Purpose |
|---------|---------|
| `src/FastMediatR.Core` | Core abstractions, built-in behaviors, notification publishers, options. |
| `src/FastMediatR.Generator` | Roslyn incremental generator (dispatcher + DI registration). |
| `src/FastMediatR.Extensions.DI` | `AddFastMediatR<TMarker>()` entry point and assembly scanning. |
| `src/FastMediatR.Telemetry` | Optional OpenTelemetry tracing behavior. |
| `samples/FastMediatR.Sample.Api` | Reference minimal-API sample wired end-to-end. |

## Quick links

- **Design specification:** [docs/260524_1200-fastmediatr-design.md](docs/260524_1200-fastmediatr-design.md)
- **Implementation task breakdown:** [docs/260524_1230-fastmediatr-task-breakdown.md](docs/260524_1230-fastmediatr-task-breakdown.md)
- **Usage guide (living):** [docs/260524_1300-fastmediatr-usage-guide.md](docs/260524_1300-fastmediatr-usage-guide.md)
- **Testing guide:** [docs/260529_1230-fastmediatr-testing-guide.md](docs/260529_1230-fastmediatr-testing-guide.md)
- **Security & performance review:** [docs/260529_1200-security-performance-review.md](docs/260529_1200-security-performance-review.md)
- **Reference sample app:** [`samples/FastMediatR.Sample.Api`](samples/FastMediatR.Sample.Api)
- **.NET / C# coding standards:** [docs/DotNet.md](docs/DotNet.md)

## Build

```bash
dotnet build FastMediatR.slnx
dotnet test  FastMediatR.slnx
```

Requires the .NET 10 SDK (see `global.json`).
The canonical solution file is `FastMediatR.slnx`.

## License

[MIT](LICENSE)
