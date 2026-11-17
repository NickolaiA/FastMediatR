# FastMediatR — Usage Guide

> **Status:** living document (pre-1.0)  
> **Date created:** 2026-05-24  
> **Companion to:** [260524_1200-fastmediatr-design.md](260524_1200-fastmediatr-design.md), [260524_1230-fastmediatr-task-breakdown.md](260524_1230-fastmediatr-task-breakdown.md)  
> **Reference sample:** [`samples/FastMediatR.Sample.Api`](../samples/FastMediatR.Sample.Api) — every snippet in this guide is copied from (or kept in lockstep with) the sample project.

This guide is the canonical end-user documentation for FastMediatR. It is updated at the end of every implementation phase; sections are filled in as features land. Until v1.0 is tagged, expect sections marked _(coming in Phase N)_ to be incomplete or absent.

---

## Changelog

<!-- Newest first. One entry per phase. -->

- **2026-05-30 — Exception handling pipeline shipped.** New `IRequestExceptionHandler<,,>`, `IRequestExceptionAction<,>`, and `RequestExceptionHandlerState<TResponse>` abstractions in `FastMediatR.Core`, plus two auto-registered behaviors (`RequestExceptionProcessorBehavior`, `RequestExceptionActionProcessorBehavior`) and the `FastMediatRConfig.RequestExceptionActionProcessorStrategy` toggle. New §8 Exception Handling Pipeline added; subsequent sections renumbered. Sample API gains the `POST /payments` use case.
- **2026-05-29 — Hardening from security & performance review.** `TaskWhenAllPublisher` now propagates `OperationCanceledException` correctly when handlers are cancelled (previously threw `NullReferenceException`). `AddOpenBehavior` now validates that the supplied type implements `IPipelineBehavior<,>`. The reflection-based assembly scanner tolerates `ReflectionTypeLoadException` (partially loadable assemblies). The generated dispatcher pipeline now short-circuits to the handler with zero closure allocations when no behaviors are registered and avoids a redundant `.ToArray()` copy otherwise. `TracingBehavior` documents redaction guidance for exception details on spans. See [260529_1200-security-performance-review.md](260529_1200-security-performance-review.md).
- **2026-05-25 — Phase 4 — Built-in pipeline behaviors shipped.** `ValidationBehavior`, `LoggingBehavior`, `PerformanceBehavior`, `RequestPreProcessorBehavior`, `RequestPostProcessorBehavior`, `TaskWhenAllPublisher`, `SequentialAwaitPublisher`, and `NotificationPublisherStrategy` are all live. `FastMediatROptions` moved to `FastMediatR.Core` (namespace `FastMediatR`). Sections §6 Pipeline Behaviors, §7 Pre- and Post-Processors, and §9 Notification Publishing Strategies filled in.
- **2026-05-25 — Phase 3 — DI Extensions shipped.** `AddFastMediatR<TMarker>()` entry point, assembly scanning, `[HandlerLifetime]` override, multi-assembly registration, open-behavior registration, and `FastMediatROptions` binding are all live. Sections §2 Installation, §3 Quick start (final), §5 Dependency injection, and §11 Configuration & options filled in.
- **2026-05-24 — Phase 2 — Source generator shipped.** The Roslyn `IIncrementalGenerator` now emits `FastMediatRDispatcher` (zero-reflection switch dispatch) and `FastMediatRGeneratedRegistration` (DI wiring) for any consumer assembly. FM001 / FM002 / FM003 diagnostics are live. Sample API wired end-to-end: four dispatch endpoints, generator referenced as `OutputItemType="Analyzer"`. Section §10 filled in.
- **2026-05-24 — Phase 1 — Core abstractions landed.** Filled in §1 Introduction & goals, the §3 Quick start stub, and §4 Core concepts with code copied from the sample API. No dispatch is wired yet — the types compile and can be referenced; full execution lands in Phase 3.

---

## 1. Introduction & Goals

FastMediatR is a high-performance, AOT-friendly, MIT-licensed in-process mediator for .NET 10 with a public API that is a near drop-in replacement for [MediatR](https://github.com/jbogard/MediatR). It uses a Roslyn `IIncrementalGenerator` to emit zero-reflection dispatch code at compile time.

**Goals**

- **API-compatible with MediatR for the 95 % case** — rename `using MediatR;` to `using FastMediatR;` and swap `AddMediatR(...)` for `AddFastMediatR<TMarker>(...)` (full migration table in §14).
- **Faster `Send` / `Publish` than MediatR with materially lower allocations** — see §16 for measured numbers.
- **Native AOT and trimming friendly out of the box** — the library ships `IsAotCompatible=true` and `IsTrimmable=true`; only one reflection touchpoint, guarded by `[DynamicDependency]` (see §13).
- **Build-time validation of handler wiring** — the source generator emits errors `FM001` (no handler) and `FM002` (duplicate handler) before you run the app (see §10).

**Non-goals**

- Cross-process / distributed messaging — use [MassTransit](https://masstransit.io/) or [Wolverine](https://wolverine.netlify.app/) instead.
- Behavior trees, sagas, or workflow orchestration.
- A general-purpose IoC container — FastMediatR composes on top of `Microsoft.Extensions.DependencyInjection`.

---

## 2. Installation

> **Note:** FastMediatR is pre-1.0. The package is not yet published to NuGet.org. Build from source and use project references until v1.0.0 is tagged.

Once published, install via:

```bash
dotnet add package FastMediatR
# optional telemetry package (Phase 6)
dotnet add package FastMediatR.Telemetry
```

Add the Roslyn source generator to your project so it can emit the zero-reflection dispatch table:

```xml
<!-- In your consuming .csproj -->
<ItemGroup>
  <ProjectReference Include="...FastMediatR.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
  <ProjectReference Include="...FastMediatR.Extensions.DI.csproj" />
</ItemGroup>
```

Enable generated file output if you want to inspect the emitted code:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

---

## 3. Quick Start

The following end-to-end example is taken verbatim from [`samples/FastMediatR.Sample.Api`](../samples/FastMediatR.Sample.Api).

**Step 1 — define your request types and handlers:**

```csharp
using FastMediatR;

// Command that returns a value
public sealed record CreateUserCommand(string Email, string DisplayName) : IRequest<Guid>;

// Void command (returns Unit)
public sealed record DeactivateUserCommand(Guid UserId) : IRequest;

// Query (same shape as a command — intent is by naming, not by type)
public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserDto?>;
public sealed record UserDto(Guid Id, string Email, string DisplayName);

// Notification with multiple subscribers
public sealed record UserCreatedNotification(Guid UserId, string Email) : INotification;
```

```csharp
public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
{
    public Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guid.NewGuid());
    }
}

public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand>
{
    public Task<Unit> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
        => Unit.Task;   // pre-completed singleton task — zero allocations
}
```

**Step 2 — register FastMediatR in `Program.cs`:**

```csharp
using FastMediatR.Extensions.DI;

var builder = WebApplication.CreateBuilder(args);

#pragma warning disable IL2026 // suppress if your app is not AOT-compiled
builder.Services.AddFastMediatR<Program>();
#pragma warning restore IL2026

var app = builder.Build();
```

**Step 3 — inject `IMediator` (or `ISender` / `IPublisher`) into your endpoints:**

```csharp
app.MapPost("/users", async (CreateUserCommand command, IMediator mediator, CancellationToken ct) =>
{
    var id = await mediator.Send(command, ct).ConfigureAwait(false);
    return Results.Created($"/users/{id}", new { id });
});

app.MapPost("/users/{id:guid}/events/created",
    async (Guid id, string email, IMediator mediator, CancellationToken ct) =>
    {
        await mediator.Publish(new UserCreatedNotification(id, email), ct).ConfigureAwait(false);
        return Results.Accepted();
    });
```

That's it. The Roslyn generator emits the dispatch table at compile time; there is no reflection at runtime.

---

## 4. Core Concepts

| Concept | Type | Purpose |
|---------|------|---------|
| Request (with response) | `IRequest<TResponse>` | A point-to-point message dispatched via `ISender.Send` — exactly one handler runs and returns a `TResponse`. |
| Void request | `IRequest` | Shorthand for `IRequest<Unit>` — same dispatch path, no special-casing in the generator. |
| Notification | `INotification` | A broadcast message dispatched via `IPublisher.Publish` — zero or more `INotificationHandler<T>` subscribers run, coordinated by the chosen `INotificationPublisher` strategy. |
| Request handler | `IRequestHandler<TRequest, TResponse>` | Exactly one per request type (the generator enforces this with diagnostic `FM002`). Scoped lifetime by default. |
| Notification handler | `INotificationHandler<TNotification>` | Zero, one, or many per notification type. Scoped lifetime by default. |
| Sender | `ISender` | Dispatches requests. Inject into endpoints / controllers / application services. |
| Publisher | `IPublisher` | Broadcasts notifications. |
| Mediator | `IMediator` | Convenience: `IMediator : ISender, IPublisher`. Inject when you need both. |
| `Unit` | `readonly record struct` | The void-return marker. Use `Unit.Value` or, in handlers, return the pre-completed `Unit.Task` to avoid allocating a fresh `Task<Unit>` on the hot path. |
| `[HandlerLifetime]` | attribute | Apply to a concrete handler class to register it as `Transient` or `Singleton` instead of the default `Scoped`. The generator emits warning `FM003` when this attribute deviates from the configured default. |

### The `IRequest` / `IRequest<Unit>` equivalence

This is the central design decision that lets the generator emit a single switch statement for both "command" and "query" shapes:

```csharp
public interface IRequest : IRequest<Unit> { }
```

A void command is, at the type level, exactly the same thing as a request that returns `Unit` — there is no second dispatch path to maintain.

### Why `Unit` is a `readonly record struct`

- **Zero heap allocation** on every void-command return.
- **Value equality** out of the box — every `Unit` compares equal to every other `Unit`.
- **`Unit.Task`** is a pre-completed `Task<Unit>` singleton; synchronous handlers should return this directly rather than building a new task.

### Notifications: fire-and-forget at the call site

`Publish` is a fan-out: the caller awaits a single `Task` that represents "all handlers have completed under the chosen strategy". The two strategies that ship with FastMediatR (Phase 4) are:

- **`TaskWhenAllPublisher`** (default) — runs all handlers concurrently and surfaces multi-failure as `AggregateException`.
- **`SequentialAwaitPublisher`** — runs handlers one at a time, short-circuits on first failure.

---

## 5. Dependency Injection

### Registration entry point

Call `AddFastMediatR<TMarker>()` once in `Program.cs`. The type argument is the **marker type** whose assembly the extension uses as the primary scan target (usually `Program` in a minimal-API project):

```csharp
using FastMediatR.Extensions.DI;

#pragma warning disable IL2026, IL3050  // suppress for non-AOT projects
builder.Services.AddFastMediatR<Program>();
#pragma warning restore IL2026, IL3050
```

`AddFastMediatR<TMarker>` is annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` because it uses reflection-based assembly scanning. For fully AOT-compiled apps the generator-emitted `FastMediatRGeneratedRegistration.Register(services)` call is the trim-safe path (no `[RequiresUnreferencedCode]` on the generated code).

### Fluent configuration

Pass a configuration delegate to customise the registration:

```csharp
builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.RegisterHandlersFromAssemblyContaining<Program>()
       .RegisterHandlersFromAssembly(typeof(SomeOtherHandler).Assembly)
       .AddOpenBehavior(typeof(MyLoggingBehavior<,>))
       .AddOpenBehavior(typeof(MyValidationBehavior<,>), ServiceLifetime.Transient));
```

| Member | Description |
|--------|-------------|
| `RegisterHandlersFromAssemblyContaining<T>()` | Adds the assembly that contains `T` to the scan list. Fluent — returns `this`. |
| `RegisterHandlersFromAssembly(Assembly)` | Adds an assembly to the scan list explicitly. Fluent — returns `this`. |
| `AddOpenBehavior(Type, ServiceLifetime?)` | Registers an open-generic `IPipelineBehavior<,>` implementation. The type must be an open generic type definition that implements `IPipelineBehavior<,>` (otherwise `ArgumentException` is thrown). Behaviors run in declaration order (first registered = outermost). Defaults to `Scoped`. |
| `DefaultHandlerLifetime` | Sets the default DI lifetime for all discovered handlers. Default: `Scoped`. |

### What `AddFastMediatR` registers

In order:

1. **Generated registration** — locates `FastMediatR.Generated.FastMediatRGeneratedRegistration` in the marker assembly via `Assembly.GetType()` and calls `Register(services)`. This wires the zero-reflection `ISender` / `IPublisher` / `IMediator` dispatch implementation. The resolved delegate is cached per assembly in a `ConcurrentDictionary`.
2. **Assembly scanner** — iterates all concrete, non-abstract, non-open-generic types in each registered assembly and registers:
   - `IRequestHandler<TRequest, TResponse>` — via `TryAdd` (first registration wins, prevents duplicates).
   - `INotificationHandler<TNotification>` — via `TryAdd`.
   - `IRequestPreProcessor<TRequest>` — concrete type via `TryAdd`; closed `IRequestPreProcessor<TRequest>` interface via `Add` (allows `IEnumerable<>` resolution of all processors in order).
   - `IRequestPostProcessor<TRequest, TResponse>` — same double-registration pattern as pre-processors.
3. **Open behaviors** — each `AddOpenBehavior` call registers the type as `IPipelineBehavior<,>` using `Add` (preserving declaration order).
4. **Options** — binds `FastMediatROptions` from the `"FastMediatR"` configuration section (see §11).

### `[HandlerLifetime]` per-handler lifetime override

Apply `[HandlerLifetime(ServiceLifetime.Singleton)]` (or `Transient`) to a concrete handler class to override the `DefaultHandlerLifetime` for that handler only:

```csharp
using FastMediatR;
using Microsoft.Extensions.DependencyInjection;

// This handler is resolved as Singleton regardless of DefaultHandlerLifetime.
[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class CachedConfigQueryHandler : IRequestHandler<GetConfigQuery, Config>
{
    public Task<Config> Handle(GetConfigQuery request, CancellationToken ct) => …;
}
```

The source generator emits diagnostic **FM003** (Info) when a handler's explicit lifetime differs from the configured default, so deviations are visible in build output without blocking the build.

### Wire-up — the generated registration locator

For the majority of projects `AddFastMediatR<TMarker>` handles everything. If you need the trim-safe path (AOT) you can call the generated registration directly:

```csharp
using FastMediatR.Generated;

FastMediatRGeneratedRegistration.Register(builder.Services);
```

This call has no reflection and is fully trim-compatible.

---

## 6. Pipeline Behaviors

Pipeline behaviors intercept every `Send` call and let you add cross-cutting concerns (logging, validation, caching, …) without touching your handlers. They are generic over `TRequest` and `TResponse`, so a single behavior class covers all request types.

### Writing a custom behavior

Implement `IPipelineBehavior<TRequest, TResponse>` and call `next(cancellationToken)` to continue the pipeline:

```csharp
using FastMediatR;

public sealed class TimingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var sw = Stopwatch.StartNew();
        TResponse result = await next(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"{typeof(TRequest).Name} took {sw.ElapsedMilliseconds} ms");
        return result;
    }
}
```

### Registering behaviors

Register open-generic behaviors in declaration order (outermost first) via `AddOpenBehavior`:

```csharp
builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>))        // outermost
       .AddOpenBehavior(typeof(PerformanceBehavior<,>))
       .AddOpenBehavior(typeof(ValidationBehavior<,>))     // innermost (before handler)
       .AddOpenBehavior(typeof(TimingBehavior<,>)));
```

**Registration order is pipeline order.** Behaviors registered first wrap behaviors registered later. Pre/post processor behaviors (`RequestPreProcessorBehavior<,>` and `RequestPostProcessorBehavior<,>`) are always placed innermost — after all user behaviors — automatically by `AddFastMediatR`.

### Built-in `ValidationBehavior`

`ValidationBehavior<TRequest, TResponse>` resolves all `IValidator<TRequest>` instances from DI and runs them concurrently before invoking `next`. If any validator fails, it throws `FluentValidation.ValidationException` (a list of all failures across all validators) and the handler is never called.

**Setup:**

```csharp
// 1. Register FluentValidation validators
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// 2. Add ValidationBehavior to the pipeline
builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)));
```

```csharp
using FluentValidation;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(c => c.DisplayName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
```

If there are no `IValidator<TRequest>` registrations for a given request type, `ValidationBehavior` passes through without any overhead.

### Built-in `LoggingBehavior`

`LoggingBehavior<TRequest, TResponse>` logs every request entry and exit at `Information` level and logs failures at `Error` level. It is controlled by `FastMediatROptions`:

| Option | Effect |
|--------|--------|
| `EnableLogging: true` (default) | Logs `Handling {RequestType}` + `Handled {RequestType} in {ElapsedMs}ms` |
| `EnableLogging: false` | Short-circuits immediately — zero overhead |
| `EnablePayloadLogging: true` | Adds a structured `{@Request}` property containing the serialised request — **off by default** to avoid logging sensitive data |

```csharp
builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>)));
```

`LoggingBehavior` uses `[LoggerMessage]`-generated log methods and `TimeProvider` injection so it is testable and overhead-free on the hot path when disabled.

### Built-in `PerformanceBehavior`

`PerformanceBehavior<TRequest, TResponse>` measures handler elapsed time via `TimeProvider.GetTimestamp()` / `GetElapsedTime()` and logs a `Warning` when it exceeds `FastMediatROptions.PerformanceThresholdMs`:

```
Slow request: CreateUserCommand took 612ms (threshold: 500ms)
```

```csharp
builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>)));
```

Configure the threshold in `appsettings.json`:

```json
{
  "FastMediatR": {
    "PerformanceThresholdMs": 300
  }
}
```

---

## 7. Pre- and Post-Processors

Pre- and post-processors are a lightweight alternative to full behaviors when you need to run side-effect logic before or after every handler of a given request type, without wrapping the `next()` call.

### `IRequestPreProcessor<TRequest>`

Implement and register before the handler runs:

```csharp
using FastMediatR;

public sealed class AuditPreProcessor : IRequestPreProcessor<CreateUserCommand>
{
    private readonly IAuditLog _log;

    public AuditPreProcessor(IAuditLog log) => _log = log;

    public Task Process(CreateUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _log.RecordAsync($"CreateUser initiated for {request.Email}", cancellationToken);
    }
}
```

### `IRequestPostProcessor<TRequest, TResponse>`

Implement to run after the handler returns a successful response:

```csharp
public sealed class NotifyPostProcessor : IRequestPostProcessor<CreateUserCommand, Guid>
{
    private readonly IEventBus _bus;

    public NotifyPostProcessor(IEventBus bus) => _bus = bus;

    public Task Process(CreateUserCommand request, Guid response, CancellationToken cancellationToken)
        => _bus.PublishAsync(new UserCreatedEvent(response, request.Email), cancellationToken);
}
```

**Post-processors do NOT run if the handler throws.** This matches the MediatR convention and keeps them simple — they are not a substitute for try/finally logic.

### Auto-registration

`AddFastMediatR<TMarker>()` scans all registered assemblies for concrete implementations of `IRequestPreProcessor<TRequest>` and `IRequestPostProcessor<TRequest, TResponse>` and registers them automatically. You do not need to call anything extra.

If you need to skip auto-registration for a specific processor (for example, to control its lifetime or add conditional logic), register it manually before calling `AddFastMediatR` and the scanner will skip it (uses `TryAdd` semantics).

### Pipeline placement

The `RequestPreProcessorBehavior<,>` and `RequestPostProcessorBehavior<,>` wrapping behaviors are always registered innermost — after any `AddOpenBehavior` registrations — by `AddFastMediatR`. The execution order is:

```
LoggingBehavior         (outermost)
  PerformanceBehavior
    ValidationBehavior
      RequestPreProcessorBehavior   ← pre-processors run here (all in order)
        ← handler runs here →
      RequestPostProcessorBehavior  ← post-processors run here (all in order)
    /ValidationBehavior
  /PerformanceBehavior
/LoggingBehavior
```

---

## 8. Exception Handling Pipeline

The exception-handling pipeline lets you apply **type-routed error policy** as a cross-cutting concern — translate domain exceptions into fallback responses, or run side-effects (logging, metrics) when a request fails — without wrapping every handler in `try`/`catch`. It mirrors MediatR's `IRequestExceptionHandler` / `IRequestExceptionAction` model.

> **AOT note.** The exception behaviors live in `FastMediatR.Extensions.DI` and resolve handlers by walking the runtime exception's type hierarchy, so they use reflection internally. They are wired up only through `AddFastMediatR`, which is already the reflection-based opt-in path (annotated `RequiresUnreferencedCode` / `RequiresDynamicCode`). Apps that need a fully reflection-free startup and do not use this feature are unaffected — the abstractions in `FastMediatR.Core` stay AOT-clean. Conversely, on the pure source-generated path (`FastMediatRGeneratedRegistration.Register(services)`) the exception behaviors are **not** registered and any `IRequestExceptionHandler`/`IRequestExceptionAction` implementations are **silently ignored** — no warning or diagnostic is emitted. The feature currently requires `AddFastMediatR`; generator-based registration for exception handlers/actions is tracked as a future optimization in the feature-gap roadmap (item #10).

### `IRequestExceptionHandler<TRequest, TResponse, TException>` — recover with a fallback

An exception handler can **suppress** an exception and supply a fallback response by calling `state.SetHandled(...)`:

```csharp
using FastMediatR;

public sealed class PaymentDeclinedExceptionHandler
    : IRequestExceptionHandler<ProcessPaymentCommand, ProcessPaymentResult, PaymentDeclinedException>
{
    public Task Handle(
        ProcessPaymentCommand request,
        PaymentDeclinedException exception,
        RequestExceptionHandlerState<ProcessPaymentResult> state,
        CancellationToken cancellationToken)
    {
        // Translate the domain exception into a graceful, non-faulted result.
        state.SetHandled(new ProcessPaymentResult(false, exception.Message));
        return Task.CompletedTask;
    }
}
```

When a handler calls `SetHandled`, the pipeline returns `state.Response` instead of rethrowing. If no handler suppresses the exception, the original exception is rethrown with its stack trace preserved.

### `IRequestExceptionAction<TRequest, TException>` — observe without suppressing

An exception action is a **side-effect only** — it runs when a request fails but cannot suppress the exception:

```csharp
public sealed class PaymentDeclinedExceptionAction(ILogger<PaymentDeclinedExceptionAction> logger)
    : IRequestExceptionAction<ProcessPaymentCommand, PaymentDeclinedException>
{
    public Task Execute(
        ProcessPaymentCommand request,
        PaymentDeclinedException exception,
        CancellationToken cancellationToken)
    {
        logger.LogWarning("Payment declined for order {OrderId}: {Reason}", request.OrderId, exception.Message);
        return Task.CompletedTask; // the original exception still propagates
    }
}
```

### Exception-type matching

Handlers and actions are matched by walking the thrown exception's type hierarchy, **most-derived first**. A handler registered for a base exception type therefore also handles derived exceptions. For handlers, the **first** one to call `SetHandled` wins and stops the search; for actions, **all** matching actions across the hierarchy run.

Handlers registered for the *same* exception type run in DI registration order. That order follows the assembly scan (marker assembly first, then `RegisterHandlersFromAssembly` additions) and the `Assembly.GetTypes()` enumeration within each assembly — which the CLR does **not** specify across assemblies. Within one assembly the order is practically stable; do not depend on the relative order of handlers declared in *different* assemblies.

### Auto-registration

`AddFastMediatR<TMarker>()` scans all registered assemblies for concrete implementations of `IRequestExceptionHandler<,,>` and `IRequestExceptionAction<,>` and registers them automatically, along with the two wrapping behaviors. No extra call is required.

### Strategy: when do actions run?

`FastMediatRConfig.RequestExceptionActionProcessorStrategy` controls when exception **actions** run relative to exception **handlers**:

| Strategy | Behavior |
|---|---|
| `ApplyForUnhandledExceptions` (default) | Actions run **only** when no handler suppresses the exception (the action behavior sits *outside* the handler behavior). |
| `ApplyForAllExceptions` | Actions **always** run when an exception is thrown, even if a handler later suppresses it (the action behavior sits *inside* the handler behavior). |

```csharp
builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.RequestExceptionActionProcessorStrategy =
        RequestExceptionActionProcessorStrategy.ApplyForAllExceptions);
```

When `AddFastMediatR` is called more than once on the same service collection (for example an app and a shared library each registering their own marker), the **first** call's strategy wins: the built-in exception behaviors are registered idempotently, so a later call cannot re-nest them. Choose the strategy in the application's composition root. The same first-call-wins rule applies to `DefaultHandlerLifetime`: each assembly is scanned once, by the first call that reaches it — that call's lifetime applies, and a later call's value only affects assemblies not yet scanned.

### Fault semantics & cancellation

- **A faulty handler/action does not hide the original exception.** If a handler or action itself throws, that fault propagates unwrapped (stack preserved) with the original request exception attached to it under `Exception.Data["FastMediatR.OriginalException"]` — so neither failure is lost.
- **Handlers observe the request's token — which may already be cancelled.** The most common failure reaching this path is a cancellation, so a handler that observes the token may itself abort. Observe it only when abandoning recovery work is the intent.
- **Actions always complete.** Actions are invoked with a non-cancellable token (`CancellationToken.None`) instead of the request token, guaranteeing logging/metrics side-effects run even when the observed failure is itself the request's cancellation.
- **Cancellation is suppressible like any other exception.** A handler broad enough to match `OperationCanceledException` — e.g. one registered for `Exception` — can call `SetHandled` and turn a cancellation into a normal-looking response. FastMediatR deliberately mirrors MediatR and does not special-case cancellation. To let cancellation propagate, do not register suppressing handlers broad enough to match it.

### Pipeline placement

The two exception behaviors are registered **after** your `AddOpenBehavior` registrations (so your behaviors stay outermost) but **before** the pre/post-processor behaviors (so they catch exceptions thrown by the handler *and* the pre/post-processors). With the default `ApplyForUnhandledExceptions` strategy the order is:

```
LoggingBehavior                       (outermost — your behaviors)
  PerformanceBehavior
    ValidationBehavior
      RequestExceptionActionProcessorBehavior   ← runs actions when unhandled
        RequestExceptionProcessorBehavior       ← handlers can suppress here
          RequestPreProcessorBehavior
            ← handler runs here →
          RequestPostProcessorBehavior
/...
```

Under `ApplyForAllExceptions` the action and processor behaviors swap places so actions always run.

> See [`samples/FastMediatR.Sample.Api`](../samples/FastMediatR.Sample.Api) — the `POST /payments` endpoint (`ProcessPaymentCommand`) demonstrates a handler that throws `PaymentDeclinedException`, an exception handler that converts it to a graceful result, and an exception action that logs the decline. The sample sets `ApplyForAllExceptions` so the action still runs although the handler suppresses the exception — with the default strategy it would never fire for declines.

---

## 9. Notification Publishing Strategies

FastMediatR ships two `INotificationPublisher` implementations that control how `IPublisher.Publish` fans out to multiple `INotificationHandler<TNotification>` subscribers.

### `TaskWhenAllPublisher` (default)

All handlers are started concurrently with `Task.WhenAll`. If one or more handlers throw, **all** handlers still run to completion and the exception is surfaced as an `AggregateException` containing all failures. If handlers are cancelled (rather than faulted), the resulting `OperationCanceledException` is propagated unchanged.

```
Publish(notification)
  ├── Handler A  ─┐
  ├── Handler B  ─┤ all start simultaneously → Task.WhenAll
  └── Handler C  ─┘
```

This is the default when neither strategy is explicitly configured.

### `SequentialAwaitPublisher`

Handlers run one at a time, `await`ed in registration order. If a handler throws, subsequent handlers are **not** invoked. The cancellation token is checked between handlers.

```
Publish(notification)
  → Handler A (awaited)
  → Handler B (awaited, only if A succeeded)
  → Handler C (awaited, only if B succeeded)
```

### Configuring the strategy

Pass the strategy via `FastMediatRConfig.NotificationPublisherStrategy` in the `AddFastMediatR` configuration lambda:

```csharp
using FastMediatR.Extensions.DI;

builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.NotificationPublisherStrategy = NotificationPublisherStrategy.Sequential);
```

| Value | Behavior |
|-------|----------|
| `NotificationPublisherStrategy.ParallelWhenAll` (default `0`) | `TaskWhenAllPublisher` |
| `NotificationPublisherStrategy.Sequential` (`1`) | `SequentialAwaitPublisher` |

### Replacing the publisher entirely

`INotificationPublisher` is registered via `TryAdd`. Register your own implementation **before** calling `AddFastMediatR` and it will be used instead:

```csharp
builder.Services.AddSingleton<INotificationPublisher, MyCustomPublisher>();
builder.Services.AddFastMediatR<Program>();
```

### Scalability — bounding notification fan-out

`TaskWhenAllPublisher` starts **every** handler concurrently with **no max-degree-of-parallelism cap**. For most apps this is ideal, but a notification with many heavyweight handlers (DB writes, outbound HTTP, etc.) can spike thread-pool and connection usage under sustained load.

If you need to bound concurrency, provide a custom `INotificationPublisher`:

```csharp
public sealed class ThrottledPublisher(int maxConcurrency = 4) : INotificationPublisher
{
    private readonly SemaphoreSlim _gate = new(maxConcurrency);

    public async Task Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlers,
        INotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(notification);

        var tasks = new Task[handlers.Count];
        for (int i = 0; i < handlers.Count; i++)
        {
            NotificationHandlerExecutor executor = handlers[i];
            tasks[i] = RunThrottledAsync(executor, notification, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunThrottledAsync(
        NotificationHandlerExecutor executor,
        INotification notification,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await executor.Handler(notification, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

Register it before `AddFastMediatR` (see "Replacing the publisher entirely" above), or switch to `SequentialAwaitPublisher` when one-at-a-time execution is acceptable. See the [security & performance review — Scalability Notes](260529_1200-security-performance-review.md#scalability-notes) for the full rationale.



## 10. Source-Generator Diagnostics

The FastMediatR source generator validates handler wiring at **compile time** and emits the following diagnostics.

| ID    | Severity | Title |
|-------|----------|-------|
| FM001 | Error    | No handler registered for request type |
| FM002 | Error    | Duplicate handlers for the same request type |
| FM003 | Info     | Handler uses a non-default DI lifetime |

### FM001 — No handler for request type

**Triggers when** a concrete `IRequest<TResponse>` type is present in the compilation but no concrete `IRequestHandler<TRequest, TResponse>` is discovered.

```csharp
// ✗ FM001 fires here — no handler for PingQuery
public sealed record PingQuery(string Message) : IRequest<string>;
```

**Fix:** add the matching handler.

```csharp
// ✓
public sealed class PingQueryHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken ct)
        => Task.FromResult(request.Message);
}
```

### FM002 — Duplicate handlers

**Triggers when** two or more concrete `IRequestHandler<TRequest, TResponse>` implementations are discovered for the **same** `TRequest`.

```csharp
// ✗ FM002 fires on BOTH handlers
public sealed class HandlerA : IRequestHandler<PingQuery, string> { … }
public sealed class HandlerB : IRequestHandler<PingQuery, string> { … }
```

**Fix:** remove the duplicate, or split the types into separate composition roots so each assembly has exactly one handler per request type.

### FM003 — Non-default DI lifetime

**Triggers when** a handler is annotated with `[HandlerLifetime]` and the specified lifetime differs from the FastMediatR default (`Scoped`).

```csharp
// FM003 — Singleton lifetime deviates from the Scoped default
[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class CachedQueryHandler : IRequestHandler<GetConfigQuery, Config> { … }
```

FM003 is **Info** severity — it does not block the build. It exists so deviations are visible in the build output and code-review tooling. Suppress per-site with:

```csharp
#pragma warning disable FM003
[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class CachedQueryHandler : IRequestHandler<GetConfigQuery, Config> { … }
#pragma warning restore FM003
```

### Wire up the generated registration

Add one call to `FastMediatRGeneratedRegistration.Register(services)` in your `Program.cs` (until Phase 3 lands `AddFastMediatR<>`):

```csharp
using FastMediatR.Generated;

var builder = WebApplication.CreateBuilder(args);
FastMediatRGeneratedRegistration.Register(builder.Services);
```

Then inject `IMediator` (or `ISender` / `IPublisher`) into your minimal-API endpoints or controllers:

```csharp
app.MapPost("/users", async (CreateUserCommand cmd, IMediator mediator, CancellationToken ct) =>
{
    var id = await mediator.Send(cmd, ct);
    return Results.Created($"/users/{id}", new { id });
});
```

All of the snippets above are taken from [`samples/FastMediatR.Sample.Api/Program.cs`](../samples/FastMediatR.Sample.Api/Program.cs).

---

## 11. Configuration & Options

`FastMediatROptions` is the runtime configuration record for FastMediatR. It is bound automatically by `AddFastMediatR<TMarker>()` from the `"FastMediatR"` section of your `appsettings.json`.

### `appsettings.json`

```json
{
  "FastMediatR": {
    "PerformanceThresholdMs": 300,
    "EnableLogging": true,
    "EnablePayloadLogging": false
  }
}
```

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PerformanceThresholdMs` | `int` | `500` | Threshold (ms) above which `PerformanceBehavior` logs a warning. Must be ≥ 1 (`[Range(1, int.MaxValue)]`). |
| `EnableLogging` | `bool` | `true` | Master switch for `LoggingBehavior`. Set to `false` to short-circuit logging with zero overhead. |
| `EnablePayloadLogging` | `bool` | `false` | When `true`, `LoggingBehavior` includes request payload in log output. Off by default to avoid logging sensitive data. |

### Validation

Options are validated at startup via `ValidateDataAnnotations()` + `ValidateOnStart()`. A misconfigured `PerformanceThresholdMs: 0` will cause an `OptionsValidationException` during `IHost.StartAsync()` — fail-fast, not at first request.

### Reading options from your own code

Inject `IOptions<FastMediatROptions>` wherever you need the values:

```csharp
using FastMediatR.Extensions.DI;
using Microsoft.Extensions.Options;

public sealed class MyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly FastMediatROptions _options;

    public MyBehavior(IOptions<FastMediatROptions> options)
    {
        _options = options.Value;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // use _options.PerformanceThresholdMs etc.
        return await next(cancellationToken).ConfigureAwait(false);
    }
}
```

The section name is available as a constant for configuration binding in your own code:

```csharp
// FastMediatROptions.SectionName == "FastMediatR"
```

---

## 12. Observability

`FastMediatR.Telemetry` adds an `ActivitySource("FastMediatR")`-based tracing behavior for `Send` calls.

### Why use it

- **Distributed-trace correlation.** Each `Send` becomes a span that nests under the ambient `Activity` (e.g. the inbound ASP.NET Core request or an upstream service call), so you can see mediator dispatch latency inline with the rest of a distributed trace in Jaeger, Zipkin, Application Insights, etc.
- **Zero overhead when nobody is listening.** If no `ActivityListener` / OTel exporter is subscribed to the `FastMediatR` source, `StartActivity` returns `null` and the behavior calls the next step directly — the only cost is a single null check per request. This makes it safe to leave registered in production and switch tracing on/off purely via exporter configuration.
- **PII-safe by default.** Only the request/response **type names** are recorded as tags — never the payload. (Contrast with `LoggingBehavior` + `EnablePayloadLogging`, which can serialize the full request.)
- **Complements, not replaces, logging.** `LoggingBehavior` answers "what happened" in logs; tracing answers "how long and in what call context" across service boundaries. Use both.

Install the optional package alongside the core package:

```bash
dotnet add package FastMediatR.Telemetry
```

Register telemetry before `AddFastMediatR<>()` so the tracing behavior wraps the full pipeline:

```csharp
builder.Services.AddFastMediatRTelemetry();

builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.RegisterHandlersFromAssemblyContaining<Program>()
       .AddOpenBehavior(typeof(LoggingBehavior<,>))
       .AddOpenBehavior(typeof(PerformanceBehavior<,>))
       .AddOpenBehavior(typeof(ValidationBehavior<,>)));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddFastMediatRInstrumentation()
        .AddConsoleExporter());
```

`AddFastMediatRInstrumentation()` is a discoverable shorthand for `tracing.AddSource("FastMediatR")` — either form works.

Activity conventions:

- Source name: `FastMediatR`
- Span name: short request type name, for example `CreateUserCommand`
- Tags: `mediator.request_type`, `mediator.response_type`
- Success: `ActivityStatusCode.Ok`
- Failure: `ActivityStatusCode.Error` plus `Activity.AddException(ex)`

Notifications published via `IPublisher.Publish` do not flow through `IPipelineBehavior<,>`, so Phase 6 tracing covers `Send` only.

> **PII / exception details:** on failure the span records `ex.Message` and the stack trace per OpenTelemetry semantic conventions. Exception messages can leak sensitive data in some apps — in PII-sensitive environments, configure a span processor or exporter to scrub the `exception.message` / `exception.stacktrace` attributes before export. See the [security & performance review](260529_1200-security-performance-review.md#6--note--telemetry-records-exception-details) for details.

---

## 13. AOT & Trimming Guidance

FastMediatR is `IsAotCompatible=true` and `IsTrimmable=true`. The source generator eliminates all runtime reflection from the hot path. The only reflection touchpoint is the one-time startup lookup of the generated registration class, annotated with `[DynamicDependency]` to preserve it through trimming.

### What the generator emits

For every compiled assembly that contains `IRequest<>` or `INotification` implementors, the generator emits two `internal` classes in the `FastMediatR.Generated` namespace:

| Generated class | Purpose |
|----------------|---------|
| `FastMediatRDispatcher` | Type-safe switch dispatch for `Send<TResponse>` and `Publish`. No `MethodInfo`, no `dynamic`. |
| `FastMediatRGeneratedRegistration` | Registers the dispatcher and every discovered handler via `services.Add(...)`. |

### AOT-safe startup

In a Normal (non-AOT) app, `AddFastMediatR<TMarker>` locates `FastMediatRGeneratedRegistration` automatically:

```csharp
builder.Services.AddFastMediatR<Program>(cfg =>
    cfg.RegisterHandlersFromAssemblyContaining<Program>());
```

In an AOT-published app, call the generated registration class directly to avoid the one-time `Assembly.GetType` lookup:

```csharp
// AOT-safe: reference the generated class directly
using FastMediatR.Generated;

var builder = WebApplication.CreateBuilder(args);
FastMediatRGeneratedRegistration.Register(builder.Services);   // emitted by source generator
```

> **Tip:** You can call `FastMediatRGeneratedRegistration.Register(services)` in any app — not only AOT builds. The generated class is `internal`, so it is accessible only from the assembly it was generated into.

### Project configuration

Add these properties to your `.csproj` to enable AOT publishing and ensure the generator emits its files:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <IsAotCompatible>true</IsAotCompatible>
  <IsTrimmable>true</IsTrimmable>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

### Suppressing IL2026 for assembly-scanning APIs

`RegisterHandlersFromAssemblyContaining<T>` is annotated `[RequiresUnreferencedCode]` because it inspects `Assembly.GetTypes()` at startup. In AOT mode, wrap the call with `#pragma warning disable IL2026` or switch to the direct `FastMediatRGeneratedRegistration.Register(services)` approach shown above.

```csharp
#pragma warning disable IL2026
services.AddFastMediatR<Program>(cfg =>
    cfg.RegisterHandlersFromAssemblyContaining<Program>());
#pragma warning restore IL2026
```

### Verifying AOT compatibility

The `tests/FastMediatR.Aot.SmokeTest` project is published with `PublishAot=true` as part of CI. It exercises `Send<TResponse>`, `Send(IRequest)`, and `Publish` using the generated registration class. A non-zero exit code fails the CI step.

---

## 14. Migration from MediatR

FastMediatR is a drop-in replacement for MediatR. The public interface surface (`IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`, `INotification`, `INotificationHandler<TNotification>`, `ISender`, `IPublisher`, `IMediator`, `IPipelineBehavior<,>`) is identical by design.

### Package swap table

| MediatR | FastMediatR |
|---------|-------------|
| `using MediatR;` | `using FastMediatR;` |
| `services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>())` | `services.AddFastMediatR<Program>(cfg => cfg.RegisterHandlersFromAssemblyContaining<Program>())` |
| `IMediator` / `ISender` / `IPublisher` | identical |
| `IRequest<TResponse>` | identical |
| `IRequest` (void) | identical (maps to `IRequest<Unit>` internally) |
| `INotification` | identical |
| `IPipelineBehavior<TRequest, TResponse>` | identical |
| `IRequestPreProcessor<TRequest>` | identical |
| `IRequestPostProcessor<TRequest, TResponse>` | identical |
| `IValidator<T>` (via FluentValidation) | identical — use `ValidationBehavior<,>` |
| `MediatR.Extensions.Microsoft.DependencyInjection` | `FastMediatR.Extensions.DI` |

### Worked migration example

Suppose you have an existing MediatR handler:

```csharp
// BEFORE — MediatR
using MediatR;

public record CreateOrderCommand(string CustomerId, decimal Amount) : IRequest<Guid>;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    public Task<Guid> Handle(CreateOrderCommand request, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());
}
```

And the DI registration:

```csharp
// BEFORE — MediatR startup
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
```

**Step 1 — Swap the NuGet package:**

```xml
<!-- Remove -->
<PackageReference Include="MediatR" Version="12.*" />
<PackageReference Include="MediatR.Extensions.Microsoft.DependencyInjection" Version="12.*" />

<!-- Add -->
<PackageReference Include="FastMediatR" Version="1.*" />
<PackageReference Include="FastMediatR.Extensions.DI" Version="1.*" />
```

**Step 2 — Update `using` directives:**

```csharp
// AFTER — FastMediatR (no other changes needed)
using FastMediatR;

public record CreateOrderCommand(string CustomerId, decimal Amount) : IRequest<Guid>;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    public Task<Guid> Handle(CreateOrderCommand request, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());
}
```

**Step 3 — Update the DI registration:**

```csharp
// AFTER — FastMediatR startup
services.AddFastMediatR<Program>(cfg =>
    cfg.RegisterHandlersFromAssemblyContaining<Program>());
```

That is the entire migration. All call sites using `ISender.Send(...)`, `IPublisher.Publish(...)`, and pipeline behaviors compile and behave identically.

### Known differences

| Behavior | MediatR | FastMediatR |
|----------|---------|-------------|
| Dispatch mechanism | Reflection at request time | Source-generated switch (compile time) |
| AOT support | Limited / opt-in | First-class |
| `Unit` equality | `Unit` is a `struct` | `Unit` is a `readonly record struct` |
| `ValidationBehavior` | Third-party (MediatR.Extensions.FluentValidation) | Built-in (`FastMediatR.Core`) |
| `LoggingBehavior` | Third-party | Built-in |
| `PerformanceBehavior` | Third-party | Built-in |

---

## 15. Troubleshooting & FAQ

_(Coming in Phase 8.)_

---

## 16. Performance Characteristics

_(Coming in Phase 7. See [`docs/benchmarks/`](benchmarks/) for raw results.)_

---

## 17. Document Status

This guide is published as part of the FastMediatR repository and is updated at the close of every implementation phase per the task breakdown. The **U** (usage guide) closing gate in [260524_1230-fastmediatr-task-breakdown.md](260524_1230-fastmediatr-task-breakdown.md) is what keeps this document honest.
