#if NET6_0_OR_GREATER
using System;
using System.Data;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;

namespace Quine.SqlInvoke;

/// <summary>
/// Defines a "prototype" for an SQL command.  The class may be inherited to enable dynamic construction of
/// the command text from other properties on the derived record.
/// </summary>
/// <typeparam name="TParameters">
/// A type packaging up the command's parameter set.  Use <c>DBNull</c> for parameterless commands.
/// </typeparam>
/// <remarks>
/// <para>
/// Derived classes must have a public parameterless constructor.  Valid instances of this class can be constructed
/// only by <see cref="SqlRowAccessor{TRow}.CreateInvokable{T}"/> with <c>TRow</c> being of the same type as
/// <c>TParameters</c>.  For the most common use-case of fixed-text command with parameters,
/// <see cref="SqlRowAccessor{TRow}.CreateInvokable(string, CommandType)"/> should be used.
/// </para>
/// <para>
/// The indirection through <c>SqlRowAccessor</c> for instance creation exists so that procedurally-generated
/// command text works correctly for row accessors that are projections of the full row accessor for <c>TParameters</c>.
/// </para>
/// <para>
/// This class exposes <c>Execute*</c> methods and <see cref="Self"/> member for simple, one-time invocations of the
/// command, without going through an <see cref="SqlExecutable{TParameters}"/>.  For multiple invocations, an
/// executable (<see cref="CreateExecutable(SqlConnection, SqlTransaction)"/>) should be created.
/// </para>
/// </remarks>
public record SqlInvokable<TParameters> where TParameters : class
{
    /// <summary>
    /// For derived classes and internal use only.
    /// </summary>
    internal protected SqlInvokable() { }

    /// <summary>
    /// Utility constructor for the most common case of fixed-text commands.
    /// </summary>
    internal SqlInvokable(string commandText, CommandType commandType) {
        _CommandText = commandText;
        _CommandType = commandType;
    }

    /// <summary>
    /// Returns the current text for the command.
    /// </summary>
    public virtual string CommandText => Throw_If_Unset(_CommandText, nameof(CommandText));
    private readonly string _CommandText;

    /// <summary>
    /// Returns the command type.  Default is <c>CommandType.Text</c>.
    /// </summary>
    public virtual CommandType CommandType => Throw_If_Unset(_CommandType, nameof(CommandType));
    private readonly CommandType _CommandType;

    private static T Throw_If_Unset<T>(T value, string propname) {
        if (value.Equals(default))
            throw new NotImplementedException($"Invalid implementation of {propname}.");
        return value;
    }

    /// <summary>
    /// Initialized by the framework as part of construction.
    /// </summary>
    public SqlRowAccessor<TParameters> RowAccessor { get; internal set; }

    /// <summary>
    /// Creates an instance of <see cref="SqlExecutable{TParameters}"/> from <c>this</c> and associates its SQL
    /// command with <paramref name="sqlconn"/> and <paramref name="tx"/>.  Default implementation returns an
    /// instance of <see cref="SqlExecutable{TParameters}"/>.
    /// </summary>
    /// <param name="sqlconn">Connection to associate the command with.</param>
    /// <param name="tx">Transaction to associate the command with; may be null.</param>
    /// <returns>
    /// An initialized instance of <see cref="SqlExecutable{TParameters}"/>, or a derived class, which must be
    /// disposed of when no longer needed.
    /// </returns>
    public virtual SqlExecutable<TParameters> CreateExecutable(SqlConnection sqlconn, SqlTransaction tx = null) =>
        new SqlExecutable<TParameters>(RowAccessor, CreateSqlCommand(sqlconn, tx));

    /// <summary>
    /// Creates an instance of SQL command from <c>this</c>,  using <typeparamref name="TParameters"/> as the
    /// command's parameter set.  The default implementation should suffice in almost all cases.
    /// </summary>
    /// <param name="sqlconn">Connection to associate the command with.</param>
    /// <param name="tx">Transaction to associate the command with; may be null.</param>
    /// <returns>
    /// An instance of SQL command initialized with connection, transaction, <see cref="CommandText"/> and <see cref="CommandType"/>.
    /// </returns>
    protected virtual SqlCommand CreateSqlCommand(SqlConnection sqlconn, SqlTransaction tx) {
        var cmd = new SqlCommand() {
            CommandText = this.CommandText,
            CommandType = this.CommandType,
            Connection = sqlconn,
            Transaction = tx
        };
        var ok = true;
        try {
            RowAccessor.CreateCommandParameters(cmd);
            return cmd;
        }
        catch {
            ok = false;
            throw;
        }
        finally {
            if (!ok)
                cmd.Dispose();
        }
    }

    /// <summary>
    /// Available only for types derived from <c>SqlInvokable</c> that act as their own parameter sets.
    /// The struct exposes <c>Execute*</c> methods that use <c>this</c> for parameters.
    /// </summary>
    /// <exception cref="NotSupportedException">This instance cannot act as its own parameter set.</exception>
    public SelfExecutable Self => new(this);

    public async Task<int> ExecuteNonQueryAsync(TParameters parameters, SqlConnection sqlconn, SqlTransaction tx = null) {
        using var e = CreateExecutable(sqlconn, tx);
        return await e.ExecuteNonQueryAsync(parameters);
    }

    public int ExecuteNonQuery(TParameters parameters, SqlConnection sqlconn, SqlTransaction tx = null) {
        using var e = CreateExecutable(sqlconn, tx);
        return e.ExecuteNonQuery(parameters);
    }

    public async Task<T> ExecuteScalarAsync<T>(TParameters parameters, SqlConnection sqlconn, SqlTransaction tx = null) {
        using var e = CreateExecutable(sqlconn, tx);
        return await e.ExecuteScalarAsync<T>(parameters);
    }

    public T ExecuteScalar<T>(TParameters parameters, SqlConnection sqlconn, SqlTransaction tx = null) {
        using var e = CreateExecutable(sqlconn, tx);
        return e.ExecuteScalar<T>(parameters);
    }

    public async Task<SqlResultSetReader> ExecuteReaderAsync(TParameters parameters, SqlConnection sqlconn, SqlTransaction tx = null) {
        var e = CreateExecutable(sqlconn, tx);
        var r = await e.ExecuteReaderAsync(parameters);
        r.Disposed += OnResultSetReaderDisposed;
        return r;
    }

    public SqlResultSetReader ExecuteReader(TParameters parameters, SqlConnection sqlconn, SqlTransaction tx = null) {
        var e = CreateExecutable(sqlconn, tx);
        var r = e.ExecuteReader(parameters);
        r.Disposed += OnResultSetReaderDisposed;
        return r;
    }

    private void OnResultSetReaderDisposed(SqlResultSetReader sender) {
        // XXX: Technically, we should dispose of the SqlExecutable, but it only contains SqlCommand.
        // TODO: Make reader contain SqlExecutable instead of SqlCommand.
        sender.Disposed -= OnResultSetReaderDisposed;
        sender.SqlCommand.Dispose();
    }

    public readonly struct SelfExecutable {
        private readonly SqlInvokable<TParameters> invokable;
        private readonly TParameters parameters;
        internal SelfExecutable(SqlInvokable<TParameters> invokable) {
            if (invokable is TParameters parameters) {
                this.invokable = invokable;
                this.parameters = parameters;
            }
            else {
                throw new NotSupportedException("This instance cannot act as its own parameter set.");
            }
        }

        public Task<int> ExecuteNonQueryAsync(SqlConnection sqlconn, SqlTransaction tx = null) =>
            invokable.ExecuteNonQueryAsync(parameters, sqlconn, tx);
        public int ExecuteNonQuery(SqlConnection sqlconn, SqlTransaction tx = null) =>
            invokable.ExecuteNonQuery(parameters, sqlconn, tx);
        public Task<T> ExecuteScalarAsync<T>(SqlConnection sqlconn, SqlTransaction tx = null) =>
            invokable.ExecuteScalarAsync<T>(parameters, sqlconn, tx);
        public T ExecuteScalar<T>(SqlConnection sqlconn, SqlTransaction tx = null) =>
            invokable.ExecuteScalar<T>(parameters, sqlconn, tx);
        public Task<SqlResultSetReader> ExecuteReaderAsync(SqlConnection sqlconn, SqlTransaction tx = null) =>
            invokable.ExecuteReaderAsync(parameters, sqlconn, tx);
        public SqlResultSetReader ExecuteReader(SqlConnection sqlconn, SqlTransaction tx = null) =>
            invokable.ExecuteReader(parameters, sqlconn, tx);
    }
}
#endif
