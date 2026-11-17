using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace FastMediatR.Extensions.DI;

/// <summary>
/// Fluent configuration builder passed to
/// <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{FastMediatRConfig}?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Instances of this class are created internally by <c>AddFastMediatR</c> and passed to the
/// caller-supplied delegate. Do not retain a reference to the instance after the delegate returns.
/// </para>
/// <para>
/// All configuration is consumed synchronously while <c>AddFastMediatR</c> executes: setting properties or
/// calling methods on this builder after that call has returned has no effect (with the exception of
/// service-descriptor entries already added to the <c>IServiceCollection</c>).
/// </para>
/// </remarks>
public sealed class FastMediatRConfig
{
    /// <summary>Backing list for the assemblies to scan.</summary>
    private readonly List<Assembly> _assemblies = [];

    /// <summary>Backing list of open-generic behavior registrations.</summary>
    private readonly List<(Type ImplementationType, ServiceLifetime Lifetime)> _openBehaviors = [];

    /// <summary>
    /// Gets or sets the default DI lifetime applied to handler types that do not carry a
    /// <see cref="HandlerLifetimeAttribute"/>.
    /// </summary>
    /// <value>Defaults to <see cref="ServiceLifetime.Scoped"/>.</value>
    /// <remarks>
    /// The value is consumed while assemblies are scanned. When <c>AddFastMediatR</c> is called more than
    /// once on the same service collection, each assembly is scanned only by the <strong>first</strong> call
    /// that reaches it, so that call's <see cref="DefaultHandlerLifetime"/> applies to it; a later call's
    /// value affects only assemblies that have not been scanned yet.
    /// </remarks>
    public ServiceLifetime DefaultHandlerLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Gets or sets the <see cref="INotificationPublisher"/> strategy registered by
    /// <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}"/>.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="NotificationPublisherStrategy.ParallelWhenAll"/> — all handlers
    /// run concurrently and every failure is surfaced via <see cref="System.AggregateException"/>.
    /// </value>
    /// <remarks>
    /// This property configures the default strategy. To use a fully custom publisher,
    /// call <c>services.Replace(ServiceDescriptor.Singleton&lt;INotificationPublisher, MyPublisher&gt;())</c>
    /// after <c>AddFastMediatR</c>.
    /// </remarks>
    public NotificationPublisherStrategy NotificationPublisherStrategy { get; set; } =
        NotificationPublisherStrategy.ParallelWhenAll;

    /// <summary>
    /// Gets or sets when <see cref="IRequestExceptionAction{TRequest,TException}"/> instances run
    /// relative to <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> processing.
    /// </summary>
    /// <value>Defaults to <see cref="RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions"/>.</value>
    /// <remarks>
    /// Must be set <em>inside</em> the <c>configure</c> delegate passed to
    /// <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{FastMediatRConfig}?)"/>:
    /// the value is read once during registration because it determines the order in which the exception
    /// behaviors are registered. Changing this property after <c>AddFastMediatR</c> has run has no effect.
    /// When <c>AddFastMediatR</c> is called more than once on the same service collection (e.g. an app
    /// plus a shared library, each with its own marker), the <strong>first</strong> call's strategy wins —
    /// the built-in behaviors are registered idempotently, so later calls cannot re-nest them.
    /// </remarks>
    public RequestExceptionActionProcessorStrategy RequestExceptionActionProcessorStrategy { get; set; } =
        RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions;

    /// <summary>
    /// Gets the additional assemblies that the <see cref="AssemblyScanner"/> will scan for
    /// handler, pre-processor and post-processor types.
    /// </summary>
    internal IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>
    /// Gets the open-generic pipeline behavior registrations added via
    /// <see cref="AddOpenBehavior(Type, ServiceLifetime)"/>.
    /// </summary>
    internal IReadOnlyList<(Type ImplementationType, ServiceLifetime Lifetime)> OpenBehaviors => _openBehaviors;

    /// <summary>
    /// Registers the assembly that contains <typeparamref name="T"/> for reflection-based handler scanning.
    /// </summary>
    /// <typeparam name="T">Any type whose containing assembly should be scanned.</typeparam>
    /// <returns>The same <see cref="FastMediatRConfig"/> instance for fluent chaining.</returns>
    public FastMediatRConfig RegisterHandlersFromAssemblyContaining<T>() =>
        RegisterHandlersFromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Registers the specified <paramref name="assembly"/> for reflection-based handler scanning.
    /// The assembly is scanned for concrete implementations of <see cref="IRequestHandler{TRequest,TResponse}"/>,
    /// <see cref="INotificationHandler{TNotification}"/>, <see cref="IRequestPreProcessor{TRequest}"/>,
    /// and <see cref="IRequestPostProcessor{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="assembly">The assembly to scan. Must not be <see langword="null"/>.</param>
    /// <returns>The same <see cref="FastMediatRConfig"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is <see langword="null"/>.</exception>
    public FastMediatRConfig RegisterHandlersFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers an open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/> implementation that will
    /// be applied to every request type dispatched through the pipeline.
    /// </summary>
    /// <param name="openBehaviorType">
    /// The open-generic type definition (e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>).
    /// Must implement <see cref="IPipelineBehavior{TRequest,TResponse}"/> and must be an open generic type definition.
    /// </param>
    /// <param name="lifetime">
    /// The DI lifetime for this behavior. Defaults to <see cref="ServiceLifetime.Transient"/>.
    /// </param>
    /// <returns>The same <see cref="FastMediatRConfig"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="openBehaviorType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="openBehaviorType"/> is not an open generic type definition,
    /// or when it does not implement <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
    /// </exception>
    public FastMediatRConfig AddOpenBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type openBehaviorType,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        if (!openBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Type '{openBehaviorType.FullName}' must be an open generic type definition (e.g. typeof(MyBehavior<,>)).",
                nameof(openBehaviorType));
        }

        if (!ImplementsOpenPipelineBehavior(openBehaviorType))
        {
            throw new ArgumentException(
                $"Type '{openBehaviorType.FullName}' must implement IPipelineBehavior<TRequest, TResponse>.",
                nameof(openBehaviorType));
        }

        _openBehaviors.Add((openBehaviorType, lifetime));
        return this;
    }

    /// <summary>
    /// Determines whether <paramref name="openBehaviorType"/> implements the open-generic
    /// <see cref="IPipelineBehavior{TRequest,TResponse}"/> interface.
    /// </summary>
    /// <param name="openBehaviorType">The open generic type definition to inspect.</param>
    /// <returns><see langword="true"/> when the type implements the open-generic behavior interface.</returns>
    private static bool ImplementsOpenPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type openBehaviorType)
    {
        Type openBehaviorInterface = typeof(IPipelineBehavior<,>);

        foreach (Type iface in openBehaviorType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == openBehaviorInterface)
            {
                return true;
            }
        }

        return false;
    }
}
