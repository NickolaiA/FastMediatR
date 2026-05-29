namespace FastMediatR;

/// <summary>
/// Represents a single notification handler invocation, captured as a delegate plus
/// the handler instance for diagnostic / debugging purposes.
/// </summary>
/// <param name="HandlerInstance">
/// The resolved handler object. Exposed so that custom <see cref="INotificationPublisher"/>
/// implementations can inspect the handler (for example, to log its type name on failure).
/// </param>
/// <param name="Handler">
/// A pre-bound delegate that, when invoked, calls the handler's <c>Handle</c> method with
/// the strongly-typed notification cast performed by the source generator.
/// </param>
/// <remarks>
/// <para>
/// Modelled as a <see langword="readonly"/> <see langword="record"/> <see langword="struct"/>
/// so that the array allocated by the source generator on the publish hot path contains
/// inline value-typed entries with zero per-handler boxing.
/// </para>
/// </remarks>
public readonly record struct NotificationHandlerExecutor(
    object HandlerInstance,
    Func<INotification, CancellationToken, Task> Handler);
