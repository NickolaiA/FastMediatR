# FastMediatR — Library Design Specification

> **Version:** 1.12  
> **Date:** 2026-05-27  
> **Status:** Phase 6 Telemetry package shipped  
> **Target:** .NET 10 / C# 14

> **Reconcile log**
>
> - **2026-05-26 — Phase 5 Integration & End-to-End Tests code review (v1.9 → v1.10):** Full review of all Phase 5 source files (3 new test projects, sample API exception handler, `ServiceCollectionExtensions` TimeProvider fix). Issues found and fixed:
>   - **`ServiceCollectionExtensions.AddFastMediatR` did not register `TimeProvider`:** `PerformanceBehavior<,>` depends on `System.TimeProvider` via constructor injection. This is not automatically available in the .NET DI container and caused `InvalidOperationException` at runtime for any app using `PerformanceBehavior<,>`. Fixed: added `services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System)` in step 7 of `AddFastMediatR`. `TryAdd` allows test hosts to pre-register `FakeTimeProvider` before calling `AddFastMediatR`.
>   - **`MigrationCompatibilityTests` validator registered under wrong service type:** `EchoCommandValidator` was registered as `AbstractValidator<EchoCommand>` instead of `IValidator<EchoCommand>`. `ValidationBehavior<,>` resolves `IEnumerable<IValidator<TRequest>>`, so the mis-typed registration was invisible to the behavior and the test never exercised validation. Fixed to register as `IValidator<EchoCommand>`.
>   - **`AotSmokeTest/Program.cs` dead null checks:** The ISender/IPublisher segregation assertion used `if (sender is null || publisher is null)` after `GetRequiredService<T>()`. `GetRequiredService<T>()` never returns null — it throws `InvalidOperationException` if the service is absent — making the null check unreachable dead code. Replaced with functional dispatch assertions through `ISender` and `IPublisher` respectively.
>   - **`CHANGELOG.md` architecture test names were wrong:** Planned test names (`Core_HasNoExternalDependencies`, `Core_AllSealedConcreteTypes_AreSealed`, etc.) were written to the CHANGELOG without being updated to match the actual implemented names (`Core_HasNoDependencyOnExtensionsDi`, `Core_PublicConcreteClasses_AreSealed`, `Core_NoBlockingTaskUsage`, `ExtensionsDi_PublicConcreteClasses_AreSealed`, `ExtensionsDi_NoBlockingTaskUsage`). Fixed.
>   - Test count: **126 / 126** passing (92 core/behavior + 12 generator + 16 integration + 6 architecture).
>
> - **2026-05-25 — Phase 4 comprehensive code review second pass (v1.8 → v1.9):** Full review of all Phase 4 source files, test files, sample API and support files. Five additional issues found and fixed:
>   - **`ServiceCollectionExtensions.cs` step-6 comment was logically backwards:** "Using TryAdd so consumers who call `services.Replace(...)` before `AddFastMediatR` can override it" is incorrect. `TryAdd` *preserves* a registration made before `AddFastMediatR`; it does not enable `Replace`-based overrides. Fixed: comment now accurately states that `TryAdd` preserves a pre-registered `INotificationPublisher` and directs consumers to call `services.Replace(...)` *after* `AddFastMediatR` to swap the default.
>   - **`SequentialAwaitPublisher` XML doc said "between each handler" but the check fires before the first handler too:** The `cancellationToken.ThrowIfCancellationRequested()` in the `for` loop runs at `i=0` before any handler executes. Fixed: doc now says "Checked before each handler invocation (including before the first handler)".
>   - **`PerformanceBehavior` XML doc code example was inconsistent with the sample registration order:** The doc showed `PerformanceBehavior` as outermost, contradicting the sample which registers `LoggingBehavior` first (outermost). Fixed: example updated to the canonical three-behavior order matching the sample (`LoggingBehavior` outermost, `PerformanceBehavior` second, `ValidationBehavior` innermost).
>   - **`Program.cs` header comment listed behaviors in wrong order:** Said "ValidationBehavior → LoggingBehavior → PerformanceBehavior" but actual registration order is "LoggingBehavior → PerformanceBehavior → ValidationBehavior". Fixed.
>   - **Missing test coverage for cancellation edge cases in publishers:** `SequentialAwaitPublisher` had no test verifying an already-cancelled token prevents all handlers from running; `TaskWhenAllPublisher` had no test verifying the cancellation token is forwarded to handler delegates. Two new tests added: `Publish_WhenAlreadyCancelled_ThrowsImmediately_WithNoHandlerInvoked` and `Publish_ForwardsCancellationTokenToHandlers`.
>   - Test count: 104/104 passing (90 core/behavior + 12 generator + 2 new publisher cancellation tests).
>
> - **2026-05-25 — Phase 4 code review fix (v1.7 → v1.8):** Critical bug found and fixed during Phase 4 code review:
>   - **CRITICAL: Generated dispatcher bypassed all pipeline behaviors and `INotificationPublisher` (§6.3 / §10.1 deferred work not completed).** The Phase 2 design explicitly deferred `RunPipeline_<RequestType>` wrapper emission to Phase 4 (see v1.4 reconcile note). Tasks 4.1–4.6 correctly implemented all behavior classes and publishers, but `DispatcherEmitter.cs` was never updated to wire them in. Symptoms: `ValidationBehavior`, `LoggingBehavior`, `PerformanceBehavior`, `RequestPreProcessorBehavior`, `RequestPostProcessorBehavior` were all registered in DI but silently skipped at runtime; `INotificationPublisher` strategy (parallel/sequential) was also bypassed.
>   - **Fix — `Send<TResponse>` and `Send(object)` dispatch:** Switch arms now call `ExecutePipeline<TRequest, TResponse>(request, handler, ct)` instead of calling the handler directly. `ExecutePipeline` is a private generic helper method emitted once in the dispatcher class. It resolves `IEnumerable<IPipelineBehavior<TRequest, TResponse>>` via `GetServices<>()`, converts to array, then builds a `RequestHandlerDelegate<TResponse>` chain by iterating from last-to-first (so DI registration order = first-registered outermost). The handler delegate is always the innermost.
>   - **Fix — `Publish` dispatch:** Each `case` block now resolves `INotificationPublisher` from DI, pre-resolves each handler into a local variable, constructs a `NotificationHandlerExecutor[]` (strongly-typed cast from `INotification` to the concrete notification type in each handler lambda), and delegates to `publisher.Publish(executors, notification, ct)`. The publisher strategy (parallel/sequential/custom) is fully respected.
>   - **Generated file — `using System.Linq;` added:** Required for `GetServices<T>().ToArray()` in `ExecutePipeline`.
>   - **Generator tests updated:** Three new tests added to `FastMediatRGeneratorTests.cs`: `SingleHandler_DispatcherCallsExecutePipelineNotHandlerDirectly`, `SingleHandler_DispatcherContainsPipelineHelperMethod`, `NotificationHandlers_PublishDelegatesToINotificationPublisher`.
>   - Test count: 102/102 passing (90 core/behavior + 12 generator).
>
> - **2026-05-25 — Phase 4 Built-in Pipeline Behaviors (v1.6 → v1.7):** All Phase 4 behaviors shipped. Intentional deviations:
>   - **`FastMediatROptions` namespace moved to `FastMediatR.Core`:** The design had `FastMediatROptions` in `FastMediatR.Extensions.DI`. It was moved to `FastMediatR.Core` (namespace `FastMediatR`) so that `LoggingBehavior` and `PerformanceBehavior` — which live in Core — can inject `IOptions<FastMediatROptions>` without a circular dependency on Extensions.DI. Consumers who were importing via `using FastMediatR.Extensions.DI;` should now use `using FastMediatR;` or `using Microsoft.Extensions.Options;`.
>   - **`FastMediatRConfig.NotificationPublisherStrategy` shipped (previously deferred):** `NotificationPublisherStrategy` enum and the `cfg.NotificationPublisherStrategy` property are now live in Extensions.DI. Defaults to `ParallelWhenAll`.
>   - **Shared `ValidationContext` bug fixed:** Initial implementation of `ValidationBehavior` passed one `ValidationContext<TRequest>` instance to all concurrent validators via `Task.WhenAll`. FluentValidation accumulates failures in the context's internal collection, causing double-counting when the same context is shared. Fixed: each validator now receives a fresh `new ValidationContext<TRequest>(request)`.
>   - **NSubstitute limitation documented:** NSubstitute 5.x (Castle.DynamicProxy) cannot create proxy types for generic interfaces where the type parameter is a `private` or `internal` nested type, or when the defining assembly is strongly-named (e.g. FluentValidation's `IValidator<T>`). All Phase 4 test files use concrete fake implementations instead of substitutes.
>   - **`[LoggerMessage]` structured key convention:** `{@Request}` in a `[LoggerMessage]` template produces structured state key `@Request` (the `@` prefix is preserved). Tests asserting on `StructuredState` must check for `"@Request"`, not `"Request"`.
>   - Test count: 99/99 passing (90 core/behavior + 9 generator).
>
> - **2026-05-25 — Phase 3 DI Extensions (v1.5 → v1.6):** `FastMediatR.Extensions.DI` shipped. The following deviations from §7 / §8.4 are intentional and tracked:
>   - **AOT annotation strategy (§7.4):** The design specified using `[DynamicDependency]` to preserve the generated type through trimming. The shipped implementation uses `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` on `AddFastMediatR<TMarker>` instead. This surfaces the trim / AOT unsafety at the call site (warning propagates to the consumer) rather than silently rooting the generated type; it is a valid, idiomatic .NET approach and achieves the same safety goal.
>   - **`FastMediatROptions` property names (§8.4):** Design listed `PerformanceThresholdMs`, `EnableLogging`, `LogRequestPayload`. Shipped: `PerformanceThresholdMs` (unchanged) and `EnablePayloadLogging` (combines the payload-logging flag). `EnableLogging` control is deferred to Phase 4 when `LoggingBehavior` is implemented; there is nothing to toggle until then.
>   - **`FastMediatRConfig.NotificationPublisherStrategy` deferred (§7.1):** The `cfg.NotificationPublisherStrategy` knob shown in the design is deferred to Phase 4. `INotificationPublisher` implementations (`TaskWhenAllPublisher`, `SequentialAwaitPublisher`) do not exist in `FastMediatR.Core` yet. The config class ships without this property.
>   - **Tasks 3.5 and 3.6 deferred to Phase 4:** Auto-registration of `RequestPreProcessorBehavior` / `RequestPostProcessorBehavior` (3.5) and `INotificationPublisher` (3.6) are deferred because those concrete types are Phase 4 work. The `AssemblyScanner` *does* register pre/post processor implementations and their closed interface mappings (so `IEnumerable<IRequestPreProcessor<T>>` resolves correctly); the behavior wrapper that drives them through the pipeline is Phase 4.
>   - **Code review findings:** Six build-time issues fixed during review: invalid XML `<paramref>` tags (CS1734), missing `[RequiresDynamicCode]` attribute (IL3050), `Unit.TaskValue` → `Unit.Task` (CS0117), unresolvable `<cref>` attributes (CS1574), `FakeGenerated.Register` using `AddSingleton` → `TryAddSingleton` (idempotency). No captive-dependency or ordering invariant violations found in the production code.
>   - Test count: 58/58 passing (26 core + 9 generator + 23 DI).
>
> - **2026-05-25 — Phase 2 code review (v1.4 → v1.5):** Four issues found and fixed during code review:
>   - **Interface-exclusion bug (FM001 false positive)** — `IsCandidateTypeDeclaration` accepted `InterfaceDeclarationSyntax` nodes, causing any interface extending `IRequest<T>` (e.g. `interface IAdminCommand : IRequest<bool>`) to be classified as a concrete request type and trigger FM001. Fixed at both the syntax-pass level (explicit `InterfaceDeclarationSyntax` early-out) and the semantic-pass level (`TypeKind.Interface || IsAbstract` guard in `Aggregate`). Regression test added: `MarkerInterface_DoesNotTriggerFM001`.
>   - **Code style** — Two missing blank lines before `if` statements in `HandlerDiscoveryProvider` (`Transform` and `Aggregate`) corrected to comply with DotNet.md §18.
>   - **Generated null guard** — `RegistrationEmitter` was emitting `if (services is null) throw new ArgumentNullException(...)`. Updated to `ArgumentNullException.ThrowIfNull(services)` (idiomatic .NET 6+ pattern).
>   - No trust/injection issues found in emitter string construction (all values are fully-qualified type names sourced from Roslyn symbols, not user-controlled strings). Generator is correctly incremental via `EquatableArray<T>` pipeline models. FM001/FM002/FM003 descriptors match design §6.4.
>   - Test count: 35/35 passing (26 core + 9 generator).
>
> - **2026-05-24 — Phase 2 closeout (v1.3 → v1.4):** Source generator (`FastMediatR.Generator`) shipped. The following deviations from §6.2 / §6.3 are intentional and tracked:
>   - **§6.3 `RunPipeline_<RequestType>` per-request pipeline wrappers** — not emitted in Phase 2. The current dispatcher calls the handler directly (no behavior wrapping). This method family will be added in Phase 4 when `IPipelineBehavior<,>` registration lands in the DI extensions. The emitter architecture is designed so `PipelineEmitter.cs` can be added without touching the existing switch arms.
>   - **§10.1 `PublishNotification_<EventType>` per-notification methods** — inline fan-out implemented directly inside the `Publish` switch block in `DispatcherEmitter.cs` rather than as separate private methods. Functionally equivalent for Phase 2; can be refactored into named helpers in Phase 4 if needed.
>   - **`HandlerLifetime` internal ordering** — emitted as `Scoped=0` (default), `Singleton=1`, `Transient=2`. FM003 fires when `lifetime != HandlerLifetime.Scoped` (i.e., != 0).
>   - **Test harness** — `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` package (required Roslyn 3.8.0) conflicts with CPM-pinned Roslyn 4.11.0; custom `CSharpGeneratorDriver` harness used in `FastMediatR.Generator.Tests` instead.
>   - **No other signature drift** — `IMediator`, `ISender`, `IPublisher`, `INotificationPublisher`, `INotificationHandler<>`, `IRequestHandler<,>` all match §5 verbatim.
>
> - **2026-05-24 — Phase 1 closeout (v1.3-draft → v1.3):** §5 abstractions shipped with the following intentional refinements to the inline code samples (none widen the public surface beyond what task breakdown §1.x already enumerated):
>   - **§5.2** — `IRequest<out TResponse>` now derives from a new non-generic marker `IBaseRequest`. `IBaseRequest` is required so that `ISender.Send(object, CancellationToken)` (referenced in §5.7) and the source generator can identify any request instance without knowing its response type at compile time. This matches the MediatR public surface.
>   - **§5.3** — A convenience refinement `IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>` was added alongside the two-argument form so void commands can be implemented without spelling out `Unit` in the handler signature. Same shape as MediatR.
>   - **No signature drift** on the dispatch surface: `ISender.Send<TResponse>` / `ISender.Send(object,…)` / `IPublisher.Publish` / `IPipelineBehavior<,>.Handle` / `RequestHandlerDelegate<TResponse>` all match §5 verbatim.
>   - **CA suppressions** documented in source (not design changes): `CA1711` on `RequestHandlerDelegate<TResponse>` and `CA1716` on the `next` parameter of `IPipelineBehavior<TRequest, TResponse>.Handle`. Both exist solely for MediatR API parity and carry `[SuppressMessage]` attributes with the rationale.

> **Governing best-practices reference:** All code produced for FastMediatR — library,
> source generator, DI extensions, telemetry package, tests and benchmarks — must comply
> with [DotNet.md](DotNet.md). DotNet.md defines the .NET 10 / C# 14 baseline
> (platform target, language features, DI, async, options pattern, logging, testing,
> security, observability, coding style and anti-patterns) that this specification
> assumes throughout. Where this document is silent, DotNet.md is authoritative.

---

## 1. Purpose & Goals

FastMediatR is a lightweight, high-performance mediator library for .NET 10 that is **API-compatible with MediatR** (drop-in replacement). Its defining characteristic is a **Roslyn incremental source generator** that emits the entire request-dispatch table at compile time, resulting in **zero-reflection dispatch** at runtime.

### Design goals

| Goal | Description |
|------|-------------|
| Zero-reflection dispatch | Roslyn generator emits strongly-typed switch dispatch at compile time |
| Drop-in compatible | Same interface names as MediatR (`IRequest<T>`, `IRequestHandler<T,R>`, `ISender`) |
| Pipeline support | Full `IPipelineBehavior<TRequest, TResponse>` with DI-ordered execution |
| Async-only | All handlers and behaviors return `Task<T>` — no sync paths |
| Built-in behaviors | Validation, Logging, Performance monitoring (all opt-in) |
| Telemetry | Optional OpenTelemetry tracing in a separate package |
| Testability | Every class designed for mockability; full unit test coverage shipped with the library |
| ConfigureAwait(false) | All internal async code uses `ConfigureAwait(false)` — safe for library use in any synchronization context |
| MIT licensed | Free to use commercially; no licensing fee unlike MediatR (LuckyPennySoftware) |
| Modern C# 14 | Records, primary constructors, `required` members, collection expressions, file-scoped namespaces, nullable enabled (per [DotNet.md §1](DotNet.md#1-platform--language-baseline)) |
| Supports all request type kinds | `class`, `record`, `record struct`, `struct` may all implement `IRequest<T>` |
| AOT-ready & trim-safe | Compatible with `PublishAot` and `IsTrimmable=true`; no reflection on the dispatch hot path; the single startup-time `Assembly.GetType()` lookup is annotated with `[DynamicallyAccessedMembers]` |

---

## 2. Scope

The following features are included in v1.0:

| Feature | Included |
|---------|----------|
| `Send` — request/response (`IRequest<TResponse>`) | ✅ |
| Notifications / pub-sub (`INotification`) | ✅ |
| Pipeline behaviors (`IPipelineBehavior`) | ✅ |
| Pre/post-processors | ✅ |
| Void / fire-and-forget commands (`IRequest<Unit>`) | ✅ |
| Untyped `Send(object)` returning `Task<object?>` | ✅ |
| Streaming (`IStreamRequest<T>`) | ❌ v2 |
| Sync `Send()` | ❌ async-only |

---

## 3. Repository Structure

```
FastMediatR/
├── FastMediatR.slnx
├── Directory.Build.props                # Shared hygiene (§17)
├── .editorconfig                        # DotNet.md §18 style rules
├── .github/
│   └── workflows/
│       └── ci.yml                       # Build, test, pack on push/PR
├── src/
│   ├── FastMediatR/                     # Packable wrapper — produces FastMediatR.nupkg
│   ├── FastMediatR.Core/                # Interfaces, Unit type, base behaviors, RequestPre/PostProcessorBehavior
│   ├── FastMediatR.Generator/           # Roslyn IIncrementalGenerator (analyzer)
│   ├── FastMediatR.Extensions.DI/       # AddFastMediatR<TMarker>() + scanning
│   └── FastMediatR.Telemetry/           # OpenTelemetry ActivitySource tracing (separate package)
├── tests/
│   ├── FastMediatR.Tests/               # xUnit v3 unit tests
│   ├── FastMediatR.Generator.Tests/     # Source-generator snapshot tests
│   ├── FastMediatR.Integration.Tests/   # WebApplicationFactory end-to-end
│   ├── FastMediatR.Architecture.Tests/  # NetArchTest rules
│   └── FastMediatR.Telemetry.Tests/     # ActivityListener-based tracing behavior tests
├── benchmarks/
│   └── FastMediatR.Benchmarks/          # BenchmarkDotNet vs MediatR
└── docs/
    ├── 260524_1200-fastmediatr-design.md
    └── DotNet.md
```

---

## 4. NuGet Package Strategy

| Package | Contents | Target |
|---------|----------|--------|
| `FastMediatR` | Core + Generator + DI extensions bundled | Consumers |
| `FastMediatR.Telemetry` | OpenTelemetry tracing behavior | Consumers who need OTel |

### 4.1 How `FastMediatR.nupkg` bundles three projects

`src/FastMediatR/FastMediatR.csproj` is a **thin packable wrapper**. It does not contain code itself; it `ProjectReference`s the three contributing projects and uses an MSBuild target to copy their compiled DLLs into the produced `.nupkg`:

```xml
<ItemGroup>
  <!-- These references contribute DLLs to the nupkg but are NOT exposed transitively -->
  <ProjectReference Include="..\FastMediatR.Core\FastMediatR.Core.csproj"
                    PrivateAssets="all"
                    ReferenceOutputAssembly="true" />
  <ProjectReference Include="..\FastMediatR.Extensions.DI\FastMediatR.Extensions.DI.csproj"
                    PrivateAssets="all"
                    ReferenceOutputAssembly="true" />
  <!-- Generator is shipped under analyzers/dotnet/cs (Roslyn convention) -->
  <ProjectReference Include="..\FastMediatR.Generator\FastMediatR.Generator.csproj"
                    PrivateAssets="all"
                    ReferenceOutputAssembly="false"
                    OutputItemType="Analyzer" />
</ItemGroup>

<!-- Copy referenced project DLLs into the package lib/net10.0 folder -->
<Target Name="IncludeProjectReferenceDllsInPackage" BeforeTargets="_GetPackageFiles">
  <ItemGroup>
    <None Include="$(OutputPath)FastMediatR.Core.dll"             Pack="true" PackagePath="lib/net10.0" />
    <None Include="$(OutputPath)FastMediatR.Extensions.DI.dll"    Pack="true" PackagePath="lib/net10.0" />
    <None Include="$(OutputPath)FastMediatR.Generator.dll"        Pack="true" PackagePath="analyzers/dotnet/cs" />
  </ItemGroup>
</Target>
```

The sub-projects (`FastMediatR.Core`, `FastMediatR.Generator`, `FastMediatR.Extensions.DI`) all set `<IsPackable>false</IsPackable>`. Only `src/FastMediatR` and `src/FastMediatR.Telemetry` produce NuGet packages.

---

## 5. Core Abstractions (`FastMediatR.Core`)

### 5.1 `Unit`

```csharp
/// <summary>
/// Represents a void return type for commands that produce no result.
/// Equivalent to MediatR's Unit struct. Modelled as a readonly record struct
/// for value-equality semantics with zero allocation (DotNet.md §1).
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
    public override string ToString() => "()";
}
```

> **Note:** `Unit` is a `readonly record struct` — leverages C# 14 record-struct semantics for value
> equality with zero allocation. This aligns with the DotNet.md guidance on using `record struct`
> for small stack-allocated value types in hot paths (see [DotNet.md §1](DotNet.md#1-platform--language-baseline)).

### 5.2 Request interfaces

```csharp
/// <summary>
/// Response-agnostic marker base for every request type. Required so that
/// the untyped <see cref="ISender.Send(object, CancellationToken)"/> overload and the
/// source generator can identify any request instance without knowing its response type
/// at compile time. Matches the MediatR public surface.
/// </summary>
public interface IBaseRequest { }

/// <summary>
/// Marker interface for a request that returns <typeparamref name="TResponse"/>.
/// </summary>
public interface IRequest<out TResponse> : IBaseRequest { }

/// <summary>
/// Marker interface for a request with no return value (fire-and-forget command).
/// Handlers return Task&lt;Unit&gt;.
/// </summary>
public interface IRequest : IRequest<Unit> { }
```

### 5.3 Handler interface

```csharp
/// <summary>
/// Handles a request of type <typeparamref name="TRequest"/>
/// and returns <typeparamref name="TResponse"/>.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Processes the request and returns a response.
    /// </summary>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Convenience refinement for void commands: a handler of <see cref="IRequest"/> is
/// a handler of <see cref="IRequest{Unit}"/>. Allows the implementer to omit <c>Unit</c>
/// from the implementation's type parameter list.
/// </summary>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
}
```

### 5.4 Notification interfaces

```csharp
/// <summary>
/// Marker interface for a notification that is dispatched to all registered handlers.
/// </summary>
public interface INotification { }

/// <summary>
/// Handles a notification of type <typeparamref name="TNotification"/>.
/// All handlers are invoked in parallel via Task.WhenAll.
/// </summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
```

**Fanout strategy:** `Task.WhenAll` — all handlers run concurrently. If one or more throw, all exceptions are wrapped in `AggregateException` and rethrown.

### 5.5 Pipeline behavior

```csharp
/// <summary>
/// Represents a pipeline behavior that wraps a request handler.
/// Execution order is determined by DI registration order (first registered = outermost).
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

/// <summary>
/// Delegate representing the next step in the pipeline.
/// Accepts a <see cref="CancellationToken"/> — matches the MediatR 12+ API and avoids
/// capturing the token via closure, which prevents stale-token bugs when behaviors
/// re-enter the pipeline with a derived token.
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);
```

### 5.6 Pre/Post processors

```csharp
/// <summary>
/// Runs before the handler, executed as the innermost pipeline behavior
/// (<see cref="RequestPreProcessorBehavior{TRequest,TResponse}"/> auto-registered by DI).
/// All pre-processors for a request type run sequentially before the handler is invoked.
/// </summary>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Runs after the handler, executed by the innermost pipeline behavior
/// (<see cref="RequestPostProcessorBehavior{TRequest,TResponse}"/> auto-registered by DI).
/// All post-processors for a request type run sequentially after the handler returns.
/// </summary>
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}
```

### 5.7 Sender / Publisher interfaces

```csharp
/// <summary>
/// Sends a request through the pipeline to a single handler.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Typed dispatch. Resolves <see cref="IRequestHandler{TRequest,TResponse}"/> and
    /// runs all registered <see cref="IPipelineBehavior{TRequest,TResponse}"/> in order.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Untyped dispatch — resolves the handler dynamically and boxes the response as
    /// <c>object?</c>. Useful for dynamic scenarios such as deserialised requests from
    /// a message bus or generic command processors.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="request"/> does not implement <see cref="IRequest{TResponse}"/> or <see cref="IRequest"/>.</exception>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes a notification to all registered handlers in parallel via <c>Task.WhenAll</c>.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Resolves all <see cref="INotificationHandler{TNotification}"/> from DI and
    /// invokes them concurrently. Exceptions from all handlers are aggregated into a
    /// single <see cref="AggregateException"/> and rethrown.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is null.</exception>
    Task Publish(
        INotification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Combined mediator interface composing <see cref="ISender"/> and <see cref="IPublisher"/>.
/// Prefer injecting <see cref="ISender"/> or <see cref="IPublisher"/> directly
/// to keep controller/service dependencies narrow.
/// </summary>
public interface IMediator : ISender, IPublisher { }
```

---

## 6. Source Generator (`FastMediatR.Generator`)

### 6.1 Strategy

Uses `IIncrementalGenerator` (Roslyn incremental API) for optimal IDE performance in large solutions. The generator runs at compile time and emits a **partial class** `FastMediatRDispatcher` with a strongly-typed `Send` dispatch table.

### 6.2 What the generator emits

Given handlers registered in the consuming assembly:

```csharp
// Input: user writes
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderId> { ... }
public class GetOrderHandler    : IRequestHandler<GetOrderQuery,    OrderDto>  { ... }
```

The generator emits (into a hidden `.g.cs` file):

```csharp
// Generated — do not edit
#nullable enable
namespace FastMediatR.Generated;

internal sealed partial class FastMediatRDispatcher(IServiceProvider sp) : IMediator
{
    private readonly IServiceProvider _sp = sp;

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            CreateOrderCommand r => (Task<TResponse>)(object)RunPipeline_CreateOrderCommand(r, ct),
            GetOrderQuery r      => (Task<TResponse>)(object)RunPipeline_GetOrderQuery(r, ct),
            _ => throw new InvalidOperationException(
                     $"No handler registered for {request.GetType().FullName}")
        };
    }

    public async Task<object?> Send(object request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            CreateOrderCommand r => (object?)await RunPipeline_CreateOrderCommand(r, ct).ConfigureAwait(false),
            GetOrderQuery r      => (object?)await RunPipeline_GetOrderQuery(r, ct).ConfigureAwait(false),
            _ => throw new ArgumentException(
                     $"{request.GetType().Name} does not implement IRequest",
                     nameof(request))
        };
    }

    public Task Publish(INotification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return notification switch
        {
            OrderPlacedEvent n   => PublishNotification_OrderPlacedEvent(n, ct),
            UserRegisteredEvent n => PublishNotification_UserRegisteredEvent(n, ct),
            _ => Task.CompletedTask  // unknown notification — silent no-op (MediatR-compatible)
        };
    }
}
```

No `Type.GetType()`, no `Activator.CreateInstance()`, no `MethodInfo.Invoke()` — pure switch dispatch.

> **Covariance limitation (matches MediatR):** Because `Task<T>` is invariant, calling
> `Send<Base>((IRequest<Derived>)request)` succeeds at compile time (since `IRequest<out T>` is
> covariant) but the runtime cast `(Task<Base>)(object)Task<Derived>` will throw
> `InvalidCastException`. This is a fundamental BCL limitation, not a FastMediatR bug. Always
> call `Send<TResponse>` with the exact response type declared by the request.

### 6.3 Pipeline wrapping

The generator also emits a pipeline-wrapping call that resolves registered `IPipelineBehavior<TRequest, TResponse>` behaviors in DI registration order:

```csharp
// Simplified generated pipeline runner for CreateOrderCommand.
// All internal awaits use ConfigureAwait(false) per library convention.
private async Task<OrderId> RunPipeline_CreateOrderCommand(
    CreateOrderCommand request, CancellationToken ct)
{
    var behaviors = _sp.GetServices<IPipelineBehavior<CreateOrderCommand, OrderId>>()
                       .ToArray();
    RequestHandlerDelegate<OrderId> handler = (innerCt) =>
        _sp.GetRequiredService<IRequestHandler<CreateOrderCommand, OrderId>>()
           .Handle(request, innerCt);

    for (int i = behaviors.Length - 1; i >= 0; i--)
    {
        var behavior = behaviors[i];
        var next = handler;
        handler = (innerCt) => behavior.Handle(request, next, innerCt);
    }

    return await handler(ct).ConfigureAwait(false);
}
```

`IEnumerable<IPipelineBehavior<...>>` from DI preserves registration order. Behaviors registered first wrap outermost (run first/last).

> **Allocation note (deliberate trade-off):** Each `Send()` allocates (a) one delegate per
> behavior to capture the `request`/`next` pair, and (b) one `IPipelineBehavior[]` from
> `GetServices().ToArray()`. The first is intrinsic to the pipeline pattern. The second is
> a knowing trade-off against pipeline reconfigurability via DI replacement. Benchmark numbers
> in §13 must surface allocations explicitly; if measured pressure justifies it, a future
> optimisation may cache the resolved behavior array per closed generic in a
> `ConcurrentDictionary` keyed by `(TRequest, TResponse)` after first resolution.

### 6.4 Failure modes

| Scenario | Generator behavior |
|----------|--------------------|
| Handler interface found, but no concrete class registered in DI scan | Compiler **error** `FM001`: "No handler found for request type `{Type}`" |
| Multiple handlers for the same request type | Compiler **error** `FM002`: "Multiple handlers found for `{Type}`" |
| Request type found in assembly but not scanned | Compiler **warning** `FM003` (informational) |

### 6.5 Supported request type kinds

The generator emits `is`-pattern / `switch` dispatch arms that work uniformly for every C# type kind. Consumers may declare requests as **any** of the following:

| Kind | Example | Notes |
|------|---------|-------|
| `class` | `public sealed class CreateOrderCommand : IRequest<Guid>` | Classic reference type |
| `record` | `public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto>` | **Recommended** for CQRS messages (DotNet.md §1) |
| `record struct` | `public readonly record struct PingQuery : IRequest<long>` | Allocation-free hot-path requests |
| `struct` | `public readonly struct TickCommand : IRequest<Unit>` | Rare — use only when value semantics matter |

The generator handles all four kinds; no special attribute or marker is required beyond `IRequest<TResponse>` / `IRequest`. The generated `switch` arm uses `request switch { CreateOrderCommand r => … }` which works for class and `record`, and a `request is PingQuery p` arm for value-type requests (avoids unnecessary boxing in the value-type case).

---

## 7. DI Registration (`FastMediatR.Extensions.DI`)

### 7.1 Registration method

```csharp
// In Program.cs / Startup.cs — typed-marker overload is AOT-safe and unambiguous.
builder.Services.AddFastMediatR<Program>(cfg =>
{
    cfg.RegisterHandlersFromAssembly(typeof(CreateOrderHandler).Assembly);
    cfg.RegisterHandlersFromAssembly(typeof(OtherHandler).Assembly);

    // Optional: change default handler lifetime
    cfg.DefaultHandlerLifetime = ServiceLifetime.Scoped; // default

    // Optional: customise notification publish strategy (see §10.1).
    cfg.NotificationPublisherStrategy = NotificationPublisherStrategy.ParallelWhenAll;

    // Opt-in built-in behaviors using open-generic typeof syntax.
    // Order = pipeline execution order: first registered = outermost (runs first, exits last).
    // Recommended ordering:
    //   1. PerformanceBehavior outermost — measures the ENTIRE pipeline latency
    //   2. ValidationBehavior — short-circuits invalid requests before logging
    //   3. LoggingBehavior innermost — only logs requests that pass validation
    cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

Signature:

```csharp
public static IServiceCollection AddFastMediatR<TGeneratedRegistrationMarker>(
    this IServiceCollection services,
    Action<FastMediatRConfig>? configure = null);
```

`TGeneratedRegistrationMarker` is **any type** from the assembly where the source generator emitted `FastMediatRGeneratedRegistration` (typically `Program` or `Startup`). The library locates the generated registration class in `typeof(TGeneratedRegistrationMarker).Assembly` — deterministic, trim-safe, AOT-safe.

### 7.2 What `RegisterHandlersFromAssembly` does

Assembly scanning registers (in this order):
1. All `IRequestHandler<TRequest, TResponse>` implementations.
2. All `INotificationHandler<TNotification>` implementations.
3. All `IRequestPreProcessor<TRequest>` implementations.
4. All `IRequestPostProcessor<TRequest, TResponse>` implementations.
5. If any pre-processors were found: registers `RequestPreProcessorBehavior<TRequest, TResponse>` as an `IPipelineBehavior` **after** all user-registered behaviors (so it lands innermost in pipeline order).
6. If any post-processors were found: registers `RequestPostProcessorBehavior<TRequest, TResponse>` similarly innermost.

The generated `FastMediatRDispatcher` is **not** registered here — it is registered by the generator-emitted `FastMediatRGeneratedRegistration.Register()` that `AddFastMediatR<TMarker>()` invokes once at startup (see §7.4).

### 7.3 Handler lifetime

Default: **Scoped**. Override per-handler by decorating with `[HandlerLifetime(ServiceLifetime.Transient)]` or globally via `cfg.DefaultHandlerLifetime`.

```csharp
[HandlerLifetime(ServiceLifetime.Transient)]
public class GetCachedDataHandler : IRequestHandler<GetCachedDataQuery, CachedDto> { ... }
```

### 7.4 Generated Dispatcher DI Registration (Architecture Detail)

**Problem:** `FastMediatRDispatcher` is emitted by the Roslyn source generator into the **consumer's** assembly at compile time. The library's `AddFastMediatR<TMarker>()` method lives in `FastMediatR.Extensions.DI` and cannot reference a generated type at compile time.

**Solution:** The generator emits **two** artifacts into the consumer's assembly:
1. The `FastMediatRDispatcher` class itself.
2. A static class `FastMediatRGeneratedRegistration` with a `Register(IServiceCollection)` method.

The library's `AddFastMediatR<TMarker>(services, configure)` resolves the generated class once at startup via `typeof(TMarker).Assembly.GetType("FastMediatR.Generated.FastMediatRGeneratedRegistration")` and invokes `Register` via a cached `MethodInfo` delegate. This single lookup is the only reflection-based **type lookup** in the entire library; every `Send()` / `Publish()` call thereafter is pure switch dispatch.

```csharp
// Generated into consumer's assembly:
namespace FastMediatR.Generated;

internal static class FastMediatRGeneratedRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddScoped<IMediator, FastMediatRDispatcher>();
        services.AddScoped<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());
    }
}
```

**AOT / trim safety:** The library exposes the lookup site with:

```csharp
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods,
                   "FastMediatR.Generated.FastMediatRGeneratedRegistration",
                   "")]   // empty assembly = same as TMarker.Assembly at call site
public static IServiceCollection AddFastMediatR<TGeneratedRegistrationMarker>(...) { ... }
```

Combined with `<IsAotCompatible>true</IsAotCompatible>` on the packable project, this makes the lookup AOT- and trim-safe. Should the generated class be trimmed away (which would only happen if `AddFastMediatR<TMarker>` was itself unreferenced), the library throws a clear `InvalidOperationException` directing the developer to add a `[DynamicDependency]` or check trimming roots.

**Note on "reflection":** The dispatcher itself contains zero reflection. The DI container's `GetServices<IPipelineBehavior<T,R>>()` uses MS.DI internal closed-generic resolution which is fully AOT-supported in .NET 8+. The single `Assembly.GetType()` happens once per startup.

All built-in behaviors are **opt-in** via `cfg.AddOpenBehavior(typeof(...))`. Users register their own behaviors using the same API.

### 8.1 `ValidationBehavior<TRequest, TResponse>`

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Resolves IEnumerable<IValidator<TRequest>> from DI.
    // If any validator exists, runs ValidateAsync().
    // On failure: throws FluentValidation.ValidationException.
    // If no validators registered for TRequest: passes through.
}
```

**Dependency:** FluentValidation.DependencyInjectionExtensions  
Register validators alongside: `builder.Services.AddValidatorsFromAssembly(...)`.

### 8.2 `LoggingBehavior<TRequest, TResponse>`

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(
    IOptions<FastMediatROptions> options,
    TimeProvider timeProvider,
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var startTs = timeProvider.GetTimestamp();

        if (options.Value.LogRequestPayload)
            LoggingBehaviorLog.HandlingWithPayload(logger, requestName, request);
        else
            LoggingBehaviorLog.Handling(logger, requestName);

        try
        {
            var response = await next(ct).ConfigureAwait(false);
            var elapsedMs = (long)timeProvider.GetElapsedTime(startTs).TotalMilliseconds;
            LoggingBehaviorLog.Handled(logger, requestName, elapsedMs);
            return response;
        }
        catch (Exception ex)
        {
            // try/catch (not try/finally) so failure logs distinguish from success at the same level.
            var elapsedMs = (long)timeProvider.GetElapsedTime(startTs).TotalMilliseconds;
            LoggingBehaviorLog.Failed(logger, ex, requestName, elapsedMs);
            throw;
        }
    }
}

// Source-generated, zero-allocation log methods (DotNet.md §15).
// PII safety: payload variant uses {@Request} structured destructuring and is only invoked
// when FastMediatROptions.LogRequestPayload is explicitly enabled.
internal static partial class LoggingBehaviorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestType}")]
    public static partial void Handling(ILogger logger, string requestType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestType} {@Request}")]
    public static partial void HandlingWithPayload(ILogger logger, string requestType, object request);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestType} in {ElapsedMs}ms")]
    public static partial void Handled(ILogger logger, string requestType, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed {RequestType} after {ElapsedMs}ms")]
    public static partial void Failed(ILogger logger, Exception ex, string requestType, long elapsedMs);
}
```

### 8.3 `PerformanceBehavior<TRequest, TResponse>`

```csharp
public sealed class PerformanceBehavior<TRequest, TResponse>(
    TimeProvider timeProvider,
    IOptions<FastMediatROptions> options,
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var startTs = timeProvider.GetTimestamp();
        try
        {
            return await next(ct).ConfigureAwait(false);
        }
        finally
        {
            // Measure on both success AND failure paths — slow failures are also actionable.
            var elapsedMs = (long)timeProvider.GetElapsedTime(startTs).TotalMilliseconds;
            var threshold = options.Value.PerformanceThresholdMs;
            if (elapsedMs > threshold)
                PerformanceLog.Slow(logger, typeof(TRequest).Name, elapsedMs, threshold);
        }
    }
}

internal static partial class PerformanceLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Slow request: {RequestType} took {ElapsedMs}ms (threshold: {Threshold}ms)")]
    public static partial void Slow(ILogger logger, string requestType, long elapsedMs, int threshold);
}
```

> **Why `TimeProvider` not `Stopwatch`:** DotNet.md §1 mandates `TimeProvider` for testability.
> `TimeProvider.GetTimestamp()` / `GetElapsedTime(long)` deliver the same monotonic precision
> as `Stopwatch` while remaining mockable in unit tests via `FakeTimeProvider`.

### 8.4 `FastMediatROptions`

```csharp
public sealed class FastMediatROptions
{
    /// <summary>Milliseconds above which PerformanceBehavior logs a warning.</summary>
    [Range(1, int.MaxValue)]
    public int PerformanceThresholdMs { get; set; } = 500;

    /// <summary>Enables the logging behavior when using AddFastMediatR defaults.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// When true, LoggingBehavior emits the full request payload via structured
    /// destructuring ({@Request}). DEFAULT IS FALSE for safety — enabling this in
    /// production may leak PII, secrets, tokens, or other sensitive data into logs.
    /// Only enable when you have audited every IRequest type for sensitive fields
    /// and applied appropriate redaction (e.g. [LogProperties(redact:true)] attributes).
    /// </summary>
    public bool LogRequestPayload { get; set; } = false;
}
```

**Registration with eager validation** (per DotNet.md §11 — Options Pattern):

```csharp
// Inside AddFastMediatR:
services.AddOptions<FastMediatROptions>()
        .BindConfiguration("FastMediatR")
        .ValidateDataAnnotations()
        .ValidateOnStart();
```

In `appsettings.json`:

```json
{
  "FastMediatR": {
    "PerformanceThresholdMs": 200,
    "EnableLogging": true,
    "LogRequestPayload": false
  }
}
```

---

## 9. Pipeline Execution Order

Given behaviors registered in the **recommended** order (from §7.1):

```
1. PerformanceBehavior   ← outermost: first registered, measures total latency
2. ValidationBehavior    ← second: short-circuits before handler if validation fails
3. LoggingBehavior       ← third: logs only requests that passed validation
(auto) RequestPreProcessorBehavior   ← auto-registered innermost by DI scan
(auto) RequestPostProcessorBehavior  ← auto-registered innermost by DI scan
```

Execution flow for a single `Send()` (outermost → innermost → handler → innermost → outermost):

```
PerformanceBehavior.Handle (enter)         ← starts timer for FULL pipeline
  → ValidationBehavior.Handle (enter)      ← runs IValidator<T>; throws on failure
      → LoggingBehavior.Handle (enter)     ← logs "Handling {RequestType}"
          → RequestPreProcessorBehavior    ← auto-registered; runs IRequestPreProcessor<T>[]
              → IRequestHandler.Handle     ← actual business logic
          → RequestPostProcessorBehavior   ← auto-registered; runs IRequestPostProcessor<T,R>[]
      ← LoggingBehavior.Handle (exit)       ← logs "Handled in {ms}ms"
  ← ValidationBehavior.Handle (exit)
← PerformanceBehavior.Handle (exit)        ← warns if total time > threshold
```

> **Note:** `RequestPreProcessorBehavior` and `RequestPostProcessorBehavior` are concrete classes
> in `FastMediatR.Core` that implement `IPipelineBehavior<TRequest, TResponse>` and are
> automatically registered by `RegisterHandlersFromAssembly` when pre/post-processor
> implementations are found. They do **not** need to be manually added via `AddOpenBehavior`.

> **Registration ordering guarantee:** Auto-registration of `RequestPreProcessorBehavior` and
> `RequestPostProcessorBehavior` is performed **after** all user-supplied `AddOpenBehavior(...)`
> calls in the `FastMediatRConfig` build. Because `IEnumerable<IPipelineBehavior<,>>` preserves
> DI registration order and the pipeline wraps from the **end of the enumerable inward**, the
> auto-registered behaviors always land innermost (closest to the handler) regardless of user
> configuration. This is a deterministic, testable invariant (verified in
> `FastMediatR.Architecture.Tests`).

---

## 10. Notification Dispatch

```
IPublisher.Publish(notification, ct)
  → ArgumentNullException.ThrowIfNull(notification)
  → generated switch dispatches to PublishNotification_<T>(n, ct)
  → resolves all INotificationHandler<TNotification> from DI
  → if no handlers registered → return Task.CompletedTask (silent no-op, MediatR-compatible)
  → delegates to configured INotificationPublisher (see §10.1)
```

> **Note:** Notifications do **not** participate in `IPipelineBehavior`, `IRequestPreProcessor`,
> or `IRequestPostProcessor`. These are request/response concepts only.
> Cross-cutting concerns for notifications (e.g. logging) should be implemented inside
> individual `INotificationHandler` implementations or by wrapping `IPublisher` directly.

### 10.1 `INotificationPublisher` strategies

FastMediatR ships two publisher strategies in v1.0, configurable via `FastMediatRConfig.NotificationPublisherStrategy` or by replacing the `INotificationPublisher` DI registration:

```csharp
public interface INotificationPublisher
{
    Task Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlers,
        INotification notification,
        CancellationToken ct);
}

public readonly record struct NotificationHandlerExecutor(
    object HandlerInstance,
    Func<INotification, CancellationToken, Task> Handler);
```

**1. `TaskWhenAllPublisher` (default):** invokes all handlers in parallel and surfaces every failure.

```csharp
public sealed class TaskWhenAllPublisher : INotificationPublisher
{
    public async Task Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlers,
        INotification notification,
        CancellationToken ct)
    {
        var tasks = new Task[handlers.Count];
        for (var i = 0; i < handlers.Count; i++)
            tasks[i] = handlers[i].Handler(notification, ct);

        var whenAll = Task.WhenAll(tasks);
        try
        {
            await whenAll.ConfigureAwait(false);
        }
        catch
        {
            // await Task.WhenAll only re-throws the FIRST exception. To surface ALL handler
            // failures, rethrow the underlying AggregateException captured on the joined Task.
            // This is the documented, idiomatic BCL pattern for "all errors" semantics.
            throw whenAll.Exception!;
        }
    }
}
```

**2. `SequentialAwaitPublisher`:** invokes handlers one at a time in registration order; the first failure stops the publish and is propagated unwrapped (no aggregation).

```csharp
public sealed class SequentialAwaitPublisher : INotificationPublisher
{
    public async Task Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlers,
        INotification notification,
        CancellationToken ct)
    {
        for (var i = 0; i < handlers.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await handlers[i].Handler(notification, ct).ConfigureAwait(false);
        }
    }
}
```

Consumers may register a custom `INotificationPublisher` (e.g. one that swallows individual handler exceptions and logs them) by calling `services.Replace(ServiceDescriptor.Singleton<INotificationPublisher, MyPublisher>())` after `AddFastMediatR`.

---

## 11. OpenTelemetry Tracing (`FastMediatR.Telemetry`)

A separate NuGet package `FastMediatR.Telemetry` ships the optional OpenTelemetry tracing integration. The package depends on the OpenTelemetry SDK core package (`OpenTelemetry`, no exporter) and `FastMediatR.Core`; consumers wire their own exporters independently.

> **Publish path:** Notifications do not flow through `IPipelineBehavior<,>` so `TracingBehavior<,>` only covers `Send` calls. Notification tracing is out of scope for v1.0; callers who need it should wrap `IPublisher` directly.

---

### 11.1 Internal ActivitySource holder

```csharp
/// <summary>
/// Holds the shared <see cref="ActivitySource"/> instance for FastMediatR telemetry.
/// Centralised here so that <see cref="TracingBehavior{TRequest,TResponse}"/> and
/// the <c>TracerProviderBuilder</c> extension reference the same source name constant.
/// </summary>
internal static class FastMediatRActivitySource
{
    /// <summary>The well-known name used when subscribing via an ActivityListener or OTel SDK.</summary>
    internal const string ActivitySourceName = "FastMediatR";

    /// <summary>
    /// The shared <see cref="ActivitySource"/> instance.
    /// Version taken from the assembly informational version so spans carry a consistent version tag.
    /// </summary>
    internal static readonly ActivitySource Source =
        new(ActivitySourceName,
            typeof(FastMediatRActivitySource).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0");
}
```

`internal static readonly ActivitySource` is the standard .NET pattern (same as ASP.NET Core, gRPC, etc.). The source is created once at class-init time; no allocation on hot paths.

---

### 11.2 `TracingBehavior<TRequest, TResponse>`

```csharp
/// <summary>
/// Pipeline behavior that wraps each <c>Send</c> operation in a
/// <see cref="System.Diagnostics.Activity"/> span.
/// </summary>
/// <remarks>
/// <para>
/// Activity name is the short type name of <typeparamref name="TRequest"/> (e.g.
/// <c>"CreateUserCommand"</c>) — readable in Jaeger / Zipkin without requiring tag drilling.
/// </para>
/// <para>
/// If no listener is subscribed to the <c>"FastMediatR"</c> ActivitySource (i.e.
/// <see cref="System.Diagnostics.ActivitySource.StartActivity"/> returns <see langword="null"/>)
/// the behavior passes through with zero overhead — no allocations, no branching beyond a
/// single null check.
/// </para>
/// <para>
/// <b>PII safety:</b> Only the type names of the request and response are recorded as tags,
/// never the request payload. Consumers who need payload attributes should implement a
/// separate <see cref="IPipelineBehavior{TRequest,TResponse}"/> and add enrichment via
/// <c>Activity.Current</c>.
/// </para>
/// </remarks>
public sealed class TracingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Wraps the next step in the pipeline with an OpenTelemetry span.
    /// The span name is the short name of <typeparamref name="TRequest"/>.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="next">The next step — either the handler or the next inner behavior.</param>
    /// <param name="cancellationToken">Propagated to <paramref name="next"/>.</param>
    /// <returns>The response returned by the pipeline.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // StartActivity returns null when no listener is subscribed — fast path,
        // no allocation beyond the null check itself.
        using var activity = FastMediatRActivitySource.Source
            .StartActivity(typeof(TRequest).Name, ActivityKind.Internal);

        if (activity is null)
            return await next(cancellationToken).ConfigureAwait(false);

        // Tags use fully-qualified names for unambiguous querying across assemblies.
        // "mediator.*" prefix follows the OTel semantic-conventions informal naming convention
        // for non-standard instrumentation layers.
        activity.SetTag("mediator.request_type", typeof(TRequest).FullName);
        activity.SetTag("mediator.response_type", typeof(TResponse).FullName);

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            activity.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            // Record the exception before rethrowing so the span captures it
            // whether or not the calling code observes Activity.Current.
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddException(ex);      // BCL API — writes an "exception" event using OTel semantic conventions
            throw;
        }
    }
}
```

---

### 11.3 DI registration extension

```csharp
/// <summary>
/// Extension methods for registering <c>FastMediatR.Telemetry</c> services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TracingBehavior{TRequest,TResponse}"/> as an open-generic
    /// <see cref="IPipelineBehavior{TRequest,TResponse}"/>. The behavior is added as
    /// the <em>outermost</em> pipeline step — call this <em>before</em>
    /// <c>AddFastMediatR</c> / <c>cfg.AddOpenBehavior</c> so it wraps the full pipeline
    /// latency including validation and logging.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFastMediatRTelemetry(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));
        return services;
    }
}
```

**Ordering:** Register `AddFastMediatRTelemetry()` **before** `AddFastMediatR(...)`. Because DI registration order maps directly to pipeline execution order (first-registered = outermost), registering telemetry first ensures the tracing span wraps the entire pipeline — including validation, logging, and the handler.

```csharp
// Correct order in Program.cs:
builder.Services.AddFastMediatRTelemetry();           // outermost — spans the full pipeline
builder.Services.AddFastMediatR<Program>(cfg =>
{
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));  // second
    cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    // ... etc.
});
```

---

### 11.4 OpenTelemetry SDK wiring extension

```csharp
/// <summary>
/// Extension methods for wiring FastMediatR telemetry into the OpenTelemetry SDK.
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Adds the <c>"FastMediatR"</c> <see cref="ActivitySource"/> to the
    /// <see cref="TracerProviderBuilder"/> so that spans emitted by
    /// <see cref="TracingBehavior{TRequest,TResponse}"/> are captured and exported.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddFastMediatRInstrumentation(
        this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(FastMediatRActivitySource.ActivitySourceName);
    }
}
```

---

### 11.5 Activity conventions

| Property | Value | Example |
|----------|-------|---------|
| ActivitySource name | `"FastMediatR"` | Constant for OTel wiring |
| Activity name | `typeof(TRequest).Name` | `"CreateUserCommand"` |
| `ActivityKind` | `Internal` | Mediator calls are in-process |
| `mediator.request_type` tag | `typeof(TRequest).FullName` | `"FastMediatR.Sample.Api.Commands.CreateUserCommand"` |
| `mediator.response_type` tag | `typeof(TResponse).FullName` | `"FastMediatR.Sample.Api.Dtos.UserDto"` |
| On success | `SetStatus(Ok)` | — |
| On exception | `SetStatus(Error, ex.Message)` + `AddException(ex)` | — |
| Parent context | Propagated automatically | BCL `Activity.Current` chain |

---

### 11.6 Sample consumer wiring

```csharp
// Program.cs — complete wiring example with console exporter
builder.Services.AddFastMediatRTelemetry();

builder.Services.AddFastMediatR<Program>(cfg =>
{
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("MySampleApi"))
        .AddAspNetCoreInstrumentation()
        .AddFastMediatRInstrumentation()          // FastMediatR.Telemetry extension
        .AddConsoleExporter());                   // swap for OTLP in production
```

---

### 11.7 AOT & trim safety

`TracingBehavior<,>` uses only BCL types (`System.Diagnostics.Activity`, `System.Diagnostics.ActivitySource`) and `OpenTelemetry.Api`. Neither involves reflection on the dispatch hot path:

- `typeof(TRequest).Name` / `.FullName` — resolved by the JIT at generic instantiation time, not via `Type.GetType()` or `Activator`; trim-safe.
- `activity.AddException(ex)` — BCL method; no reflection.
- `ActivitySource.StartActivity(...)` — BCL method, fully AOT-safe in .NET 8+.

`FastMediatR.Telemetry.csproj` sets `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>` (inherited from `Directory.Build.props`). No `[RequiresUnreferencedCode]` or `[RequiresDynamicCode]` annotations are required.

---

### 11.8 Unit test strategy (`FastMediatR.Telemetry.Tests`)

Tests use an in-memory `ActivityListener` subscribed to `"FastMediatR"` to assert on emitted spans without starting any exporter. No OTel SDK or collector is required — only `System.Diagnostics.ActivitySource`.

```csharp
// Arrange
using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == FastMediatRActivitySource.ActivitySourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = activity => recorded.Add(activity),
};
ActivitySource.AddActivityListener(listener);
```

| Test | Assertion |
|------|-----------|
| `Send_WithListener_EmitsOneActivity` | `recorded` contains exactly 1 span |
| `Send_WithListener_ActivityHasCorrectName` | `activity.DisplayName == typeof(TRequest).Name` |
| `Send_WithListener_TagsPopulated` | `mediator.request_type` and `mediator.response_type` tags set |
| `Send_WithListener_SuccessfulRequest_StatusIsOk` | `activity.Status == ActivityStatusCode.Ok` |
| `Send_HandlerThrows_StatusIsErrorAndExceptionRecorded` | `Status.Error`, exception event present |
| `Send_WithNoListener_PassesThroughWithNoAllocation` | `StartActivity` returns null; next is still called |
| `Send_ParentActivity_PropagatesContext` | child span's `ParentId` matches parent's `Id` |

---

## 12. Testing Strategy

All source code in `src/` must have corresponding unit tests. The test stack follows
[DotNet.md §16](DotNet.md#16-testing) — modern, free, popular:

| Tool | Purpose |
|------|---------|
| **xUnit v3** | Test framework (latest, async-first) |
| **NSubstitute** | Mocking (cleaner syntax than Moq, actively maintained, MIT) |
| **FluentAssertions** | Expressive readable assertions |
| **FakeTimeProvider** (`Microsoft.Extensions.Time.Testing`) | Controls `TimeProvider` in `PerformanceBehavior` tests |
| **Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing** | Snapshot-tests the Roslyn generator (FM001/FM002/FM003 diagnostics) |
| **NetArchTest** | Architecture rule enforcement (layering, sealed behaviors, no `async void`, etc.) |
| **WebApplicationFactory** (integration only) | End-to-end tests of `AddFastMediatR()` registered in a real ASP.NET Core host |

### 12.0 Test project inventory

Four test projects are in active use:

| Project | Type | Tests | Contents |
|---------|------|-------|----------|
| `tests/FastMediatR.Tests` | Unit | 92 | Core abstractions, all pipeline behaviors, publishers, pre/post processors |
| `tests/FastMediatR.Generator.Tests` | Unit | 12 | Roslyn generator output snapshots; FM001/FM002/FM003 diagnostics |
| `tests/FastMediatR.Integration.Tests` | Integration | 16 | `EndToEndTests` (9) via `WebApplicationFactory<Program>` + `MigrationCompatibilityTests` (7) via standalone `ServiceCollection` |
| `tests/FastMediatR.Architecture.Tests` | Architecture | 6 | NetArchTest dependency direction, type sealing, and Mono.Cecil IL inspection for blocking Task usage |
| `tests/FastMediatR.Telemetry.Tests` | Unit | TBD | In-memory `ActivityListener` assertions for `TracingBehavior`: span created, tags correct, `ActivityStatusCode.Error` on exception, parent context propagated |

A fifth project, `tests/FastMediatR.Aot.SmokeTest`, is a console app published with `PublishAot=true` as a CI gate. It is not an xUnit project and therefore not counted in the test totals above.

| Area | Positive | Negative | Edge |
|------|----------|----------|------|
| `ISender.Send()` dispatches to correct handler | ✅ | Handler not found → InvalidOperationException | Concurrent sends |
| `IPublisher.Publish()` fanout | All handlers invoked | One handler throws → AggregateException | Zero handlers = no-op |
| `ValidationBehavior` | Valid request passes through | Invalid throws ValidationException | No validators registered = passes through |
| `LoggingBehavior` | Log written on enter/exit | — | Exception path still logs |
| `PerformanceBehavior` | Fast request = no warning | Slow request = warning logged | Threshold = 0 logs everything |
| Pre/Post processors | Runs in correct order | Pre throws = handler not called | Empty lists = no-op |
| Pipeline ordering | First registered = outermost | Single behavior | Empty pipeline |
| `Unit` value type | Equality, hash code | — | Struct default value |
| Generator `FM001` diagnostic | — | Missing handler emits error | — |
| Generator `FM002` diagnostic | — | Duplicate handler emits error | — |

### 12.2 Testing the mediator without source generator

The DI extension `AddFastMediatR` registers `FastMediatRDispatcher`. Tests can either:
- Use the real dispatcher wired with an `IServiceCollection` / `ServiceProvider`
- Mock `ISender` directly (preferred for handler unit tests)

---

## 13. Benchmark Project (`FastMediatR.Benchmarks`)

Uses **BenchmarkDotNet** to compare:

| Benchmark | FastMediatR | MediatR |
|-----------|-------------|---------|
| `Send` — simple handler, no behaviors | ✅ | ✅ |
| `Send` — 3 pipeline behaviors | ✅ | ✅ |
| `Send` — with ValidationBehavior (valid) | ✅ | ✅ |
| `Publish` — 5 notification handlers | ✅ | ✅ |
| Cold start / first Send latency | ✅ | ✅ |

Report metrics: Mean, Allocated bytes, Gen0/Gen1 GC.

---

## 14. CI/CD (GitHub Actions)

File: `.github/workflows/ci.yml`

```yaml
name: CI

on:
  push:
    branches: [main, develop, feature/**]
  pull_request:
    branches: [main]

jobs:
  build-test:
    name: Build & test (${{ matrix.os }})
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet restore
      - run: dotnet format --verify-no-changes --no-restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release --logger "trx" --collect:"XPlat Code Coverage"

  pack:
    name: Pack NuGet artifacts
    needs: build-test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet pack src/FastMediatR -c Release --no-build -o ./artifacts
      - run: dotnet pack src/FastMediatR.Telemetry -c Release --no-build -o ./artifacts
      - uses: actions/upload-artifact@v4
        with:
          name: nupkg
          path: ./artifacts/*.nupkg

  publish:
    name: Publish to NuGet.org
    runs-on: ubuntu-latest
    needs: pack
    if: startsWith(github.ref, 'refs/tags/v')
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: nupkg
          path: ./artifacts
      - run: dotnet nuget push ./artifacts/*.nupkg
               --api-key ${{ secrets.NUGET_API_KEY }}
               --source https://api.nuget.org/v3/index.json
               --skip-duplicate
```

- The **`build-test`** job runs on Ubuntu, Windows, and macOS to catch Roslyn source-generator behavior differences across IDE/runtime combinations.
- **`dotnet format --verify-no-changes`** runs in CI to enforce DotNet.md §18 style rules (matches the `.editorconfig` shipped at repo root).
- The **`publish`** job triggers only on a Git tag push matching `v*` (e.g. `v1.0.0`).

### 14.1 NuGet package metadata

Both packable projects (`src/FastMediatR/FastMediatR.csproj` and `src/FastMediatR.Telemetry/FastMediatR.Telemetry.csproj`) must declare full metadata per current NuGet best practices:

```xml
<PropertyGroup>
  <!-- Identity -->
  <PackageId>FastMediatR</PackageId>
  <Version>1.0.0</Version>
  <Authors>FastMediatR Contributors</Authors>
  <Company>FastMediatR</Company>
  <Product>FastMediatR</Product>

  <!-- Description -->
  <Title>FastMediatR — Zero-reflection MediatR replacement</Title>
  <Description>
    A lightweight, high-performance, MIT-licensed mediator library for .NET 10.
    Drop-in compatible with MediatR. Uses a Roslyn source generator to emit
    strongly-typed dispatch tables — no reflection at runtime, missing handlers
    become build errors.
  </Description>
  <PackageTags>mediator;mediatr;cqrs;source-generator;dispatch;dotnet10</PackageTags>

  <!-- License & links -->
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageProjectUrl>https://github.com/&lt;org&gt;/FastMediatR</PackageProjectUrl>
  <RepositoryUrl>https://github.com/&lt;org&gt;/FastMediatR</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageIcon>icon.png</PackageIcon>

  <!-- Quality -->
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
  <Deterministic>true</Deterministic>
  <EnablePackageValidation>true</EnablePackageValidation>
</PropertyGroup>

<ItemGroup>
  <None Include="../../README.md" Pack="true" PackagePath="\" />
  <None Include="../../assets/icon.png" Pack="true" PackagePath="\" />
  <PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.*" PrivateAssets="All" />
</ItemGroup>
```

**Non-packable projects** (`tests/*`, `benchmarks/*`) set `<IsPackable>false</IsPackable>`.

---

## 15. Drop-in Migration from MediatR

| MediatR | FastMediatR | Change needed |
|---------|-------------|---------------|
| `MediatR.IRequest<T>` | `FastMediatR.IRequest<T>` | Change `using` |
| `MediatR.IRequest` (non-generic void) | `FastMediatR.IRequest` | Change `using` |
| `MediatR.IRequestHandler<T,R>` | `FastMediatR.IRequestHandler<T,R>` | Change `using` |
| `MediatR.INotification` | `FastMediatR.INotification` | Change `using` |
| `MediatR.INotificationHandler<T>` | `FastMediatR.INotificationHandler<T>` | Change `using` |
| `MediatR.IPipelineBehavior<T,R>` | `FastMediatR.IPipelineBehavior<T,R>` | Change `using` |
| `MediatR.ISender` | `FastMediatR.ISender` | Change `using` |
| `MediatR.IPublisher` | `FastMediatR.IPublisher` | Change `using` |
| `MediatR.IMediator` | `FastMediatR.IMediator` | Change `using` |
| `MediatR.Unit` | `FastMediatR.Unit` | Change `using` |
| `MediatR.Pipeline.IRequestPreProcessor<T>` | `FastMediatR.IRequestPreProcessor<T>` | Change `using`, no `Pipeline` namespace |
| `MediatR.Pipeline.IRequestPostProcessor<T,R>` | `FastMediatR.IRequestPostProcessor<T,R>` | Change `using`, no `Pipeline` namespace |
| `RequestHandlerDelegate<T>()` (parameterless) | `RequestHandlerDelegate<T>(CancellationToken)` | Update all behavior `next()` calls to `next(ct)` |
| `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` | `services.AddFastMediatR(cfg => cfg.RegisterHandlersFromAssembly(...))` | Rename both method and argument |
| `cfg.AddOpenBehavior(typeof(...))` | `cfg.AddOpenBehavior(typeof(...))` | No change |
| `IMediator.Send(object)` returning `Task<object?>` | `ISender.Send(object)` returning `Task<object?>` | No change (interface split) |

> **Key breaking change from MediatR 12:** If your behaviors use `await next()` (no argument),
> you must update them to `await next(cancellationToken)`. This is the only code change
> required beyond updating `using` directives.

---

## 16. Open Questions / Deferred Decisions

| # | Question | Decision |
|---|----------|---------|
| 1 | ~~Support multiple assemblies in one source-gen invocation?~~ | **Resolved:** The source generator runs in the startup/consumer project and sees all types from all referenced assemblies in the compilation. `RegisterHandlersFromAssembly()` called multiple times is fully supported in v1.0. |
| 2 | `IStreamRequest<T>` / streaming support | Deferred to v2 |
| 3 | Private NuGet feed vs NuGet.org for initial release | Decision needed before first release |
| 4 | Versioning strategy (SemVer, CalVer?) | SemVer recommended (`MAJOR.MINOR.PATCH`) |
| 5 | `netstandard2.1` compatibility | Not in scope for v1 (.NET 10 only) |
| 6 | License | Deferred — MIT recommended (competitive advantage vs MediatR commercial licensing) |
| 7 | Constrained / conditional handlers (MediatR v12 feature) | Not supported in v1.0 — FM002 error fires if two handlers match the same request type |
| 8 | `MediatR.IRequest` non-generic maps to which FastMediatR type? | `FastMediatR.IRequest : IRequest<Unit>` — handlers return `Task<Unit>` |

---

## 17. Project Hygiene & Compliance with DotNet.md

Every `.csproj` in this repository (source, test, benchmark) **must** apply the project-hygiene rules from [DotNet.md §18 — Project Hygiene](DotNet.md#project-hygiene). A shared `Directory.Build.props` at the repo root centralises these:

```xml
<!-- Directory.Build.props (repo root) -->
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn> <!-- silence "missing XML" only on internal types -->
  </PropertyGroup>
</Project>
```

Plus a repo-root `.editorconfig` enforcing the formatting / `var` / blank-line-before-`if` rules from DotNet.md §18.

### 17.1 Required DotNet.md compliance checklist

| Rule (DotNet.md section) | Applied to FastMediatR |
|--------------------------|------------------------|
| §1 — .NET 10 / C# 14 baseline; `record`, `record struct`, `required`, primary ctors, file-scoped namespaces, `TimeProvider` | ✅ enforced throughout |
| §3 — Built-in DI; constructor injection; correct lifetimes; no service-locator | ✅ Dispatcher / behaviors are Scoped; no `IServiceProvider.GetService` on hot path |
| §6 — `async`/`await` end-to-end, no `.Result`/`.Wait()`, no `async void`, `ConfigureAwait(false)` in library code, `CancellationToken` propagated everywhere | ✅ enforced; NetArchTest rule fails the build on any violation |
| §9 — Boundary validation via FluentValidation + dedicated `ValidationBehavior` | ✅ §8.1 |
| §11 — Options pattern with `BindConfiguration` + `ValidateDataAnnotations` + `ValidateOnStart` | ✅ §8.4 |
| §13 — `IDisposable` / `await using`; no LOH allocations in hot dispatch path | ✅ generator emits non-allocating dispatch; no internal `HttpClient` |
| §14 — No logging of secrets / PII; structured logging only | ✅ §8.2 `LogRequestPayload=false` by default |
| §15 — Structured logging via `[LoggerMessage]` source-generated methods; OpenTelemetry via `ActivitySource` | ✅ §8.2 + §11 |
| §16 — xUnit v3 + NSubstitute + FluentAssertions + FakeTimeProvider + NetArchTest + WebApplicationFactory | ✅ §12 |
| §18 — One class per file; XML docs on all public types/members; `sealed` by default; `<Nullable>enable</Nullable>`; warnings as errors; `dotnet format --verify-no-changes` enforced in CI | ✅ enforced by `Directory.Build.props` + `.editorconfig` + the CI `dotnet format` step (§14) |

Any code change that fails one of these checks must either fix the code or update this design document (with rationale) — silent suppression is not allowed.

### 17.2 AOT & Trimming Compatibility

FastMediatR is a v1.0 target for **NativeAOT** and full **assembly trimming**. The design choices that make this possible:

| Concern | Mitigation |
|---------|-----------|
| Hot-path dispatch | Pure switch expression emitted by the source generator — no reflection, no `Type.GetType`, no `Activator.CreateInstance`, no `MethodInfo.Invoke`. |
| Pipeline behavior resolution | `IServiceProvider.GetServices<IPipelineBehavior<TReq, TRes>>()` is a closed generic that MS.DI resolves without reflection emit in AOT (.NET 8+). |
| Generated registration lookup | Single startup-time `Assembly.GetType("FastMediatR.Generated.FastMediatRGeneratedRegistration")` annotated with `[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, "...", "")]` (§7.4). |
| FluentValidation reflection | Lives in **user code** — not a FastMediatR concern. Library does not constrain consumer’s trimming choices. |
| Project settings | Packable projects set `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>`. CI runs `dotnet publish -c Release -r linux-x64 /p:PublishAot=true` against a smoke-test app to detect regressions. |
| `TimeProvider` registration | `AddFastMediatR` calls `services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System)` so `PerformanceBehavior<,>` resolves without manual setup. Test hosts substitute `FakeTimeProvider` by pre-registering before `AddFastMediatR`; `TryAdd` preserves the pre-registered instance. |

A dedicated `tests/FastMediatR.Aot.SmokeTest` console project verifies that the dispatch path — command + void-command + notification fan-out + ISender/IPublisher interface segregation — works correctly when published with `PublishAot=true`, using `FastMediatRGeneratedRegistration.Register(services)` directly (no reflection-based `AddFastMediatR` call). Returns exit code 0 on success.

---

## 18. Summary

FastMediatR delivers a **drop-in MediatR replacement** for .NET 10 / C# 14 applications that need lower overhead and compile-time safety guarantees. The zero-reflection source generator is the primary differentiator: handler dispatch becomes a compiler-verified switch expression, meaning missing handlers are **build errors, not runtime surprises**. The built-in behaviors (validation, logging, performance) follow the same extensible pipeline model users already know from MediatR, while the optional telemetry package keeps the core lean.

Unlike MediatR (now commercially licensed by LuckyPennySoftware), FastMediatR targets the **MIT license** — free to use in any project, commercial or otherwise.

The entire codebase is built to comply with [DotNet.md](DotNet.md), the governing best-practices reference for .NET 10 / C# 14 in this repository. That document covers platform baseline, DI, async, options, logging, security, observability, testing, and coding style; this spec covers only what is FastMediatR-specific.
