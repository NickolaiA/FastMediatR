using System.Reflection;
using FluentAssertions;
using Mono.Cecil;
using NetArchTest.Rules;
using Xunit;

namespace FastMediatR.Architecture.Tests;

/// <summary>
/// Architecture tests for the FastMediatR library projects.
/// Uses NetArchTest.Rules for dependency direction and type-sealing assertions,
/// and a custom Mono.Cecil–based rule to verify that no library code calls
/// <c>.Result</c> or <c>.Wait()</c> on Tasks (blocking async operations).
///
/// Note on <c>ConfigureAwait(false)</c> enforcement:
///   The library projects have <c>&lt;AnalysisMode&gt;AllEnabledByDefault&lt;/AnalysisMode&gt;</c>
///   and <c>&lt;TreatWarningsAsErrors&gt;true&lt;/TreatWarningsAsErrors&gt;</c>, which activates
///   CA2007 ("Consider calling ConfigureAwait on the awaited task").  Any missing
///   <c>ConfigureAwait(false)</c> in library source code therefore fails the build —
///   no separate runtime test is required for this rule.
/// </summary>
public sealed class ArchitectureTests
{
    // ── Assembly references ────────────────────────────────────────────────────

    /// <summary>Compiled <c>FastMediatR.Core</c> assembly under test.</summary>
    private static readonly Assembly CoreAssembly = typeof(IMediator).Assembly;

    /// <summary>Compiled <c>FastMediatR.Extensions.DI</c> assembly under test.</summary>
    private static readonly Assembly DiAssembly = typeof(FastMediatR.Extensions.DI.FastMediatRConfig).Assembly;

    // ── Dependency direction ───────────────────────────────────────────────────

    /// <summary>
    /// <c>FastMediatR.Core</c> must not depend on <c>FastMediatR.Extensions.DI</c>.
    /// The Core assembly is a low-level library; the DI assembly composes on top of it.
    /// </summary>
    [Fact]
    public void Core_HasNoDependencyOnExtensionsDi()
    {
        // Act
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("FastMediatR.Extensions.DI")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "FastMediatR.Core must not reference FastMediatR.Extensions.DI; " +
                     "the dependency must flow in the other direction only. " +
                     "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// <c>FastMediatR.Extensions.DI</c> must depend on the <c>FastMediatR</c> namespace
    /// (the Core assembly), confirming the one-way dependency is wired correctly.
    /// </summary>
    [Fact]
    public void ExtensionsDi_HasDependencyOnCore()
    {
        // Act — collect types in DI assembly that reference Core.
        var types = Types.InAssembly(DiAssembly)
            .That()
            .HaveDependencyOn("FastMediatR")
            .GetTypes();

        // Assert
        // At least one type in Extensions.DI references Core — confirming the link exists.
        types.Should().NotBeEmpty(
            because: "FastMediatR.Extensions.DI must reference FastMediatR.Core types");
    }

    // ── Type sealing ───────────────────────────────────────────────────────────

    /// <summary>
    /// All public, concrete, non-static classes in <c>FastMediatR.Core</c> must be <c>sealed</c>.
    /// Sealed types improve performance (devirtualisation), prevent unintended subclassing,
    /// and make the public API surface explicit.
    /// </summary>
    [Fact]
    public void Core_PublicConcreteClasses_AreSealed()
    {
        // Act
        var result = Types.InAssembly(CoreAssembly)
            .That()
            .ArePublic()
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .AreNotStatic()
            .Should()
            .BeSealed()
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "every public concrete class in FastMediatR.Core must be sealed. " +
                     "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// All public, concrete, non-static classes in <c>FastMediatR.Extensions.DI</c>
    /// must be <c>sealed</c>.
    /// </summary>
    [Fact]
    public void ExtensionsDi_PublicConcreteClasses_AreSealed()
    {
        // Act
        var result = Types.InAssembly(DiAssembly)
            .That()
            .ArePublic()
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .AreNotStatic()
            .Should()
            .BeSealed()
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "every public concrete class in FastMediatR.Extensions.DI must be sealed. " +
                     "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ── No blocking async (.Result / .Wait) ───────────────────────────────────

    /// <summary>
    /// No type in <c>FastMediatR.Core</c> may call <c>.Result</c> or <c>.Wait()</c>
    /// on a <c>Task</c> or <c>ValueTask</c> — these are blocking operations that can
    /// cause deadlocks in async contexts.
    /// Detection uses a custom Mono.Cecil rule to inspect the compiled IL.
    /// </summary>
    [Fact]
    public void Core_NoBlockingTaskUsage()
    {
        // Act
        var result = Types.InAssembly(CoreAssembly)
            .That()
            .AreClasses()
            .Should()
            .MeetCustomRule(new NoBlockingTaskRule())
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "library code must never block on Task.Result or Task.Wait(). " +
                     "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// No type in <c>FastMediatR.Extensions.DI</c> may call <c>.Result</c> or <c>.Wait()</c>
    /// on a <c>Task</c> or <c>ValueTask</c>.
    /// </summary>
    [Fact]
    public void ExtensionsDi_NoBlockingTaskUsage()
    {
        // Act
        var result = Types.InAssembly(DiAssembly)
            .That()
            .AreClasses()
            .Should()
            .MeetCustomRule(new NoBlockingTaskRule())
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "library code must never block on Task.Result or Task.Wait(). " +
                     "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}

/// <summary>
/// NetArchTest custom rule that checks compiled IL for blocking Task calls
/// (<c>get_Result</c> property getter or <c>Wait()</c> method call on Task/ValueTask types).
/// Implemented via Mono.Cecil — the same IL-analysis library used internally by NetArchTest.
/// </summary>
internal sealed class NoBlockingTaskRule : ICustomRule
{
    /// <summary>Returns <see langword="true"/> if the type contains no blocking Task calls.</summary>
    /// <param name="type">The Mono.Cecil type definition to inspect.</param>
    /// <returns><see langword="false"/> if any method body calls <c>.Result</c> or <c>.Wait()</c>.</returns>
    public bool MeetsRule(TypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);

        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference mr)
                {
                    continue;
                }

                var declaringTypeName = mr.DeclaringType.FullName;
                bool isTaskOrValueTask =
                    declaringTypeName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
                    declaringTypeName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal);

                if (isTaskOrValueTask && (mr.Name == "get_Result" || mr.Name == "Wait"))
                {
                    return false; // Violation found — does NOT meet the rule.
                }
            }
        }

        return true; // No blocking calls found.
    }
}
