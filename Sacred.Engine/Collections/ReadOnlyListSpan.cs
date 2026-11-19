using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Sacred.Engine;

/// <summary>Allocation-free iteration over <see cref="IReadOnlyList{T}"/> values on hot paths. A <c>foreach</c> over the
/// interface boxes a new enumerator on every call; the engine's frame lists are always <see cref="List{T}"/> or arrays,
/// so expose them as spans instead.</summary>
internal static class ReadOnlyListSpan
{
    /// <summary>The span is valid until the list is next modified; do not add to or clear the list while iterating it.</summary>
    public static ReadOnlySpan<T> Of<T>(IReadOnlyList<T>? list) => list switch
    {
        null => default,
        List<T> concrete => CollectionsMarshal.AsSpan(concrete),
        T[] array => array,
        _ => ToArray(list)
    };

    // Unknown implementation: copy once rather than fail, keeping callers correct if a new list type shows up.
    private static T[] ToArray<T>(IReadOnlyList<T> list)
    {
        var copy = new T[list.Count];
        for (var index = 0; index < copy.Length; index++)
            copy[index] = list[index];
        return copy;
    }
}
