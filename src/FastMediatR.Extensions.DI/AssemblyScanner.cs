using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FastMediatR.Extensions.DI;

/// <summary>
/// Scans one or more <see cref="Assembly"/> instances for FastMediatR handler and processor types
/// and registers them into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// The scanner discovers concrete (non-abstract, non-generic) classes that implement at least one
/// of the following open-generic interfaces:
/// <list type="bullet">
///   <item><see cref="IRequestHandler{TRequest,TResponse}"/></item>
///   <item><see cref="INotificationHandler{TNotification}"/></item>
///   <item><see cref="IRequestPreProcessor{TRequest}"/></item>
///   <item><see cref="IRequestPostProcessor{TRequest,TResponse}"/></item>
///   <item><see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/></item>
///   <item><see cref="IRequestExceptionAction{TRequest,TException}"/></item>
/// </list>
/// </para>
/// <para>
/// <strong>Handler registration</strong> — each handler concrete type is registered once
/// (via <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection, ServiceDescriptor)"/>)
/// using the lifetime determined by <see cref="GetEffectiveLifetime"/>.
/// </para>
/// <para>
/// <strong>Closed-interface mapping registration</strong> — in addition to the concrete-type registration,
/// each implemented <see cref="IRequestPreProcessor{TRequest}"/>, <see cref="IRequestPostProcessor{TRequest,TResponse}"/>,
/// <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> or
/// <see cref="IRequestExceptionAction{TRequest,TException}"/> interface is registered as a closed mapping
/// (via <c>Add</c>, not <c>TryAdd</c>) so the pipeline behaviors resolve <em>all</em> matching
/// implementations in DI registration order. Execution order therefore follows assembly scan order,
/// which is unspecified across assemblies — do not depend on cross-assembly relative order.
/// </para>
/// <para>
/// Handler types decorated with <see cref="HandlerLifetimeAttribute"/> override the
/// default lifetime supplied by the caller.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "AssemblyScanner uses Assembly.GetTypes() and Type.GetInterfaces() to discover handler types at runtime. " +
    "For full AOT/trimming safety use the source generator instead of the reflection-based scanner.")]
internal static class AssemblyScanner
{
    /// <summary>Open-generic form of <see cref="IRequestHandler{TRequest,TResponse}"/>.</summary>
    private static readonly Type _requestHandlerOpen = typeof(IRequestHandler<,>);

    /// <summary>Open-generic form of <see cref="INotificationHandler{TNotification}"/>.</summary>
    private static readonly Type _notificationHandlerOpen = typeof(INotificationHandler<>);

    /// <summary>Open-generic form of <see cref="IRequestPreProcessor{TRequest}"/>.</summary>
    private static readonly Type _preProcessorOpen = typeof(IRequestPreProcessor<>);

    /// <summary>Open-generic form of <see cref="IRequestPostProcessor{TRequest,TResponse}"/>.</summary>
    private static readonly Type _postProcessorOpen = typeof(IRequestPostProcessor<,>);

    /// <summary>Open-generic form of <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>.</summary>
    private static readonly Type _exceptionHandlerOpen = typeof(IRequestExceptionHandler<,,>);

    /// <summary>Open-generic form of <see cref="IRequestExceptionAction{TRequest,TException}"/>.</summary>
    private static readonly Type _exceptionActionOpen = typeof(IRequestExceptionAction<,>);

    /// <summary>
    /// The open-generic interfaces whose closed implementations get an <em>additional</em> closed-interface
    /// mapping registration (pre/post-processors and exception handlers/actions). One set drives one shared
    /// registration branch, so the four shapes can never drift apart.
    /// </summary>
    private static readonly HashSet<Type> _closedMappingOpenGenerics =
    [
        _preProcessorOpen,
        _postProcessorOpen,
        _exceptionHandlerOpen,
        _exceptionActionOpen,
    ];

    /// <summary>
    /// Scans <paramref name="assembly"/> and registers all discovered handler and processor types
    /// into <paramref name="services"/>.
    /// </summary>
    /// <param name="services">
    /// The service collection to populate. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="assembly">
    /// The assembly to scan. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="defaultLifetime">
    /// The lifetime applied to handler types that do not carry a <see cref="HandlerLifetimeAttribute"/>.
    /// </param>
    internal static void Scan(IServiceCollection services, Assembly assembly, ServiceLifetime defaultLifetime)
    {
        foreach (Type type in GetLoadableTypes(assembly))
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
            {
                continue;
            }

            ServiceLifetime lifetime = GetEffectiveLifetime(type, defaultLifetime);
            bool concreteRegistered = false;

            foreach (Type iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType)
                {
                    continue;
                }

                Type openGeneric = iface.GetGenericTypeDefinition();

                if (openGeneric == _requestHandlerOpen || openGeneric == _notificationHandlerOpen)
                {
                    if (!concreteRegistered)
                    {
                        services.TryAdd(ServiceDescriptor.Describe(type, type, lifetime));
                        concreteRegistered = true;
                    }
                }
                else if (_closedMappingOpenGenerics.Contains(openGeneric))
                {
                    if (!concreteRegistered)
                    {
                        services.TryAdd(ServiceDescriptor.Describe(type, type, lifetime));
                        concreteRegistered = true;
                    }

                    // Register the closed interface in addition to the concrete type so the pipeline
                    // behaviors resolve every matching implementation in registration order:
                    // IEnumerable<IRequestPreProcessor<T>> / IEnumerable<IRequestPostProcessor<T,TResponse>>
                    // for the pre/post-processor behaviors, and GetServices(closed
                    // IRequestExceptionHandler<,,> / IRequestExceptionAction<,>) for the exception behaviors.
                    // Use Add (not TryAdd) to allow multiple implementations per closed interface.
                    Type concreteType = type;
                    services.Add(ServiceDescriptor.Describe(iface, sp => sp.GetRequiredService(concreteType), lifetime));
                }
            }
        }
    }

    /// <summary>
    /// Returns the effective <see cref="ServiceLifetime"/> for <paramref name="handlerType"/>.
    /// A <see cref="HandlerLifetimeAttribute"/> on the type takes precedence over the
    /// <paramref name="defaultLifetime"/> supplied by the caller.
    /// </summary>
    /// <param name="handlerType">The handler type to inspect.</param>
    /// <param name="defaultLifetime">The fallback lifetime.</param>
    /// <returns>The effective <see cref="ServiceLifetime"/>.</returns>
    private static ServiceLifetime GetEffectiveLifetime(Type handlerType, ServiceLifetime defaultLifetime)
    {
        HandlerLifetimeAttribute? attribute = handlerType.GetCustomAttribute<HandlerLifetimeAttribute>(inherit: false);
        return attribute?.Lifetime ?? defaultLifetime;
    }

    /// <summary>
    /// Returns the types defined in <paramref name="assembly"/>, tolerating partial load failures.
    /// </summary>
    /// <param name="assembly">The assembly to enumerate.</param>
    /// <returns>
    /// All successfully loaded types. When <see cref="Assembly.GetTypes"/> throws a
    /// <see cref="ReflectionTypeLoadException"/> (for example, a plugin assembly that references a
    /// missing optional dependency), the types that <em>did</em> load are returned and the
    /// unloadable ones are skipped, so a single broken type cannot abort the whole registration.
    /// </returns>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
