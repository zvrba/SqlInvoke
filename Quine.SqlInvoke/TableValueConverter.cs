#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Quine.SqlInvoke;

internal sealed class TableValueConverter<TRow> : ISqlValueConverter<IEnumerable<TRow>, DataTable> where TRow : class
{
    private readonly SqlRowAccessor<TRow> rd;
    private readonly DataTable dtprototype;

    public QuotedName SqlName { get; }
    public SqlDbType SqlDbType => SqlDbType.Structured;
    public Type SqlType => typeof(DataTable);

    internal TableValueConverter(SqlRowAccessor<TRow> rd, string sqlTableTypeName) {
        if (string.IsNullOrEmpty(sqlTableTypeName))
            throw new InvalidConfigurationException("Table-valued type must define SqlName corresponding to that used in CREATE TYPE.", type: typeof(TRow));
        this.rd = rd;
        this.SqlName = sqlTableTypeName;
        this.dtprototype = CreateDataTable();   // Depends on od.
    }

    private DataTable CreateDataTable() {
        var ordinals = rd.Columns.Select(x => x.Ordinal).ToArray();
        Array.Sort(ordinals);
        for (int i = 0; i < ordinals.Length; ++i)
            if (ordinals[i] != i)
                throw new InvalidConfigurationException("Invalid ordinal detected.", type: typeof(TRow));

        var dt = new DataTable();
        foreach (var ma in rd.Columns)
            ma.CreateDataColumn(dt);
        return dt;
    }

    public DataTable ConvertToSql(IEnumerable<TRow> value) {
        var dt = dtprototype.Clone();
        if (value != null) {
            foreach (var v in value)
                dt.Rows.Add(ToObjectArray(v));
        }
        return dt;
    
        object[] ToObjectArray(TRow instance) {
            if (instance == null)   // TODO: Parameter to exception!
                throw new InvalidValueException(null, string.Format("Cannot convert null instance of {0} to DataRow.", typeof(TRow).FullName));
            var os = new object[rd.Columns.Count];
            foreach (var column in rd.Columns) {
                int i = column.Ordinal;
                os[i] = column.GetValue(instance);
            }
            return os;
        }
    }

    public IEnumerable<TRow> ConvertToMember(DataTable value) =>
        throw new NotSupportedException();
}
#endif
