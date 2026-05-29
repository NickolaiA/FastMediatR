namespace FastMediatR;

/// <summary>
/// Represents a void return value for requests that produce no result.
/// </summary>
/// <remarks>
/// <para>
/// Equivalent to MediatR's <c>Unit</c> type. Modelled as a
/// <see langword="readonly"/> <see langword="record"/> <see langword="struct"/> so that
/// equality is value-based and instances cost zero heap allocations on the hot path
/// (see <see href="../../docs/DotNet.md">DotNet.md §1</see>).
/// </para>
/// <para>
/// All instances of <see cref="Unit"/> compare equal to one another; the type carries no state.
/// </para>
/// </remarks>
public readonly record struct Unit
{
    /// <summary>
    /// The single canonical <see cref="Unit"/> value. Prefer this over <c>default</c>
    /// when returning from a handler for readability.
    /// </summary>
    public static readonly Unit Value;

    /// <summary>
    /// A pre-completed <see cref="Task{TResult}"/> wrapping <see cref="Value"/>.
    /// Use this in synchronous handler implementations to avoid allocating a new task.
    /// </summary>
    public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

    /// <summary>
    /// Returns the canonical textual representation of a <see cref="Unit"/> value.
    /// </summary>
    /// <returns>The string <c>"()"</c>.</returns>
    public override string ToString() => "()";
}
