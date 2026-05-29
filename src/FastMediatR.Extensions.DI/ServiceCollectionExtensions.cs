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

        // 2. Scan the TMarker assembly for handler and processor types (reflection fallback / pre-post processors).
        AssemblyScanner.Scan(services, markerAssembly, config.DefaultHandlerLifetime);

        // 3. Scan any additional assemblies supplied via RegisterHandlersFromAssembly.
        foreach (Assembly assembly in config.Assemblies)
        {
            AssemblyScanner.Scan(services, assembly, config.DefaultHandlerLifetime);
        }

        // 4. Register open-generic pipeline behaviors in declaration order (outermost first).
        //    Pre/post-processor behaviors are appended innermost below (task 3.5).
        foreach ((Type implementationType, ServiceLifetime lifetime) in config.OpenBehaviors)
        {
            services.Add(ServiceDescriptor.Describe(typeof(IPipelineBehavior<,>), implementationType, lifetime));
        }

        // 5. (Task 3.5) Auto-register RequestPreProcessorBehavior and RequestPostProcessorBehavior
        //    AFTER user behaviors so they land innermost (closest to the handler) regardless of
        //    user configuration. The behaviors no-op when no processors are registered.
        services.Add(ServiceDescriptor.Describe(
            typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>), ServiceLifetime.Scoped));
        services.Add(ServiceDescriptor.Describe(
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
