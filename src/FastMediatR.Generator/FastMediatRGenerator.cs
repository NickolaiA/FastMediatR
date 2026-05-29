using System.Collections.Immutable;
using System.Threading;
using FastMediatR.Generator.Discovery;
using FastMediatR.Generator.Emitters;
using FastMediatR.Generator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FastMediatR.Generator;

/// <summary>
/// The FastMediatR Roslyn incremental source generator.
/// </summary>
/// <remarks>
/// <para>
/// This generator scans the consumer's compilation for all concrete implementations of
/// <c>IRequestHandler&lt;TRequest, TResponse&gt;</c> and <c>INotificationHandler&lt;TNotification&gt;</c>,
/// validates handler cardinality (FM001 / FM002), and emits:
/// <list type="bullet">
///   <item><description><c>FastMediatR.Generated.FastMediatRDispatcher</c> — a zero-reflection <c>IMediator</c> implementation that dispatches via switch expressions.</description></item>
///   <item><description><c>FastMediatR.Generated.FastMediatRGeneratedRegistration</c> — a static <c>Register(IServiceCollection)</c> method wiring every handler into DI.</description></item>
/// </list>
/// </para>
/// <para>
/// The pipeline uses <see cref="IncrementalValueProvider{TValues}"/> so the IDE only
/// re-runs expensive semantic steps when handler declarations actually change.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class FastMediatRGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ── Syntax pass ──────────────────────────────────────────────────────
        // Cheap predicate that keeps only type declarations with a base list.
        // Runs on every keystroke; must stay allocation-free.
        IncrementalValuesProvider<HandlerDiscoveryProvider.Candidate?> candidates =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => HandlerDiscoveryProvider.IsCandidateTypeDeclaration(node),
                transform: static (ctx, ct) => HandlerDiscoveryProvider.Transform(ctx, ct));

        // ── Semantic pass ─────────────────────────────────────────────────────
        // Combine all candidates + the compilation into a single DiscoveryResult.
        // Backed by EquatableArray<T> so downstream nodes cache correctly.
        IncrementalValueProvider<DiscoveryResult> discoveryResult =
            candidates
                .Collect()
                .Combine(context.CompilationProvider)
                .Select(static (pair, ct) =>
                    HandlerDiscoveryProvider.Aggregate(pair.Left, pair.Right, ct));

        // ── Emitters ─────────────────────────────────────────────────────────
        context.RegisterSourceOutput(discoveryResult, static (spc, result) =>
        {
            // Short-circuit when FastMediatR.Core is not referenced.
            if (result.RequestHandlers.Count == 0
                && result.NotificationHandlers.Count == 0
                && result.Requests.Count == 0)
            {
                return;
            }

            DispatcherEmitter.Emit(spc, result);
            RegistrationEmitter.Emit(spc, result);
        });
    }
}
