using System.Collections.Immutable;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FastMediatR.Generator.Tests;

/// <summary>
/// End-to-end snapshot tests for <see cref="FastMediatRGenerator"/>.
/// Each test compiles a minimal input compilation, runs the generator, and
/// asserts that the expected source files and diagnostics are produced.
/// </summary>
public sealed class FastMediatRGeneratorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Roslyn compilation containing the given source text(s) plus the
    /// FastMediatR.Core assembly and common BCL references, then runs the generator
    /// and returns the output sources and diagnostics.
    /// </summary>
    /// <param name="sources">Additional C# source texts added to the compilation.</param>
    /// <returns>
    /// A tuple of (<see cref="ImmutableArray{T}"/> generatedSources,
    /// <see cref="ImmutableArray{T}"/> diagnostics).
    /// </returns>
    private static async Task<(ImmutableArray<(string HintName, string Source)> Sources, ImmutableArray<Diagnostic> Diagnostics)>
        RunGeneratorAsync(params string[] sources)
    {
        var driver = CSharpGeneratorDriver.Create(new FastMediatRGenerator());

        var compilation = BuildCompilation(sources);

        GeneratorDriverRunResult result = await Task.Run(() =>
        {
            GeneratorDriver run = driver.RunGenerators(compilation);
            return run.GetRunResult();
        }).ConfigureAwait(false);

        var generatedSources = result.GeneratedTrees
            .Select(t => (HintName: System.IO.Path.GetFileName(t.FilePath), Source: t.GetText().ToString()))
            .ToImmutableArray();

        var allDiagnostics = result.Diagnostics;

        return (generatedSources, allDiagnostics);
    }

    /// <summary>Builds a minimal Roslyn compilation with FastMediatR.Core on the reference list.</summary>
    private static CSharpCompilation BuildCompilation(string[] sources)
    {
        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s))
            .ToArray();

        // Core assembly and all BCL refs needed by FastMediatR.Core consumers.
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastMediatR.IMediator).Assembly.Location),
            // System.Runtime (needed by .NET 10 TFM references)
            MetadataReference.CreateFromFile(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
                + "System.Runtime.dll"),
        };

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a single IRequest and matching handler are present, the generator should
    /// produce exactly two files (dispatcher + registration) with no diagnostics.
    /// </summary>
    [Fact]
    public async Task SingleHandler_EmitsTwoFilesWithNoDiagnostics()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record PingCommand(string Message) : IRequest<string>;

            public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken ct)
                    => Task.FromResult(request.Message);
            }
            """;

        var (sources, diagnostics) = await RunGeneratorAsync(source);

        diagnostics.Should().BeEmpty();
        sources.Should().HaveCount(2);
        sources.Select(s => s.HintName).Should().Contain("FastMediatRDispatcher.g.cs");
        sources.Select(s => s.HintName).Should().Contain("FastMediatRGeneratedRegistration.g.cs");
    }

    /// <summary>
    /// The dispatcher should contain a switch arm for the request type.
    /// </summary>
    [Fact]
    public async Task SingleHandler_DispatcherContainsSwitchArm()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record PingCommand(string Message) : IRequest<string>;

            public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken ct)
                    => Task.FromResult(request.Message);
            }
            """;

        var (sources, _) = await RunGeneratorAsync(source);

        string dispatcher = sources.Single(s => s.HintName == "FastMediatRDispatcher.g.cs").Source;
        dispatcher.Should().Contain("PingCommandHandler");
        dispatcher.Should().Contain("PingCommand");
    }

    /// <summary>
    /// The registration should contain TryAddScoped calls for the handler.
    /// </summary>
    [Fact]
    public async Task SingleHandler_RegistrationContainsTryAddScoped()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record PingCommand(string Message) : IRequest<string>;

            public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken ct)
                    => Task.FromResult(request.Message);
            }
            """;

        var (sources, _) = await RunGeneratorAsync(source);

        string registration = sources.Single(s => s.HintName == "FastMediatRGeneratedRegistration.g.cs").Source;
        registration.Should().Contain("TryAddScoped<global::PingCommandHandler>");
    }

    /// <summary>
    /// FM002: two handlers for the same request type should produce an FM002 error diagnostic.
    /// </summary>
    [Fact]
    public async Task DuplicateHandlers_EmitsFM002Diagnostic()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record PingCommand(string Message) : IRequest<string>;

            public sealed class PingCommandHandlerA : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken ct)
                    => Task.FromResult(request.Message);
            }

            public sealed class PingCommandHandlerB : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken ct)
                    => Task.FromResult(request.Message);
            }
            """;

        var (_, diagnostics) = await RunGeneratorAsync(source);

        diagnostics.Should().Contain(d => d.Id == "FM002");
    }

    /// <summary>
    /// FM001: a request type with no handler should produce an FM001 error diagnostic.
    /// </summary>
    [Fact]
    public async Task NoHandler_EmitsFM001Diagnostic()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            // No handler for PingCommand — FM001 should fire.
            public sealed record PingCommand(string Message) : IRequest<string>;
            """;

        var (_, diagnostics) = await RunGeneratorAsync(source);

        diagnostics.Should().Contain(d => d.Id == "FM001");
    }

    /// <summary>
    /// Notification handlers: multiple handlers for the same notification type should
    /// all appear in the generated Publish switch arm, and no diagnostics should be emitted.
    /// </summary>
    [Fact]
    public async Task MultipleNotificationHandlers_AllAppearsInPublish()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record UserCreatedEvent(Guid UserId) : INotification;

            public sealed class SendEmailHandler : INotificationHandler<UserCreatedEvent>
            {
                public Task Handle(UserCreatedEvent n, CancellationToken ct) => Task.CompletedTask;
            }

            public sealed class AuditHandler : INotificationHandler<UserCreatedEvent>
            {
                public Task Handle(UserCreatedEvent n, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var (sources, diagnostics) = await RunGeneratorAsync(source);

        // Notifications with no matching IRequest<T> types should produce no FM001/FM002.
        diagnostics.Should().NotContain(d => d.Id == "FM001" || d.Id == "FM002");

        string dispatcher = sources.Single(s => s.HintName == "FastMediatRDispatcher.g.cs").Source;
        dispatcher.Should().Contain("SendEmailHandler");
        dispatcher.Should().Contain("AuditHandler");
    }

    /// <summary>
    /// Void command (IRequest without type parameter, i.e. IRequest : IRequest&lt;Unit&gt;):
    /// handler should be discovered correctly.
    /// </summary>
    [Fact]
    public async Task VoidCommand_HandlerDiscoveredCorrectly()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record DeleteUserCommand(Guid UserId) : IRequest;

            public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
            {
                public Task<Unit> Handle(DeleteUserCommand request, CancellationToken ct)
                    => Unit.Task;
            }
            """;

        var (sources, diagnostics) = await RunGeneratorAsync(source);

        diagnostics.Should().BeEmpty();

        string dispatcher = sources.Single(s => s.HintName == "FastMediatRDispatcher.g.cs").Source;
        dispatcher.Should().Contain("DeleteUserCommandHandler");
    }

    /// <summary>
    /// When the compilation has no FastMediatR handlers at all (only unrelated types),
    /// the generator should produce no files.
    /// </summary>
    [Fact]
    public async Task NoHandlers_NoOutputProduced()
    {
        string source = """
            public sealed class UnrelatedClass { }
            """;

        var (sources, diagnostics) = await RunGeneratorAsync(source);

        sources.Should().BeEmpty();
        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// The dispatcher's Send method must call <c>ExecutePipeline</c> — not the handler directly —
    /// so that all registered <c>IPipelineBehavior</c> instances are executed.
    /// </summary>
    [Fact]
    public async Task SingleHandler_DispatcherCallsExecutePipelineNotHandlerDirectly()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record PingCommand(string Message) : IRequest<string>;

            public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken ct)
                    => Task.FromResult(request.Message);
            }
            """;

        var (sources, _) = await RunGeneratorAsync(source);

        string dispatcher = sources.Single(s => s.HintName == "FastMediatRDispatcher.g.cs").Source;

        // Must route through ExecutePipeline so pipeline behaviors are invoked.
        dispatcher.Should().Contain("ExecutePipeline<");
        // The switch arm must pass the handler as a lambda delegate to ExecutePipeline,
        // NOT call Handle directly as the switch arm's return value.
        // Direct call (wrong): GetRequiredService<PingCommandHandler>().Handle(r, cancellationToken)
        // Pipeline call (correct): ExecutePipeline<...>(r, ct => GetRequiredService<PingCommandHandler>().Handle(r, ct), cancellationToken)
        dispatcher.Should().Contain("ct => _services.GetRequiredService<global::PingCommandHandler>().Handle(r, ct)");
    }

    /// <summary>
    /// The <c>ExecutePipeline</c> helper method must be present in the generated dispatcher and
    /// must resolve <c>IPipelineBehavior</c> instances from DI, enabling open-generic
    /// behavior registrations (Validation, Logging, Performance) to be resolved and chained.
    /// </summary>
    [Fact]
    public async Task SingleHandler_DispatcherContainsPipelineHelperMethod()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record PingCommand(string Message) : IRequest<string>;

            public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken ct)
                    => Task.FromResult(request.Message);
            }
            """;

        var (sources, _) = await RunGeneratorAsync(source);

        string dispatcher = sources.Single(s => s.HintName == "FastMediatRDispatcher.g.cs").Source;

        // ExecutePipeline helper must be emitted.
        dispatcher.Should().Contain("private Task<TResponse> ExecutePipeline<TRequest, TResponse>(");
        // Must resolve pipeline behaviors from DI.
        dispatcher.Should().Contain("GetServices<global::FastMediatR.IPipelineBehavior<TRequest, TResponse>>()");
        // Must short-circuit to the handler when no behaviors are registered (zero closure allocations).
        dispatcher.Should().Contain("if (count == 0)");
        // Must build the delegation chain (iterates behaviors in reverse so first-registered is outermost).
        dispatcher.Should().Contain("for (int i = count - 1; i >= 0; i--)");
    }

    /// <summary>
    /// The generated <c>Publish</c> implementation must delegate to <c>INotificationPublisher</c>
    /// rather than calling each notification handler directly, so the publisher strategy
    /// (parallel or sequential) is respected.
    /// </summary>
    [Fact]
    public async Task NotificationHandlers_PublishDelegatesToINotificationPublisher()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            public sealed record UserCreatedEvent(Guid UserId) : INotification;

            public sealed class SendEmailHandler : INotificationHandler<UserCreatedEvent>
            {
                public Task Handle(UserCreatedEvent n, CancellationToken ct) => Task.CompletedTask;
            }

            public sealed class AuditHandler : INotificationHandler<UserCreatedEvent>
            {
                public Task Handle(UserCreatedEvent n, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var (sources, _) = await RunGeneratorAsync(source);

        string dispatcher = sources.Single(s => s.HintName == "FastMediatRDispatcher.g.cs").Source;

        // Must resolve INotificationPublisher from DI.
        dispatcher.Should().Contain("GetRequiredService<global::FastMediatR.INotificationPublisher>()");
        // Must build a NotificationHandlerExecutor array.
        dispatcher.Should().Contain("global::FastMediatR.NotificationHandlerExecutor[]");
        // Must delegate to publisher.Publish.
        dispatcher.Should().Contain("__publisher.Publish(__executors, n, cancellationToken)");
        // Must NOT call handlers directly (no inline .Handle(n, ...) calls).
        dispatcher.Should().NotContain("SendEmailHandler>().Handle(");
        dispatcher.Should().NotContain("AuditHandler>().Handle(");
    }

    /// <summary>
    /// A marker interface that extends IRequest&lt;T&gt; (a common DDD pattern) must NOT
    /// trigger FM001. Only concrete types that have no handler should trigger FM001.
    /// </summary>
    [Fact]
    public async Task MarkerInterface_DoesNotTriggerFM001()
    {
        string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FastMediatR;

            // Marker interface — must not be treated as a concrete request needing a handler.
            public interface IAdminCommand : IRequest<bool> { }

            public sealed record DeleteUserCommand(Guid Id) : IAdminCommand;

            public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, bool>
            {
                public Task<bool> Handle(DeleteUserCommand request, CancellationToken ct)
                    => Task.FromResult(true);
            }
            """;

        var (sources, diagnostics) = await RunGeneratorAsync(source);

        // FM001 must not fire for IAdminCommand.
        diagnostics.Should().NotContain(d => d.Id == "FM001");
        sources.Should().HaveCount(2);
    }
}
