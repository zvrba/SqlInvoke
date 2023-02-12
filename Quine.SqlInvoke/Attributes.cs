using System;
using System.Collections.Generic;
using System.Data;

namespace Quine.SqlInvoke
{

/// <summary>
/// Applying this attribute to a field or a property defines marshalling attributes to/from SQL for the member in question.
/// The <c>SqlInvoke</c> infrastructure creates value descriptors of appropriate types through reflection.
/// </summary>
/// <remarks>
/// "Unset" / "Unspecified" in the documentation of individual members means <c>null</c> for reference types,
/// or <c>int.MinValue</c> for integer types.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Field | AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = false)]
public class SqlMarshalAttribute : Attribute
{
    /// <summary>
    /// Constructor. See documentation of individual properties for description.
    /// </summary>
    public SqlMarshalAttribute(
        SqlDbType sqlDbType = (SqlDbType)int.MinValue,
        int size = int.MinValue,
        ParameterDirection direction = (ParameterDirection)int.MinValue,
        string sqlName = null,
        int ordinal = int.MinValue,
        bool isComputed = false) {
        Direction = direction;
        SqlName = sqlName;
        SqlDbType = sqlDbType;
        Size = size;
        Ordinal = ordinal;
        IsComputed = isComputed;
    }

    /// <summary>
    /// SQL name for the column or parameter.  If unspecified, the member name is used.
    /// </summary>
    public string SqlName { get; }

    /// <summary>
    /// Overrides the automatically inferred SQL type when specified.  MUST be specified when type mapping is ambiguous,
    /// i.e., to distinguish between char, varchar, nchar and nvarchar and various date/time types.
    /// </summary>
    public SqlDbType SqlDbType { get; }

    /// <summary>
    /// Sets maximum size for the parameter.  Must be specified for CHAR and BINARY types; use -1 for MAX.
    /// Should not be specified for other types.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// MUST be specified in classes used to build a <c>DataTable</c> for table-valued parameters.
    /// Ordinals must start at 0 and have increasing values without gaps.  The ordinal must match
    /// the ordinal of the column in <c>CREATE TYPE</c> statement.
    /// </summary>
    /// <seealso cref="SqlTypeConverterAttribute"/>
    public int Ordinal { get; }

    /// <summary>
    /// Default is input if left unspecified.
    /// </summary>
    public ParameterDirection Direction { get; }

    /// <summary>
    /// Marks the member as computed column.  Has no effect on parameters.
    /// </summary>
    public bool IsComputed { get; }
}

/// <summary>
/// This attribute should be applied to a class to map it to a table, view or a structured type created with
/// <c>CREATE TYPE</c> statement.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SqlTableAttribute : Attribute
{
    /// <summary>
    /// Constrcutor.
    /// </summary>
    /// <param name="name">Full SQL name (including schema) of the view, table or TYPE.</param>
    /// <param name="keys">List of member names that define a (possibly composite) key. May be empty.</param>
    public SqlTableAttribute(string name, params string[] keys) {
        this.Name = name;
        this.Keys = keys;
    }
    public string Name { get; }
    public IReadOnlyList<string> Keys { get; }
}

/// <summary>
/// May be applied to a class for "global" conversions of that type to SQL, or to a member for per-member
/// converter.  The type must implement <see cref="ISqlValueConverter{TMember, TSql}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Field | AttributeTargets.Property)]
public class SqlTypeConverterAttribute : Attribute
{
    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="converterType">
    /// Converter type to use for conversion to a primitive SQL value.
    /// The type must implement an <see cref="ISqlValueConverter{TMember, TSql}"/>.
    /// </param>
    public SqlTypeConverterAttribute(Type converterType) {
        this.ConverterType = converterType;
    }
    public Type ConverterType { get; }
}
}   // namespace

