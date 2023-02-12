#if NET6_0_OR_GREATER
using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace Quine.SqlInvoke;

/// <summary>
/// Used to execute concrete commands created from <see cref="SqlInvokable{TParameters}"/>.
/// </summary>
/// <typeparam name="TParameters">
/// A type packaging up the command's parameter set.  Use <c>DBNull</c> for parameterless commands.
/// </typeparam>
/// <remarks>
/// This class may be derived to override <c>Execute*</c> methods with custom execution logic.
/// </remarks>
public class SqlExecutable<TParameters> : IDisposable where TParameters : class
{
    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="rowAccessor">Value to initialize <see cref="RowAccessor"/>.  Must not be null.</param>
    /// <param name="sqlCommand">Value to initialize <see cref="SqlCommand"/>.  Must not be null.</param>
    internal protected SqlExecutable(SqlRowAccessor<TParameters> rowAccessor, SqlCommand sqlCommand) {
        ArgumentNullException.ThrowIfNull(rowAccessor);
        ArgumentNullException.ThrowIfNull(sqlCommand);
        RowAccessor = rowAccessor;
        SqlCommand = sqlCommand;
    }

    /// <summary>
    /// Row accessor for parameters.
    /// </summary>
    public SqlRowAccessor<TParameters> RowAccessor { get; }

    /// <summary>
    /// An instance of the SQL command object.  Initialized by the framework as part of construction.
    /// </summary>
    public SqlCommand SqlCommand { get; private set; }

    /// <summary>
    /// Disposes of <see cref="SqlCommand"/>.
    /// </summary>
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (SqlCommand == null)
            return;
        if (disposing) {
            SqlCommand.Dispose();
            SqlCommand = null;
        }
    }

    private void Throw_If_Disposed() {
        if (SqlCommand == null)
            throw new ObjectDisposedException(nameof(SqlInvokable<TParameters>), "The object has been disposed or not initialized for execution.");
    }

    /// <summary>
    /// Executes <see cref="SqlCommand"/> as non-query with parameters <paramref name="parameters"/>.
    /// </summary>
    /// <param name="parameters">An instance of parameters to pass to the command.</param>
    /// <returns>Number of rows affected.</returns>
    public virtual async Task<int> ExecuteNonQueryAsync(TParameters parameters) {
        ArgumentNullException.ThrowIfNull(parameters);
        Throw_If_Disposed();
        RowAccessor.WriteCommandParameters(SqlCommand, parameters);
        var ret = await SqlCommand.ExecuteNonQueryAsync();
        RowAccessor.ReadCommandParameters(SqlCommand, parameters);
        return ret;
    }

    /// <summary>
    /// Synchronous version of <see cref="ExecuteNonQueryAsync(TParameters)"/>.
    /// </summary>
    public virtual int ExecuteNonQuery(TParameters parameters) {
        ArgumentNullException.ThrowIfNull(parameters);
        Throw_If_Disposed();
        RowAccessor.WriteCommandParameters(SqlCommand, parameters);
        var ret = SqlCommand.ExecuteNonQuery();
        RowAccessor.ReadCommandParameters(SqlCommand, parameters);
        return ret;
    }

    /// <summary>
    /// Executes <see cref="SqlCommand"/> as scalar query with parameters <paramref name="parameters"/>.
    /// </summary>
    /// <param name="parameters">An instance of parameters to pass to the command.</param>
    /// <returns>Scalar query result.</returns>
    /// <typeparam name="T">Return type of the query.</typeparam>
    public virtual async Task<T> ExecuteScalarAsync<T>(TParameters parameters) {
        ArgumentNullException.ThrowIfNull(parameters);
        Throw_If_Disposed();
        RowAccessor.WriteCommandParameters(SqlCommand, parameters);
        var ret = await SqlCommand.ExecuteScalarAsync();
        RowAccessor.ReadCommandParameters(SqlCommand, parameters);
        return (T)ret;
    }

    /// <summary>
    /// Synchronous version of <see cref="ExecuteScalarAsync(TParameters)"/>.
    /// </summary>
    public virtual T ExecuteScalar<T>(TParameters parameters) {
        ArgumentNullException.ThrowIfNull(parameters);
        Throw_If_Disposed();
        RowAccessor.WriteCommandParameters(SqlCommand, parameters);
        var ret = SqlCommand.ExecuteScalar();
        RowAccessor.ReadCommandParameters(SqlCommand, parameters);
        return (T)ret;
    }

    /// <summary>
    /// Executes <see cref="SqlCommand"/> as result-set returning query with parameters <paramref name="parameters"/>.
    /// </summary>
    /// <param name="parameters">An instance of parameters to pass to the command.</param>
    /// <returns>
    /// An instance of <see cref="SqlResultSetReader"/>.  Output parameters are populated only upon disposal of
    /// the returned instance.
    /// </returns>
    public virtual async Task<SqlResultSetReader> ExecuteReaderAsync(TParameters parameters) {
        ArgumentNullException.ThrowIfNull(parameters);
        Throw_If_Disposed();
        RowAccessor.WriteCommandParameters(SqlCommand, parameters);
        var reader = await SqlCommand.ExecuteReaderAsync();
        var ret = new SqlResultSetReader(RowAccessor.Owner, reader, SqlCommand, parameters);
        ret.Disposed += OnResultSetReaderDisposed;
        return ret;

    }

    /// <summary>
    /// Synchronous version of <see cref="ExecuteReaderAsync(TParameters)"/>.
    /// </summary>
    public virtual SqlResultSetReader ExecuteReader(TParameters parameters) {
        ArgumentNullException.ThrowIfNull(parameters);
        Throw_If_Disposed();
        RowAccessor.WriteCommandParameters(SqlCommand, parameters);
        var reader = SqlCommand.ExecuteReader();
        var ret = new SqlResultSetReader(RowAccessor.Owner, reader, SqlCommand, parameters);
        ret.Disposed += OnResultSetReaderDisposed;
        return ret;
    }

    private void OnResultSetReaderDisposed(SqlResultSetReader sender) {
        sender.Disposed -= OnResultSetReaderDisposed;
        RowAccessor.ReadCommandParameters(sender.SqlCommand, (TParameters)sender.Parameters);
    }
}

#if false

/// <summary>
/// Actually executes the command created by <see cref="SqlInvokable{TParameters}.With(TParameters, SqlConnection, SqlTransaction)"/>.
/// </summary>
/// <typeparam name="TParameters">
/// A type packaging up the command's parameter set.
/// </typeparam>
public interface ISqlInvoker<TParameters> : IDisposable where TParameters : class
{
    /// <summary>
    /// The command about to be executed.  May be customized or prepared before invoking one of the execute methods.
    /// </summary>
    SqlCommand SqlCommand { get; }

    /// <summary>
    /// Row accessor that maps <typeparamref name="TParameters"/>.
    /// </summary>
    SqlRowAccessor<TParameters> RowAccessor { get; }
    
    Task<int> ExecuteNonQueryAsync(TParameters parameters);
    Task<TRet> ExecuteScalarAsync<TRet>(TParameters parameters);
    Task<SqlResultSetReader> ExecuteReaderAsync(TParameters parameters);

    int ExecuteNonQuery(TParameters parameters);
    TRet ExecuteScalar<TRet>(TParameters parameters);
    SqlResultSetReader ExecuteReader(TParameters parameters);

    /// <summary>
    /// Default invoker created by <see cref="SqlInvokable{TParameters}.With(SqlConnection, SqlTransaction)"/>.
    /// Uses <see cref="RowAccessor"/> as default execution strategy.
    /// </summary>
    public class Default : ISqlInvoker<TParameters>
    {
        public SqlRowAccessor<TParameters> RowAccessor { get; }

        public Default(SqlInvokable<TParameters> invokable, SqlConnection sqlconn, SqlTransaction tx) {
            this.SqlCommand = new SqlCommand() {
                CommandText = invokable.CommandText,
                CommandType = invokable.CommandType,
                Connection = sqlconn,
                Transaction = tx
            };
            try {
                invokable.RowAccessor.CreateCommandParameters(this.SqlCommand);
                this.RowAccessor = invokable.RowAccessor;
            }
            finally {
                if (this.RowAccessor == null)
                    this.SqlCommand.Dispose();
            }
        }

        public SqlCommand SqlCommand { get; }
        
        public Task<int> ExecuteNonQueryAsync(TParameters parameters) => RowAccessor.ExecuteNonQueryAsync(SqlCommand, parameters);
        public async Task<TRet> ExecuteScalarAsync<TRet>(TParameters parameters) => (TRet)(await RowAccessor.ExecuteScalarAsync(SqlCommand, parameters));
        public Task<SqlResultSetReader> ExecuteReaderAsync(TParameters parameters) => RowAccessor.ExecuteReaderAsync(SqlCommand, parameters);

        public int ExecuteNonQuery(TParameters parameters) => RowAccessor.ExecuteNonQuery(SqlCommand, parameters);
        public TRet ExecuteScalar<TRet>(TParameters parameters) => (TRet)RowAccessor.ExecuteScalar(SqlCommand, parameters);
        public SqlResultSetReader ExecuteReader(TParameters parameters) => RowAccessor.ExecuteReader(SqlCommand, parameters);

        private bool _disposed;
        protected virtual void Dispose(bool disposing) {
            if (!_disposed) {
                if (disposing)
                    SqlCommand.Dispose();
                _disposed = true;
            }
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}

#endif
#endif
