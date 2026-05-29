# FastMediatR — Testing Guide

**Date:** 2026-05-29
**Audience:** Contributors running, debugging, and extending the FastMediatR test suite.

This guide explains how the `tests/` projects are organized, how to run them, and how to add new tests. All commands assume the repository root (`C:\Projects\GitHub\FastMediatR`) as the working directory and the .NET SDK pinned in [global.json](../global.json) (`10.0.100`).

---

## 1. Test project overview

The solution contains six test projects, each with a focused responsibility.

| Project | Type | What it verifies | Key dependencies |
|---------|------|------------------|------------------|
| [FastMediatR.Tests](../tests/FastMediatR.Tests) | Unit (xUnit v3) | Core abstractions (`Unit`, `IRequest`), DI registration, assembly scanning, options binding, pipeline behaviors, and both notification publishers. | NSubstitute, FluentAssertions, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Diagnostics.Testing` |
| [FastMediatR.Telemetry.Tests](../tests/FastMediatR.Telemetry.Tests) | Unit (xUnit v3) | `TracingBehavior`, the telemetry DI extensions, and the `TracerProviderBuilder` extensions. | FluentAssertions |
| [FastMediatR.Generator.Tests](../tests/FastMediatR.Generator.Tests) | Source-generator snapshot | Compiles minimal inputs, runs `FastMediatRGenerator`, and asserts on the emitted dispatcher/registration source and diagnostics (FM001/FM002). | `Microsoft.CodeAnalysis.CSharp`, FluentAssertions |
| [FastMediatR.Integration.Tests](../tests/FastMediatR.Integration.Tests) | Integration (E2E) | Boots `FastMediatR.Sample.Api` via `WebApplicationFactory<Program>` and exercises every endpoint over HTTP; also proves MediatR drop-in compatibility. | `Microsoft.AspNetCore.Mvc.Testing`, MediatR, FluentValidation |
| [FastMediatR.Architecture.Tests](../tests/FastMediatR.Architecture.Tests) | Architecture rules | Dependency direction (Core must not reference DI), type sealing, and a custom Mono.Cecil rule that forbids `.Result`/`.Wait()` blocking calls. | NetArchTest.Rules, Mono.Cecil |
| [FastMediatR.Aot.SmokeTest](../tests/FastMediatR.Aot.SmokeTest) | AOT publish gate | A console app published with `PublishAot=true`; exit code `0` proves the AOT-safe startup path works with zero trim/AOT warnings. | (none — uses the generated registration directly) |

> **Note:** `FastMediatR.Aot.SmokeTest` is **not** an xUnit project — it is an executable validated by *publishing and running it* (see §5), not by `dotnet test`.

---

## 2. Running the tests

### Run everything

```powershell
dotnet test FastMediatR.slnx -c Debug
```

This runs all five xUnit projects. The expected result at the time of writing is **138 tests, 0 failures**.

### Run a single project

```powershell
dotnet test tests/FastMediatR.Tests/FastMediatR.Tests.csproj -c Debug
```

### Run a subset by name

```powershell
# Only tests whose fully-qualified name contains "Publisher"
dotnet test FastMediatR.slnx --filter "FullyQualifiedName~Publisher"

# A single test class
dotnet test FastMediatR.slnx --filter "FullyQualifiedName~TaskWhenAllPublisherTests"

# A single trait/category (if applied via [Trait])
dotnet test FastMediatR.slnx --filter "Category=Integration"
```

### Faster inner loop

```powershell
# Skip the rebuild when binaries are already current
dotnet test FastMediatR.slnx --no-build

# See each test name as it runs
dotnet test FastMediatR.slnx --logger "console;verbosity=detailed"
```

---

## 3. Running tests from VS Code

- **Test Explorer:** open the Testing view (beaker icon) to discover, run, and debug individual tests with breakpoints.
- **CodeLens:** the `Run | Debug` actions appear above each `[Fact]`/`[Theory]`.
- The repo includes [start-web-api.cmd](../start-web-api.cmd) to launch the sample API manually if you want to probe endpoints outside the integration harness.

---

## 4. Code coverage

Every xUnit project references `coverlet.collector`, so coverage can be collected without extra setup:

```powershell
dotnet test FastMediatR.slnx --collect:"XPlat Code Coverage"
```

Cobertura reports are written under each project's `TestResults/<guid>/coverage.cobertura.xml`. To produce a human-readable HTML report, install `dotnet-reportgenerator-globaltool` and point it at the generated files.

---

## 5. The AOT smoke test

This project is the CI gate that guarantees FastMediatR stays AOT/trim-clean. It is exercised by publishing, not by `dotnet test`:

```powershell
# Windows
dotnet publish tests/FastMediatR.Aot.SmokeTest/FastMediatR.Aot.SmokeTest.csproj -c Release -r win-x64 /p:PublishAot=true
./tests/FastMediatR.Aot.SmokeTest/bin/Release/net10.0/win-x64/publish/FastMediatR.Aot.SmokeTest.exe
echo $LASTEXITCODE   # 0 = pass
```

```bash
# Linux (CI)
dotnet publish tests/FastMediatR.Aot.SmokeTest/FastMediatR.Aot.SmokeTest.csproj -c Release -r linux-x64 /p:PublishAot=true
./tests/FastMediatR.Aot.SmokeTest/bin/Release/net10.0/linux-x64/publish/FastMediatR.Aot.SmokeTest
```

What it proves:

- The source-generated `FastMediatRGeneratedRegistration.Register(services)` startup path produces **zero** trim/AOT analyzer warnings (the project sets `EnableTrimAnalyzer`/`EnableAotAnalyzer` to `true`).
- The fully reflection-free dispatch path (request, void/`Unit` request, and notification publish) works in a native AOT binary.

A non-zero exit code, or any IL2xxx/IL3xxx warning during publish, fails the gate. See [usage guide §12](260524_1300-fastmediatr-usage-guide.md) for the AOT guidance this test enforces.

---

## 6. How the integration tests work

`FastMediatR.Integration.Tests` boots the real sample API in-memory.

- [CustomWebApplicationFactory.cs](../tests/FastMediatR.Integration.Tests/CustomWebApplicationFactory.cs) derives from `WebApplicationFactory<Program>`, sets the environment to `Testing`, and injects deterministic `FastMediatR:*` options via in-memory configuration (e.g. a high `PerformanceThresholdMs` so no test accidentally trips the slow-request warning).
- [EndToEndTests.cs](../tests/FastMediatR.Integration.Tests/EndToEndTests.cs) drives the full HTTP pipeline (behaviors → validation → handler → notification fan-out).
- [MigrationCompatibilityTests.cs](../tests/FastMediatR.Integration.Tests/MigrationCompatibilityTests.cs) builds a plain `ServiceCollection` (no HTTP) to prove the FastMediatR API shape matches MediatR. The two-line migration recipe is documented in that file's header and in [usage guide §13](260524_1300-fastmediatr-usage-guide.md).

Because the source generator runs on the integration-test compilation, handler types defined inside the test project get their own generated dispatcher — no manual wiring required.

---

## 7. How the generator snapshot tests work

`FastMediatR.Generator.Tests` ([FastMediatRGeneratorTests.cs](../tests/FastMediatR.Generator.Tests/FastMediatRGeneratorTests.cs)) uses a small harness:

1. `BuildCompilation(sources)` assembles a minimal Roslyn `CSharpCompilation` referencing `FastMediatR.Core` plus the BCL.
2. `RunGeneratorAsync(...)` runs `FastMediatRGenerator` through a `CSharpGeneratorDriver` and returns the generated sources and diagnostics.
3. Individual tests assert on substrings of the emitted code (e.g. the pipeline fast-path `if (count == 0)` branch) and on expected diagnostics.

> **Important:** when you change anything in [DispatcherEmitter.cs](../src/FastMediatR.Generator/Emitters/DispatcherEmitter.cs) or [RegistrationEmitter.cs](../src/FastMediatR.Generator/Emitters/RegistrationEmitter.cs), the string assertions in these tests will likely need updating. Run this project first after generator edits.

---

## 8. Adding new tests

Conventions used across the suite:

- **Framework:** xUnit v3 with `[Fact]`/`[Theory]`; assertions via FluentAssertions (`result.Should().Be(...)`).
- **Naming:** `Method_Scenario_ExpectedResult` (e.g. `Publish_WhenHandlerCancels_PropagatesOperationCanceledException`). `CA1707` is intentionally suppressed in test projects to allow underscores.
- **Mocking:** prefer NSubstitute (already referenced in `FastMediatR.Tests`).
- **Time:** never use `DateTime.Now`/`Stopwatch` directly — inject `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` so timing is deterministic.
- **Logging assertions:** use `FakeLogCollector` from `Microsoft.Extensions.Diagnostics.Testing` to assert on emitted log entries.
- **Placement:** put the test in the project that owns the code under test (unit → `FastMediatR.Tests`/`FastMediatR.Telemetry.Tests`; generator → `FastMediatR.Generator.Tests`; full-stack → `FastMediatR.Integration.Tests`). Mirror the source folder structure (e.g. `Behaviors/`, `Publishers/`, `Core/`, `DI/`).

After adding tests, validate with:

```powershell
dotnet test FastMediatR.slnx -c Debug
```

---

## 9. Continuous integration checklist

A green build/CI run requires all of the following:

1. `dotnet build FastMediatR.slnx -c Release` — succeeds with `TreatWarningsAsErrors=true`.
2. `dotnet test FastMediatR.slnx -c Debug` — 138/138 passing.
3. AOT smoke publish (§5) — exit code `0`, no IL2xxx/IL3xxx warnings.
4. Architecture tests (part of step 2) — dependency direction and no-blocking-call rules hold.

---

## Related documents

- **Usage guide:** [260524_1300-fastmediatr-usage-guide.md](260524_1300-fastmediatr-usage-guide.md)
- **Security & performance review:** [260529_1200-security-performance-review.md](260529_1200-security-performance-review.md)
- **Task breakdown (test phases 5.1–5.5):** [260524_1230-fastmediatr-task-breakdown.md](260524_1230-fastmediatr-task-breakdown.md)
