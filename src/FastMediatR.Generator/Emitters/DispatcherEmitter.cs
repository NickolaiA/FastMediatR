using System.Collections.Generic;
using System.Linq;
using System.Text;
using FastMediatR.Generator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace FastMediatR.Generator.Emitters;

/// <summary>
/// Emits <c>FastMediatR.Generated.FastMediatRDispatcher</c> — a source-generated
/// <c>global::FastMediatR.IMediator</c> implementation that dispatches to
/// concrete handler types via switch expressions, eliminating all reflection.
/// </summary>
internal static class DispatcherEmitter
{
    /// <summary>Generated file hint name (used by <c>AddSource</c>).</summary>
    public const string HintName = "FastMediatRDispatcher.g.cs";

    /// <summary>
    /// Generates the full source text for the dispatcher class.
    /// </summary>
    /// <param name="context">Source production context; used to report diagnostics and add source.</param>
    /// <param name="discovery">Aggregated discovery result containing all handler and request information.</param>
    public static void Emit(SourceProductionContext context, DiscoveryResult discovery)
    {
        // Validate: each request type must have exactly one handler (FM001 / FM002).
        bool hasErrors = ValidateRequestHandlerCardinality(context, discovery);

        if (hasErrors)
        {
            // Emit a stub so downstream tooling doesn't report missing type errors on top of FM001/FM002.
            context.AddSource(HintName, SourceText.From(BuildStub(), Encoding.UTF8));
            return;
        }

        context.AddSource(HintName, SourceText.From(BuildDispatcher(discovery), Encoding.UTF8));
    }

    /// <summary>
    /// Validates that every discovered request type has exactly one handler. Reports FM001 and FM002
    /// diagnostics via the production context.
    /// </summary>
    /// <param name="context">Source production context for diagnostic reporting.</param>
    /// <param name="discovery">Aggregated discovery result.</param>
    /// <returns><see langword="true"/> when at least one FM001 or FM002 diagnostic was reported.</returns>
    private static bool ValidateRequestHandlerCardinality(SourceProductionContext context, DiscoveryResult discovery)
    {
        bool hasErrors = false;

        // Build a lookup: request FQN → list of handlers
        var handlersByRequest = new Dictionary<string, List<RequestHandlerInfo>>();

        foreach (RequestHandlerInfo handler in discovery.RequestHandlers)
        {
            if (!handlersByRequest.TryGetValue(handler.RequestFullyQualifiedName, out List<RequestHandlerInfo>? list))
            {
                list = new List<RequestHandlerInfo>();
                handlersByRequest[handler.RequestFullyQualifiedName] = list;
            }

            list.Add(handler);
        }

        // FM002 — duplicate handlers
        foreach (KeyValuePair<string, List<RequestHandlerInfo>> entry in handlersByRequest)
        {
            if (entry.Value.Count <= 1)
            {
                continue;
            }

            string names = string.Join(", ", entry.Value.Select(h => h.HandlerFullyQualifiedName));

            // Report on every duplicate handler's location
            foreach (RequestHandlerInfo handler in entry.Value)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.FM002_DuplicateHandler,
                    handler.HandlerLocation.ToLocation(),
                    entry.Value[0].RequestFullyQualifiedName,
                    entry.Value[0].ResponseFullyQualifiedName,
                    names));
            }

            hasErrors = true;
        }

        // FM001 — request types with no handler
        foreach (RequestTypeInfo request in discovery.Requests)
        {
            if (handlersByRequest.ContainsKey(request.RequestFullyQualifiedName))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.FM001_NoHandler,
                request.DeclarationLocation.ToLocation(),
                request.RequestFullyQualifiedName,
                request.ResponseFullyQualifiedName));

            hasErrors = true;
        }

        // FM003 — non-default lifetime info (notification handlers too)
        foreach (RequestHandlerInfo handler in discovery.RequestHandlers)
        {
            if (handler.LifetimeOverride is { } lifetime && lifetime != HandlerLifetime.Scoped)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.FM003_NonDefaultLifetime,
                    handler.HandlerLocation.ToLocation(),
                    handler.HandlerFullyQualifiedName,
                    lifetime.ToString()));
            }
        }

        foreach (NotificationHandlerInfo handler in discovery.NotificationHandlers)
        {
            if (handler.LifetimeOverride is { } lifetime && lifetime != HandlerLifetime.Scoped)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.FM003_NonDefaultLifetime,
                    handler.HandlerLocation.ToLocation(),
                    handler.HandlerFullyQualifiedName,
                    lifetime.ToString()));
            }
        }

        return hasErrors;
    }

    /// <summary>Builds the full dispatcher source text.</summary>
    /// <param name="discovery">Aggregated discovery result.</param>
    /// <returns>The generated C# source code as a string.</returns>
    private static string BuildDispatcher(DiscoveryResult discovery)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// FastMediatR source generator — dispatcher");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace FastMediatR.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Source-generated <see cref=\"global::FastMediatR.IMediator\"/> implementation.</summary>");
        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"FastMediatR.Generator\", \"1.0.0\")]");
        sb.AppendLine("internal sealed class FastMediatRDispatcher : global::FastMediatR.IMediator");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IServiceProvider _services;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Initialises the dispatcher with the DI service provider.</summary>");
        sb.AppendLine("    /// <param name=\"services\">The service provider used to resolve handler instances.</param>");
        sb.AppendLine("    public FastMediatRDispatcher(IServiceProvider services)");
        sb.AppendLine("    {");
        sb.AppendLine("        _services = services ?? throw new ArgumentNullException(nameof(services));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ISender.Send<TResponse>
        EmitSendGeneric(sb, discovery);

        // ISender.Send(object)
        EmitSendObject(sb, discovery);

        // IPublisher.Publish
        EmitPublish(sb, discovery);

        // Private pipeline execution helper
        EmitPipelineHelper(sb);

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>Emits the <c>ISender.Send&lt;TResponse&gt;</c> implementation.</summary>
    private static void EmitSendGeneric(StringBuilder sb, DiscoveryResult discovery)
    {
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public Task<TResponse> Send<TResponse>(global::FastMediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");

        if (discovery.RequestHandlers.Count == 0)
        {
            sb.AppendLine("        throw new InvalidOperationException(\"No request handlers are registered.\");");
        }
        else
        {
            sb.AppendLine("        ArgumentNullException.ThrowIfNull(request);");
            sb.AppendLine("        return request switch");
            sb.AppendLine("        {");

            foreach (RequestHandlerInfo handler in discovery.RequestHandlers)
            {
                sb.AppendLine($"            {handler.RequestFullyQualifiedName} r => (Task<TResponse>)(object)ExecutePipeline<{handler.RequestFullyQualifiedName}, {handler.ResponseFullyQualifiedName}>(r, ct => _services.GetRequiredService<{handler.HandlerFullyQualifiedName}>().Handle(r, ct), cancellationToken),");
            }

            sb.AppendLine("            _ => throw new InvalidOperationException($\"No handler registered for request type '{request.GetType().FullName}'.\"),");
            sb.AppendLine("        };");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>Emits the <c>ISender.Send(object)</c> implementation.</summary>
    private static void EmitSendObject(StringBuilder sb, DiscoveryResult discovery)
    {
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(request);");

        if (discovery.RequestHandlers.Count == 0)
        {
            sb.AppendLine("        throw new InvalidOperationException(\"No request handlers are registered.\");");
        }
        else
        {
            sb.AppendLine("        return request switch");
            sb.AppendLine("        {");

            foreach (RequestHandlerInfo handler in discovery.RequestHandlers)
            {
                sb.AppendLine($"            {handler.RequestFullyQualifiedName} r => (object?)await ExecutePipeline<{handler.RequestFullyQualifiedName}, {handler.ResponseFullyQualifiedName}>(r, ct => _services.GetRequiredService<{handler.HandlerFullyQualifiedName}>().Handle(r, ct), cancellationToken).ConfigureAwait(false),");
            }

            sb.AppendLine("            _ => throw new InvalidOperationException($\"No handler registered for request type '{request.GetType().FullName}'.\"),");
            sb.AppendLine("        };");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>Emits the <c>IPublisher.Publish</c> implementation.</summary>
    private static void EmitPublish(StringBuilder sb, DiscoveryResult discovery)
    {
        // Group notification handlers by notification type
        var byNotification = new Dictionary<string, List<NotificationHandlerInfo>>();

        foreach (NotificationHandlerInfo handler in discovery.NotificationHandlers)
        {
            if (!byNotification.TryGetValue(handler.NotificationFullyQualifiedName, out List<NotificationHandlerInfo>? list))
            {
                list = new List<NotificationHandlerInfo>();
                byNotification[handler.NotificationFullyQualifiedName] = list;
            }

            list.Add(handler);
        }

        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public async Task Publish(global::FastMediatR.INotification notification, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(notification);");

        if (byNotification.Count == 0)
        {
            sb.AppendLine("        // No notification handlers registered.");
        }
        else
        {
            sb.AppendLine("        switch (notification)");
            sb.AppendLine("        {");

            foreach (KeyValuePair<string, List<NotificationHandlerInfo>> entry in byNotification)
            {
                sb.AppendLine($"            case {entry.Key} n:");
                sb.AppendLine("            {");
                sb.AppendLine("                var __publisher = _services.GetRequiredService<global::FastMediatR.INotificationPublisher>();");

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    sb.AppendLine($"                var __h{i} = _services.GetRequiredService<{entry.Value[i].HandlerFullyQualifiedName}>();");
                }

                sb.AppendLine("                var __executors = new global::FastMediatR.NotificationHandlerExecutor[]");
                sb.AppendLine("                {");

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    sb.AppendLine($"                    new(__h{i}, (notif, ct) => __h{i}.Handle(({entry.Key})notif, ct)),");
                }

                sb.AppendLine("                };");
                sb.AppendLine("                await __publisher.Publish(__executors, n, cancellationToken).ConfigureAwait(false);");
                sb.AppendLine("                break;");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
    }

    /// <summary>
    /// Emits the private <c>ExecutePipeline&lt;TRequest, TResponse&gt;</c> helper method that resolves
    /// all registered <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c> instances from DI, chains them
    /// around the handler delegate (first-registered = outermost), and executes the resulting pipeline.
    /// </summary>
    private static void EmitPipelineHelper(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Resolves all registered <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c> from DI,");
        sb.AppendLine("    /// chains them around <paramref name=\"handler\"/> (first-registered = outermost),");
        sb.AppendLine("    /// and executes the resulting pipeline.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private Task<TResponse> ExecutePipeline<TRequest, TResponse>(");
        sb.AppendLine("        TRequest request,");
        sb.AppendLine("        global::FastMediatR.RequestHandlerDelegate<TResponse> handler,");
        sb.AppendLine("        CancellationToken cancellationToken)");
        sb.AppendLine("        where TRequest : global::FastMediatR.IRequest<TResponse>");
        sb.AppendLine("    {");
        sb.AppendLine("        // GetServices already materialises the registrations; avoid an extra ToArray()");
        sb.AppendLine("        // allocation when the container hands back an indexable collection (the common case).");
        sb.AppendLine("        global::System.Collections.Generic.IReadOnlyList<global::FastMediatR.IPipelineBehavior<TRequest, TResponse>> behaviors =");
        sb.AppendLine("            _services.GetServices<global::FastMediatR.IPipelineBehavior<TRequest, TResponse>>()");
        sb.AppendLine("                as global::System.Collections.Generic.IReadOnlyList<global::FastMediatR.IPipelineBehavior<TRequest, TResponse>>");
        sb.AppendLine("            ?? _services.GetServices<global::FastMediatR.IPipelineBehavior<TRequest, TResponse>>().ToArray();");
        sb.AppendLine("        int count = behaviors.Count;");
        sb.AppendLine("        // Fast path: no behaviors registered — invoke the handler directly with zero closure allocations.");
        sb.AppendLine("        if (count == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return handler(cancellationToken);");
        sb.AppendLine("        }");
        sb.AppendLine("        global::FastMediatR.RequestHandlerDelegate<TResponse> pipeline = handler;");
        sb.AppendLine("        for (int i = count - 1; i >= 0; i--)");
        sb.AppendLine("        {");
        sb.AppendLine("            var next = pipeline;");
        sb.AppendLine("            var b = behaviors[i];");
        sb.AppendLine("            pipeline = ct => b.Handle(request, next, ct);");
        sb.AppendLine("        }");
        sb.AppendLine("        return pipeline(cancellationToken);");
        sb.AppendLine("    }");
    }

    /// <summary>Emits a stub dispatcher when there are FM001/FM002 errors to avoid cascading type-missing errors.</summary>
    /// <returns>Stub source code.</returns>
    private static string BuildStub()
    {
        return """
// <auto-generated/>
// FastMediatR source generator — dispatcher stub (emitted due to FM001/FM002 errors)
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FastMediatR.Generated;

[global::System.CodeDom.Compiler.GeneratedCode("FastMediatR.Generator", "1.0.0")]
internal sealed class FastMediatRDispatcher : global::FastMediatR.IMediator
{
    public Task<TResponse> Send<TResponse>(global::FastMediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("FastMediatR dispatcher is not available because handler registration errors (FM001/FM002) were detected. See build output for details.");

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("FastMediatR dispatcher is not available because handler registration errors (FM001/FM002) were detected. See build output for details.");

    public Task Publish(global::FastMediatR.INotification notification, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("FastMediatR dispatcher is not available because handler registration errors (FM001/FM002) were detected. See build output for details.");
}
""";
    }
}
