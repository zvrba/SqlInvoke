using System;

namespace Quine.SqlInvoke {

/// <summary>
/// <para>
/// Converts values between any member type and a supported SQL type.  The implementation must be stateless,
/// reentrant and declare a public parameterless constructor.
/// Conversions to/from SQL nulls (<c>DbNull</c>) are transparently handled before calling into
/// this interface.  Thus, a converter for <c>int</c> as member type will handle both <c>int</c> and <c>int?</c> members.
/// </para>
/// <para>
/// The interface provides correct default implementations for most members of <see cref="ISqlValueDescriptor"/> and
/// these should not be overridden.  Client code need only provide implementation for <c>SqlDbType</c> and, for
/// <c>CHAR</c> and <c>BINARY</c> types, <c>Size</c>.
/// </para>
/// </summary>
/// <typeparam name="TMember">Actual type of the member.</typeparam>
/// <typeparam name="TSql">Native SQL type that the member type maps to.</typeparam>
public interface ISqlValueConverter<TMember, TSql> : ISqlValueDescriptor
{
#if NET6_0_OR_GREATER
    QuotedName ISqlValueDescriptor.SqlName => default;
    int ISqlValueDescriptor.Size => int.MinValue;
    Type ISqlValueDescriptor.SqlType => typeof(TSql);
    bool ISqlValueDescriptor.IsNullable => false;
    int ISqlValueDescriptor.Ordinal => int.MinValue;
    Type ISqlValueDescriptor.MemberType => typeof(TMember);
    Type ISqlValueDescriptor.ConverterType => null;
#endif

    /// <summary>
    /// Converts an SQL value to member value.
    /// </summary>
    /// <param name="value">SQL value to convert.  This will never be null.</param>
    /// <returns>
    /// The converted native value.  Must never return null.
    /// </returns>
    /// <exception cref="NotSupportedException">The conversion is not supported.</exception>
    /// <exception cref="FormatException">The value is invalid for the type.</exception>
    TMember ConvertToMember(TSql value);

    /// <summary>
    /// Converts member value to SQL value.
    /// </summary>
    /// <param name="value">Member value to convert.</param>
    /// <returns>
    /// The converted SQL value.  Must never return null.
    /// </returns>
    /// <exception cref="NotSupportedException">The conversion is not supported.</exception>
    /// <exception cref="FormatException">The value is invalid for the type.</exception>
    TSql ConvertToSql(TMember value);
}
}   // namespace
