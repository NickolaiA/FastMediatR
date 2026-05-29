# FastMediatR — Security & Performance Code Review

**Date:** 2026-05-29
**Scope:** `FastMediatR.Core`, `FastMediatR.Extensions.DI`, `FastMediatR.Generator`, `FastMediatR.Telemetry`
**Focus:** Security (OWASP Top 10), performance, and scalability

## Summary

The codebase is well-engineered and security-conscious. Hot paths are clean, PII logging is opt-in and documented, reflection is constant-driven (no injection surface), and AOT/trimming is correctly annotated. One genuine correctness bug was found, plus a few minor performance/robustness notes.

> **Update 2026-05-29:** All findings have been addressed. See the [usage guide changelog](260524_1300-fastmediatr-usage-guide.md#changelog).

| # | Severity | Area | Issue | Status |
|---|----------|------|-------|--------|
| 1 | 🔴 Bug | `TaskWhenAllPublisher` | `NullReferenceException` on handler cancellation | ✅ Fixed |
| 2 | 🟡 Robustness | `AssemblyScanner` | Unhandled `ReflectionTypeLoadException` | ✅ Fixed |
| 3 | 🟡 Performance | Generated `ExecutePipeline` | Per-request array + closure allocations | ✅ Fixed |
| 4 | 🟢 Minor | `ValidationBehavior` | Redundant null filter | ✅ Fixed |
| 5 | 🟢 Minor | `FastMediatRConfig.AddOpenBehavior` | No `IPipelineBehavior<,>` verification | ✅ Fixed |
| 6 | 🟢 Note | `TracingBehavior` | Exception details written to spans (PII) | ✅ Documented |

---

## 1. 🔴 Bug — `TaskWhenAllPublisher` throws `NullReferenceException` on cancellation

**Status:** ✅ Fixed (2026-05-29)
**File:** `src/FastMediatR.Core/TaskWhenAllPublisher.cs`

```csharp
try { await whenAll.ConfigureAwait(false); }
catch { throw whenAll.Exception!; }
```

When a handler is **canceled** (not faulted), `Task.WhenAll` transitions to the `Canceled` state and its `.Exception` property is `null`. The `catch` swallows the `OperationCanceledException` and then `throw whenAll.Exception!` dereferences null, throwing a `NullReferenceException` that masks the real cancellation.

**Suggested fix:**

```csharp
catch
{
    // Surface all faults if any handler faulted; otherwise let cancellation/OCE propagate.
    if (whenAll.Exception is { } ex)
    {
        throw ex;
    }
    throw;
}
```

This preserves the "all errors aggregated" behavior on faults while propagating cancellation correctly. Should be paired with a regression test.

---

## 2. 🟡 Robustness — `AssemblyScanner.Scan` can throw `ReflectionTypeLoadException`

**Status:** ✅ Fixed (2026-05-29) — `GetLoadableTypes` now catches `ReflectionTypeLoadException` and uses the successfully loaded types.
**File:** `src/FastMediatR.Extensions.DI/AssemblyScanner.cs`

`assembly.GetTypes()` throws `ReflectionTypeLoadException` if any type fails to load (common with plugin assemblies or missing optional dependencies). The whole `AddFastMediatR` call would fail.

**Suggestion:** Catch it and use `ex.Types.Where(t => t is not null)`, or document that scanned assemblies must be fully loadable.

---

## 3. 🟡 Performance — per-request allocations in generated `ExecutePipeline`

**Status:** ✅ Fixed (2026-05-29) — added a zero-behavior fast path (invokes the handler directly with no closures) and avoid the `.ToArray()` allocation when the DI container already returns an indexable collection.
**File:** `src/FastMediatR.Generator/Emitters/DispatcherEmitter.cs`

```csharp
var behaviors = _services.GetServices<...>().ToArray();
...
pipeline = ct => b.Handle(request, next, ct); // closure per behavior
```

Every `Send` allocates a behavior array (`.ToArray()`) plus N closure objects for the chain. This is the standard MediatR-style trade-off and is fine for most apps, but it is the main remaining hot-path allocation for a library branded "Fast."

**Resolution:** The generated `ExecutePipeline` now (1) returns `handler(cancellationToken)` directly when no behaviors are registered — eliminating the array and all closures for the no-behavior case — and (2) casts the `GetServices` result to `IReadOnlyList<>` instead of calling `.ToArray()` when the container already returns an indexable collection (the default `Microsoft.Extensions.DependencyInjection` provider does), avoiding the extra copy. When behaviors are present the closure chain is still built, which is required to compose them.

---

## 4. 🟢 Minor — redundant work in `ValidationBehavior`

**Status:** ✅ Fixed (2026-05-29)
**File:** `src/FastMediatR.Core/ValidationBehavior.cs`

```csharp
.Where(f => f is not null)
```

This filter is unnecessary — FluentValidation never yields null `ValidationFailure` entries. Removing it avoids an extra enumerator allocation on the validation-failure (cold) path.

---

## 5. 🟢 Minor — `AddOpenBehavior` doesn't verify the type implements `IPipelineBehavior<,>`

**Status:** ✅ Fixed (2026-05-29)
**File:** `src/FastMediatR.Extensions.DI/FastMediatRConfig.cs`

The method validates the type is an open generic but not that it implements `IPipelineBehavior<,>`. A wrong type registers fine and fails later at resolve time with a less obvious error. A guard here would give a clearer failure.

---

## 6. 🟢 Note — telemetry records exception details

**Status:** ✅ Documented (2026-05-29) — added an "Exception detail caution" note to the `TracingBehavior` XML docs advising redaction in PII-sensitive environments.
**File:** `src/FastMediatR.Telemetry/TracingBehavior.cs`

```csharp
activity.SetStatus(ActivityStatusCode.Error, ex.Message);
activity.AddException(ex);
```

This writes the exception message and stack trace to spans. This is correct OpenTelemetry behavior, but in PII-sensitive environments exception messages can leak data. Worth a one-line documentation note.

---

## Security Summary

No OWASP Top 10 issues found. Specifically verified:

- **No injection surface** in reflection — generated type/method names are compile-time constants (`GeneratedRegistrationLocator.cs`), not user input.
- **PII logging** is opt-in (`EnablePayloadLogging` defaults to `false`) and clearly documented.
- **No arbitrary assembly loading** — only explicitly supplied assemblies are scanned.
- Source-generated logging uses `[LoggerMessage]` (no format-string injection).

---

## Scalability Notes

A scalability-focused pass (2026-05-29) found **no defects**. The items below are intentional design trade-offs documented for operators running FastMediatR under high load. None require code changes.

| Area | Characteristic | Guidance |
|------|----------------|----------|
| `TaskWhenAllPublisher` | Notification handlers fan out with **no max-degree-of-parallelism cap** — every handler starts concurrently. | For notifications with many heavyweight handlers, supply a custom `INotificationPublisher` that throttles concurrency (e.g. via `SemaphoreSlim` or `Parallel.ForEachAsync` with `MaxDegreeOfParallelism`), or switch to `SequentialAwaitPublisher`. |
| Generated `Publish` | Each publish allocates a `NotificationHandlerExecutor[]` plus one closure per handler. | Negligible except at very high notification throughput; inherent to the publisher contract. |
| `ValidationBehavior` | Validators run via `Task.WhenAll`, adding task-scheduling overhead for synchronous CPU-bound validators. | Beneficial for genuinely async validators; negligible otherwise. |
| Request pipeline | Per-request closure chain is built only when ≥1 behavior is registered (no-behavior fast path returns the handler directly). | Already optimal; no action. |
| `GeneratedRegistrationLocator` | Reflection-based registration delegate is cached per assembly in a `ConcurrentDictionary`. | Repeated `AddFastMediatR` calls (multi-host test scenarios) pay reflection only once. |

**Key takeaway:** the only scalability lever worth operator attention is **unbounded notification fan-out**. Apps that publish to many concurrent handlers under sustained load should provide a throttling `INotificationPublisher`.
