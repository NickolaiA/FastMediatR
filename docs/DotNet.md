# .NET & C# Best Practices

> **Platform target:** .NET 10 · C# 14  
> **Last reviewed:** March 2026  
> These rules are aligned with current Microsoft guidance, .NET 10 platform capabilities, and widely accepted community practices.

---

## Table of Contents

1. [Platform & Language Baseline](#1-platform--language-baseline)
2. [Architecture & Design Principles](#2-architecture--design-principles)
3. [Dependency Injection](#3-dependency-injection)
4. [HTTP Client Management](#4-http-client-management)
5. [Resilience & Fault Handling](#5-resilience--fault-handling)
6. [Async Programming & Concurrency](#6-async-programming--concurrency)
7. [ASP.NET Core Web API Design & RESTful Principles](#7-aspnet-core-web-api-design--restful-principles)
8. [API Versioning](#8-api-versioning)
9. [Validation](#9-validation)
10. [Persistence & EF Core](#10-persistence--ef-core)
11. [Configuration & Options Pattern](#11-configuration--options-pattern)
12. [Caching, Throughput & Rate Limiting](#12-caching-throughput--rate-limiting)
13. [Memory Management & Resource Handling](#13-memory-management--resource-handling)
14. [Security](#14-security)
15. [Observability, Logging & OpenTelemetry](#15-observability-logging--opentelemetry)
16. [Testing](#16-testing)
17. [Background Services & Channels](#17-background-services--channels)
18. [Coding Style & Anti-Patterns](#18-coding-style--anti-patterns)

---

## 1. Platform & Language Baseline

- **Target .NET 10** and use the default language version it ships with (C# 14). Do not force older language versions unless there is a hard compatibility requirement.
- Prefer the built-in ASP.NET Core stack before adding third-party infrastructure for logging, DI, OpenAPI, rate limiting, problem details, health checks, and configuration. ASP.NET Core 10 and .NET 10 continue to expand first-party support.
- Optimize for **clarity first, performance second, micro-optimizations last**. The .NET 10 runtime and JIT produce excellent results for straightforward code.

### Modern C# Language Features to Leverage

| Feature | Guidance |
|---|---|
| **Primary constructors** (C# 12+) | Use for services and records to reduce boilerplate. Prefer over manually written constructors for DI classes. |
| **`required` members** | Use on DTO/record properties to enforce initialization at compile time without custom constructors. |
| **Records & record structs** | Use `record` for immutable DTOs, value objects, and CQRS messages. Use `record struct` for small stack-allocated value types in hot paths. |
| **Collection expressions** (C# 12+) | Prefer `[1, 2, 3]` syntax over `new List<int> { 1, 2, 3 }` for collection literals. |
| **Pattern matching** | Use `switch` expressions, property patterns, list patterns, and `is` type checks. Eliminates many `if`/`else if` chains and null checks. |
| **Nullable reference types** | Enable `<Nullable>enable</Nullable>` project-wide. Annotate correctly — never use `!` to silence warnings without a documented reason. |
| **`global using`** | Centralize common namespace imports in a `GlobalUsings.cs` file per project. |
| **`file`-scoped namespaces** | Use single-line namespace declarations throughout. |
| **`Span<T>` / `Memory<T>`** | Use in performance-sensitive string/buffer work. Prefer `ReadOnlySpan<char>` over `string` for parsing. |
| **`SearchValues<T>`** (.NET 8+) | Use for repeated membership searches over a fixed set of characters or values. |
| **`FrozenDictionary` / `FrozenSet`** (.NET 8+) | Use for read-only lookup tables built once at startup. |
| **`TimeProvider`** (.NET 8+) | Inject `TimeProvider` instead of `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly, so time can be mocked in tests. |

```csharp
// Good: primary constructor + record DTO with required members
public sealed record CreateOrderCommand
{
    public required Guid CustomerId { get; init; }
    public required IReadOnlyList<OrderLineDto> Lines { get; init; }
}

// Good: TimeProvider injection
public class OrderService(TimeProvider timeProvider)
{
    public DateTimeOffset Now => timeProvider.GetUtcNow();
}
```

---

## 2. Architecture & Design Principles

### Clean Architecture Layers

```
Domain          → entities, value objects, domain services, domain events, invariants
Application     → use cases, commands/queries, DTOs, interfaces, validation, orchestration
Infrastructure  → EF Core, HTTP clients, file storage, queues, cache, auth integrations
Presentation    → Web API endpoints/controllers, filters, request/response contracts
```

- Keep business rules independent from frameworks and infrastructure choices.
- Model around **business use cases**, not database tables. Keep domain language aligned with bounded contexts.
- Do **not** put business logic in controllers, EF entities, or middleware.
- Prefer a **modular monolith** unless there is a proven operational need for microservices.

### SOLID Principles

| Principle | Practical rule |
|---|---|
| **Single Responsibility (SRP)** | Each class/method does one thing. If you need "and" in the description, split it. |
| **Open/Closed (OCP)** | Extend via composition and strategy patterns, not by modifying existing code. |
| **Liskov Substitution (LSP)** | Subtypes must behave correctly wherever the base type is expected. |
| **Interface Segregation (ISP)** | Prefer focused, narrow interfaces over fat ones. Avoid `IEverything`. |
| **Dependency Inversion (DIP)** | Depend on abstractions injected by the DI container, not on concrete implementations. |

#### S — Single Responsibility Principle

A class should have only one reason to change. If a class handles both business logic and persistence, email formatting and sending, or both validation and transformation, split it.

```csharp
// Bad: one class does too many things
public class OrderService
{
    public void PlaceOrder(Order order)
    {
        // validate order
        // save to database
        // send confirmation email
        // update inventory
    }
}

// Good: each class has a single responsibility
public class OrderValidator { /* validation only */ }
public class OrderRepository { /* persistence only */ }
public class OrderConfirmationEmailSender { /* email only */ }
public class InventoryService { /* inventory only */ }

public class OrderService(
    OrderValidator validator,
    OrderRepository repository,
    OrderConfirmationEmailSender emailSender,
    InventoryService inventory)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        validator.Validate(order);
        await repository.AddAsync(order, ct);
        await emailSender.SendAsync(order, ct);
        await inventory.DeductAsync(order, ct);
    }
}
```

#### O — Open/Closed Principle

Classes should be open for extension but closed for modification. Add new behaviour by composing new types rather than editing existing ones.

```csharp
// Bad: adding a new discount type requires modifying this class
public class DiscountCalculator
{
    public decimal Calculate(Order order, string discountType)
    {
        if (discountType == "seasonal") return order.Total * 0.10m;
        if (discountType == "loyalty")  return order.Total * 0.15m;
        return 0;
    }
}

// Good: new discount types are added by implementing the interface
public interface IDiscountStrategy
{
    decimal Calculate(Order order);
}

public class SeasonalDiscount : IDiscountStrategy
{
    public decimal Calculate(Order order) => order.Total * 0.10m;
}

public class LoyaltyDiscount : IDiscountStrategy
{
    public decimal Calculate(Order order) => order.Total * 0.15m;
}

public class DiscountCalculator(IEnumerable<IDiscountStrategy> strategies)
{
    public decimal Calculate(Order order) =>
        strategies.Sum(s => s.Calculate(order));
}
```

#### L — Liskov Substitution Principle

Derived types must be substitutable for their base types without altering program correctness. Do not override methods in a way that weakens preconditions, strengthens postconditions, or throws unexpected exceptions.

```csharp
// Bad: Square overrides Rectangle in a way that breaks callers
public class Rectangle
{
    public virtual int Width  { get; set; }
    public virtual int Height { get; set; }
    public int Area => Width * Height;
}

public class Square : Rectangle
{
    public override int Width  { set { base.Width = base.Height = value; } }
    public override int Height { set { base.Width = base.Height = value; } }
}

// Good: use a common abstraction instead of inheritance
public interface IShape { int Area { get; } }
public class Rectangle(int width, int height) : IShape
{
    public int Area => width * height;
}
public class Square(int side) : IShape
{
    public int Area => side * side;
}
```

#### I — Interface Segregation Principle

Clients should not be forced to depend on methods they do not use. Split large interfaces into smaller, focused ones.

```csharp
// Bad: implementors are forced to provide methods they don't need
public interface IWorker
{
    void Work();
    void Eat();
    void Sleep();
}

// Good: separate interfaces for separate concerns
public interface IWorkable  { void Work(); }
public interface IFeedable  { void Eat();  }
public interface IRestable  { void Sleep(); }

public class HumanWorker  : IWorkable, IFeedable, IRestable { /* ... */ }
public class RobotWorker  : IWorkable { /* robots don't eat or sleep */ }
```

#### D — Dependency Inversion Principle

High-level modules should not depend on low-level modules; both should depend on abstractions. Inject dependencies via the constructor — never `new` up concrete infrastructure classes inside domain or application code.

```csharp
// Bad: high-level service depends directly on a concrete low-level class
public class OrderService
{
    private readonly SqlOrderRepository _repo = new SqlOrderRepository();
}

// Good: depend on an abstraction; the DI container supplies the implementation
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
    Task<Order?> FindAsync(Guid id, CancellationToken ct);
}

public class OrderService(IOrderRepository repository)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct) =>
        await repository.AddAsync(order, ct);
}

// Registration (Program.cs / extension method)
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
```

### CQRS / MediatR Pattern

- Use Command/Query separation via MediatR or a hand-rolled mediator for medium/large applications.
- Commands mutate state; queries return data — never both in the same handler.
- Keep handlers thin: validate input, invoke the domain, persist, publish events.
- **Commit the Unit of Work exclusively from Application layer command handlers** — classes typically suffixed `Handler`. Repositories and infrastructure services must never call `SaveChangesAsync` directly; only the handler that orchestrates the full transaction should.
- **Repositories must only be accessed from Application layer handlers** — never directly from controllers or other service classes. Route all data access through CQRS handlers to maintain predictable layer boundaries.

```csharp
// Command
public sealed record CreateOrderCommand(Guid CustomerId, IReadOnlyList<OrderLineDto> Lines)
    : IRequest<Guid>;

// Handler
public sealed class CreateOrderHandler(IOrderRepository repo, IUnitOfWork uow)
    : IRequestHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Lines);
        repo.Add(order);
        await uow.SaveChangesAsync(ct);
        return order.Id;
    }
}
```

---

## 3. Dependency Injection

- Use the **built-in DI container** unless a proven gap exists. Register services with correct lifetimes:
  - **Singleton** — stateless, thread-safe shared services (caches, configuration, `HttpClient` via factory, `TimeProvider`).
  - **Scoped** — request-scoped services (`DbContext`, unit-of-work, per-request state).
  - **Transient** — lightweight, stateless services with no shared state.

- Prefer **constructor injection**. Avoid service locator patterns (`IServiceProvider.GetService`) except in framework-driven edge cases.

- In **hosted services / `BackgroundService`**, inject `IServiceScopeFactory`, create a scope, and resolve scoped dependencies inside it — never inject `DbContext` or other scoped services directly into a singleton-hosted service.

```csharp
// Correct: scope inside BackgroundService
public class MyWorker(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        // ...
    }
}
```

- Use **`IServiceCollection` extension methods** (defined per feature or layer) to keep `Program.cs` tidy:

```csharp
// Infrastructure/ServiceCollectionExtensions.cs
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("Default")));
        return services;
    }
}
```

- Avoid **captive dependencies**: never inject a scoped service into a singleton. The .NET DI container will throw at startup (in development) when scope validation is enabled.

---

## 4. HTTP Client Management

### Why `new HttpClient()` Is Dangerous

Creating `HttpClient` instances manually with `new HttpClient()` causes **socket exhaustion** because `HttpClient` does not release underlying `HttpMessageHandler`/socket connections immediately on dispose. Additionally, a long-lived manually created `HttpClient` does **not** respect DNS changes.

> **Rule:** Never use `new HttpClient()` outside of test code. Always use `IHttpClientFactory`.

### IHttpClientFactory Patterns

**1. Named clients** — for simple cases where you need different configurations per external service:

```csharp
// Registration
builder.Services.AddHttpClient("PaymentsApi", client =>
{
    client.BaseAddress = new Uri("https://payments.example.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Usage
public class PaymentService(IHttpClientFactory factory)
{
    public async Task<PaymentResult> ChargeAsync(...)
    {
        using var client = factory.CreateClient("PaymentsApi");
        var response = await client.PostAsJsonAsync("charges", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentResult>(ct);
    }
}
```

**2. Typed clients** — the preferred pattern for non-trivial integrations. Encapsulates all HTTP logic for one external service behind an interface:

```csharp
// Interface
public interface IPaymentsApiClient
{
    Task<PaymentResult> ChargeAsync(ChargeRequest request, CancellationToken ct = default);
}

// Implementation — injected as transient by the factory
public sealed class PaymentsApiClient(HttpClient client) : IPaymentsApiClient
{
    public async Task<PaymentResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("charges", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentResult>(ct)
            ?? throw new InvalidOperationException("Deserialization returned null.");
    }
}

// Registration
builder.Services.AddHttpClient<IPaymentsApiClient, PaymentsApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PaymentsApi:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
```

**3. Direct factory injection** — when you genuinely need ad-hoc clients with varying configuration at runtime:

```csharp
using var client = factory.CreateClient();
```

### HttpClient Configuration Best Practices

- Set `BaseAddress` during registration, not at call time.
- Set `Timeout` at the client level as a global backstop, and use per-call `CancellationToken` for request-level cancellation.
- Do **not** set `DefaultRequestHeaders` inside request methods — they are not thread-safe when mutated on a shared `HttpClient`.
- Use `ReadFromJsonAsync` / `PostAsJsonAsync` from `System.Net.Http.Json` instead of manual serialization.
- When handling non-success status codes yourself, use `response.IsSuccessStatusCode` or `response.EnsureSuccessStatusCode()` consistently. Do not silently swallow errors.

```csharp
// Prefer System.Net.Http.Json extension methods
var result = await client.GetFromJsonAsync<OrderDto>($"orders/{id}", ct);
await client.PostAsJsonAsync("orders", command, ct);
await client.PutAsJsonAsync($"orders/{id}", update, ct);
```

### HttpClient and JSON Options

Pass `JsonSerializerOptions` at registration rather than per-call to avoid repeated compilation of serialization metadata:

```csharp
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
```

---

## 5. Resilience & Fault Handling

### Microsoft.Extensions.Http.Resilience (Polly 8 Integration)

.NET 8+ includes `Microsoft.Extensions.Http.Resilience`, which wraps Polly 8 with first-class DI integration. Prefer it over adding Polly manually.

```xml
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="9.*" />
```

**Standard resilience pipeline** — provides retry, circuit breaker, total timeout, attempt timeout, and hedging with sensible defaults:

```csharp
builder.Services
    .AddHttpClient<IPaymentsApiClient, PaymentsApiClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["PaymentsApi:BaseUrl"]!);
    })
    .AddStandardResilienceHandler();   // retry + circuit breaker + timeouts built in
```

**Custom resilience pipeline** — when you need fine-grained control:

```csharp
builder.Services
    .AddHttpClient<IPaymentsApiClient, PaymentsApiClient>(...)
    .AddResilienceHandler("payments-pipeline", builder =>
    {
        // Innermost: per-attempt timeout
        builder.AddTimeout(TimeSpan.FromSeconds(5));

        // Retry with exponential back-off and jitter
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                args.Outcome.Result?.StatusCode is
                    HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        });

        // Circuit breaker
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 10,
            BreakDuration = TimeSpan.FromSeconds(15)
        });

        // Outermost: total request timeout
        builder.AddTimeout(TimeSpan.FromSeconds(30));
    });
```

### Resilience Rules

- Apply resilience only to **idempotent** or **safe** operations (GET, HEAD) or operations explicitly designed for retry (idempotency keys).
- Use **hedging** (parallel retry strategy) only for latency-sensitive read paths where duplicate calls are acceptable.
- Log or record circuit-breaker state changes to observability systems so you can act on degraded dependencies.
- Do not retry on `4xx` client errors (except `429 Too Many Requests` with `Retry-After`). Only retry transient failures.
- Use `CancellationToken` throughout so resilience pipelines respect request cancellation and do not retry after the client has disconnected.

---

## 6. Async Programming & Concurrency

### Async/Await Core Rules

- Use `async`/`await` end-to-end for all I/O-bound work: HTTP, DB, file, queue, cloud SDK.
- **Never block** on async code with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` in request paths — this deadlocks classic synchronization contexts and wastes thread-pool threads in ASP.NET Core.
- **Never use `async void`** except for legacy event handlers. ASP.NET Core cannot observe completion or capture exceptions from `async void`.
- Do **not** wrap already-async server code in `Task.Run()` and immediately await it. ASP.NET Core runs on thread-pool threads already.
- Accept and propagate `CancellationToken` through application, infrastructure, and EF calls. The .NET code quality analyzer CA2016 enforces this.
- Use `Task`/`Task<T>` by default. Use `ValueTask`/`ValueTask<T>` only in hot paths where profiling proves allocation pressure — and only when you fully understand the ownership rules (no double-await, no multiple continuations).
- Use `ConfigureAwait(false)` in library code; it is unnecessary in ASP.NET Core application code because there is no `SynchronizationContext`.

### Async Streams

Use `IAsyncEnumerable<T>` when data is naturally streamed or paged — for example, reading large result sets from a DB, streaming from a queue, or server-sent events. Do not use it for ordinary CRUD responses where a materialized list is simpler.

```csharp
// EF Core async stream
public async IAsyncEnumerable<OrderSummary> GetOrdersAsync(
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var order in _db.Orders.AsAsyncEnumerable().WithCancellation(ct))
        yield return order.ToSummary();
}
```

### Concurrency & Thread Safety

- Use `SemaphoreSlim` for async-compatible mutual exclusion (unlike `lock`, which is sync-only).
- Use `ConcurrentDictionary<K,V>` for thread-safe caches or shared state dictionaries. Prefer `GetOrAdd(key, valueFactory)`, but note the factory may be called more than once concurrently.
- Use `System.Threading.Channels` for producer/consumer patterns inside a process instead of `BlockingCollection<T>`.
- Do **not** use `Thread` or `ThreadPool.QueueUserWorkItem` — use `Task`, `Parallel`, or channels.
- Avoid shared mutable state in services registered as `Singleton`. When shared state is necessary, protect it explicitly or use immutable/atomic types.

```csharp
// Async mutex pattern with SemaphoreSlim
private readonly SemaphoreSlim _lock = new(1, 1);

public async Task<T> GetOrCreateAsync(string key, Func<Task<T>> factory, CancellationToken ct)
{
    if (_cache.TryGetValue(key, out var cached)) return cached;
    await _lock.WaitAsync(ct);
    try
    {
        if (_cache.TryGetValue(key, out cached)) return cached;
        var value = await factory();
        _cache[key] = value;
        return value;
    }
    finally { _lock.Release(); }
}
```

### Parallel & Batch Processing

- Use `Parallel.ForEachAsync` (.NET 6+) for CPU-bound or mixed parallel loops with async body:

```csharp
await Parallel.ForEachAsync(items, new ParallelOptions
{
    MaxDegreeOfParallelism = Environment.ProcessorCount,
    CancellationToken = ct
}, async (item, token) =>
{
    await ProcessItemAsync(item, token);
});
```

- When fanning out multiple independent async tasks, use `Task.WhenAll` — but capture exceptions from all tasks, not just the first one.

---

## 7. ASP.NET Core Web API Design & RESTful Principles

### Controller vs. Minimal API

- **Controller-based APIs** — prefer for larger enterprise APIs that need filters, model binding, action results, `[ApiController]` conventions, and familiar team structure.
- **Minimal APIs** — prefer for small focused surface areas, thin proxy endpoints, or when team is comfortable with endpoint filters.
- Both support OpenAPI generation, authentication, and filters in .NET 10.

### Routing & Status Codes

- Use attribute routing with explicit HTTP verbs. Design routes as resources (`/orders/{id}`, not `/getOrderById`).
- Return proper HTTP status codes: `200 OK`, `201 Created` (with `Location` header), `204 No Content`, `400 Bad Request`, `404 Not Found`, `409 Conflict`, `422 Unprocessable Entity`.
- Use **Problem Details** (`application/problem+json`) for errors via `AddProblemDetails()` and `UseExceptionHandler`. Never return raw exception messages to clients.

```csharp
// Program.cs
builder.Services.AddProblemDetails();
// ...
app.UseExceptionHandler();
app.UseStatusCodePages();
```

```csharp
// Controller returning typed problem
return Problem(
    title: "Order not found",
    detail: $"No order with id '{id}' exists.",
    statusCode: StatusCodes.Status404NotFound);
```

### OpenAPI — `Microsoft.AspNetCore.OpenApi` + Scalar

> **Do not use Swashbuckle or Swagger UI for new projects.** ASP.NET Core 9+ ships a first-party OpenAPI document generator (`Microsoft.AspNetCore.OpenApi`) and **Scalar** is the recommended interactive API explorer to replace Swagger UI. Scalar is actively maintained, supports OpenAPI 3.1, has a modern UI, and integrates with zero config.

```xml
<!-- API project .csproj -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.*" />
<PackageReference Include="Scalar.AspNetCore" Version="2.*" />
```

```csharp
// Program.cs — registration
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info.Title   = "Orders API";
        doc.Info.Version = "v1";
        doc.Info.Contact = new() { Name = "Platform Team", Email = "platform@example.com" };
        return Task.CompletedTask;
    });
});
```

```csharp
// Program.cs — pipeline (development only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                 // serves /openapi/v1.json
    app.MapScalarApiReference();      // serves /scalar/v1 — interactive UI
}
```

**Enriching endpoint metadata** — keep OpenAPI output accurate and useful:

```csharp
// Controller action
[HttpPost]
[ProducesResponseType<OrderCreatedResponse>(StatusCodes.Status201Created)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[EndpointSummary("Place a new order")]
[EndpointDescription("Creates an order for the specified customer. Returns the order ID and a Location header.")]
[Tags("Orders")]
public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken ct) { ... }

// Minimal API
app.MapPost("/v1/orders", CreateOrderHandler)
   .WithSummary("Place a new order")
   .WithDescription("Creates an order. Returns 201 with Location header.")
   .WithTags("Orders")
   .Produces<OrderCreatedResponse>(StatusCodes.Status201Created)
   .ProducesValidationProblem()
   .RequireAuthorization("CanManageOrders");
```

**Security scheme in OpenAPI:**

```csharp
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

// Transformer
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemes)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument doc, OpenApiDocumentTransformerContext ctx, CancellationToken ct)
    {
        var authSchemes = await schemes.GetAllSchemesAsync();
        if (authSchemes.Any(s => s.Name == JwtBearerDefaults.AuthenticationScheme))
        {
            doc.Components ??= new();
            doc.Components.SecuritySchemes["Bearer"] = new()
            {
                Type   = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            };
        }
    }
}
```

- Restrict `/openapi/` and `/scalar/` routes to **development only** (shown above). Never expose raw schema documents on public production endpoints.
- When supporting multiple API versions, register one OpenAPI document per version and configure Scalar to show all versions.

### CORS

- Configure CORS explicitly and narrowly. Do **not** use `AllowAnyOrigin` + `AllowCredentials` (rejected by browsers and a security vulnerability).
- Define named policies per environment:

```csharp
builder.Services.AddCors(opts => opts.AddPolicy("AllowFrontend", policy =>
    policy.WithOrigins(builder.Configuration["AllowedOrigins"]!.Split(';'))
          .WithMethods("GET", "POST", "PUT", "DELETE")
          .WithHeaders("Authorization", "Content-Type")));

app.UseCors("AllowFrontend");
```

### Request/Response Design

- Prefer separate request/response DTOs per endpoint — do **not** expose EF entities directly.
- For list endpoints, return a paginated wrapper:

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
```

- Use **cursor-based pagination** for large or fast-changing datasets. Use **offset pagination** only for small, stable result sets.
- Avoid returning `null` from API endpoints. Return `404` or an empty collection as appropriate.

### Health Checks

```csharp
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddUrlGroup(new Uri(builder.Configuration["PaymentsApi:BaseUrl"]!), "payments-api");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

---

### RESTful API Design

#### Resource Naming

- Use **nouns, not verbs** in resource paths. The HTTP method is the verb.
- Use **plural nouns** for collections: `/orders`, `/customers`, `/products`.
- Use **kebab-case** for multi-word resource names: `/order-items`, `/shipping-addresses`.
- Express hierarchy only when the relationship is natural and shallow (max 2 levels deep):

```
/customers/{customerId}/orders          ← customer's orders
/orders/{orderId}/lines                 ← order lines
/orders/{orderId}/lines/{lineId}        ← specific line
```

- Prefer flat endpoints over deep nesting when the child resource has its own identity:

```
/orders/{orderId}/lines/{lineId}  ← fine for tightly owned sub-resources
/products/{productId}             ← instead of /categories/{id}/products/{id}/reviews/{id}/...
```

#### HTTP Method Semantics

| Method | Semantics | Safe? | Idempotent? | Common status codes |
|---|---|---|---|---|
| **GET** | Retrieve a resource or collection | Yes | Yes | 200, 404 |
| **POST** | Create a new resource or trigger an action | No | No | 201 + Location, 400, 409, 422 |
| **PUT** | Replace a resource completely (full update) | No | Yes | 200, 204, 404 |
| **PATCH** | Partial update (JSON Merge Patch / JSON Patch) | No | No* | 200, 204, 400, 404, 422 |
| **DELETE** | Remove a resource | No | Yes | 204, 404 |
| **HEAD** | Same as GET but no body — for existence checks | Yes | Yes | 200, 404 |
| **OPTIONS** | Report allowed methods (CORS preflight) | Yes | Yes | 200, 204 |

*PATCH is idempotent only when using JSON Merge Patch with absolute values; not with increment-style patches.

#### HTTP Status Codes Reference

| Code | Name | When to return |
|---|---|---|
| 200 | OK | Successful GET, PUT, PATCH with body |
| 201 | Created | Successful POST that created a resource — include `Location` header |
| 202 | Accepted | Async operation accepted; processing not yet complete |
| 204 | No Content | Successful DELETE, PUT, or PATCH with no response body |
| 400 | Bad Request | Malformed request syntax, invalid parameters |
| 401 | Unauthorized | Missing or invalid authentication credentials |
| 403 | Forbidden | Authenticated but not authorised for this operation |
| 404 | Not Found | Resource does not exist |
| 405 | Method Not Allowed | HTTP method not supported on this endpoint |
| 409 | Conflict | State conflict (duplicate, optimistic concurrency) |
| 410 | Gone | Resource existed but is permanently deleted |
| 415 | Unsupported Media Type | `Content-Type` not supported |
| 422 | Unprocessable Entity | Syntactically valid but semantically invalid (business rule violation) |
| 429 | Too Many Requests | Rate limit exceeded — include `Retry-After` header |
| 500 | Internal Server Error | Unhandled server-side error — return Problem Details, never expose stack traces |
| 502 | Bad Gateway | Upstream dependency is unavailable |
| 503 | Service Unavailable | Service overloaded or in maintenance — include `Retry-After` |

#### Idempotency

- Design **PUT** and **DELETE** to be idempotent — a second identical call must return the same result as the first.
- For **POST** operations that should be idempotent (e.g., payment capture, order placement), require an **`Idempotency-Key`** header. Store results keyed by this value and return the cached response on duplicate calls:

```csharp
[HttpPost]
public async Task<IActionResult> CreateOrder(
    [FromBody] CreateOrderRequest request,
    [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey,
    CancellationToken ct)
{
    if (idempotencyKey.HasValue)
    {
        var cached = await _idempotency.GetAsync(idempotencyKey.Value, ct);
        if (cached is not null) return cached.ToActionResult();
    }
    // ... process, then store result against idempotencyKey
}
```

#### Content Negotiation

- Accept `Accept: application/json` as the default. Support `application/problem+json` for error responses.
- Return **`415 Unsupported Media Type`** when the client sends a `Content-Type` the API does not handle.
- Do **not** include format extensions in URLs (`/orders.json`). Use `Accept` headers.
- For APIs that must support multiple formats, use `AddControllers().AddXmlSerializerFormatters()` and let the ASP.NET Core formatter pipeline handle negotiation.

```csharp
builder.Services
    .AddControllers(options => options.RespectBrowserAcceptHeader = true)
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
```

#### Filtering, Sorting & Searching

Use **query string parameters** for collection operations. Follow consistent naming:

```
GET /orders?status=pending&customerId=abc              ← filtering
GET /orders?sort=placedAt:desc,total:asc               ← sorting
GET /orders?q=invoice+2025                             ← full-text search
GET /orders?page=2&pageSize=25                         ← offset pagination
GET /orders?after=cursor_abc123&limit=25               ← cursor pagination
GET /orders?fields=id,status,total                     ← sparse fieldsets
```

- Validate and whitelist all accepted filter/sort parameter values. Never pass raw query parameter values into SQL.
- Return **`400 Bad Request`** with a descriptive Problem Details body for invalid filter values.
- Document all accepted query parameters in OpenAPI using `[FromQuery]` parameters or Minimal API `.WithOpenApi()` configuration.

#### Pagination

- Return pagination metadata alongside the collection:

```csharp
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    string? NextCursor = null)
{
    public bool HasMore => NextCursor is not null || (Page * PageSize) < TotalCount;
}
```

- Use **cursor pagination** for large, fast-changing datasets (feed-style). Use **offset pagination** for small, stable datasets with UI page controls.
- Always impose a **maximum page size** (e.g., 100). Ignore client requests that exceed it.

#### HATEOAS (Hypermedia)

For internal APIs, HATEOAS is usually not worth the complexity. For public APIs or APIs consumed by unknown clients, include a minimal `_links` map pointing to related actions:

```json
{
  "id": "ord_123",
  "status": "pending",
  "_links": {
    "self":    { "href": "/v1/orders/ord_123",        "method": "GET" },
    "confirm": { "href": "/v1/orders/ord_123/confirm", "method": "POST" },
    "cancel":  { "href": "/v1/orders/ord_123/cancel",  "method": "POST" }
  }
}
```

Keep link generation consistent by using `IUrlHelper` or `LinkGenerator` rather than constructing URLs manually.

#### Long-Running Operations

For operations that cannot complete within a normal HTTP timeout, use the **202 Accepted + polling** pattern:

```csharp
// POST /reports — kicks off async generation
[HttpPost]
[ProducesResponseType(StatusCodes.Status202Accepted)]
public IActionResult StartReportGeneration([FromBody] ReportRequest request)
{
    var jobId = _jobQueue.Enqueue(request);
    return AcceptedAtAction(
        nameof(GetReportStatus),
        new { jobId },
        new { jobId, status = "queued" });
}

// GET /reports/{jobId}/status
[HttpGet("{jobId}/status")]
public async Task<IActionResult> GetReportStatus(Guid jobId, CancellationToken ct)
{
    var job = await _jobs.GetAsync(jobId, ct);
    if (job is null)  return NotFound();
    if (!job.IsReady) return Ok(new { jobId, status = job.Status });
    return Ok(new { jobId, status = "done", downloadUrl = job.ResultUrl });
}
```

---

## 8. API Versioning

- Version APIs from day one. Changing an unversioned public API is a breaking change.
- Use `Asp.Versioning.Http` (formerly `Microsoft.AspNetCore.Mvc.Versioning`).
- Prefer **URL segment versioning** (`/v1/orders`) for public APIs; header versioning for internal/partner APIs.

```csharp
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });
```

```csharp
[ApiController]
[Route("v{version:apiVersion}/orders")]
[ApiVersion(1)]
public class OrdersV1Controller : ControllerBase { ... }
```

- Deprecate old versions with `[ApiVersion(1, Deprecated = true)]` before removing them.
- Never delete an older version without a communicated sunset timeline.

---

## 9. Validation

### Boundary Validation

- Validate at the **application boundary**. Input validation happens before business logic executes.
- With `[ApiController]`, ASP.NET Core automatically returns `400 Bad Request` with problem details for binding/validation failures.
- `.NET 10` Minimal APIs include built-in endpoint filter validation discovery.

### Data Annotations vs. FluentValidation

- Use **Data Annotations** (`[Required]`, `[Range]`, `[MaxLength]`) for simple, declarative validation on DTOs.
- Use **FluentValidation** for complex, multi-step, or database-dependent validation rules:

```csharp
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator(IOrderRepository repo)
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().ForEach(line =>
        {
            line.ChildRules(l =>
            {
                l.RuleFor(x => x.ProductId).NotEmpty();
                l.RuleFor(x => x.Quantity).GreaterThan(0);
            });
        });
        RuleFor(x => x.CustomerId)
            .MustAsync(async (id, ct) => await repo.ExistsAsync(id, ct))
            .WithMessage("Customer not found.");
    }
}
```

- Register FluentValidation with MediatR pipeline behavior for automatic pre-handler validation:

```csharp
// Registration
builder.Services.AddValidatorsFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
```

### Domain Invariants vs. Input Validation

- Domain invariants live in the domain model — they protect business rules regardless of how the operation is triggered.
- Input validation protects the HTTP layer from malformed data.
- These are separate concerns. Never replace domain invariant enforcement with HTTP attribute validation.

---

## 10. Persistence & EF Core

### Core Rules

- Register `DbContext` as **scoped** and keep its lifetime aligned with the HTTP request or an explicit unit of work.
- Use `AsNoTracking()` for all read-only queries — it significantly reduces memory pressure and improves performance.
- **Avoid lazy loading** in APIs and services. It easily produces the N+1 query problem. Prefer explicit `Include()`/`ThenInclude()` for eager loading.
- Shape queries to return only what is needed. Use projections (`.Select(x => new Dto {...})`) rather than loading full entities for read operations.
- Use **raw SQL** (`FromSqlRaw`, `ExecuteSqlRawAsync`) only when LINQ cannot express the query well, or when LINQ generates poor SQL that profiling confirms is a problem.

```csharp
// Good: projection, AsNoTracking, CancellationToken
public async Task<IReadOnlyList<OrderSummaryDto>> GetSummariesAsync(
    Guid customerId, CancellationToken ct)
{
    return await _db.Orders
        .AsNoTracking()
        .Where(o => o.CustomerId == customerId)
        .Select(o => new OrderSummaryDto(o.Id, o.Status, o.Total))
        .ToListAsync(ct);
}
```

### Migrations

- Apply migrations at startup via `MigrateAsync()` only for simple apps or tests. For production apps, run migrations as a separate deployment step or init container.
- Keep migrations small and reversible when possible. Review generated migration SQL before committing.
- Never edit a migration that has already been applied to a production database.

### Optimistic Concurrency

- Enable concurrency tokens (`[Timestamp]` or `IsRowVersion()`) for entities that can be updated concurrently. Handle `DbUpdateConcurrencyException` explicitly.

### Connection Resiliency

- Enable EF Core's built-in retry on transient failures for SQL Server/Azure SQL:

```csharp
options.UseSqlServer(connectionString, sql =>
    sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null));
```

---

## 11. Configuration & Options Pattern

### Configuration Sources

Load configuration from multiple sources in precedence order (highest to lowest):

1. Environment variables
2. `appsettings.{Environment}.json`
3. `appsettings.json`
4. Secret Manager (development) / Azure Key Vault / environment secrets (production)

**Never hardcode secrets, connection strings, or environment-specific URLs in source code or `appsettings.json`.**

### Options Pattern

Prefer the Options pattern over direct `IConfiguration` access inside services. It provides validation, reloading, and testability.

| Interface | Lifetime | Reflects reloads? | Use when |
|---|---|---|---|
| `IOptions<T>` | Singleton | No (snapshot at startup) | Most services — stable config |
| `IOptionsSnapshot<T>` | Scoped | Yes (per-request snapshot) | Web layer, per-request config reads |
| `IOptionsMonitor<T>` | Singleton | Yes (immediate) | Background services, change notification |

```csharp
// Options class with validation
public sealed class PaymentsApiOptions
{
    public const string SectionName = "PaymentsApi";

    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 30;
}

// Registration with eager validation on startup
builder.Services
    .AddOptions<PaymentsApiOptions>()
    .BindConfiguration(PaymentsApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

```csharp
// Usage in a service
public sealed class PaymentsApiClient(
    HttpClient client,
    IOptions<PaymentsApiOptions> options)
{
    private readonly PaymentsApiOptions _options = options.Value;
    // ...
}
```

### Feature Flags

- Use `Microsoft.FeatureManagement` for runtime feature toggles without redeploy:

```csharp
builder.Services.AddFeatureManagement();
```

```csharp
public class OrderService(IFeatureManager features)
{
    public async Task<OrderResult> PlaceOrderAsync(...)
    {
        if (await features.IsEnabledAsync("NewPricingEngine"))
            return await PlaceWithNewPricingAsync(...);
        return await PlaceWithLegacyPricingAsync(...);
    }
}
```

---

## 12. Caching, Throughput & Rate Limiting

### In-Memory Cache

- Use `IMemoryCache` for single-instance, low-latency caching. It is a singleton and does not survive deployment.
- Use `AddMemoryCache()` and inject `IMemoryCache`. Avoid static dictionaries as manual caches.
- Set expiration (absolute or sliding) on every cached entry. Unbounded caches cause memory pressure.

```csharp
var order = await _cache.GetOrCreateAsync($"order:{id}", async entry =>
{
    entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
    entry.SetSlidingExpiration(TimeSpan.FromMinutes(1));
    return await _repo.GetByIdAsync(id, ct);
});
```

### Distributed Cache

- Use `IDistributedCache` for multi-instance deployments. Back it with Redis via `StackExchange.Redis` or `Azure Cache for Redis`.
- Serialize to `byte[]` or use a typed wrapper. Prefer `System.Text.Json` serialization.
- Design keys carefully: use a consistent prefix/namespace to avoid key collisions.

### Output Caching

- Use ASP.NET Core's first-party output caching for cacheable endpoint responses:

```csharp
builder.Services.AddOutputCache(opts =>
{
    opts.AddBasePolicy(b => b.Expire(TimeSpan.FromSeconds(60)));
    opts.AddPolicy("Products", b => b.Expire(TimeSpan.FromMinutes(10)).Tag("products"));
});

app.UseOutputCache();

// Endpoint
app.MapGet("/products", GetProducts).CacheOutput("Products");
```

### Rate Limiting

- Use ASP.NET Core's built-in rate limiting middleware. Load-test your chosen policy before deploying to production:

```csharp
builder.Services.AddRateLimiter(opts =>
{
    opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.User.Identity?.Name ?? ctx.Request.Headers.Host.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));
    opts.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = 429, Title = "Too many requests." }, ct);
    };
});

app.UseRateLimiter();
```

### Response Compression

- Enable gzip/Brotli compression for JSON API responses and document downloads. Exclude already-compressed formats (images, video).

---

## 13. Memory Management & Resource Handling

### `IDisposable` and `using`

- Dispose `IDisposable` objects with `using` statements or `using` declarations. Do not rely on GC finalization for managed resources.
- Prefer `await using` for `IAsyncDisposable` objects (`DbConnection`, `HttpResponseMessage`, streams).
- Do **not** dispose `HttpClient` instances obtained from `IHttpClientFactory` — the factory manages handler lifetime.

```csharp
// Good: await using for async disposable
await using var transaction = await _db.Database.BeginTransactionAsync(ct);
```

### Strings & Buffers

- Avoid excessive `string` concatenation in loops — use `StringBuilder` or `string.Create`.
- Prefer `ReadOnlySpan<char>` / `ReadOnlyMemory<char>` for parsing and slicing without heap allocation.
- Use `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` for case-insensitive comparisons. Never use `.ToLower()` == `.ToLower()` comparisons.
- Use `SearchValues<char>` for repeated membership tests against a fixed set of characters.

```csharp
// Fast, allocation-free number extraction
private static readonly SearchValues<char> Digits = SearchValues.Create("0123456789");

public static ReadOnlySpan<char> ExtractDigits(ReadOnlySpan<char> input)
    => input.TrimStart(input[..input.IndexOfAnyExcept(Digits)]);
```

### `ArrayPool<T>` and `MemoryPool<T>`

- Use `ArrayPool<T>.Shared.Rent()` / `Return()` for short-lived large arrays to avoid LOH allocations. Always return arrays even on exceptions:

```csharp
var buffer = ArrayPool<byte>.Shared.Rent(minimumLength: 4096);
try
{
    var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 4096), ct);
    // process buffer[0..bytesRead]
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

### Object Pooling

- Use `Microsoft.Extensions.ObjectPool` for expensive-to-create objects in hot paths (e.g., `StringBuilder`, custom parsers).

### Avoiding LOH Allocations

- Arrays > 85 KB are allocated on the Large Object Heap (LOH), which is collected infrequently. Use pooling or streaming for large payloads.
- Streaming JSON with `System.Text.Json` + `Utf8JsonWriter` avoids materializing large JSON strings.

---

## 14. Security

### Secrets Management

- **Never hardcode** secrets, API keys, or connection strings.
- Use **Secret Manager** for local development (`dotnet user-secrets`).
- Use **environment variables**, **Azure Key Vault**, or **AWS Secrets Manager** in production.
- Rotate secrets and use Managed Identity / workload identity to access Key Vault without static credentials.

### Authentication & Authorization

- Use ASP.NET Core Identity with modern standards. .NET 10 ships passkey support (WebAuthn/FIDO2) in Identity.
- For API-to-API auth, use **JWT Bearer** tokens with proper validation:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = builder.Configuration["Auth:Authority"];
        opts.Audience = builder.Configuration["Auth:Audience"];
        opts.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
```

- Define **authorization policies** rather than scattering `[Authorize(Roles = "...")]` everywhere:

```csharp
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("CanManageOrders", policy =>
        policy.RequireClaim("permission", "orders:write"));
});
```

### Input Security

- Enable `[FromBody]` size limits to prevent request body DoS attacks.
- Use parameterized queries / EF Core for all DB operations — never string-concatenate SQL.
- Validate and canonicalize file paths before any file system operation to prevent path traversal.
- Set `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`, and `Strict-Transport-Security` headers at the ingress or via middleware.

### Sensitive Data

- Never log passwords, tokens, PII, or card numbers. Use log scrubbing or destructuring policies to sanitize `ILogger` output.
- Mark sensitive properties with `[JsonIgnore]` or create separate response DTOs that exclude them.
- Encrypt PII at rest and in transit. Use column-level encryption or application-level encryption for highly sensitive fields.

### HTTPS & TLS

- Always enforce HTTPS. Use `app.UseHttpsRedirection()` and HSTS:

```csharp
app.UseHsts();
app.UseHttpsRedirection();
```

---

## 15. Observability, Logging & OpenTelemetry

### Structured Logging

- Use `ILogger<T>` with structured message templates. Never use string interpolation in log messages — it prevents structured log queries and is slower.

```csharp
// Good
_logger.LogInformation("Order {OrderId} created for customer {CustomerId}", order.Id, order.CustomerId);

// Bad — prevents structured querying and loses context
_logger.LogInformation($"Order {order.Id} created for customer {order.CustomerId}");
```

- Define log scope for correlation:

```csharp
using (_logger.BeginScope(new Dictionary<string, object> { ["OrderId"] = orderId }))
{
    // all logs within this scope include OrderId
}
```

- Define log levels deliberately: `Trace`/`Debug` for diagnostic detail, `Information` for business events, `Warning` for recoverable issues, `Error` for failures requiring attention, `Critical` for application-threatening failures.
- Use `[LoggerMessage]` source-generated logging in hot paths for zero-allocation log calls:

```csharp
public static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} dispatched")]
    public static partial void OrderDispatched(ILogger logger, Guid orderId);
}
```

### OpenTelemetry

- Instrument with OpenTelemetry from day one. .NET 10 ships strong built-in OTel support.

```csharp
builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

- Add **distributed tracing** across service boundaries. Pass `traceparent` headers automatically via `HttpClient` instrumentation.

### Health Checks

- Add health checks for the app and critical dependencies (DB, cache, downstream APIs). Use `/health/live` for liveness and `/health/ready` for readiness in Kubernetes.
- Expose detailed health responses only on internal/admin endpoints, not on public-facing ones.

### Metrics

- Use `System.Diagnostics.Metrics` (built-in OTel-compatible API) for custom business metrics:

```csharp
private static readonly Meter OrderMeter = new("Orders", "1.0");
private static readonly Counter<long> OrdersPlaced = OrderMeter.CreateCounter<long>("orders.placed");

// In handler
OrdersPlaced.Add(1, new TagList { { "channel", "web" } });
```

---

## 16. Testing

### Test Project Structure

```
src/
  MyApp.Domain/
  MyApp.Application/
  MyApp.Infrastructure/
  MyApp.Api/
tests/
  MyApp.Domain.Tests/          ← unit tests (no I/O)
  MyApp.Application.Tests/     ← unit tests with mocked deps
  MyApp.Integration.Tests/     ← real DB / HTTP via TestContainers
  MyApp.Architecture.Tests/    ← dependency rule enforcement
```

### Unit Testing

- Use **xUnit v3** as the test framework. Use **NSubstitute** or **Moq** for mocking (NSubstitute has cleaner syntax).
- Follow the **Arrange–Act–Assert** pattern. Keep each test focused on one behaviour.
- Test behaviour, not implementation. Do not assert on private methods or internal state — redesign if you feel the urge.
- Use `TimeProvider.System` in production code and inject `FakeTimeProvider` (from `Microsoft.Extensions.Time.Testing`) in tests to control time.

```csharp
public sealed class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_ReturnsNewOrderId()
    {
        // Arrange
        var repo = Substitute.For<IOrderRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var sut = new CreateOrderHandler(repo, uow);
        var command = new CreateOrderCommand(Guid.NewGuid(), [new OrderLineDto(Guid.NewGuid(), 2)]);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBe(Guid.Empty);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

### Integration Testing

- Use **`WebApplicationFactory<TProgram>`** from `Microsoft.AspNetCore.Mvc.Testing` for full API integration tests.
- Use **Testcontainers** (Docker containers spun up per test run) for real database/Redis/Kafka integration tests.

```csharp
public sealed class OrdersApiTests(OrdersApiFactory factory) : IClassFixture<OrdersApiFactory>
{
    [Fact]
    public async Task PostOrder_ValidRequest_Returns201()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/orders", new
        {
            CustomerId = Guid.NewGuid(),
            Lines = new[] { new { ProductId = Guid.NewGuid(), Quantity = 1 } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}

public sealed class OrdersApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Replace real DB with Testcontainers SQL Server
        });
    }
}
```

### Architecture Tests

- Use **NetArchTest** or **ArchUnitNET** to enforce layer dependency rules as executable tests:

```csharp
[Fact]
public void Domain_Should_Not_Depend_On_Infrastructure()
{
    var result = Types.InAssembly(typeof(DomainAssemblyMarker).Assembly)
        .Should().NotHaveDependencyOn("MyApp.Infrastructure")
        .GetResult();
    result.IsSuccessful.Should().BeTrue();
}
```

### Test Quality Rules

- Use **FluentAssertions** for expressive, readable assertions.
- Keep tests deterministic — no `Thread.Sleep`, no random data without seeded generators, no dependency on external services in unit tests.
- Aim for high coverage of domain and application layers. Infrastructure and API integration coverage via integration tests.
- Do not mock what you can use directly (value objects, pure functions, in-memory implementations).

---

## 17. Background Services & Channels

### BackgroundService / WorkerService

- Use `BackgroundService` or `IHostedService` for background work. Never fire-and-forget tasks from request handlers.
- Protect the execution loop with try/catch and log all exceptions — unhandled exceptions in `ExecuteAsync` stop the hosted service.

```csharp
public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxMessageProcessor>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;  // graceful shutdown
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing outbox messages");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

### System.Threading.Channels

Use `Channel<T>` for in-process producer/consumer patterns. It is the modern, allocation-efficient replacement for `BlockingCollection<T>`:

```csharp
// Registration
builder.Services.AddSingleton(_ =>
    Channel.CreateBounded<OutboxMessage>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true
    }));

// Producer (from request handler)
await _channel.Writer.WriteAsync(message, ct);

// Consumer (BackgroundService)
await foreach (var message in _channel.Reader.ReadAllAsync(stoppingToken))
{
    await ProcessAsync(message, stoppingToken);
}
```

### Outbox Pattern

- For reliable message publishing without distributed transactions, use the **Transactional Outbox Pattern**: save events to an outbox table in the same DB transaction as the business operation, then a background worker publishes and deletes them.

### Graceful Shutdown

- Subscribe to `IHostApplicationLifetime.ApplicationStopping` or respect `CancellationToken` passed to `ExecuteAsync`.
- Set shutdown timeout with `services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30))`.

---

## 18. Coding Style & Anti-Patterns

### Code Generation Rules

When generating code for this stack:

- Generate **production-style code**, not demo code.
- Use **clear names**: prefer `customerId` over `id`, `GetOrdersByCustomerAsync` over `GetData`.
- Write **small, focused classes** with single responsibilities.
- Put **each class and interface in its own file**, named after the type it contains (e.g., `OrderService.cs`, `IOrderRepository.cs`). Never define multiple public types in the same file.
- Keep **controllers and endpoints thin** — translate HTTP to application requests only.
- Keep domain logic out of infrastructure and HTTP layers.
- Prefer **composition over inheritance**.
- Favor **immutability** for request/response contracts — use `record`, `init`, or `readonly`.
- Use **nullable reference types** correctly — never suppress with `!` without a comment.
- Add `CancellationToken` to all async public methods that may perform I/O.
- Do **not suppress** warnings without explaining why with a comment.
- Do **not introduce** unnecessary abstractions, base classes, or helper utilities.
- Use **first-party ASP.NET Core / .NET 10 features** before third-party packages.
- Add **OpenAPI metadata**, problem details, logging, health checks, and rate limiting by default.
- **Services must be stateless** — never store per-request state in instance fields on a service class. Use scoped or transient services and pass state via method arguments.
- **Controllers must not access `DbContext` directly** — all data access goes through repositories injected via abstraction interfaces.
- Use **`Guid`** for all entity identifier properties. Never use auto-incremented `int` IDs across API boundaries.
- Add **XML documentation comments** to all public classes, interfaces, and their public members:
  - `<summary>` on every public type and member.
  - `<param>` on every method parameter.
  - `<returns>` on every non-void method.
  - `<exception>` for documented exception cases.
  - Private members do not require XML docs; inline comments suffice where logic is non-obvious.

```csharp
/// <summary>Processes a new order placement request.</summary>
/// <param name="cmd">The command containing customer and line item data.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>The newly created order identifier.</returns>
/// <exception cref="ValidationException">Thrown when the command fails validation.</exception>
public async Task<Guid> Handle(CreateOrderCommand cmd, CancellationToken ct) { ... }
```

### `var` Usage Rules

| Situation | Rule |
|---|---|
| Simple value types (`string`, `int`, `bool`, `decimal`, `float`, `double`) | Use the explicit type, not `var` |
| Complex type obvious from right-hand side (`var order = new Order()`) | `var` is acceptable |
| Anonymous types | Always use `var` |
| LINQ query results | Use `var` to avoid repetitive type specifications |
| Nullable value types | Use the explicit nullable type (e.g., `int?`), not `var` |

```csharp
// Good
string name = user.FullName;
int count = order.Lines.Count;
var order = new Order(customerId);          // type obvious from right side
var grouped = orders.GroupBy(o => o.Status); // LINQ

// Bad
var name = user.FullName;   // string is not obvious without reading the property
var count = order.Lines.Count; // int should be explicit
```

### Blank Line Before `if` Statements

Always insert a blank line before an `if` statement unless it is immediately preceded by an opening brace `{`.

```csharp
// Bad
var result = repo.Find(id);
if (result is null) return NotFound();

// Good
var result = repo.Find(id);

if (result is null) return NotFound();

// Also good — immediately after opening brace, no blank line needed
{
    if (result is null) return NotFound();
}
```

### Common Anti-Patterns to Avoid

| Anti-Pattern | Problem | Correct Approach |
|---|---|---|
| `new HttpClient()` | Socket exhaustion, DNS staleness | Use `IHttpClientFactory` |
| `.Result` / `.Wait()` on `Task` | Deadlock risk, thread-pool starvation | `await` end-to-end |
| `async void` | Unobservable exceptions, no completion tracking | `async Task` everywhere |
| `Task.Run(() => syncMethod())` in ASP.NET Core | Wastes threads, no benefit | Just call directly or use async overloads |
| Returning EF entities from API | Tight coupling, cyclic refs, over-exposure | Use projection DTOs |
| Lazy loading in APIs | N+1 queries | Explicit `Include()` or projections |
| `DbContext` as Singleton | Thread-safety issues, leaked change tracking | Scoped lifetime only |
| Repository called directly from controller or service | Violates Clean Architecture layering; bypasses handler orchestration | Access repositories only from Application layer command/query handlers |
| Unit of Work committed outside a command handler | Creates hidden transaction boundaries; breaks handler isolation | Call `uow.SaveChangesAsync()` only in `*Handler` classes |
| `catch (Exception ex) { /* ignore */ }` | Silently loses errors | Log + rethrow or handle explicitly |
| String interpolation in log messages | No structured data, allocation overhead | Message templates with named holes |
| Misconfigured CORS (`AllowAnyOrigin` + `AllowCredentials`) | Security vulnerability | Narrow origin allowlist |
| Hardcoded secrets in source | Critical security risk | Secret Manager / Key Vault |
| Singleton capturing scoped dependency | Captive dependency, staleness | Inject `IServiceScopeFactory` |
| Overly broad `try/catch` | Masks root cause | Catch specific exceptions at the right level |
| LOH allocations in hot paths | GC pressure, latency spikes | `ArrayPool<T>`, streaming, `Span<T>` |
| Manual `Thread` creation | Hard to reason about, thread-pool bypass | `Task`, `Parallel.ForEachAsync`, channels |

### Project Hygiene

- Enable **nullability** and **implicit usings** in every csproj: `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
- Enable warnings as errors in CI: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Use an `EditorConfig` (`.editorconfig`) for consistent formatting across the team.
- Use `dotnet format` and/or Roslyn Analyzers in CI.
- Keep `Program.cs` clean — extract registration logic into `IServiceCollection` extension methods per feature/layer.
- Use `[assembly: InternalsVisibleTo("Tests")]` instead of making test-only types public.

---

*Reference: [Microsoft .NET documentation](https://learn.microsoft.com/dotnet), [ASP.NET Core documentation](https://learn.microsoft.com/aspnet/core), [EF Core documentation](https://learn.microsoft.com/ef/core), [Polly documentation](https://github.com/App-vNext/Polly), [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/).*
