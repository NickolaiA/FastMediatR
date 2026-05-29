using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace FastMediatR.Extensions.DI;

/// <summary>
/// Locates the source-generator-emitted <c>FastMediatR.Generated.FastMediatRGeneratedRegistration</c> type
/// inside a marker assembly and invokes its <c>Register</c> method to wire up the zero-reflection
/// dispatcher and all discovered handler registrations.
/// </summary>
/// <remarks>
/// <para>
/// The located <see cref="MethodInfo"/> delegate is cached per assembly so repeated
/// <c>AddFastMediatR</c> calls (e.g. in integration test hosts) pay the reflection cost only once.
/// </para>
/// <para>
/// If the generated type is absent — for instance when the source generator is not referenced or
/// when the assembly does not contain any handlers — this class logs nothing and returns silently.
/// Consumers relying on zero-reflection AOT dispatch should ensure the generator is referenced and
/// at least one handler exists in the marker assembly.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "GeneratedRegistrationLocator uses Assembly.GetType() and MethodInfo.Invoke() to locate " +
    "the source-generated FastMediatRGeneratedRegistration type at runtime. " +
    "For full AOT/trimming safety call FastMediatRGeneratedRegistration.Register(services) directly.")]
internal static class GeneratedRegistrationLocator
{
    /// <summary>The fully-qualified name of the type emitted by the source generator.</summary>
    private const string GeneratedTypeName = "FastMediatR.Generated.FastMediatRGeneratedRegistration";

    /// <summary>The name of the static registration method on the generated type.</summary>
    private const string RegisterMethodName = "Register";

    /// <summary>
    /// Per-assembly cache mapping each marker assembly to its resolved <c>Register</c> delegate
    /// (or <see langword="null"/> when the generated type is absent in that assembly).
    /// </summary>
    private static readonly ConcurrentDictionary<Assembly, Action<IServiceCollection>?> _cache = new();

    /// <summary>
    /// Attempts to locate the source-generated registration type in <paramref name="markerAssembly"/>
    /// and invokes its <c>Register</c> method.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to populate. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="markerAssembly">
    /// The assembly in which to search for the generated type. Must not be <see langword="null"/>.
    /// </param>
    internal static void Register(IServiceCollection services, Assembly markerAssembly)
    {
        Action<IServiceCollection>? invoker = _cache.GetOrAdd(markerAssembly, Resolve);
        invoker?.Invoke(services);
    }

    /// <summary>
    /// Resolves and compiles the <c>Register</c> delegate for the given <paramref name="assembly"/>.
    /// Returns <see langword="null"/> when the generated type or method cannot be located.
    /// </summary>
    /// <param name="assembly">The assembly to search. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A delegate that, when called, forwards the <see cref="IServiceCollection"/> argument to the
    /// generated static <c>Register</c> method; or <see langword="null"/> if the type is absent.
    /// </returns>
    private static Action<IServiceCollection>? Resolve(Assembly assembly)
    {
        Type? generatedType = assembly.GetType(GeneratedTypeName);

        if (generatedType is null)
        {
            return null;
        }

        MethodInfo? registerMethod = generatedType.GetMethod(
            RegisterMethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(IServiceCollection)],
            modifiers: null);

        if (registerMethod is null)
        {
            return null;
        }

        return services => registerMethod.Invoke(obj: null, parameters: [services]);
    }
}
