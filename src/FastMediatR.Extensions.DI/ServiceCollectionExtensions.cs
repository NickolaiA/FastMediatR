using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FastMediatR.Extensions.DI;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering FastMediatR.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Scans <paramref name="assembly"/> at most once per <paramref name="services"/> collection.
    /// A repeated scan (e.g. a marker assembly that is also passed via
    /// <see cref="FastMediatRConfig.RegisterHandlersFromAssembly"/>, or a second
    /// <c>AddFastMediatR</c> call) would duplicate the closed-interface mappings registered with
    /// <c>Add</c> — pre/post-processor and exception handler/action lookups resolve
    /// <c>IEnumerable</c>/multiple services — causing those implementations to run twice per request.
    /// </summary>
    [RequiresUnreferencedCode(
        "Scans the assembly for handler and processor types via reflection; see AssemblyScanner.")]
    private static void ScanOnce(IServiceCollection services, Assembly assembly, ServiceLifetime lifetime)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ScannedAssemblyRecord)
                && ReferenceEquals(((ScannedAssemblyRecord)descriptor.ImplementationInstance!).Assembly, assembly))
            {
                return;
            }
        }

        services.AddSingleton(new ScannedAssemblyRecord(assembly));
        AssemblyScanner.Scan(services, assembly, lifetime);
    }

    /// <summary>
    /// Sentinel registration recording that <see cref="AssemblyScanner"/> already scanned an assembly.
    /// Internal (not private) so container-interop and service-descriptor inspection tooling can
    /// resolve every registered type without tripping over an inaccessible one.
    /// </summary>
    /// <param name="Assembly">The assembly that was scanned.</param>
    internal sealed record ScannedAssemblyRecord(Assembly Assembly);

    /// <summary>
    /// Registers all FastMediatR services for the assembly that contains <typeparamref name="TMarker"/>.
    /// </summary>
    /// <typeparam name="TMarker">
    /// A type whose containing assembly is treated as the primary assembly for:
    /// <list type="bullet">
    ///   <item>locating the source-generated <c>FastMediatRGeneratedRegistration</c> zero-reflection dispatcher, and</item>
    ///   <item>reflection-based scanning for handler and processor types.</item>
    /// </list>
    /// </typeparam>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to configure. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="configure">
    /// Optional delegate to configure additional assemblies, open-generic behaviors, default handler lifetime, etc.
    /// When <see langword="null"/> the defaults defined by <see cref="FastMediatRConfig"/> apply.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Registration order:
    /// <list type="number">
    ///   <item>Source-generated <c>FastMediatRGeneratedRegistration.Register(services)</c> (zero-reflection path).</item>
    ///   <item>Reflection-based assembly scan of the <typeparamref name="TMarker"/> assembly (handler and processor types).</item>
    ///   <item>Reflection-based assembly scans of any assemblies added via <see cref="FastMediatRConfig.RegisterHandlersFromAssembly"/>.</item>
    ///   <item>Open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/> registrations.</item>
    ///   <item><see cref="FastMediatROptions"/> bound from the <c>"FastMediatR"</c> configuration section.</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method uses <see cref="System.Reflection"/> internally and is annotated
    /// <see cref="RequiresUnreferencedCodeAttribute"/> accordingly. For fully AOT-safe registration
    /// without this method, call <c>FastMediatRGeneratedRegistration.Register(services)</c> directly.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(
        "AddFastMediatR uses reflection (Assembly.GetType, Assembly.GetTypes) to locate the " +
        "source-generated registration type and to scan assemblies for handler types. " +
        "For full AOT/trimming safety call FastMediatRGeneratedRegistration.Register(services) directly.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "AddFastMediatR calls BindConfiguration which may generate dynamic code at runtime when AOT compiling. " +
        "For full AOT/trimming safety call FastMediatRGeneratedRegistration.Register(services) directly.")]
    public static IServiceCollection AddFastMediatR<TMarker>(
        this IServiceCollection services,
        Action<FastMediatRConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var config = new FastMediatRConfig();
        configure?.Invoke(config);

        Assembly markerAssembly = typeof(TMarker).Assembly;

        // 1. Invoke the source-generated zero-reflection registration (if present).
        GeneratedRegistrationLocator.Register(services, markerAssembly);

        // 2. Scan the TMarker assembly for handler and processor types (reflection fallback / pre-post
        //    processors). ScanOnce dedupes: the marker assembly may also be listed explicitly via
        //    RegisterHandlersFromAssembly (a very common pattern), and a double scan would register
        //    every closed handler/action/processor mapping twice.
        ScanOnce(services, markerAssembly, config.DefaultHandlerLifetime);

        // 3. Scan any additional assemblies supplied via RegisterHandlersFromAssembly.
        foreach (Assembly assembly in config.Assemblies)
        {
            ScanOnce(services, assembly, config.DefaultHandlerLifetime);
        }

        // 4. Register open-generic pipeline behaviors in declaration order (outermost first).
        //    Pre/post-processor behaviors are appended innermost below (task 3.5).
        foreach ((Type implementationType, ServiceLifetime lifetime) in config.OpenBehaviors)
        {
            services.Add(ServiceDescriptor.Describe(typeof(IPipelineBehavior<,>), implementationType, lifetime));
        }

        // 4a. Auto-register the exception-handling behaviors AFTER user behaviors (so user behaviors
        //     stay outermost) and BEFORE the pre/post-processor behaviors (so exceptions thrown by
        //     pre/post processors and the handler are caught). Their relative order is governed by the
        //     configured strategy:
        //       - ApplyForUnhandledExceptions (default): action behavior OUTSIDE the processor behavior,
        //         so actions run only when no handler suppresses the exception.
        //       - ApplyForAllExceptions: action behavior INSIDE the processor behavior, so actions
        //         always run when an exception is thrown.
        //     First-registered = outermost.
        //     TryAddEnumerable makes these built-in registrations idempotent: a repeated
        //     AddFastMediatR call (e.g. two marker types) must not stack duplicate behaviors, which
        //     would run exception actions twice per failure. User AddOpenBehavior entries above stay
        //     plain Add — duplicates there are the caller's choice.
        if (config.RequestExceptionActionProcessorStrategy ==
            RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions)
        {
            services.TryAddEnumerable(ServiceDescriptor.Describe(
                typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>), ServiceLifetime.Scoped));
            services.TryAddEnumerable(ServiceDescriptor.Describe(
                typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>), ServiceLifetime.Scoped));
        }
        else
        {
            services.TryAddEnumerable(ServiceDescriptor.Describe(
                typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>), ServiceLifetime.Scoped));
            services.TryAddEnumerable(ServiceDescriptor.Describe(
                typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>), ServiceLifetime.Scoped));
        }

        // 5. (Task 3.5) Auto-register RequestPreProcessorBehavior and RequestPostProcessorBehavior
        //    AFTER user behaviors so they land innermost (closest to the handler) regardless of
        //    user configuration. The behaviors no-op when no processors are registered.
        //    TryAddEnumerable for the same idempotency reason as step 4a.
        services.TryAddEnumerable(ServiceDescriptor.Describe(
            typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>), ServiceLifetime.Scoped));
        services.TryAddEnumerable(ServiceDescriptor.Describe(
            typeof(IPipelineBehavior<,>), typeof(RequestPostProcessorBehavior<,>), ServiceLifetime.Scoped));

        // 6. (Task 3.6) Register the INotificationPublisher based on the chosen strategy.
        //    Using TryAdd so a custom INotificationPublisher registered BEFORE AddFastMediatR
        //    is preserved (TryAdd skips registration if one already exists). To replace the
        //    default AFTER calling AddFastMediatR, use:
        //      services.Replace(ServiceDescriptor.Singleton<INotificationPublisher, MyPublisher>());
        if (config.NotificationPublisherStrategy == NotificationPublisherStrategy.Sequential)
        {
            services.TryAdd(ServiceDescriptor.Singleton<INotificationPublisher, SequentialAwaitPublisher>());
        }
        else
        {
            services.TryAdd(ServiceDescriptor.Singleton<INotificationPublisher, TaskWhenAllPublisher>());
        }

        // 7. Register TimeProvider.System as the default TimeProvider so that
        //    PerformanceBehavior<,> can be resolved without requiring the caller to
        //    register it manually. Using TryAddSingleton so that test hosts can
        //    substitute FakeTimeProvider by registering their own before this call.
        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);

        // 8. Bind FastMediatROptions from configuration and enable start-up validation.
        services
            .AddOptions<FastMediatROptions>()
            .BindConfiguration(FastMediatROptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
