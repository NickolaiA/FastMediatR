using Microsoft.CodeAnalysis;

namespace FastMediatR.Generator;

/// <summary>
/// All diagnostics emitted by the FastMediatR source generator.
/// </summary>
/// <remarks>
/// IDs are reserved in the <c>FM0xx</c> namespace. Help link URLs point to the
/// usage guide §9 (source-generator diagnostics) once published.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "FastMediatR";
    private const string HelpRoot = "https://github.com/NickolaiA/FastMediatR/blob/main/docs/260524_1300-fastmediatr-usage-guide.md";

    /// <summary>FM001 — no handler is registered for the request type.</summary>
    public static readonly DiagnosticDescriptor FM001_NoHandler = new(
        id: "FM001",
        title: "No handler registered for request type",
        messageFormat: "No IRequestHandler<{0}, {1}> implementation was discovered in the compilation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every type implementing IRequest<TResponse> must have exactly one concrete handler " +
            "implementing IRequestHandler<TRequest, TResponse> in the compilation that references " +
            "the FastMediatR source generator.",
        helpLinkUri: HelpRoot + "#fm001-no-handler");

    /// <summary>FM002 — more than one handler is registered for the same request type.</summary>
    public static readonly DiagnosticDescriptor FM002_DuplicateHandler = new(
        id: "FM002",
        title: "Duplicate handlers for the same request type",
        messageFormat: "Multiple IRequestHandler<{0}, {1}> implementations were discovered: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Exactly one handler may be registered per request type. Remove the duplicate " +
            "implementations or split them into separate composition roots.",
        helpLinkUri: HelpRoot + "#fm002-duplicate-handler");

    /// <summary>FM003 — handler carries a <c>[HandlerLifetime]</c> attribute that overrides the configured default.</summary>
    public static readonly DiagnosticDescriptor FM003_NonDefaultLifetime = new(
        id: "FM003",
        title: "Handler uses a non-default DI lifetime",
        messageFormat: "Handler '{0}' is registered with lifetime '{1}' via [HandlerLifetime]; the FastMediatR default is Scoped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "[HandlerLifetime] overrides the default DI lifetime. This diagnostic surfaces the " +
            "override in the build output so it is visible to reviewers.",
        helpLinkUri: HelpRoot + "#fm003-non-default-lifetime");
}
