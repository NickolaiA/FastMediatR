// This file lives in the FastMediatR.Tests assembly so that GeneratedRegistrationLocator
// can locate it by the conventional type name when AddFastMediatR<TestMarker> is called.
// It acts as a stand-in for what FastMediatRGenerator emits in consumer assemblies.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FastMediatR.Generated;

/// <summary>
/// Fake source-generated registration placed in the test assembly.
/// Allows round-trip tests to exercise <c>GeneratedRegistrationLocator</c> without running the
/// actual Roslyn generator. Mimics the shape of the real emitted class.
/// </summary>
internal static class FastMediatRGeneratedRegistration
{
    /// <summary>
    /// Marker service type registered by this fake to allow tests to assert that the generated
    /// registration was discovered and invoked.
    /// </summary>
    internal sealed class FakeGeneratedMarker;

    /// <summary>
    /// Fake implementation of the generated static registration entry point.
    /// Registers <see cref="FakeGeneratedMarker"/> as a singleton so tests can assert invocation.
    /// </summary>
    /// <param name="services">The service collection to populate. Must not be <see langword="null"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for fluent chaining.</returns>
    public static IServiceCollection Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<FakeGeneratedMarker>();
        return services;
    }
}
