#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;

namespace Quine.SqlInvoke;

/// <summary>
/// Utility class for reading results sets.  Output parameters are not populated until all results have been
/// read and the instance disposed.
/// </summary>
public sealed class SqlResultSetReader : IDisposable
{
    private readonly SqlContext owner;

    internal SqlResultSetReader(SqlContext owner, SqlDataReader reader, SqlCommand sqlcmd, object parameters) {
        this.owner = owner;
        this.DataReader = reader;
        this.SqlCommand = sqlcmd;
        this.Parameters = parameters;
        this.HasMoreResults = true;
    }

    /// <summary>
    /// For read-back of output parameters in event callback.
    /// </summary>
    internal readonly SqlCommand SqlCommand;

    /// <summary>
    /// For write-back of output parameters in event callback.
    /// </summary>
    internal readonly object Parameters;    // For writing output parameters on dispose.

    /// <summary>
    /// Set by <see cref="GetRows{T}"/> and <see cref="GetRowsAsync{T}"/> after completion of the iteration
    /// to indicate whether more result sets exists.
    /// </summary>
    public bool HasMoreResults { get; private set; }

    /// <summary>
    /// Data reader instance used by <c>this</c>.  Disposed of when <c>this</c> is disposed.
    /// </summary>
    public SqlDataReader DataReader { get; }

    // With event, we don't need to track explicit parent-child relationship.
    // The handler is responsible for writing output parameter values and disposing the command.
    internal event Action<SqlResultSetReader> Disposed;

    bool _isDisposed = false;
    public void Dispose() {
        if (_isDisposed)
            return;
        _isDisposed = true;
        DataReader.Dispose();
        try {
            Disposed?.Invoke(this);
        }
        finally {
            Disposed = null;
        }
    }

    /// <summary>
    /// Returns an enumerable for reading rows of type <typeparamref name="TRow"/>.
    /// When the enumerable is exhausted, it navigates to the next result set.
    /// </summary>
    /// <typeparam name="TRow">Class describing the record to be populated.</typeparam>
    /// <exception cref="InvalidOperationException">No more result sets are available.</exception>
    public IEnumerable<TRow> GetRows<TRow>() where TRow : class, new() {
        if (!HasMoreResults)
            throw new InvalidOperationException("No more result sets available.");

        var ra = owner.GetRowAccessor<TRow>();
        while (DataReader.Read()) {
            var item = new TRow();
            ra.ReadDataRecord(DataReader, item);
            yield return item;
        }
        HasMoreResults = DataReader.NextResult();
    }

    /// <summary>
    /// Async version of <see cref="GetRows{TRow}"/>.
    /// </summary>
    public async IAsyncEnumerable<TRow> GetRowsAsync<TRow>() where TRow : class, new() {
        if (!HasMoreResults)
            throw new InvalidOperationException("No more result sets available.");

        var ra = owner.GetRowAccessor<TRow>();
        while (await DataReader.ReadAsync()) {
            var item = new TRow();
            ra.ReadDataRecord(DataReader, item);
            yield return item;
        }
        HasMoreResults = await DataReader.NextResultAsync();
    }

    /// <summary>
    /// Iterates over all rows of the current result set.  When the iteration has finished, it navigates to
    /// the next result set.
    /// </summary>
    /// <typeparam name="TRow">Class describing the record to be populated.</typeparam>
    /// <param name="processor">
    /// A delegate that is repeatedly invoked with rows of the current result set as they are asynchronously read.
    /// </param>
    /// <param name="reuseInstance">
    /// If true, <paramref name="processor"/> will be always invoked with the same instance (updated in-place) of 
    /// <typeparamref name="TRow"/>.  WARNING: This is an optimization for large data volumes and difficult to use
    /// correctly.  If in doubt, leave it as <c>false</c> (default).
    /// </param>
    /// <exception cref="InvalidOperationException">No more result sets are available.</exception>
    public async Task IterateRowsAsync<TRow>(Action<TRow> processor, bool reuseInstance = false) where TRow : class, new() {
        if (!HasMoreResults)
            throw new InvalidOperationException("No more result sets available.");

        var ra = owner.GetRowAccessor<TRow>();
        var item = new TRow();

        while (await DataReader.ReadAsync()) {
            ra.ReadDataRecord(DataReader, item);
            processor(item);
            if (!reuseInstance)
                item = new();
        }
        HasMoreResults = await DataReader.NextResultAsync();
    }

    /// <summary>
    /// Synchronous version of <see cref="IterateRowsAsync{TRow}(Action{TRow}, bool)"/>.
    /// </summary>
    public void IterateRows<TRow>(Action<TRow> processor, bool reuseInstance = false) where TRow : class, new() {
        if (!HasMoreResults)
            throw new InvalidOperationException("No more result sets available.");

        var ra = owner.GetRowAccessor<TRow>();
        var item = new TRow();

        while (DataReader.Read()) {
            ra.ReadDataRecord(DataReader, item);
            processor(item);
            if (!reuseInstance)
                item = new();
        }
        HasMoreResults = DataReader.NextResult();
    }
}
#endif
