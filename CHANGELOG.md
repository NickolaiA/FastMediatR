# Changelog

All notable changes to FastMediatR will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — Exception Handling Pipeline

- **`FastMediatR.Core`** — new exception-handling abstractions (AOT-clean, abstractions only):
  - `IRequestExceptionHandler<TRequest, TResponse, TException>` — handles an exception thrown by the pipeline and can supply a fallback response via `RequestExceptionHandlerState<TResponse>.SetHandled(...)`, suppressing the exception. Matches the MediatR signature.
  - `IRequestExceptionAction<TRequest, TException>` — side-effect-only observer that runs when a request fails but **cannot** suppress the exception (e.g. logging, metrics).
  - `RequestExceptionHandlerState<TResponse>` (sealed) — carries the `Handled` flag and `Response`; `SetHandled(response)` marks the exception handled.
  - `RequestExceptionActionProcessorStrategy` enum — `ApplyForUnhandledExceptions` (default) / `ApplyForAllExceptions`.
- **`FastMediatR.Extensions.DI`** — two auto-registered pipeline behaviors plus wiring:
  - `RequestExceptionProcessorBehavior<TRequest, TResponse>` — catches exceptions, offers them to every matching `IRequestExceptionHandler`, and returns the fallback response when one suppresses the exception; otherwise rethrows preserving the stack trace.
  - `RequestExceptionActionProcessorBehavior<TRequest, TResponse>` — runs every matching `IRequestExceptionAction`, then rethrows.
  - Exception/action matching walks the thrown exception's type hierarchy (most-derived first); the first handler to call `SetHandled` wins, while **all** matching actions run.
  - `AssemblyScanner` now discovers `IRequestExceptionHandler<,,>` and `IRequestExceptionAction<,>` implementations and registers them as closed-interface mappings.
  - `AddFastMediatR` auto-registers the two behaviors **between** user `AddOpenBehavior` registrations and the pre/post-processor behaviors (so they wrap the handler and pre/post-processors). Their relative order is governed by `FastMediatRConfig.RequestExceptionActionProcessorStrategy`.
  - The behaviors live in `FastMediatR.Extensions.DI` and use reflection (`MakeGenericType` + DI lookup) only on the exception path; this is confined to the already reflection-based `AddFastMediatR` opt-in path, keeping `FastMediatR.Core` AOT-clean.
  - When an exception handler or action itself throws, that fault propagates unwrapped and the original request exception is attached to it under `Exception.Data["FastMediatR.OriginalException"]`, so neither failure is lost.
  - Exception **actions** are invoked with a non-cancellable token (`CancellationToken.None`) rather than the request token — the observed failure is often a cancellation, and action telemetry must still complete. Exception **handlers** still observe the request token (which may already be cancelled).
  - Built-in pipeline behaviors (exception, pre- and post-processor) are registered idempotently (`TryAddEnumerable`): repeated `AddFastMediatR` calls no longer stack duplicate behaviors, which previously would have run exception actions twice per failure.
  - `AddFastMediatR` now scans each assembly into a service collection **at most once**: the common `AddFastMediatR<TMarker>()` + `RegisterHandlersFromAssemblyContaining<TMarker>()` pattern scanned the same assembly twice, double-registering every handler, action and pre/post-processor (exception actions fired twice per failure once actions actually ran).
  - Exception-path reflection caches the closed handler/action interface types (keyed by request/response/exception), so `MakeGenericType` does not re-run per failure occurrence; invocation stays a single cached-`MethodInfo.Invoke` — a deliberate cold-path simplicity choice, documented in code.
- **`samples/FastMediatR.Sample.Api`** — new exception-handling use case: `ProcessPaymentCommand` (handler throws `PaymentDeclinedException` over the credit limit, with a culture-invariant decline message because the text becomes the HTTP response body), `PaymentDeclinedExceptionHandler` (translates it to a graceful `ProcessPaymentResult`), and `PaymentDeclinedExceptionAction` (logs the decline). Exposed via `POST /payments`. The sample configures `ApplyForAllExceptions` — under the default `ApplyForUnhandledExceptions` strategy the suppressing handler would prevent the decline action from ever running, making the demo silently inert.
- **`tests/FastMediatR.Tests`** — 31 new tests:
  - `RequestExceptionProcessorBehaviorTests` — pass-through on success, suppression → fallback, rethrow when no/unhandled handler, base-type hierarchy matching, first-suppressing-handler-wins, original exception propagates unwrapped when a handler throws synchronously, original attached to fault `Data` when a handler faults, handler fault with an unwritable `Data` still propagates the fault (best-effort attach), handler rethrowing the same exception instance is not self-attached, asynchronously faulting handler propagates unwrapped and carries the original, handlers observe the request token even when already cancelled (asymmetric with actions by design), catch-all handler registered for `Exception`, value-type `SetHandled(default)`, null guards.
  - `RequestExceptionActionProcessorBehaviorTests` — no actions on success, action runs + rethrows, all matching actions across hierarchy run, actions run with a non-cancellable token even after request cancellation, original attached to fault `Data` when an action faults, null guards.
  - `ExceptionBehaviorRegistrationTests` — default vs. `ApplyForAllExceptions` ordering, placement between user behaviors and pre/post-processor behaviors, idempotent registration across repeated `AddFastMediatR` calls, and closed-mapping dedupe when the same assembly is scanned repeatedly.
  - `ExceptionPipelineStrategyTests` — runtime strategy semantics through the real `AddFastMediatR` pipeline (descriptor order → DI → behavior chain, not hand-wired): default-strategy suppression skips actions, unhandled exceptions run actions and rethrow the original instance, `ApplyForAllExceptions` runs actions even when a handler suppresses.
- **`tests/FastMediatR.Integration.Tests`** — 3 new tests: `PaymentsEndpointTests` exercises `POST /payments` under, exactly-at and over the credit limit (boundary amount is approved — only strictly over-limit payments decline); the over-limit case proves both pipeline halves through HTTP — a graceful `200` with `approved: false` and the **exact** currency-qualified decline message (handler) and the decline logged as a warning by `PaymentDeclinedExceptionAction` (action, via a recording `ILoggerProvider`).
- Documentation: usage guide §8 "Exception Handling Pipeline" added (subsequent sections renumbered); feature-gap roadmap item #2 marked implemented.
- Test count: **172 / 172** passing (123 core/behavior, 12 generator, 20 integration, 6 architecture, 11 telemetry).

### Changed

- Migrated the canonical repository solution file from `FastMediatR.sln` to `FastMediatR.slnx` using the .NET 10 solution format. CI, README build/test commands, and design/task-breakdown documentation now target `FastMediatR.slnx`.

### Added — Phase 6 (OpenTelemetry Tracing)

- **`FastMediatR.Telemetry`** — new optional NuGet package providing zero-overhead OpenTelemetry `ActivitySource` tracing for every request dispatched through the pipeline.
  - `FastMediatRActivitySource` (internal) — shared `ActivitySource` instance with well-known name `"FastMediatR"` and assembly informational version metadata. Centralised so `TracingBehavior` and `AddFastMediatRInstrumentation` reference the same constant.
  - `TracingBehavior<TRequest, TResponse>` — `IPipelineBehavior<,>` that calls `ActivitySource.StartActivity(typeof(TRequest).Name, ActivityKind.Internal)`. Zero overhead when no listener is subscribed (fast-path null check). On success sets `ActivityStatusCode.Ok`; on exception sets `ActivityStatusCode.Error`, calls `Activity.AddException(ex)` (BCL .NET 9+, follows OTel semantic conventions), and rethrows. Request type name and FQ response type name are cached as static fields to avoid per-call reflection.
  - Activity conventions: span name = `typeof(TRequest).Name` (short name, e.g. `"CreateUserCommand"`), `ActivityKind.Internal`, tags `mediator.request_type` and `mediator.response_type` set to the fully-qualified type names.
  - `ServiceCollectionExtensions.AddFastMediatRTelemetry(IServiceCollection)` — registers `TracingBehavior<,>` as an open-generic singleton. Must be called **before** `AddFastMediatR<TMarker>` to place tracing as the outermost pipeline layer.
  - `TracerProviderBuilderExtensions.AddFastMediatRInstrumentation(TracerProviderBuilder)` — subscribes the `"FastMediatR"` source to the OTel SDK. Equivalent to `.AddSource("FastMediatR")` but named for discoverability.
  - AOT / trim safe: all types use `System.Diagnostics.ActivitySource` (BCL) only; no reflection at runtime.
- **`tests/FastMediatR.Telemetry.Tests`** — 11 new unit tests using in-memory `ActivityListener` (no OTel exporter needed):
  - `TracingBehaviorTests` (7): `Send_WithListener_EmitsOneActivity`, `Send_WithListener_ActivityHasCorrectName`, `Send_WithListener_TagsPopulated`, `Send_WithListener_SuccessfulRequest_StatusIsOk`, `Send_HandlerThrows_StatusIsErrorAndExceptionRecorded`, `Send_WithNoListener_PassesThroughWithNoAllocation`, `Send_ParentActivity_PropagatesContext`.
  - `ServiceCollectionExtensionsTests` (3): null-arg guard, singleton open-generic registration check, fluent-return check.
  - `TracerProviderBuilderExtensionsTests` (1): null-arg guard.
- **`samples/FastMediatR.Sample.Api`** — updated for Phase 6:
  - `AddFastMediatRTelemetry()` registered before `AddFastMediatR<Program>()`.
  - `AddOpenTelemetry().WithTracing(t => t.AddFastMediatRInstrumentation().AddConsoleExporter())` wired for local-dev span visibility.
  - `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.Console` package references added.
  - `ProjectReference` to `FastMediatR.Telemetry` added.
  - `/telemetry/demo` now exercises both `Send` and `Publish`; the `Send` call emits the visible `GetUserByIdQuery` span while the notification publish remains intentionally untraced in Phase 6.
  - `/health` response updated to `"Phase 6"`.
- Test count: **138 / 138** passing (92 core/behavior, 12 generator, 17 integration, 6 architecture, 11 telemetry).

### Security — Phase 6

- **Upgraded all OpenTelemetry packages from 1.11.2 → 1.15.3** (in `Directory.Packages.props`). Resolves CVE GHSA-g94r-2vxg-569j (excessive memory allocation in baggage/B3/Jaeger propagation header parsing; moderate DoS potential). Affected packages: `OpenTelemetry`, `OpenTelemetry.Api`, `OpenTelemetry.Exporter.Console`, `OpenTelemetry.Extensions.Hosting`.
- **Replaced `ActivityExtensions.RecordException` with `Activity.AddException`** — `RecordException` is marked `[Obsolete]` in OTel 1.15.3. The BCL replacement `Activity.AddException` (available since .NET 9) writes the same `"exception"` event following OTel semantic conventions.

### Fixed — Phase 5

- **`ServiceCollectionExtensions.AddFastMediatR` did not register `TimeProvider`.** `PerformanceBehavior<,>` depends on `System.TimeProvider`, which is not automatically available in the DI container. Added `services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System)` so any app registering `PerformanceBehavior<,>` works without manual setup. Test hosts that need deterministic timing can pre-register their own `TimeProvider` (e.g. `FakeTimeProvider`) before calling `AddFastMediatR` — `TryAdd` preserves the pre-registered instance.
- **`MigrationCompatibilityTests.Send_InvalidCommand_ValidationBehaviorThrowsValidationException` registered validator under wrong service type.** The validator was registered as `AbstractValidator<EchoCommand>` instead of `IValidator<EchoCommand>`. The DI container resolves `IEnumerable<IValidator<TRequest>>` for `ValidationBehavior<,>`, so the validator was never found. Fixed registration to use `IValidator<EchoCommand>`.

### Added — Phase 5 (Integration & End-to-End Tests)

- **`tests/FastMediatR.Integration.Tests`** — new test project using `WebApplicationFactory<Program>` to exercise the full ASP.NET Core + FastMediatR pipeline in-process.
  - `CustomWebApplicationFactory` — configures `Testing` environment and injects test-specific `FastMediatR:PerformanceThresholdMs` / `EnableLogging` overrides.
  - `EndToEndTests` (9 tests) — HTTP-level assertions against the sample API: `POST /users` (valid + validation-error paths), `DELETE /users/{id}`, `GET /users/{id}` (found + 404), `POST /users/{id}/events/created` (notification fan-out), `GET /health`.
  - `MigrationCompatibilityTests` (7 tests) — DI-level assertions proving the drop-in migration contract: `Send` with result, `Send` void, `Publish` fan-out with multiple handlers, validation rejection, pipeline ordering via `TrackingBehavior`, notification fan-out order with `SequentialAwaitPublisher`, and a round-trip through all three built-in behaviors.
  - `MigrationTestHandlers` — `EchoCommand`, `VoidEchoCommand`, `EchoNotification`, `EchoCommandValidator`, `TrackingBehavior<,>`, and static trackers (`NotificationTracker`, `PipelineOrderTracker`) for the migration compatibility tests.
- **`tests/FastMediatR.Architecture.Tests`** — new test project using `NetArchTest.Rules 1.3.2` and `Mono.Cecil`.
  - `ArchitectureTests` (6 tests): `Core_HasNoDependencyOnExtensionsDi`, `ExtensionsDi_HasDependencyOnCore`, `Core_PublicConcreteClasses_AreSealed`, `ExtensionsDi_PublicConcreteClasses_AreSealed`, `Core_NoBlockingTaskUsage`, `ExtensionsDi_NoBlockingTaskUsage`.
  - Note: `ConfigureAwait(false)` enforcement is handled at build time via `CA2007` (activated by `AnalysisMode=AllEnabledByDefault` + `TreatWarningsAsErrors=true`), so no separate runtime test is required.
  - `NoBlockingTaskRule` (custom `ICustomRule`) — uses Mono.Cecil IL inspection to detect `.Result` / `.Wait()` calls in any library method body.
- **`tests/FastMediatR.Aot.SmokeTest`** — new AOT console smoke-test project published with `PublishAot=true`.
  - Registers handlers directly via `FastMediatRGeneratedRegistration.Register(services)` without assembly scanning.
  - Exercises `ISender.Send<string>`, `ISender.Send<Unit>` (void command), `IPublisher.Publish` fan-out, and `ISender` / `IPublisher` resolution from DI.
  - Returns exit code `0` on success, `1` on any assertion failure — intended as a CI gate.
- **`samples/FastMediatR.Sample.Api/Program.cs`** — added `UseExceptionHandler` middleware that converts `ValidationException` to `HTTP 400` (with structured `errors` payload) and all other exceptions to `HTTP 500`. Enables clean HTTP-level assertions in `EndToEndTests`.
- Test count: **126 / 126** passing across all four test projects (92 core/behavior, 12 generator, 16 integration, 6 architecture).

### Fixed — Phase 4 comprehensive code review (second pass)

- **`ServiceCollectionExtensions.cs` step-6 comment described `TryAdd` semantics backwards.** The comment said "consumers who call `services.Replace(...)` before `AddFastMediatR` can override it", which is incorrect. `TryAdd` *preserves* a registration made before `AddFastMediatR`; it does not enable `Replace`-based overrides. Corrected to accurately state that `TryAdd` preserves a pre-registered `INotificationPublisher` and that consumers should call `services.Replace(...)` *after* `AddFastMediatR` to swap the default.
- **`SequentialAwaitPublisher` XML doc parameter description said "Checked between each handler invocation".** The cancellation check runs at the start of each loop iteration, so it also fires before the very first handler — not only between handlers. Fixed to say "Checked before each handler invocation (including before the first handler)".
- **`PerformanceBehavior` XML doc code example listed `PerformanceBehavior` as the outermost behavior**, contradicting the sample API which registers `LoggingBehavior` first (outermost). Updated the example to the canonical three-behavior order: `LoggingBehavior` outermost, `PerformanceBehavior` second, `ValidationBehavior` innermost.
- **`Program.cs` (sample API) header comment listed behaviors in the wrong order.** Comment said "ValidationBehavior → LoggingBehavior → PerformanceBehavior" but actual registration order is "LoggingBehavior → PerformanceBehavior → ValidationBehavior". Corrected.
- **Missing cancellation-coverage tests for publishers.** `SequentialAwaitPublisher` had no test verifying that an already-cancelled token prevents all handlers from running. `TaskWhenAllPublisher` had no test verifying the cancellation token is forwarded to handler delegates. Two tests added: `Publish_WhenAlreadyCancelled_ThrowsImmediately_WithNoHandlerInvoked` (`SequentialAwaitPublisherTests`) and `Publish_ForwardsCancellationTokenToHandlers` (`TaskWhenAllPublisherTests`).
- Test count: 104/104 passing.

### Fixed — Phase 4 code review

- **Critical: Generated dispatcher was bypassing all pipeline behaviors and `INotificationPublisher`.** `DispatcherEmitter.cs` had been calling request handlers directly from the switch arms and calling notification handlers directly in the `Publish` switch block. This meant every registered `IPipelineBehavior<,>` — including `ValidationBehavior`, `LoggingBehavior`, `PerformanceBehavior`, `RequestPreProcessorBehavior`, and `RequestPostProcessorBehavior` — was silently skipped at runtime even though it was correctly registered in DI. Similarly, the `INotificationPublisher` strategy (parallel vs sequential) had no effect.
  - **`Send<TResponse>` and `Send(object)` fix:** Switch arms now call `ExecutePipeline<TRequest, TResponse>(request, handlerDelegate, cancellationToken)` instead of invoking the handler directly. `ExecutePipeline` is a private generic helper emitted once per dispatcher class. It resolves `IEnumerable<IPipelineBehavior<TRequest, TResponse>>` via `GetServices<>()`, iterates the behavior array from last-to-first to build a `RequestHandlerDelegate<TResponse>` chain (first-registered behavior = outermost), then executes the chain.
  - **`Publish` fix:** Each notification case block now resolves `INotificationPublisher` from DI, pre-resolves each handler into a local variable, constructs a `NotificationHandlerExecutor[]` with strongly-typed casts from `INotification` to the concrete notification type in each handler lambda, and delegates to `publisher.Publish(executors, notification, ct)`. Publisher strategy (parallel, sequential, or custom) is now fully respected.
  - `using System.Linq;` added to the generated dispatcher file (required for `GetServices<T>().ToArray()`).
  - Three new generator tests added: `SingleHandler_DispatcherCallsExecutePipelineNotHandlerDirectly`, `SingleHandler_DispatcherContainsPipelineHelperMethod`, `NotificationHandlers_PublishDelegatesToINotificationPublisher`.
  - Test count: 102/102 passing (90 core/behavior + 12 generator).

### Added — Phase 4 (Built-in Pipeline Behaviors)

- `FastMediatROptions` moved from `FastMediatR.Extensions.DI` to `FastMediatR.Core` (namespace `FastMediatR`) so built-in behaviors can inject `IOptions<FastMediatROptions>` without a circular dependency. Added `EnableLogging` bool property (default `true`).
- `RequestPreProcessorBehavior<TRequest, TResponse>` — resolves all `IRequestPreProcessor<TRequest>` from DI and runs them in order before invoking `next`.
- `RequestPostProcessorBehavior<TRequest, TResponse>` — invokes `next` and then runs all `IRequestPostProcessor<TRequest, TResponse>` in order. Post-processors do not run if the handler throws.
- `ValidationBehavior<TRequest, TResponse>` — resolves all `IValidator<TRequest>` from DI, runs them concurrently (each with its own `ValidationContext<TRequest>`), aggregates failures across all validators, and throws `FluentValidation.ValidationException` before invoking `next`. Pass-through when no validators are registered.
- `LoggingBehavior<TRequest, TResponse>` — logs request entry at `Information` with structured `{@Request}` payload (when `EnablePayloadLogging` is true) and exit elapsed time, or `Error` on failure. Controlled by `FastMediatROptions.EnableLogging` and `EnablePayloadLogging`. Uses `[LoggerMessage]` source-generated methods and `TimeProvider` injection.
- `PerformanceBehavior<TRequest, TResponse>` — measures handler elapsed time via `TimeProvider.GetTimestamp()` / `GetElapsedTime()` and emits a `Warning` log when elapsed exceeds `FastMediatROptions.PerformanceThresholdMs`. Uses try/finally to guarantee measurement on exception paths.
- `TaskWhenAllPublisher` — default `INotificationPublisher` implementation. Starts all notification handlers concurrently via `Task.WhenAll` and surfaces multi-failure as `AggregateException`.
- `SequentialAwaitPublisher` — `INotificationPublisher` that awaits handlers one at a time in registration order, short-circuiting on first failure and checking `CancellationToken` between handlers.
- `NotificationPublisherStrategy` enum (`ParallelWhenAll = 0`, `Sequential = 1`) in `FastMediatR.Extensions.DI`.
- `FastMediatRConfig.NotificationPublisherStrategy` property (default `ParallelWhenAll`). `AddFastMediatR` registers `TaskWhenAllPublisher` or `SequentialAwaitPublisher` via `TryAdd` based on this setting.
- `AddFastMediatR` now registers `RequestPreProcessorBehavior<,>` and `RequestPostProcessorBehavior<,>` as innermost behaviors (after all user `AddOpenBehavior` calls), completing the Phase 3 deferred tasks 3.5 and 3.6.
- `FluentValidation.DependencyInjectionExtensions` package reference added to `FastMediatR.Sample.Api`.
- `CreateUserCommandValidator` (validates `Email` and `DisplayName` on `CreateUserCommand`) added to sample API.
- Sample API `Program.cs` updated to Phase 4: `LoggingBehavior<,>`, `PerformanceBehavior<,>`, `ValidationBehavior<,>` registered; `AddValidatorsFromAssemblyContaining<Program>()` called; `/health` response updated to `"Phase 4"`.
- `appsettings.json` in sample API updated with `"EnableLogging": true`.
- `Microsoft.Extensions.Diagnostics.Testing` 9.5.0 and `Microsoft.Extensions.TimeProvider.Testing` 9.0.0 added to test project for `FakeLogger<T>` and `FakeTimeProvider`.
- 32 new unit tests (90 total in `FastMediatR.Tests`): `RequestPreProcessorBehaviorTests`, `RequestPostProcessorBehaviorTests`, `ValidationBehaviorTests`, `LoggingBehaviorTests`, `PerformanceBehaviorTests`, `TaskWhenAllPublisherTests`, `SequentialAwaitPublisherTests`.

### Fixed — Phase 4

- `ValidationBehavior` now creates a fresh `ValidationContext<TRequest>` per validator instead of sharing one across concurrent validators. Sharing a context caused FluentValidation to accumulate failures from previous validators, doubling the reported error count.

### Added — Phase 3 (DI Extensions)

- `FastMediatR.Extensions.DI` project (`net10.0`, `IsPackable=false`) wiring the library into ASP.NET Core / MS DI.
  - `ServiceCollectionExtensions.AddFastMediatR<TMarker>(services, configure?)` — single entry point annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Invokes the source-generated registration, scans the marker assembly, scans additional assemblies, registers open-generic behaviors, and binds `FastMediatROptions`.
  - `FastMediatRConfig` — fluent configuration builder: `RegisterHandlersFromAssembly(Assembly)`, `RegisterHandlersFromAssemblyContaining<T>()`, `AddOpenBehavior(Type, ServiceLifetime)`, `DefaultHandlerLifetime` (default `Scoped`).
  - `AssemblyScanner` (internal) — discovers concrete `IRequestHandler<,>`, `INotificationHandler<>`, `IRequestPreProcessor<>`, and `IRequestPostProcessor<,>` implementations; registers concrete types via `TryAdd` and processor interface mappings via `Add` (preserving `IEnumerable<>` resolution order); honours `[HandlerLifetime]` attribute per-handler.
  - `GeneratedRegistrationLocator` (internal) — locates the source-generated `FastMediatR.Generated.FastMediatRGeneratedRegistration` type in the marker assembly via `Assembly.GetType()` and invokes `Register(services)` via a cached `Action<IServiceCollection>` delegate (`ConcurrentDictionary<Assembly, Action<IServiceCollection>?>` cache).
  - `FastMediatROptions` — runtime options bound from the `"FastMediatR"` configuration section: `PerformanceThresholdMs` (`[Range(1, int.MaxValue)]`, default 500), `EnablePayloadLogging` (default `false`). Validated with `ValidateDataAnnotations()` + `ValidateOnStart()`.
- `FastMediatR.Extensions.DI` added to `FastMediatR.sln`.
- `tests/FastMediatR.Tests` extended with 23 new DI tests (58 total):
  - `AssemblyScannerTests` — handler/notification/pre-post processor discovery, `[HandlerLifetime]` override, abstract type exclusion, transient lifetime, idempotency.
  - `AddFastMediatRTests` — generated registration round-trip (via `FakeGenerated`), multi-assembly scanning, open-behavior registration and ordering, options infrastructure presence, null-argument guard.
  - `FastMediatROptionsTests` — default values, `SectionName` constant, `PerformanceThresholdMs` validation failure.
  - `DI/Fixtures/FakeGenerated.cs` — fake `FastMediatR.Generated.FastMediatRGeneratedRegistration` in the test assembly; uses `TryAddSingleton` for idempotent round-trip assertions.
  - `DI/Fixtures/TestHandlers.cs` — stub request/notification/processor/handler types covering all scanner code paths.
- `samples/FastMediatR.Sample.Api` updated to Phase 3:
  - `Program.cs` switched from manual `FastMediatRGeneratedRegistration.Register` to `AddFastMediatR<Program>(cfg => ...)` with `RegisterHandlersFromAssemblyContaining<Program>()` and `AddOpenBehavior(typeof(NoOpSampleBehavior<,>))`.
  - `NoOpSampleBehavior<TRequest, TResponse>` added to `Program.cs` as a copy-pasteable open-behavior example.
  - `appsettings.json` extended with `"FastMediatR": { "PerformanceThresholdMs": 300, "EnablePayloadLogging": false }`.
  - Phase comment in `/health` response updated to `"Phase 3"`.
- `FastMediatR.Extensions.DI` project reference added to `FastMediatR.Sample.Api.csproj` and `FastMediatR.Tests.csproj`; `Microsoft.Extensions.DependencyInjection` package reference added to test project.

### Fixed — Phase 3 code review

- Removed invalid `<paramref>` tags from class- and method-level XML docs in `AssemblyScanner.cs`, `AssemblyScannerTests.cs`, and `AddFastMediatRTests.cs` (CS1734).
- Added `[RequiresDynamicCode]` alongside `[RequiresUnreferencedCode]` on `AddFastMediatR<TMarker>` — required by `BindConfiguration<TOptions>` which carries `RequiresDynamicCodeAttribute` (IL3050).
- Fixed `Unit.TaskValue` → `Unit.Task` in `TestHandlers.cs` (CS0117).
- Removed unresolvable `<cref>` attributes referencing `internal` types across assembly boundaries in test fixtures (CS1574).
- Changed `FakeGenerated.Register` from `services.AddSingleton` to `services.TryAddSingleton` so double-call idempotency tests pass correctly.

### Added — Phase 2 (Source Generator)

- `FastMediatR.Generator` project (`netstandard2.0`, `IsRoslynComponent=true`) implementing `IIncrementalGenerator`.
  - `HandlerDiscoveryProvider` — incremental syntax + semantic pipeline collecting all `IRequestHandler<,>` and `INotificationHandler<>` concrete implementations using `EquatableArray<T>` for cache-safe pipeline models.
  - `DispatcherEmitter` — emits `FastMediatR.Generated.FastMediatRDispatcher` implementing `IMediator` via zero-reflection switch-expression dispatch for all discovered request and notification handlers.
  - `RegistrationEmitter` — emits `FastMediatR.Generated.FastMediatRGeneratedRegistration` with a `Register(IServiceCollection)` method that wires the dispatcher and all handlers into DI using `TryAdd*` extension methods.
  - `Diagnostics` — FM001 (no handler for request type), FM002 (duplicate handler), FM003 (non-default lifetime) diagnostic descriptors.
  - `EquatableArray<T>` — structurally-equatable wrapper around `ImmutableArray<T>` for correct incremental caching.
  - Model records: `RequestHandlerInfo`, `NotificationHandlerInfo`, `RequestTypeInfo`, `DiscoveryResult`, `LocationInfo`.
- Generator added to `FastMediatR.sln`.
- `FastMediatR.Generator.Tests` project (xUnit v3 + FluentAssertions) with 8 passing snapshot tests covering single handler, dispatcher switch arms, TryAddScoped registration, FM001, FM002, multiple notification handlers, void command, and empty compilation.
- `samples/FastMediatR.Sample.Api` updated to Phase 2:
  - Generator referenced as `OutputItemType="Analyzer"`.
  - `Program.cs` wired with `FastMediatRGeneratedRegistration.Register(builder.Services)` and all four dispatch endpoints (`POST /users`, `DELETE /users/{id}`, `GET /users/{id}`, `POST /users/{id}/events/created`).
  - `EmitCompilerGeneratedFiles=true` added so generated files are written to `obj/Generated/` for inspection.
- Usage guide §9 Source-generator diagnostics filled in (FM001 / FM002 / FM003).

### Added — Phase 1 (Core Abstractions)

- `FastMediatR.Core` public surface — all types in the root `FastMediatR` namespace:
  - `Unit` (`readonly record struct`) with `Unit.Value` and the pre-completed `Unit.Task` singleton.
  - Marker interfaces: `IBaseRequest`, `IRequest<TResponse>`, `IRequest : IRequest<Unit>`, `INotification`.
  - Handler interfaces: `IRequestHandler<TRequest, TResponse>`, `IRequestHandler<TRequest> : IRequestHandler<TRequest, Unit>`, `INotificationHandler<TNotification>`.
  - Entry-point interfaces: `ISender`, `IPublisher`, `IMediator : ISender, IPublisher`.
  - `RequestHandlerDelegate<TResponse>(CancellationToken)` and `IPipelineBehavior<TRequest, TResponse>`.
  - Pre/post processors: `IRequestPreProcessor<TRequest>`, `IRequestPostProcessor<TRequest, TResponse>`.
  - Notification dispatch: `INotificationPublisher` interface and `NotificationHandlerExecutor` record struct.
  - `[HandlerLifetime]` attribute (Class-only, single-use, sealed).
- Full XML documentation on every public symbol (DotNet.md §18).
- `tests/FastMediatR.Tests` project (xUnit v3 + FluentAssertions + NSubstitute) with 26 passing tests covering `Unit` semantics, interface inheritance, namespace placement, and `[HandlerLifetime]` shape.
- `samples/FastMediatR.Sample.Api` now includes reference `Commands/` (`CreateUserCommand`, `DeactivateUserCommand`), `Queries/` (`GetUserByIdQuery`), and `Notifications/` (`UserCreatedNotification` with two handlers) — compile-only at this phase; runtime dispatch lands in Phase 3.
- Usage guide §1 Introduction & goals, §3 Quick-start stub, and §4 Core concepts filled with snippets copied from the sample API.

### Notes

- `RequestHandlerDelegate<TResponse>` and the `next` parameter of `IPipelineBehavior.Handle` carry `[SuppressMessage]` attributes for `CA1711` / `CA1716` respectively. Both suppressions exist solely to preserve drop-in API parity with MediatR; they are documented in the source.

### Added — Phase 0 (Repository & Toolchain Bootstrap)

- Solution `FastMediatR.sln` and empty project layout (5 src, 4 test, 1 benchmark, 1 sample).
- `Directory.Build.props` enforcing .NET 10 / C# 14 baseline, nullable, ImplicitUsings,
  TreatWarningsAsErrors, AnalysisMode `AllEnabledByDefault`, `IsAotCompatible`, `IsTrimmable`.
- `Directory.Build.targets` wiring Source Link for packable projects.
- `Directory.Packages.props` enabling centrally-managed package versions (CPM).
- `.editorconfig` enforcing DotNet.md §18 rules (file-scoped namespaces, `var` rules,
  naming conventions, async-method `Async` suffix).
- `.gitignore`, `.gitattributes`, `LICENSE` (MIT), `README.md`, this `CHANGELOG.md`.
- `.github/workflows/ci.yml` matrix CI on ubuntu/windows/macOS with
  `dotnet format --verify-no-changes` + build + test + pack.
- `global.json` pinning .NET 10 SDK.
- `samples/FastMediatR.Sample.Api` scaffold with `/health` endpoint.
- Usage guide skeleton `docs/260524_1300-fastmediatr-usage-guide.md`.
