using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace FastMediatR.Extensions.DI;

/// <summary>
/// Reflection helpers shared by <see cref="RequestExceptionProcessorBehavior{TRequest,TResponse}"/>
/// and <see cref="RequestExceptionActionProcessorBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <remarks>
/// <para>
/// These helpers resolve closed <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>
/// and <see cref="IRequestExceptionAction{TRequest,TException}"/> services for the runtime exception
/// type (and its base types). They run only on the exception path of requests dispatched through the
/// reflection-based <c>AddFastMediatR</c> registration, which is itself annotated
/// <see cref="RequiresUnreferencedCodeAttribute"/> / <see cref="RequiresDynamicCodeAttribute"/>.
/// </para>
/// <para>
/// When a handler or action itself throws, callers attach the original request exception under
/// <see cref="OriginalExceptionDataKey"/> in the fault's <see cref="Exception.Data"/> before letting
/// the fault propagate, so neither failure is lost.
/// </para>
/// <para>
/// <strong>Cache lifetime:</strong> the lookup caches below are process-lifetime and keyed by user
/// domain types — they keep the request, response and exception <c>Type</c> objects (and transitively
/// their assemblies) reachable forever. Hosts that load plugins in collectible
/// <c>AssemblyLoadContext</c>s and need to unload them must use the source-generated path instead of
/// <c>AddFastMediatR</c>.
/// </para>
/// </remarks>
internal static class ExceptionPipeline
{
    /// <summary>Open-generic form of <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>.</summary>
    private static readonly Type _handlerOpen = typeof(IRequestExceptionHandler<,,>);

    /// <summary>Open-generic form of <see cref="IRequestExceptionAction{TRequest,TException}"/>.</summary>
    private static readonly Type _actionOpen = typeof(IRequestExceptionAction<,>);

    /// <summary>Cache of resolved interface <see cref="MethodInfo"/> keyed by closed interface type.</summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> _methods = new();

    /// <summary>
    /// Cache of closed <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> interface types
    /// keyed by the (request, response, exception) triple. <c>MakeGenericType</c> would otherwise run on
    /// every failure occurrence — a handler storm during a failure cascade turns this cold path warm, so
    /// the type construction is cached alongside the <see cref="MethodInfo"/> lookup below.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResponseType, Type ExceptionType), Type> _handlerTypes = new();

    /// <summary>Cache of closed <see cref="IRequestExceptionAction{TRequest,TException}"/> interface types keyed by (request, exception).</summary>
    private static readonly ConcurrentDictionary<(Type RequestType, Type ExceptionType), Type> _actionTypes = new();

    /// <summary>
    /// Key under which the original request exception is stored in the <see cref="Exception.Data"/>
    /// of a faulting exception handler or action, so the replaced exception is not lost silently.
    /// </summary>
    internal const string OriginalExceptionDataKey = "FastMediatR.OriginalException";

    /// <summary>
    /// Best-effort attach of the original request exception to <paramref name="fault"/>'s
    /// <see cref="Exception.Data"/> under <see cref="OriginalExceptionDataKey"/>.
    /// </summary>
    /// <remarks>
    /// A custom exception type may override <see cref="Exception.Data"/> to return <see langword="null"/>
    /// or a read-only dictionary, so the attachment itself can throw. Diagnostics must never change
    /// program behavior: any failure here is swallowed, because the fault already in flight is more
    /// important to preserve than the original exception it carries.
    /// </remarks>
    /// <param name="fault">The handler/action fault about to propagate.</param>
    /// <param name="original">The original request exception to attach.</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Attaching diagnostic data must never be able to replace the fault being propagated; any failure mode of a custom Data implementation is acceptable to ignore.")]
    internal static void TryAttachOriginalException(Exception fault, Exception original)
    {
        try
        {
            fault.Data[OriginalExceptionDataKey] = original;
        }
        catch (Exception)
        {
            // Deliberately ignored — see remarks.
        }
    }

    /// <summary>
    /// Enumerates <paramref name="exceptionType"/> and every base type up to and including
    /// <see cref="Exception"/>, most-derived first.
    /// </summary>
    /// <param name="exceptionType">The runtime exception type to walk.</param>
    /// <returns>The exception type hierarchy from most-derived to <see cref="Exception"/>.</returns>
    internal static IEnumerable<Type> ExceptionTypeHierarchy(Type exceptionType)
    {
        for (Type? t = exceptionType; t is not null && typeof(Exception).IsAssignableFrom(t); t = t.BaseType)
        {
            yield return t;
        }
    }

    /// <summary>
    /// Resolves and runs every <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>
    /// matching the runtime exception type (and its base types) until one marks the exception handled.
    /// </summary>
    /// <typeparam name="TResponse">The response type of the failed request.</typeparam>
    /// <param name="serviceProvider">The DI provider used to resolve handlers.</param>
    /// <param name="requestType">The closed request type.</param>
    /// <param name="request">The request instance.</param>
    /// <param name="exception">The thrown exception.</param>
    /// <param name="state">The handler state shared across handlers.</param>
    /// <param name="cancellationToken">
    /// Token forwarded to handlers. May already be cancelled — handlers that intend to run to
    /// completion must not observe it.
    /// </param>
    /// <returns><see langword="true"/> when a handler marked the exception handled.</returns>
    [RequiresUnreferencedCode("Resolves closed IRequestExceptionHandler<,,> services via reflection.")]
    [RequiresDynamicCode("Constructs closed IRequestExceptionHandler<,,> types via MakeGenericType.")]
    internal static async Task<bool> TryHandleAsync<TResponse>(
        IServiceProvider serviceProvider,
        Type requestType,
        object request,
        Exception exception,
        RequestExceptionHandlerState<TResponse> state,
        CancellationToken cancellationToken)
    {
        foreach (Type exceptionType in ExceptionTypeHierarchy(exception.GetType()))
        {
            Type closed = _handlerTypes.GetOrAdd(
                (requestType, typeof(TResponse), exceptionType),
                static key => _handlerOpen.MakeGenericType(key.RequestType, key.ResponseType, key.ExceptionType));
            MethodInfo handle = MethodFor(closed, "Handle");

            foreach (object? handler in serviceProvider.GetServices(closed))
            {
                if (handler is null)
                {
                    continue;
                }

                await InvokeAsync(handle, handler, [request, exception, state, cancellationToken])
                    .ConfigureAwait(false);

                if (state.Handled)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves and runs every <see cref="IRequestExceptionAction{TRequest,TException}"/> matching the
    /// runtime exception type (and its base types). Actions cannot suppress the exception.
    /// </summary>
    /// <remarks>
    /// Actions are invoked with <see cref="CancellationToken.None"/> — deliberately <em>not</em> the
    /// request's token. Actions are guaranteed side-effect observers (logging, metrics); the exception
    /// they observe is frequently a cancellation, so forwarding the request token would abort the very
    /// telemetry that records the failure.
    /// </remarks>
    /// <param name="serviceProvider">The DI provider used to resolve actions.</param>
    /// <param name="requestType">The closed request type.</param>
    /// <param name="request">The request instance.</param>
    /// <param name="exception">The thrown exception.</param>
    /// <returns>A task that completes when all matching actions have run.</returns>
    [RequiresUnreferencedCode("Resolves closed IRequestExceptionAction<,> services via reflection.")]
    [RequiresDynamicCode("Constructs closed IRequestExceptionAction<,> types via MakeGenericType.")]
    internal static async Task RunActionsAsync(
        IServiceProvider serviceProvider,
        Type requestType,
        object request,
        Exception exception)
    {
        foreach (Type exceptionType in ExceptionTypeHierarchy(exception.GetType()))
        {
            Type closed = _actionTypes.GetOrAdd(
                (requestType, exceptionType),
                static key => _actionOpen.MakeGenericType(key.RequestType, key.ExceptionType));
            MethodInfo execute = MethodFor(closed, "Execute");

            foreach (object? action in serviceProvider.GetServices(closed))
            {
                if (action is null)
                {
                    continue;
                }

                await InvokeAsync(execute, action, [request, exception, CancellationToken.None])
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Returns the cached <see cref="MethodInfo"/> for <paramref name="name"/> on the closed interface.</summary>
    /// <param name="closedInterface">The closed exception-handler/action interface type.</param>
    /// <param name="name">The method name to resolve.</param>
    /// <returns>The resolved <see cref="MethodInfo"/>.</returns>
    [RequiresUnreferencedCode("Uses Type.GetMethod on a closed interface resolved at runtime.")]
    private static MethodInfo MethodFor(Type closedInterface, string name) =>
        _methods.GetOrAdd(closedInterface, static (type, methodName) => type.GetMethod(methodName)!, name);

    /// <summary>
    /// Invokes the <see cref="Task"/>-returning <paramref name="method"/> on <paramref name="target"/>,
    /// unwrapping the <see cref="TargetInvocationException"/> that <see cref="MethodBase.Invoke(object, object[])"/>
    /// raises when the target throws <em>synchronously</em> (for example, a guard clause that runs before
    /// the first <c>await</c>). This preserves the original exception and its stack trace so a faulty
    /// exception handler/action surfaces the same way it would under a direct call.
    /// </summary>
    /// <remarks>
    /// The invocation remains deliberately reflection-based: with the closed interface type and
    /// <see cref="MethodInfo"/> both cached, the per-occurrence cost is a single <c>Invoke</c> plus argument
    /// boxing on the cold (failure) path. A compiled-delegate cache (expression trees per closed interface)
    /// would remove even that — and with it this <see cref="TargetInvocationException"/> unwrapping — but is
    /// tracked as a follow-up optimization rather than done pre-emptively. Do not "optimize" this without
    /// measuring a real failure cascade first.
    /// </remarks>
    /// <param name="method">The resolved <c>Handle</c>/<c>Execute</c> method.</param>
    /// <param name="target">The handler or action instance.</param>
    /// <param name="arguments">The invocation arguments.</param>
    /// <returns>The task returned by the target method.</returns>
    private static Task InvokeAsync(MethodInfo method, object target, object?[] arguments)
    {
        try
        {
            return (Task)method.Invoke(target, arguments)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // Unreachable — Throw() above always rethrows.
        }
    }
}
