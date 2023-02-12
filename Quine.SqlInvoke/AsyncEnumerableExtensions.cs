#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Quine.SqlInvoke;

/// <summary>
/// Extension methods for fetching async enumerators in a collection.
/// </summary>
public static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> ae) {
        var ret = new List<T>();
        await foreach (var e in ae)
            ret.Add(e);
        return ret;
    }

    public static async Task<Dictionary<K, V>> ToDictionaryAsync<V, K>(this IAsyncEnumerable<V> ae, Func<V, K> key) {
        var ret = new Dictionary<K, V>();
        await foreach (var e in ae)
            ret.Add(key(e), e);
        return ret;
    }
}
#endif
