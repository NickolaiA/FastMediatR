# FastMediatR ↔ MediatR interface comparison

> Compares the **public interface surface implemented by FastMediatR** against the
> equivalent types in [MediatR](https://github.com/LuckyPennySoftware/MediatR) (`main`).
> Only types that exist in FastMediatR are listed. Each row is marked as a full match,
> a partial (compatible-but-narrowed) match, or a signature difference.

| Legend | Meaning |
|---|---|
| ✅ Match | Signature is identical (or differs only in trivia such as a default-token annotation). |
| ⚠️ Partial | Source-compatible for typical consumer code, but a parameter/constraint is narrowed. |
| ⚠️ Different | A custom implementation written against MediatR may need edits to compile. |

## Core dispatch interfaces

| FastMediatR type | Member signature | Status | Notes |
|---|---|---|---|
| `IMediator` | `: ISender, IPublisher` | ✅ Match | Identical composition. |
| `ISender` | `Task<TResponse> Send<TResponse>(IRequest<TResponse>, CancellationToken)` | ✅ Match | Verbatim. |
| `ISender` | `Task<object?> Send(object, CancellationToken)` | ✅ Match | Untyped dispatch, verbatim. |
| `IPublisher` | `Task Publish(INotification, CancellationToken)` | ⚠️ Partial | MediatR's overload is `Publish(object, …)`; FastMediatR narrows the parameter to `INotification`. Behaviorally equivalent. |

## Request / handler contracts

| FastMediatR type | Member signature | Status | Notes |
|---|---|---|---|
| `IBaseRequest` | marker | ✅ Match | Same non-generic base. |
| `IRequest<out TResponse>` | `: IBaseRequest` | ✅ Match | Verbatim. |
| `IRequest` (void) | `: IRequest<Unit>` | ⚠️ Different | Modern MediatR models void as `IRequest : IBaseRequest` (plain `Task`). FastMediatR keeps the `IRequest : IRequest<Unit>` model so void flows through `Send<Unit>`. Source-level compatible; the base-interface chain differs. |
| `IRequestHandler<in TRequest, TResponse>` | `Task<TResponse> Handle(TRequest, CancellationToken)`, `where TRequest : IRequest<TResponse>` | ✅ Match | Verbatim. |
| `IRequestHandler<in TRequest>` (void) | `: IRequestHandler<TRequest, Unit>`, returns `Task<Unit>` | ⚠️ Different | MediatR's void handler returns plain `Task` with `where TRequest : IRequest`. FastMediatR returns `Task<Unit>`. |

## Pipeline

| FastMediatR type | Member signature | Status | Notes |
|---|---|---|---|
| `IPipelineBehavior<in TRequest, TResponse>` | `Task<TResponse> Handle(TRequest, RequestHandlerDelegate<TResponse>, CancellationToken)` | ⚠️ Partial | Method signature is verbatim. The generic constraint is tighter: FastMediatR uses `where TRequest : IRequest<TResponse>` vs MediatR's `where TRequest : notnull`. |
| `RequestHandlerDelegate<TResponse>` | `delegate Task<TResponse>(CancellationToken)` | ✅ Match | MediatR has `= default` on the token; functionally identical. |
| `IRequestPreProcessor<in TRequest>` | `Task Process(TRequest, CancellationToken)`, `where TRequest : notnull` | ✅ Match | Verbatim. |
| `IRequestPostProcessor<in TRequest, in TResponse>` | `Task Process(TRequest, TResponse, CancellationToken)`, `where TRequest : notnull` | ✅ Match | Verbatim. |

## Notifications

| FastMediatR type | Member signature | Status | Notes |
|---|---|---|---|
| `INotification` | marker | ✅ Match | Verbatim. |
| `INotificationHandler<in TNotification>` | `Task Handle(TNotification, CancellationToken)`, `where TNotification : INotification` | ✅ Match | Verbatim. |
| `INotificationPublisher` | `Task Publish(IReadOnlyList<NotificationHandlerExecutor>, INotification, CancellationToken)` | ⚠️ Different | MediatR's first parameter is `IEnumerable<NotificationHandlerExecutor>` (named `handlerExecutors`); FastMediatR uses `IReadOnlyList<…>` (named `handlers`). A MediatR `INotificationPublisher` implementation would not compile unchanged. |
| `NotificationHandlerExecutor` | helper type | ✅ Match | Same role. |

## Support types

| FastMediatR type | Status | Notes |
|---|---|---|
| `Unit` | ✅ Match | `readonly record struct` with `Value` / `Task`; same semantics as MediatR's `Unit`. |

## Summary

- **Full matches:** `IMediator`, `ISender` (both overloads), `IBaseRequest`, `IRequest<TResponse>`, `IRequestHandler<,>`, `IPipelineBehavior` method, `RequestHandlerDelegate`, pre/post-processors, `INotification`, `INotificationHandler`, `Unit`, `NotificationHandlerExecutor`.
- **Partial (compatible but narrowed):** `IPublisher.Publish` parameter type, `IPipelineBehavior` generic constraint.
- **Signature differences (custom implementations may need edits):** void `IRequest` / `IRequestHandler<TRequest>` modeled over `Unit`, and `INotificationPublisher` using `IReadOnlyList` instead of `IEnumerable`.

For the vast majority of consumer code (requests, handlers, notifications, behaviors), the surface is drop-in identical. The differences only surface when writing a **custom `INotificationPublisher`** or relying on the void-handler returning a plain `Task`.

## Deep dive — the `Task<Unit>` void model

The most visible signature difference above is how each library represents a **command that returns nothing** (a "void" request). It is worth understanding because it is a deliberate design choice, not an oversight.

### The two models

**FastMediatR (the `Task<Unit>` model):**

```csharp
public interface IRequest : IRequest<Unit> { }

public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
    // inherited: Task<Unit> Handle(TRequest request, CancellationToken ct)
}
```

**Modern MediatR (the plain-`Task` model):**

```csharp
public interface IRequest : IBaseRequest { }   // NOT IRequest<Unit>

public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    Task Handle(TRequest request, CancellationToken ct);   // plain Task
}
```

### What `Unit` is

`Unit` is a "there is no meaningful value" placeholder — like `void`, but a real type you can use as a generic argument. Because you cannot write `Task<void>`, the void case is modeled as `TResponse = Unit` so that void commands and value-returning requests can flow through **one** generic pipeline (`IPipelineBehavior<TRequest, TResponse>`). A handler returns `Unit.Value` to signal "done, nothing to report".

### Why the difference matters

| Concern | `Task<Unit>` (FastMediatR) | plain `Task` (modern MediatR) |
|---|---|---|
| Void handler returns | `return Unit.Value;` (or the cached `Unit.Task`) | `return Task.CompletedTask;` |
| Pipeline / behavior model | Uniform — every request, void included, uses one `IPipelineBehavior<T, TResponse>` | Needs extra plumbing to bridge `Task` ↔ `Task<Unit>` internally |
| Generated dispatch paths | One path for all requests | A special-cased void path in addition to the value path |
| Consumer ergonomics | Slightly noisier handler bodies (`Unit.Value`) | Cleaner — reads like ordinary `async Task` code |
| Allocation | None when returning the cached `Unit.Task` | None |

### Why this makes FastMediatR better for its goals

FastMediatR is a **zero-reflection, AOT-friendly, source-generator-based** mediator. For that architecture, the `Task<Unit>` model is the stronger choice:

- **One dispatch path, not two.** Because void is just `TResponse = Unit`, the generator emits a *single* uniform dispatch and pipeline shape for every request. Modern MediatR's plain-`Task` void handler forces a second, special-cased path. Fewer emitted code paths means **less generated code, fewer edge cases, and easier AOT/trimming correctness** — exactly the properties FastMediatR optimizes for.
- **Uniform pipeline behaviors.** A single `IPipelineBehavior<TRequest, TResponse>` covers commands, queries, and void commands identically. Behavior authors never special-case void, and the library never has to bridge `Task` and `Task<Unit>` at runtime.
- **No hidden allocations.** Returning the cached `Unit.Task` keeps the void path allocation-free, matching the plain-`Task` model's allocation profile — so the simplicity is free.

The only cost is cosmetic: handler bodies return `Unit.Value` instead of nothing, and a *custom* component written against modern MediatR's plain-`Task` void handler would need a one-line tweak. For normal application code the difference is invisible — the call site is still `await sender.Send(command)`.

> **Bottom line:** the `Task<Unit>` model trades a touch of handler-body verbosity for a simpler, uniform, fully generated dispatch pipeline with no void special-casing — the right trade-off for an AOT-first, source-generated library.

## Related documents

- [Usage guide](260524_1300-fastmediatr-usage-guide.md) — §"Migrating from MediatR".
- [Design spec](260524_1200-fastmediatr-design.md) — §5 interface design.
