namespace FastMediatR.Generator.Models;

/// <summary>
/// Cacheable, value-equatable description of an <c>IRequestHandler&lt;TRequest, TResponse&gt;</c>
/// implementation discovered in the consumer's compilation.
/// </summary>
/// <param name="HandlerFullyQualifiedName">Fully-qualified handler type, e.g. <c>global::Acme.CreateOrderHandler</c>.</param>
/// <param name="RequestFullyQualifiedName">Fully-qualified request type.</param>
/// <param name="ResponseFullyQualifiedName">Fully-qualified response type (e.g. <c>global::System.Guid</c> or <c>global::FastMediatR.Unit</c> for void commands).</param>
/// <param name="RequestIsValueType">True when the request is a struct / record struct (drives <c>is</c> vs <c>switch</c> arm emission).</param>
/// <param name="LifetimeOverride">DI lifetime override from <c>[HandlerLifetime]</c>; <see langword="null"/> when the configured default applies.</param>
/// <param name="HandlerLocation">Source location of the handler class declaration, for diagnostic reporting.</param>
internal readonly record struct RequestHandlerInfo(
    string HandlerFullyQualifiedName,
    string RequestFullyQualifiedName,
    string ResponseFullyQualifiedName,
    bool RequestIsValueType,
    HandlerLifetime? LifetimeOverride,
    LocationInfo HandlerLocation);

/// <summary>
/// Cacheable, value-equatable description of an <c>INotificationHandler&lt;TNotification&gt;</c>
/// implementation discovered in the consumer's compilation.
/// </summary>
/// <param name="HandlerFullyQualifiedName">Fully-qualified handler type.</param>
/// <param name="NotificationFullyQualifiedName">Fully-qualified notification type.</param>
/// <param name="LifetimeOverride">DI lifetime override from <c>[HandlerLifetime]</c>.</param>
/// <param name="HandlerLocation">Source location of the handler class declaration.</param>
internal readonly record struct NotificationHandlerInfo(
    string HandlerFullyQualifiedName,
    string NotificationFullyQualifiedName,
    HandlerLifetime? LifetimeOverride,
    LocationInfo HandlerLocation);

/// <summary>Mirrors <c>Microsoft.Extensions.DependencyInjection.ServiceLifetime</c> without taking a runtime dependency.</summary>
internal enum HandlerLifetime
{
    /// <summary>One instance per request (scope).</summary>
    Scoped = 0,

    /// <summary>One instance per the entire application.</summary>
    Singleton = 1,

    /// <summary>A new instance every time the type is resolved.</summary>
    Transient = 2,
}

/// <summary>
/// Equatable replacement for <see cref="Microsoft.CodeAnalysis.Location"/> so handler models can be
/// safely cached across incremental-generator pipeline runs (raw <c>Location</c> contains non-equatable
/// references to syntax trees).
/// </summary>
/// <param name="FilePath">Source file path; <see cref="string.Empty"/> for synthetic locations.</param>
/// <param name="TextSpanStart">Start of the diagnostic span.</param>
/// <param name="TextSpanLength">Length of the diagnostic span.</param>
/// <param name="LineSpan">Line / column span in the source file.</param>
internal readonly record struct LocationInfo(
    string FilePath,
    int TextSpanStart,
    int TextSpanLength,
    Microsoft.CodeAnalysis.Text.LinePositionSpan LineSpan)
{
    /// <summary>Captures the equatable parts of a Roslyn <see cref="Microsoft.CodeAnalysis.Location"/>.</summary>
    /// <param name="location">The Roslyn location to snapshot.</param>
    /// <returns>An equatable <see cref="LocationInfo"/> safe to pass through the incremental-generator pipeline.</returns>
    public static LocationInfo From(Microsoft.CodeAnalysis.Location location)
    {
        if (location is null)
        {
            return default;
        }

        Microsoft.CodeAnalysis.FileLinePositionSpan span = location.GetLineSpan();
        return new LocationInfo(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            span.Span);
    }

    /// <summary>Rehydrates the <see cref="Microsoft.CodeAnalysis.Location"/> for diagnostic reporting.</summary>
    /// <returns>A Roslyn <see cref="Microsoft.CodeAnalysis.Location"/> equivalent to the captured snapshot.</returns>
    public Microsoft.CodeAnalysis.Location ToLocation()
        => Microsoft.CodeAnalysis.Location.Create(
            FilePath,
            new Microsoft.CodeAnalysis.Text.TextSpan(TextSpanStart, TextSpanLength),
            LineSpan);
}

/// <summary>
/// Aggregated, deduplicated discovery result fed into the emitter. Backed by
/// <see cref="EquatableArray{T}"/> so the incremental-generator framework can structurally
/// compare runs and short-circuit unchanged inputs.
/// </summary>
/// <param name="RequestHandlers">All request handlers discovered in the compilation.</param>
/// <param name="NotificationHandlers">All notification handlers discovered in the compilation.</param>
/// <param name="Requests">All concrete request types declared in the compilation (used to drive FM001).</param>
internal readonly record struct DiscoveryResult(
    EquatableArray<RequestHandlerInfo> RequestHandlers,
    EquatableArray<NotificationHandlerInfo> NotificationHandlers,
    EquatableArray<RequestTypeInfo> Requests);

/// <summary>
/// Cacheable description of a concrete <c>IRequest&lt;TResponse&gt;</c> type declared in the
/// consumer's compilation. Used to detect requests that have no handler (FM001).
/// </summary>
/// <param name="RequestFullyQualifiedName">Fully-qualified request type, e.g. <c>global::Acme.CreateOrderCommand</c>.</param>
/// <param name="ResponseFullyQualifiedName">Fully-qualified response type.</param>
/// <param name="IsValueType">True when the request is a struct / record struct.</param>
/// <param name="DeclarationLocation">Source location of the request type declaration.</param>
internal readonly record struct RequestTypeInfo(
    string RequestFullyQualifiedName,
    string ResponseFullyQualifiedName,
    bool IsValueType,
    LocationInfo DeclarationLocation);

/// <summary>
/// Cacheable description of a concrete <c>INotification</c> type declared in the
/// consumer's compilation.
/// </summary>
/// <param name="NotificationFullyQualifiedName">Fully-qualified notification type.</param>
/// <param name="DeclarationLocation">Source location of the notification type declaration.</param>
internal readonly record struct NotificationTypeInfo(
    string NotificationFullyQualifiedName,
    LocationInfo DeclarationLocation);
