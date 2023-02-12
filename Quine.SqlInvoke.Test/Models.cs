using System;
using System.Collections.Generic;
using System.Data;

/// <summary>
/// This file and <c>TestSqlContext.cs</c> showcase the declarative features of SqlInvoke framework.
/// </summary>
namespace Quine.SqlInvoke.Test.Models;

//
// A default converter may be attached to a type.
// See below on how to implement a converter.
//
[SqlTypeConverter(typeof(SomeEnumConverter1))]
public enum SomeEnum
{
    AValue, BValue
}

//
// Maps a concrete table in the database.  Members being mapped to SQL must be annotated with SqlMarshalAttribute;
// public fields and properties are supported.  This mapping uses fields, subsequent examples use properties.
// Views and table-valued types are mapped in the same way.
//
[SqlTable(TableName)]
public class NullableConversionModel_Raw : IEquatable<NullableConversionModel_Raw>
{
    //
    // The table name.  SQLServer name quoting should not be used; names are automatically quoted
    // with [] when passed to the server.  See QutedName struct.
    //
    public const string TableName = "dbo.NullableValueConversionTest";

    //
    // Automatically converted to/from NULL column values.
    //
    [SqlMarshal] public int? Id;
    
    
    [SqlMarshal] public float Fv;

    //
    // string and byte[] types must explicitly declare length because too large values are
    // silently truncated when passed as parameters to SQL statements.  Length overflow is
    // automatically checked by the framework.
    //
    [SqlMarshal(SqlDbType.Char, 2)] public string Ev;
    
    public bool Equals(NullableConversionModel_Raw other) => other is not null && Id.Equals(other.Id) && string.Equals(Ev, other.Ev) && Fv == other.Fv;
    public override bool Equals(object other) => other is NullableConversionModel_Raw m && Equals(m);
    public override int GetHashCode() => HashCode.Combine(Id, Ev, Fv);

    // - Id is null or not. [7]
    // - Ev is null [11] or "A" [even] or "B" [odd] 
    // - Fv is null [13] or a random real in 0,1
    public static List<NullableConversionModel_Raw> GenerateData(bool uniqueKeys) {
        var ret = new List<NullableConversionModel_Raw>();
        for (int i = 0; i < 7 * 11 * 13; ++i) {
            var m = new NullableConversionModel_Raw();

            if (uniqueKeys) m.Id = i;
            else m.Id = i % 7 == 0 ? null : i % 10;

            if (i % 11 == 0) m.Ev = null;
            else if (i % 2 == 0) m.Ev = i % 3 == 1 ? "A1" : "B1";
            else m.Ev = i % 3 == 2 ? "B2" : "A2";

            m.Fv = i / 1024.0f;   // non-nullable, deterministic and exact. (pseudo-key)

            ret.Add(m);
        }
        return ret;
    }
}

//
// The same physical table can have multiple mappings.  Here, Id is defined as a key column.
// Composite keys are supported; all column names must be mentioned after the table name.
//
[SqlTable(NullableConversionModel_Raw.TableName, nameof(Id))]
public class NullableConversionModel1
{
    [SqlMarshal]
    public int? Id { get; set; }

    //
    // Converter declared on SomeEnum type is automatically applied, together with conversion to/from SQL NULL.
    //
    [SqlMarshal]
    public SomeEnum? Ev { get; set; }

    [SqlMarshal]
    public float Fv { get; set; }
}

//
// Another twist on the same physical table.
//
[SqlTable(NullableConversionModel_Raw.TableName, nameof(Id))]
public class NullableConversionModel2
{
    [SqlMarshal]
    public int Id { get; set; }

    //
    // Type-level converter can be overridden on a per-member basis.
    // If a type-level converter is not declared, the member MUST specify it.
    //
    [SqlTypeConverter(typeof(SomeEnumConverter2))]
    [SqlMarshal]
    public SomeEnum Ev { get; set; }

    [SqlMarshal]
    public float Fv { get; set; }
}

//
// Records are supported!  Marshalled properties must have a setter though.
// This table also has a composite primary key consisting of columns Id1 and Id2.
//
[SqlTable(TableName, nameof(Id1), nameof(Id2))]
public record class EntityConversionsModel
{
    public const string TableName = "EntityConversionsTest";
    [SqlMarshal] public int Id1 { get; init; }
    [SqlMarshal] public int Id2 { get; init; }
    [SqlMarshal] public SomeEnum Ev { get; init; }
    [SqlMarshal] public float Fv { get; init; }
}

//
// This defines a table-valued type.  The table name must match the name used in CREATE TYPE statement.
// Ordinals on properties must match the declaration order of columns in CREATE TYPE.
//
[SqlTable("dbo.SelectorList")]
public record class SelectorListModel
{
    [SqlMarshal(ordinal: 0)] public int Id { get; init; }
    [SqlMarshal(ordinal: 1)] public SomeEnum Ev { get; init; }
}

//
// Converter examples.  A converter can be applied either on a type or a member.
//

public sealed class SomeEnumConverter1 : ISqlValueConverter<SomeEnum, string>
{
    public SqlDbType SqlDbType => SqlDbType.Char;
    public int Size => 2;

    public SomeEnum ConvertToMember(string value) {
        switch (value) {
        case "A1": return SomeEnum.AValue;
        case "B1": return SomeEnum.BValue;
        default: throw new FormatException("Invalid enum value " + value);
        }
    }

    public string ConvertToSql(SomeEnum value) {
        switch (value) {
        case SomeEnum.AValue: return "A1";
        case SomeEnum.BValue: return "B1";
        default: throw new FormatException("Invalid enum value " + (int)value);
        }
    }
}

public sealed class SomeEnumConverter2 : ISqlValueConverter<SomeEnum, string>
{
    public SqlDbType SqlDbType => SqlDbType.Char;
    public int Size => 2;

    public SomeEnum ConvertToMember(string value) {
        switch (value) {
        case "A2": return SomeEnum.AValue;
        case "B2": return SomeEnum.BValue;
        default: throw new FormatException("Invalid enum value " + value);
        }
    }

    public string ConvertToSql(SomeEnum value) {
        switch (value) {
        case SomeEnum.AValue: return "A2";
        case SomeEnum.BValue: return "B2";
        default: throw new FormatException("Invalid enum value " + (int)value);
        }
    }
}

//
// Examples of defining dynamic SQL statements.
// Use DBNull as parameter type block if the statement has no parameters.
//

public abstract record TableStatement : SqlInvokable<DBNull>
{
    public QuotedName TableName { get; init; }
}

public record TruncateTable : TableStatement
{
    public override string CommandText => $"TRUNCATE TABLE {TableName.Q}";
    public override CommandType CommandType => CommandType.Text;
}

public record SelectSimple : TableStatement
{
    public override string CommandText => $"SELECT {Columns} FROM {TableName.Q} {Condition}";
    public override CommandType CommandType => CommandType.Text;
    public string Condition { get; init; }
    public string Columns { get; init; }
}
