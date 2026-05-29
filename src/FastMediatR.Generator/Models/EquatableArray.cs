using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace FastMediatR.Generator.Models;

/// <summary>
/// Structurally-equatable wrapper around <see cref="ImmutableArray{T}"/> for use in the
/// incremental-generator pipeline.
/// </summary>
/// <remarks>
/// <see cref="ImmutableArray{T}"/> implements <see cref="IEquatable{T}"/> using reference
/// equality of the backing array. The incremental-generator pipeline caches values by
/// structural equality, so a raw <see cref="ImmutableArray{T}"/> would cause every
/// pipeline run to invalidate downstream nodes. <see cref="EquatableArray{T}"/> fixes
/// this by overriding <see cref="GetHashCode"/> and <see cref="Equals(EquatableArray{T})"/>
/// to compare element-by-element.
/// </remarks>
/// <typeparam name="T">Element type; must itself be equatable.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    /// <summary>Creates an <see cref="EquatableArray{T}"/> from an <see cref="ImmutableArray{T}"/>.</summary>
    /// <param name="array">The backing array; defaults to <see cref="ImmutableArray{T}.Empty"/>.</param>
    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array.IsDefault ? ImmutableArray<T>.Empty : array;
    }

    /// <summary>Singleton empty array.</summary>
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    /// <inheritdoc/>
    public int Count => _array.Length;

    /// <inheritdoc/>
    public T this[int index] => _array[index];

    /// <inheritdoc/>
    public bool Equals(EquatableArray<T> other)
    {
        if (_array.Length != other._array.Length)
        {
            return false;
        }

        for (int i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (T item in _array)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    /// <summary>Returns the underlying <see cref="ImmutableArray{T}"/>.</summary>
    /// <returns>The underlying array.</returns>
    public ImmutableArray<T> AsImmutableArray() => _array;

    /// <summary>Equality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when the arrays are element-wise equal.</returns>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when the arrays differ.</returns>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_array).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Extension helpers for <see cref="EquatableArray{T}"/>.</summary>
internal static class EquatableArrayExtensions
{
    /// <summary>Wraps an <see cref="ImmutableArray{T}"/> as an <see cref="EquatableArray{T}"/>.</summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="array">Source array.</param>
    /// <returns>An <see cref="EquatableArray{T}"/> wrapper.</returns>
    public static EquatableArray<T> ToEquatableArray<T>(this ImmutableArray<T> array)
        where T : IEquatable<T> => new(array);

    /// <summary>Materialises an enumerable as a sorted <see cref="EquatableArray{T}"/>.</summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="source">Source enumerable.</param>
    /// <param name="comparer">Comparer used for sorting.</param>
    /// <returns>A sorted, equatable array.</returns>
    public static EquatableArray<T> ToSortedEquatableArray<T>(this IEnumerable<T> source, IComparer<T> comparer)
        where T : IEquatable<T> => new(source.OrderBy(x => x, comparer).ToImmutableArray());
}
