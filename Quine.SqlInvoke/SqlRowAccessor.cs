#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Quine.SqlInvoke;

/// <summary>
/// Provides methods for binding between object instances and ADO.NET.
/// This class is thread-safe.
/// </summary>
/// <typeparam name="TRow">
/// The underlying class that describes a "row" mapped by this accessor.  Use <see cref="DBNull"/> for executing parameterless commands.
/// </typeparam>
/// <remarks>
/// Properties of this type may be declared in classes derived from <see cref="SqlContext"/>.  These will be automatically
/// initialized on context construction.
/// </remarks>
public sealed class SqlRowAccessor<TRow> where TRow : class
{
    /// <summary>
    /// Context that created this accessor.
    /// </summary>
    public SqlContext Owner { get; }

    /// <summary>
    /// Maps members annotated with <see cref="SqlMarshalAttribute"/>.
    /// </summary>
    public IReadOnlySet<SqlColumnAccessor> Columns { get; }

    /// <summary>
    /// Table or view that this accessor maps.  May be null for classes mapping command parameters or arbitrary queries.
    /// </summary>
    public QuotedName TableName { get; }

    /// <summary>
    /// Columns marked as key columns with <see cref="SqlTableAttribute"/>.
    /// This will be a subset of <see cref="Columns"/> or  <c>null</c> if no key is defined.
    /// </summary>
    public IReadOnlySet<SqlColumnAccessor> KeyColumns { get; }

    /// <summary>
    /// Builder for entity (single-row) operations that affect only <see cref="Columns"/> of this accessor.
    /// The operations are accessible only if <c>this</c> defines at least <see cref="TableName"/>.
    /// </summary>
    /// <exception cref="InvalidConfigurationException">This accessor does not define <see cref="KeyColumns"/> and/or <see cref="TableName"/>.</exception>
    public SqlEntityOperationsBuilder<TRow> EntityOperations => new SqlEntityOperationsBuilder<TRow>(this);

    internal SqlRowAccessor(
        SqlContext owner,
        IEnumerable<SqlColumnAccessor> columns,
        QuotedName tableName,
        IEnumerable<SqlColumnAccessor> keyColumns)
    {
        this.Owner = owner;
        this.TableName = tableName;

        // NB! Columns may be empty for parameterless commands.
        this.Columns = EnsureUnique(columns);
        if (keyColumns != null) {
            if (!keyColumns.Any())
                throw new InvalidConfigurationException("KeyColumns shall be null or non-empty.", type: typeof(TRow));
            this.KeyColumns = EnsureUnique(keyColumns);
            // Technically, a key column could be a computed column.  So don't forbid computed keys.
        }

        static HashSet<SqlColumnAccessor> EnsureUnique(IEnumerable<SqlColumnAccessor> accessors) {
            var ret = new HashSet<SqlColumnAccessor>(accessors, BySqlNameEqualityComparer.Instance);
            if (ret.Count != accessors.Count())
                throw new InvalidConfigurationException("Column with duplicate SqlName found in this column set.", type: typeof(TRow));
            return ret;
        }
    }

    private class BySqlNameEqualityComparer : IEqualityComparer<SqlColumnAccessor>
    {
        public static readonly BySqlNameEqualityComparer Instance = new();  // Prevent unnecessary allocations.
        private BySqlNameEqualityComparer() { }
        public bool Equals(SqlColumnAccessor x, SqlColumnAccessor y) => x?.SqlName.Equals(y?.SqlName) ?? false;
        public int GetHashCode(SqlColumnAccessor x) => x.SqlName.GetHashCode();
    }

    /// <summary>
    /// Builds a new accessor from <c>this</c> containing only the selected members.
    /// This method does not involve reflection or compilation and is thus "cheap" to invoke.
    /// </summary>
    /// <param name="selectors">
    /// An expression evaluating to an anonymous object constructor containing selected properties.
    /// Example: <c>x => new { x.Member1, x.Member2 }</c>.  All selected properties must already
    /// exist in <see cref="Columns"/>.
    /// </param>
    /// <param name="includeKey">
    /// If true, <see cref="KeyColumns"/>, if any, are added to the resulting column set.  This parameter
    /// has no effect when <c>this</c> does not define key columns.
    /// </param>
    /// <returns>
    /// A new instance of <see cref="SqlRowAccessor{T}"/> containing only selected columnsa and same
    /// <see cref="KeyColumns"/> as <c>this</c>.
    /// </returns>
    /// <remarks>
    /// Specifying an empty object for <paramref name="selectors"/> and true for <paramref name="includeKey"/> will
    /// project the accessor only to the key columns.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="selectors"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// The shape of <paramref name="selectors"/> expression is unsupported.
    /// </exception>
    /// <exception cref="InvalidConfigurationException">
    /// A selected column was not present in this column set. - OR - The resulting column list is empty.
    /// </exception>
    public SqlRowAccessor<TRow> Project(Expression<Func<TRow, object>> selectors, bool includeKey = false) {
        ArgumentNullException.ThrowIfNull(selectors);

        var members = ParseLambdaForProjection(selectors);
        var columns = new HashSet<SqlColumnAccessor>(Columns.Count, BySqlNameEqualityComparer.Instance);
        foreach (var mi in members) {
            var c = Columns.FirstOrDefault(x => x.MemberInfo == mi);
            if (c == null)
                throw new InvalidConfigurationException("Selected column is not present in this column set.", memberInfo: mi, type: typeof(TRow));
            columns.Add(c);
        }
        if (includeKey && KeyColumns != null)
            columns.UnionWith(KeyColumns);
        if (columns.Count == 0)
            throw new InvalidConfigurationException("No columns were selected for projection.");

        return new SqlRowAccessor<TRow>(Owner, columns, TableName, KeyColumns);
    }

    /// <summary>
    /// Adds all <see cref="Columns"/> as parameters to a command.  Columns that do not explicitly define a parameter
    /// direction are treated as input-only.
    /// </summary>
    /// <param name="sqlcmd">
    /// Command to add parameters to.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="sqlcmd"/> is null.</exception>
    /// <exception cref="InvalidConfigurationException">
    /// A column cannot be used as a parameter.  The command's parameter set is not changed unless all parameters
    /// pass validation.
    /// </exception>
    public void CreateCommandParameters(SqlCommand sqlcmd) {
        ArgumentNullException.ThrowIfNull(sqlcmd);
        sqlcmd.Parameters.AddRange(Columns.Select(x => x.CreateParameter()).ToArray());
    }

    /// <summary>
    /// Transfers values from an instance of <typeparamref name="TRow"/> to the command's parameter set.
    /// The parameter set must be initialized before calling this method; <see cref="CreateCommandParameters(SqlCommand)"/>.
    /// </summary>
    /// <param name="sqlcmd">Command with parameters collection populated.</param>
    /// <param name="source">Object to read values from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sqlcmd"/> or <paramref name="source"/> is null.</exception>
    public void WriteCommandParameters(SqlCommand sqlcmd, TRow source) {
        ArgumentNullException.ThrowIfNull(sqlcmd);
        ArgumentNullException.ThrowIfNull(source);
        foreach (var column in Columns.Where(c => c.ParameterIsInput))
            sqlcmd.Parameters[column.SqlName.P].Value = column.GetValue(source);
    }

    /// <summary>
    /// Transfers output parameters' values from the command's parameter set to an instance of <typeparamref name="TRow"/>.
    /// If the command was a "reader command", the output parameter values are available only after the reader has been
    /// disposed.
    /// </summary>
    /// <param name="sqlcmd">(Executed) command to read output parameter values from.</param>
    /// <param name="target">Object to write values to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sqlcmd"/> or <paramref name="target"/> is null.</exception>
    public void ReadCommandParameters(SqlCommand sqlcmd, TRow target) {
        ArgumentNullException.ThrowIfNull(sqlcmd);
        ArgumentNullException.ThrowIfNull(target);
        foreach (var column in Columns.Where(c => c.ParameterIsOutput))
            column.SetValue(target, sqlcmd.Parameters[column.SqlName.P].Value);
    }

    /// <summary>
    /// Transfers values from a data record to an instance of <typeparamref name="TRow"/>.  The data record need not
    /// supply values for all <see cref="Columns"/>.
    /// </summary>
    /// <param name="dr">Data record from which column values are read.</param>
    /// <param name="target">Object to write values to.</param>
    /// <returns>Number of values transferred to <paramref name="target"/>.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown by <c>IDataRecord</c> when the column name was not found.</exception>
    public int ReadDataRecord(IDataRecord dr, TRow target) {
        int ret = 0;
        for (int i = 0; i < dr.FieldCount; ++i) {
            var n = dr.GetName(i);
            var c = Columns.FirstOrDefault(x => x.SqlName.U == n);  // Names are returned unquoted!
            if (c != null) {
                var v = dr[i];
                c.SetValue(target, v);
                ++ret;
            }
        }
        return ret;
    }

    /// <summary>
    /// Creates a new instance of <see cref="SqlInvokable{TParameters}"/> from <paramref name="sqlCommand"/> and
    /// <paramref name="commandType"/>.  The instance has <c>this</c> as its <c>RowAccessor</c>.
    /// </summary>
    /// <returns>A new invokable instance.</returns>
    public SqlInvokable<TRow> CreateInvokable(string sqlCommand, CommandType commandType = CommandType.Text) =>
        new SqlInvokable<TRow>(sqlCommand, commandType) { RowAccessor = this };

    /// <summary>
    /// Creates a new instance of <typeparamref name="T"/> with <c>this</c> as its <c>RowAccessor</c>.
    /// </summary>
    /// <typeparam name="T">Type, inheriting from <see cref="SqlInvokable{TParameters}"/> to instantiate.</typeparam>
    /// <returns>A new invokable instance.</returns>
    public T CreateInvokable<T>() where T : SqlInvokable<TRow>, new() => new T() { RowAccessor = this };

    /// <summary>
    /// Used by <c>Project</c> methods.  Public only to enable testing.
    /// </summary>
    public static MemberInfo[] ParseLambdaForProjection(LambdaExpression lambda) {
        if (lambda.Parameters.Count != 1)
            throw new NotSupportedException("Invalid selector: lambda must have exactly 1 parameter.");
        
        var parameter = lambda.Parameters[0];
        if (lambda.Body is not NewExpression enew)
            throw new NotSupportedException("Invalid selector: lambda body must be a single new expression.");

        var ret = new MemberInfo[enew.Arguments.Count];
        int i = 0;
        foreach (var a in enew.Arguments) {
            if (a is not MemberExpression emember)
                throw new NotSupportedException("Invalid selector: anonymous object initializer must contain direct member access only.");
            if (emember.Expression != parameter)
                throw new NotSupportedException("Invalid selector: anonymous object initializer can only refer to the lambda parameter.");
            
            var member = emember.Member;
            if (!member.DeclaringType.IsAssignableFrom(typeof(TRow)))
                throw new NotSupportedException("Invalid selector: anonymous object initializer must directly reference a class member.");
            ret[i++] = member;
        }
        return ret;
    }
}
#endif
