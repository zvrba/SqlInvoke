using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Quine.SqlInvoke
{

/// <summary>
/// Describes a mapping between a member and a primitive SQL type.
/// </summary>
public interface ISqlValueDescriptor
{
#if NET6_0_OR_GREATER
    internal static readonly IReadOnlyDictionary<Type, IReadOnlyList<SqlDbType>> TypeMap = new Dictionary<Type, IReadOnlyList<SqlDbType>>() {
        { typeof(long), new SqlDbType[] { SqlDbType.BigInt } },
        { typeof(byte[]), new SqlDbType[] { SqlDbType.VarBinary, SqlDbType.Binary, SqlDbType.Timestamp } },
        { typeof(bool), new SqlDbType[] { SqlDbType.Bit } },
        { typeof(string), new SqlDbType[] { SqlDbType.Char, SqlDbType.VarChar, SqlDbType.NChar, SqlDbType.NVarChar, SqlDbType.Xml } },
        { typeof(DateTime), new SqlDbType[] { SqlDbType.DateTime2, SqlDbType.SmallDateTime } },
        { typeof(DateTimeOffset), new SqlDbType[] { SqlDbType.DateTimeOffset } },
        { typeof(double), new SqlDbType[] { SqlDbType.Float } },
        { typeof(int), new SqlDbType[] { SqlDbType.Int } },
        { typeof(float), new SqlDbType[] { SqlDbType.Real } },
        { typeof(short), new SqlDbType[] { SqlDbType.SmallInt } },
        { typeof(TimeSpan), new SqlDbType[] { SqlDbType.Time } },
        { typeof(byte), new SqlDbType[] { SqlDbType.TinyInt } },
        { typeof(Guid), new SqlDbType[] { SqlDbType.UniqueIdentifier } },
        { typeof(DataTable), new SqlDbType[] { SqlDbType.Structured } }
    };

    internal static readonly IReadOnlySet<SqlDbType> SizedTypes = new HashSet<SqlDbType>() {
        SqlDbType.VarBinary, SqlDbType.Binary,
        SqlDbType.Char, SqlDbType.NChar,
        SqlDbType.VarChar, SqlDbType.NVarChar
    };

    /// <summary>
    /// Finds interface type satisfying generic definition <paramref name="gdef"/>.
    /// </summary>
    /// <param name="source">Type which to search for interface implementation.</param>
    /// <param name="gdef">Open generic interface to look for.</param>
    /// <param name="itype">The constructed generic interface type that was found.</param>
    /// <param name="iargs">Generic arguments of <c>itype</c>.</param>
    /// <returns>
    /// The number of definitions found.
    /// </returns>
    internal static int FindInterface(Type source, Type gdef, out Type itype, out Type[] iargs) {
        itype = null;
        iargs = null;
        var intfs = source.GetInterfaces().Where(x => x.IsConstructedGenericType && x.GetGenericTypeDefinition() == gdef).ToArray();
        if (intfs.Length == 1) {
            itype = intfs[0];
            iargs = itype.GetGenericArguments();
        }
        else {
            itype = null;
            iargs = null;
        }
        return intfs.Length;
    }
#endif
    /// <summary>
    /// SQL name of column or parameter.
    /// If this is a parameter name, it should not be prefixed with <c>@</c>; it is done automatically.
    /// If this is a converter for a TVP, this must be the name of the SQL type as used in a <c>CREATE TYPE</c> statement.
    /// Other converters must use a non-empty name (e.g., name of the implementing class), but it is not used
    /// for any purpose.
    /// </summary>
    QuotedName SqlName { get; }

    /// <summary>
    /// DB type. <c>int.MinValue</c> is "unset".
    /// </summary>
    SqlDbType SqlDbType { get; }

    /// <summary>
    /// Size. <c>int.MinValue</c> is "unset".  MUST be specified for all <c>*CHAR</c> and <c>*BINARY</c> types.
    /// Use -1 to specify <c>MAX</c> size.  Other types MUST NOT specify size.
    /// </summary>
    int Size { get; }

    /// <summary>
    /// .NET representational type; must be natively supported by <see cref="IDataRecord"/>.
    /// If the actual member is a <c>Nullable</c>, this is the underlying value type as
    /// SQL always supports NULLs.
    /// </summary>
    Type SqlType { get; }

    /// <summary>
    /// True if <see cref="MemberType"/> is actually a <c>Nullable</c> value type.  This must be false
    /// for an <see cref="ISqlValueConverter{TMember, TSql}"/>.
    /// </summary>
    bool IsNullable { get; }

    /// <summary>
    /// Ordinal; <c>int.MinValue</c> is "unset".  Required to be set only when defining columns of a <c>DataTable</c>.
    /// </summary>
    int Ordinal { get; }

    /// <summary>
    /// Member type which this descriptor maps to SQL value.  This will be the same as <see cref="SqlType"/>
    /// unless 1) a converter is applied, in which case this is the .NET type, whereas <c>SqlType</c> is the
    /// SQL type after conversion, 2) the member type is a <c>Nullable{T}</c> in which case <c>SqlType</c>
    /// is the underlying type (SQL types are always nullable).
    /// </summary>
    Type MemberType { get; }

    /// <summary>
    /// Converter type; this shall be a constructed <see cref="ISqlValueConverter{TMember, TSql}"/>.
    /// Converters cannot be chained, i.e., if this is a converter type, this property must be <c>null</c>.
    /// <c>SqlDbType.Structured</c> MUST NOT specify a converter: this is a TVP passing protocol and a
    /// built-in converter is used.
    /// </summary>
    Type ConverterType { get; }
}
}   // namespace

