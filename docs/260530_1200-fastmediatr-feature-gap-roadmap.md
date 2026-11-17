# FastMediatR — feature gap analysis vs MediatR & build roadmap

> Compares the functionality of **[MediatR](https://github.com/LuckyPennySoftware/MediatR)** (`main`)
> against the current FastMediatR implementation, identifies what is **not yet implemented**,
> and proposes a prioritized roadmap with concrete designs that respect FastMediatR's
> **zero-reflection, source-generated, AOT-first** architecture.
>
> Scope: this document lists only functionality MediatR has that FastMediatR currently lacks.
> Everything already at parity (requests, handlers, notifications, pipeline behaviors,
> pre/post-processors, publisher strategies, DI integration, telemetry) is out of scope —
> see the [interface comparison](260529_1300-fastmediatr-mediatr-interface-comparison.md).

## Summary — priority table

| # | Missing capability | MediatR surface | Impact | Effort | Priority | Target |
|---|---|---|---|---|---|---|
| 1 | **Streaming requests** | `IStreamRequest<T>`, `IStreamRequestHandler<,>`, `IStreamPipelineBehavior<,>`, `ISender.CreateStream` | High — common for large result sets / `IAsyncEnumerable` APIs | High | 🔴 P1 | v0.2 |
| 2 | **Exception handling pipeline** | `IRequestExceptionHandler<,,>`, `IRequestExceptionAction<,>`, exception processor behaviors | High — resilience / cross-cutting error policy | Medium | ✅ Done (v0.2) | v0.2 |
| 3 | **Explicit (closed-type) registration** | `AddBehavior`, `AddRequestPreProcessor`, `AddRequestPostProcessor`, `AddStreamBehavior` | Medium — fine-grained DI control & MediatR parity | Low | 🟠 P2 | v0.2 |
| 4 | **Generic `Send` / `Publish` overloads** | `Send<TRequest>(TRequest) where TRequest : IRequest`, `Publish<TNotification>(TNotification)` | Medium — ergonomics + avoids boxing/interface narrowing | Low | 🟠 P2 | v0.2 |
| 5 | **Covariant notification dispatch** | Handlers registered against base/interface notification types receive derived notifications | Medium — polymorphic event handling | Medium | 🟠 P2 | v0.3 |
| 6 | **Multi-assembly registration in one call** | `RegisterServicesFromAssemblies(params Assembly[])`, `RegisterServicesFromAssemblyContaining(params)` | Low — convenience | Low | 🟢 P3 | v0.3 |
| 7 | **Custom type filtering** | `TypeEvaluator` predicate on registration | Low — niche | Low | 🟢 P3 | v0.3 |
| 8 | **Notification handler de-duplication** | De-dupes the same handler instance before dispatch | Low — correctness edge | Low | 🟢 P3 | v0.3 |
| 9 | **Open-generic request handlers** | Runtime-closed `Handler<T> : IRequestHandler<Request<T>, …>` | Low — advanced, AOT-hostile | High | 🟢 P3 | backlog |
| 10 | **Generator-emitted exception handler/action registration** | — (FastMediatR design follow-up to #2; MediatR itself resolves these by runtime reflection) | Medium — closes the "pure generated path silently ignores exception handlers" gap documented in usage guide §8 | Medium | 🟢 P3 | v0.3 |

Legend — Priority: 🔴 P1 build first · 🟠 P2 next · 🟢 P3 later/backlog · ✅ Done implemented.

---

## 1. Streaming requests (P1)

### What MediatR has

```csharp
public interface IStreamRequest<out TResponse> { }

public interface IStreamRequestHandler<in TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken ct);
}

public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>();

public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken ct);
}

// On ISender:
IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct);
IAsyncEnumerable<object?>   CreateStream(object request, CancellationToken ct);
```

FastMediatR currently has **none** of these — there is no streaming concept at all.

### Why it matters

Streaming is the standard way to return large or unbounded result sets without buffering them
in memory (paged exports, log tailing, server-sent events). It is one of MediatR's headline
features and a frequent reason teams cannot migrate.

### Proposed design (AOT-friendly)

1. **New abstractions in `FastMediatR.Core`** (one file each):
   - `IStreamRequest<out TResponse> : IBaseRequest` (reuse the existing marker base).
   - `IStreamRequestHandler<in TRequest, TResponse>`.
   - `StreamHandlerDelegate<out TResponse>` (parameterless, matching MediatR — note MediatR's
     stream delegate does *not* take a `CancellationToken`; keep parity).
   - `IStreamPipelineBehavior<in TRequest, TResponse>`.
2. **Extend `ISender`** with the two `CreateStream` overloads. This is an additive,
   non-breaking change.
3. **Source generator** — add a parallel discovery + emit path:
   - Discover `IStreamRequestHandler<,>` implementations.
   - Emit a `FastMediatRStreamDispatcher` with the same switch-dispatch shape used for
     `Send`, but wrapping the handler in registered `IStreamPipelineBehavior<,>` instances
     composed right-to-left.
   - The behavior composition is a fold over `IAsyncEnumerable`, so no `Task<Unit>` shim is
     needed; `Unit` does not apply to streams.
4. **Diagnostics** — mirror `FM001`/`FM002`: emit a new diagnostic for a stream request with
   no handler and for more than one stream handler.
5. **Cancellation** — thread the token through `CreateStream` into the handler's
   `[EnumeratorCancellation]` parameter; the generator passes it to the terminal handler call.

### Risks

- `IAsyncEnumerable` pipeline composition is harder to get allocation-lean than `Task`.
  Keep the behavior fold a simple iterator wrapper; avoid LINQ over async sequences on the hot path.
- AOT: `IAsyncEnumerable` state machines are fully AOT-safe, so no reflection is introduced.

---

## 2. Exception handling pipeline (P1) — ✅ Implemented (2026-05-30)

> **Status:** Shipped. The abstractions live in `FastMediatR.Core`; the two behaviors and the
> auto-registration logic live in `FastMediatR.Extensions.DI`. See
> [usage guide §8](260524_1300-fastmediatr-usage-guide.md) for end-user docs.
>
> **Implementation note — reflection vs. generator.** The original proposal (below) assumed the
> source generator would emit closed handler registrations for O(1) lookup. The shipped
> implementation instead resolves handlers at runtime by walking the thrown exception's type
> hierarchy and querying DI for each closed `IRequestExceptionHandler<,,>` / `IRequestExceptionAction<,>`.
> This keeps `FastMediatR.Core` fully AOT-clean (abstractions only) and confines the reflection to
> `FastMediatR.Extensions.DI`, which is already the `AddFastMediatR` opt-in path
> (`RequiresUnreferencedCode` / `RequiresDynamicCode`). A future optimization may move the closed-type
> resolution into the generator as originally envisioned (tracked as item #10).

### What MediatR has

```csharp
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TException : Exception
{
    Task Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state, CancellationToken ct);
}

public interface IRequestExceptionAction<in TRequest, in TException>
    where TException : Exception
{
    Task Execute(TRequest request, TException exception, CancellationToken ct);
}
```

Plus two auto-registered behaviors (`RequestExceptionProcessorBehavior`,
`RequestExceptionActionProcessorBehavior`) and `RequestExceptionHandlerState<TResponse>`
(which lets a handler mark an exception handled and supply a fallback response).

FastMediatR has **no exception-handling abstraction** — exceptions simply propagate out of the pipeline.

### Why it matters

Centralized, type-routed exception policy (translate domain exceptions to results, log-and-swallow,
supply fallbacks) is a widely used MediatR pattern. Without it, teams must hand-roll a behavior.

### Proposed design

1. **Core abstractions** (`FastMediatR.Core`):
   - `IRequestExceptionHandler<in TRequest, TResponse, in TException>`.
   - `IRequestExceptionAction<in TRequest, in TException>`.
   - `RequestExceptionHandlerState<TResponse>` (sealed; `Handled` flag + `Response`).
2. **Two auto-registered behaviors**, appended *outermost* of the processor behaviors so they
   wrap the handler and pre/post-processors:
   - `RequestExceptionActionProcessorBehavior<,>` — runs all matching actions, then rethrows.
   - `RequestExceptionProcessorBehavior<,>` — runs matching handlers; if one sets
     `state.Handled`, returns `state.Response` instead of rethrowing.
3. **Exception-type matching** — match handlers from the most-derived exception type up the
   inheritance chain (MediatR semantics). Because this needs the runtime exception's type
   hierarchy, resolve candidate handlers via the generated registration keyed by
   `(requestType, exceptionType)` and walk `exception.GetType().BaseType` chain. The walk uses
   `Type.BaseType` only (AOT-safe, no `GetInterfaces` reflection on the hot path); generator
   emits the exact closed handler list so no open-generic runtime closing is required.
4. **Generator** — discover `IRequestExceptionHandler`/`IRequestExceptionAction` implementations
   and emit their closed registrations, grouped by request + exception type for O(1) lookup.
5. **Config toggle** — `FastMediatRConfig.RequestExceptionActionProcessorStrategy`
   (ApplyForUnhandledExceptions vs ApplyForAllExceptions), matching MediatR.

### Risks

- Ordering relative to pre/post-processor behaviors must be specified and tested.
- Keep the exception-type walk bounded and allocation-free.

---

## 3. Explicit (closed-type) registration APIs (P2)

### What MediatR has

`MediatRServiceConfiguration` exposes, beyond open-generic registration:

- `AddBehavior<TImplementation>()` / `AddBehavior(serviceType, implementationType)` — register a
  **closed** behavior for a specific request/response.
- `AddRequestPreProcessor<T>()` / `AddRequestPostProcessor<T>()`.
- `AddStreamBehavior(...)`.

FastMediatR's `FastMediatRConfig` only has `AddOpenBehavior(Type, ServiceLifetime)` — no
closed-type behavior or explicit processor registration.

### Why it matters

Lets consumers attach a behavior to exactly one request type instead of all of them, and gives
MediatR migrators the same registration vocabulary.

### Proposed design

Add to `FastMediatRConfig`:

```csharp
public FastMediatRConfig AddBehavior<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient);
public FastMediatRConfig AddBehavior(Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient);
public FastMediatRConfig AddRequestPreProcessor<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient);
public FastMediatRConfig AddRequestPostProcessor<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient);
public FastMediatRConfig AddStreamBehavior(Type openBehaviorType, ServiceLifetime lifetime = ServiceLifetime.Transient); // pairs with #1
```

Closed behaviors are validated to implement the closed `IPipelineBehavior<TReq,TResp>` and added
as ordinary `ServiceDescriptor`s in declaration order. No generator changes required — these are
pure DI registrations consumed by the existing pipeline resolution.

### Risks

- Low. The only subtlety is preserving registration *order* relative to open behaviors.

---

## 4. Generic `Send` / `Publish` overloads (P2)

### What MediatR has

```csharp
Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest;   // void, no boxing
Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification;
Task Publish(object notification, CancellationToken ct = default);
```

FastMediatR has `Send<TResponse>(IRequest<TResponse>)`, `Send(object)`, and `Publish(INotification)`
— but **not** the generic void `Send<TRequest>` overload nor the generic/object `Publish` overloads.

### Why it matters

- The generic void `Send<TRequest>` avoids boxing the request and reads cleanly for commands.
- `Publish<TNotification>` and `Publish(object)` match MediatR call sites verbatim, easing migration
  and enabling runtime-typed publishing (e.g. from a message bus).

### Proposed design

1. **`ISender`** — add `Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest`.
   Implement by delegating to the existing `Send<Unit>` path (the void model already maps to `Unit`).
2. **`IPublisher`** — add `Publish<TNotification>(TNotification) where TNotification : INotification`
   and `Publish(object)`. The generic overload forwards to the existing interface-typed path; the
   `object` overload validates `is INotification` and forwards, throwing `ArgumentException` otherwise
   (mirroring the untyped `Send(object)` contract already in place).
3. **No generator changes** — these are thin wrappers over existing generated dispatch.

### Risks

- Minimal. Keep the interface-narrowing `Publish(INotification)` for source compatibility and add
  the others alongside.

---

## 5. Covariant notification dispatch (P2)

### What MediatR has

A handler registered for a base notification type (or interface) receives notifications of derived
types. MediatR resolves all `INotificationHandler<T>` where `T` is assignable from the runtime
notification type.

FastMediatR's generated dispatch binds handlers to the **exact** notification type only.

### Why it matters

Enables polymorphic event handling — e.g. an audit handler subscribing to a base `IDomainEvent`
receiving every concrete event.

### Proposed design

1. **Generator** — when emitting the notification dispatch table, for each concrete notification
   type also include handlers registered against any base class / implemented `INotification`-derived
   interface in the notification's compile-time hierarchy. This is computed at generation time from
   the symbol graph, so dispatch stays a static switch with **no runtime reflection**.
2. **De-dup** — ensure a handler that matches via multiple base types is invoked once (ties into #8).
3. **Opt-in flag** — gate behind `FastMediatRConfig.NotificationCovariance` (default off) to preserve
   current exact-match performance for users who do not need it.

### Risks

- Larger generated dispatch tables. Mitigate by only expanding hierarchies that actually have
  base-typed handlers.

---

## 6. Multi-assembly registration convenience (P3)

`RegisterServicesFromAssemblies(params Assembly[])` and the `params`-style
`RegisterServicesFromAssemblyContaining`. FastMediatR has single-assembly methods that can be called
repeatedly. **Design:** add `params` overloads to `FastMediatRConfig` that loop the existing
`RegisterHandlersFromAssembly`. Trivial, additive.

## 7. Custom type filtering (P3)

MediatR's `TypeEvaluator` predicate skips types during registration. **Design:** add
`FastMediatRConfig.TypeEvaluator (Func<Type,bool>)` consulted by `AssemblyScanner` before
registering a discovered type. Reflection-path only; does not affect the generated path.

## 8. Notification handler de-duplication (P3)

MediatR de-dupes identical handler executors before publishing. **Design:** when the generator emits
the handler executor list (and again if #5 lands), de-dup by implementation type at generation time.
Cheap correctness fix.

## 9. Open-generic request handlers (P3 / backlog)

MediatR can close an open-generic handler `Handler<T> : IRequestHandler<Request<T>, …>` at runtime.
This is inherently reflection/`MakeGenericType`-driven and **hostile to AOT/trimming**, which is
FastMediatR's core value proposition. **Design (if ever):** restrict to compile-time closing — the
generator enumerates concrete request types and emits the closed handler registrations — never
runtime `MakeGenericType`. Document as an explicit non-goal for the AOT path otherwise.

## 10. Generator-emitted exception handler/action registration (P3)

Design follow-up to #2, not a MediatR parity gap — MediatR resolves exception handlers/actions by
runtime reflection too. Today the two exception behaviors resolve `IRequestExceptionHandler<,,>` /
`IRequestExceptionAction<,>` services per failure occurrence, only on the reflection-based
`AddFastMediatR` path; on the **pure source-generated path** (`FastMediatRGeneratedRegistration.Register`
without `AddFastMediatR`) the behaviors are not registered and any implementations are silently
ignored — documented in [usage guide §8](260524_1300-fastmediatr-usage-guide.md) (AOT note).
**Design (if ever):** the generator discovers handler/action implementations, emits their closed
registrations grouped by `(requestType, exceptionType)`, and registers the two behaviors in the
generated composition with the strategy nesting baked in — removing the per-failure
`MakeGenericType` / DI lookup while keeping type-hierarchy matching semantics identical.
Re-engagement trigger: a consumer needs exception handling on the pure generated path, or a
measured failure cascade makes the reflection cold path hot.

---

## Suggested release sequencing

- **v0.2** — #1 Streaming, ✅ #2 Exception handling (shipped 2026-05-30), #3 Explicit registration, #4 Generic overloads.
  These deliver the most-requested MediatR features and the easy ergonomics wins together.
- **v0.3** — #5 Covariant notifications, #6 multi-assembly, #7 type filter, #8 de-dup, #10 generator exception registration.
- **Backlog** — #9 open-generic handlers, gated on a clear AOT-safe design.

## Design principles to preserve

1. **Zero runtime reflection on the hot path** — every new feature dispatches through generated,
   statically-known code. Reflection is allowed only in the opt-in `AddFastMediatR` scan path.
2. **AOT + trimming safe** — no `MakeGenericType` / `Activator.CreateInstance` on dispatch.
3. **Additive, source-compatible** — new interfaces and overloads must not break existing consumers.
4. **One uniform pipeline model** — reuse the `Unit`-based request pipeline; streams get their own
   parallel `IAsyncEnumerable` pipeline rather than bending the `Task` one.

## Maintenance & housekeeping

Tracked maintenance work with explicit removal triggers:

- **Remove the `RuntimeFrameworkVersion` pin** in `tests/FastMediatR.Aot.SmokeTest/FastMediatR.Aot.SmokeTest.csproj`
  (currently `10.0.15`) once the CI/local SDK band's bundled runtime is **≥ 10.0.14** — the pin only works around
  CVE-2026-111497 (`Microsoft.DotNet.ILCompiler` 10.0.0, Brotli decoder OOB write, fixed in 10.0.14) while the
  pinned SDK is 10.0.100. Stale pins silently rot; re-run `dotnet list package --vulnerable --include-transitive`
  after removing it. *(Added 2026-11-17, code review CR-12.)*

## Related documents

- [Interface comparison](260529_1300-fastmediatr-mediatr-interface-comparison.md)
- [Design spec](260524_1200-fastmediatr-design.md)
- [Usage guide](260524_1300-fastmediatr-usage-guide.md)
