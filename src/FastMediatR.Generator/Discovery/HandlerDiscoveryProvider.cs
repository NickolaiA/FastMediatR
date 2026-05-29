using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using FastMediatR.Generator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FastMediatR.Generator.Discovery;

/// <summary>
/// Builds the incremental-generator data pipeline that discovers all FastMediatR handler,
/// request, and notification types in the consumer's compilation.
/// </summary>
/// <remarks>
/// The pipeline:
/// <list type="number">
///   <item><description>Filters all <see cref="TypeDeclarationSyntax"/> nodes to concrete (non-abstract) classes/records/structs.</description></item>
///   <item><description>Resolves each candidate's <see cref="INamedTypeSymbol"/> and inspects its implemented interfaces for the four FastMediatR root interfaces.</description></item>
///   <item><description>Materialises the results as <see cref="EquatableArray{T}"/> wrappers so downstream emitter nodes are correctly cached.</description></item>
/// </list>
/// All discovered types are returned in fully-qualified <c>global::Namespace.Type</c> form so
/// the emitter can paste them into generated source without further symbol resolution.
/// </remarks>
internal static class HandlerDiscoveryProvider
{
    /// <summary>Fully-qualified metadata name of <c>FastMediatR.IRequest&lt;TResponse&gt;</c>.</summary>
    public const string IRequestT_MetadataName = "FastMediatR.IRequest`1";

    /// <summary>Fully-qualified metadata name of <c>FastMediatR.IRequestHandler&lt;TRequest, TResponse&gt;</c>.</summary>
    public const string IRequestHandlerT2_MetadataName = "FastMediatR.IRequestHandler`2";

    /// <summary>Fully-qualified metadata name of <c>FastMediatR.INotification</c>.</summary>
    public const string INotification_MetadataName = "FastMediatR.INotification";

    /// <summary>Fully-qualified metadata name of <c>FastMediatR.INotificationHandler&lt;TNotification&gt;</c>.</summary>
    public const string INotificationHandlerT_MetadataName = "FastMediatR.INotificationHandler`1";

    /// <summary>Fully-qualified metadata name of <c>FastMediatR.HandlerLifetimeAttribute</c>.</summary>
    public const string HandlerLifetimeAttribute_MetadataName = "FastMediatR.HandlerLifetimeAttribute";

    /// <summary>SymbolDisplayFormat used to print fully-qualified type names with a <c>global::</c> prefix.</summary>
    public static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    /// <summary>Per-type candidate discovered during the syntax pass.</summary>
    /// <param name="Symbol">Resolved type symbol.</param>
    /// <param name="Location">Declaration location for diagnostic reporting.</param>
    internal readonly record struct Candidate(INamedTypeSymbol Symbol, Location Location);

    /// <summary>
    /// Syntax-filter predicate: keeps type declarations that could plausibly be FastMediatR
    /// participants (any concrete class, record, or struct with a base list).
    /// </summary>
    /// <param name="node">Candidate syntax node.</param>
    /// <returns><see langword="true"/> when the node merits semantic inspection.</returns>
    public static bool IsCandidateTypeDeclaration(SyntaxNode node)
    {
        // Interfaces cannot be FastMediatR participants — skip them cheaply in the syntax pass
        // to avoid spurious FM001 diagnostics for marker interfaces such as
        // `interface IAdminCommand : IRequest<bool>`.
        if (node is InterfaceDeclarationSyntax)
        {
            return false;
        }

        if (node is not TypeDeclarationSyntax tds)
        {
            return false;
        }

        if (tds.BaseList is null || tds.BaseList.Types.Count == 0)
        {
            return false;
        }

        foreach (SyntaxToken modifier in tds.Modifiers)
        {
            if (modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AbstractKeyword)
                || modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Semantic transform: resolves the candidate to a <see cref="Candidate"/> tuple, returning
    /// <see langword="null"/> when the symbol fails to bind (e.g. the syntax is in a broken state).
    /// </summary>
    /// <param name="context">Generator syntax context.</param>
    /// <param name="cancellationToken">Cancellation token honoured by the host.</param>
    /// <returns>The resolved candidate or <see langword="null"/> when it cannot be bound.</returns>
    public static Candidate? Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var tds = (TypeDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(tds, cancellationToken) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        return new Candidate(symbol, tds.Identifier.GetLocation());
    }

    /// <summary>
    /// Inspects every candidate and partitions it into request handlers, notification handlers,
    /// concrete request types, and concrete notification types.
    /// </summary>
    /// <param name="candidates">Discovered candidates from the syntax pass.</param>
    /// <param name="compilation">The current compilation (for resolving FastMediatR symbols).</param>
    /// <param name="cancellationToken">Cancellation token honoured by the host.</param>
    /// <returns>An aggregated <see cref="DiscoveryResult"/>.</returns>
    public static DiscoveryResult Aggregate(
        ImmutableArray<Candidate?> candidates,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        INamedTypeSymbol? iRequestT = compilation.GetTypeByMetadataName(IRequestT_MetadataName);
        INamedTypeSymbol? iRequestHandler = compilation.GetTypeByMetadataName(IRequestHandlerT2_MetadataName);
        INamedTypeSymbol? iNotification = compilation.GetTypeByMetadataName(INotification_MetadataName);
        INamedTypeSymbol? iNotificationHandler = compilation.GetTypeByMetadataName(INotificationHandlerT_MetadataName);
        INamedTypeSymbol? handlerLifetimeAttr = compilation.GetTypeByMetadataName(HandlerLifetimeAttribute_MetadataName);

        // If FastMediatR.Core isn't referenced at all, short-circuit with an empty result so the
        // generator emits no source and produces no diagnostics.
        if (iRequestT is null || iRequestHandler is null || iNotificationHandler is null)
        {
            return new DiscoveryResult(
                EquatableArray<RequestHandlerInfo>.Empty,
                EquatableArray<NotificationHandlerInfo>.Empty,
                EquatableArray<RequestTypeInfo>.Empty);
        }

        ImmutableArray<RequestHandlerInfo>.Builder requestHandlers = ImmutableArray.CreateBuilder<RequestHandlerInfo>();
        ImmutableArray<NotificationHandlerInfo>.Builder notificationHandlers = ImmutableArray.CreateBuilder<NotificationHandlerInfo>();
        ImmutableArray<RequestTypeInfo>.Builder requests = ImmutableArray.CreateBuilder<RequestTypeInfo>();

        foreach (Candidate? maybe in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (maybe is not { } candidate)
            {
                continue;
            }

            INamedTypeSymbol symbol = candidate.Symbol;

            // Semantic-pass guard: skip interfaces and abstract types even if they slipped            // through the syntax filter (e.g. when the syntax tree is partially broken).
            if (symbol.TypeKind == TypeKind.Interface || symbol.IsAbstract)
            {
                continue;
            }
            var location = LocationInfo.From(candidate.Location);

            // Request types — concrete IRequest<T> implementations
            foreach (INamedTypeSymbol iface in symbol.AllInterfaces)
            {
                if (iface.IsGenericType && SymbolEqualityComparer.Default.Equals(iface.ConstructedFrom, iRequestT))
                {
                    ITypeSymbol response = iface.TypeArguments[0];
                    requests.Add(new RequestTypeInfo(
                        symbol.ToDisplayString(FullyQualifiedFormat),
                        response.ToDisplayString(FullyQualifiedFormat),
                        symbol.IsValueType,
                        location));
                    break;
                }
            }

            // Request handlers — concrete IRequestHandler<TRequest, TResponse> implementations
            foreach (INamedTypeSymbol iface in symbol.AllInterfaces)
            {
                if (iface.IsGenericType && SymbolEqualityComparer.Default.Equals(iface.ConstructedFrom, iRequestHandler))
                {
                    ITypeSymbol request = iface.TypeArguments[0];
                    ITypeSymbol response = iface.TypeArguments[1];
                    requestHandlers.Add(new RequestHandlerInfo(
                        symbol.ToDisplayString(FullyQualifiedFormat),
                        request.ToDisplayString(FullyQualifiedFormat),
                        response.ToDisplayString(FullyQualifiedFormat),
                        request.IsValueType,
                        ReadLifetimeOverride(symbol, handlerLifetimeAttr),
                        location));
                }
            }

            // Notification handlers — concrete INotificationHandler<TNotification> implementations
            foreach (INamedTypeSymbol iface in symbol.AllInterfaces)
            {
                if (iface.IsGenericType && SymbolEqualityComparer.Default.Equals(iface.ConstructedFrom, iNotificationHandler))
                {
                    ITypeSymbol notification = iface.TypeArguments[0];
                    notificationHandlers.Add(new NotificationHandlerInfo(
                        symbol.ToDisplayString(FullyQualifiedFormat),
                        notification.ToDisplayString(FullyQualifiedFormat),
                        ReadLifetimeOverride(symbol, handlerLifetimeAttr),
                        location));
                }
            }
        }

        return new DiscoveryResult(
            requestHandlers.ToImmutable().ToEquatableArray(),
            notificationHandlers.ToImmutable().ToEquatableArray(),
            requests.ToImmutable().ToEquatableArray());
    }

    /// <summary>
    /// Reads the <c>[HandlerLifetime]</c> attribute from a handler class and translates it
    /// into the generator's <see cref="HandlerLifetime"/> enum.
    /// </summary>
    /// <param name="symbol">Handler symbol.</param>
    /// <param name="attributeSymbol">The resolved <c>FastMediatR.HandlerLifetimeAttribute</c> symbol; may be <see langword="null"/>.</param>
    /// <returns>The override lifetime or <see langword="null"/> when the attribute is absent.</returns>
    private static HandlerLifetime? ReadLifetimeOverride(INamedTypeSymbol symbol, INamedTypeSymbol? attributeSymbol)
    {
        if (attributeSymbol is null)
        {
            return null;
        }

        AttributeData? attr = symbol.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeSymbol));

        if (attr is null || attr.ConstructorArguments.Length == 0)
        {
            return null;
        }

        TypedConstant value = attr.ConstructorArguments[0];
        if (value.Value is not int intValue)
        {
            return null;
        }

        // Mirrors Microsoft.Extensions.DependencyInjection.ServiceLifetime: Singleton=0, Scoped=1, Transient=2.
        return intValue switch
        {
            0 => HandlerLifetime.Singleton,
            1 => HandlerLifetime.Scoped,
            2 => HandlerLifetime.Transient,
            _ => null,
        };
    }
}
