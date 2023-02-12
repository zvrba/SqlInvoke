#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Quine.SqlInvoke;

using E = Expression;

/// <summary>
/// A two-way bridge between class members and SQL values.
/// </summary>
/// <seealso cref="ISqlValueDescriptor"/>
public sealed class SqlColumnAccessor : ISqlValueDescriptor
{
    private readonly Func<object, object> getDelegate;
    private readonly Action<object, object> setDelegate;
    
    internal readonly Type ContainingType;  // For most specific type in expressions.
    internal readonly MemberInfo MemberInfo;
    internal readonly ParameterDirection ParameterDirection;
    internal readonly ISqlValueDescriptor ConverterInstance;

    public QuotedName SqlName { get; }
    public SqlDbType SqlDbType { get; }
    public int Size { get; }
    public int Ordinal { get; }
    
    public Type MemberType { get; }
    public bool IsNullable { get; }
    public Type SqlType { get; }
    
    public Type ConverterType { get; }

    /// <summary>
    /// True if the mapped member is writeable.
    /// </summary>
    public bool CanWrite => setDelegate != null;

    public bool IsComputed { get; }

    internal Type UnderlyingMemberType => Nullable.GetUnderlyingType(MemberType) ?? MemberType;

    internal bool ParameterIsInput =>
        ParameterDirection == ParameterDirection.Input ||
        ParameterDirection == ParameterDirection.InputOutput ||
        (int)ParameterDirection == int.MinValue;

    internal bool ParameterIsOutput =>
        ParameterDirection == ParameterDirection.Output ||
        ParameterDirection == ParameterDirection.InputOutput ||
        ParameterDirection == ParameterDirection.ReturnValue;

    internal SqlColumnAccessor(
        SqlContext factory,
        Type containingType,
        MemberInfo mi)
    {
        var sqma = (SqlMarshalAttribute)mi.GetCustomAttribute(typeof(SqlMarshalAttribute));
        if (sqma == null)
            throw new InvalidConfigurationException("The member does not have SqlMarshalAttribute.", memberInfo: mi);
        if (!mi.DeclaringType.IsAssignableFrom(containingType))   // Inheritance.
            throw new InvalidConfigurationException("Type is not valid for the member's declaring type.", memberInfo: mi, type: containingType);
        
        this.ContainingType = containingType;
        this.MemberInfo = mi;
        this.ParameterDirection = sqma.Direction;

        this.SqlName = sqma.SqlName ?? mi.Name;
        this.SqlDbType = sqma.SqlDbType;
        this.Size = sqma.Size;
        this.Ordinal = sqma.Ordinal;
        this.IsComputed = sqma.IsComputed;
        this.ConverterType = mi.GetCustomAttribute<SqlTypeConverterAttribute>()?.ConverterType;

        this.MemberType = mi switch {
            FieldInfo fi => fi.FieldType,
            PropertyInfo pi => pi.PropertyType,
            _ => throw new InvalidConfigurationException($"Unsupported member kind {MemberInfo.GetType().Name}.", memberInfo: MemberInfo)
        };

        if (MemberType == typeof(DataTable))
            throw new InvalidConfigurationException("DataTable cannot be used directly - use IEnumerable<> instead.", memberInfo: MemberInfo, valueDescriptor: this);

        // VALUE DESCRIPTOR FULLY SET ON CLASS HERE; IValueDescriptor is valid now.

        // Order of checks is important. IEnumerable<> is the most special case.
        int interfacecount;
        if (MemberType.IsConstructedGenericType && MemberType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            if (ConverterType != null)
                throw new InvalidConfigurationException("Converter cannot be specified for IEnumerable<> members.", valueDescriptor: this);
            if (SqlDbType != SqlDbType.Structured)
                throw new InvalidConfigurationException("SqlDbType.Structured must be explicitly set on IEnumerable<> members.", valueDescriptor: this);
            ConverterInstance = factory.GetConverter(MemberType, t => CreateIEnumerableConverter(factory));
        }
        else {                                  // Not an IEnumerable<>
            if (SqlType == typeof(DataTable) || SqlDbType == SqlDbType.Structured)
                throw new InvalidConfigurationException("Use IEnumerable<> to pass structured table values.", valueDescriptor: this);

            if (ConverterType != null) {        // Converter on member.
                ConverterInstance = factory.GetConverter(ConverterType, CreateTypeConverter);
            }
            else {                              // No converter, or fallback converter on type.
                var nut = Nullable.GetUnderlyingType(MemberType);
                SqlType = nut ?? MemberType;
                this.IsNullable = nut != null;

                if (SqlType == typeof(DataTable) || SqlDbType == SqlDbType.Structured)
                    throw new InvalidConfigurationException("Use IEnumerable<> to pass structured table values.", valueDescriptor: this);

                if (!ISqlValueDescriptor.TypeMap.ContainsKey(SqlType)) {    // Type needs converter.
                    ConverterType = (IsNullable ? nut : MemberType).GetCustomAttribute<SqlTypeConverterAttribute>()?.ConverterType;
                    if (ConverterType == null)
                        throw new InvalidConfigurationException("Member is not a primitive SQL type and does not have a SqlTypeConverterAttribute.", valueDescriptor: this);
                    ConverterInstance = factory.GetConverter(ConverterType, CreateTypeConverter);
                }
            }
        }

        // APPLY CONVERTER TYPES.

        if (ConverterInstance != null) {
            interfacecount = ISqlValueDescriptor.FindInterface(ConverterInstance.GetType(), typeof(ISqlValueConverter<,>), out var itype, out var iargs);
            if (interfacecount != 1)
                throw new InvalidConfigurationException("ConverterType does not have exactly one implementation of ISqluValueConverter<>.", valueDescriptor: this);
            if (!itype.IsInstanceOfType(ConverterInstance))
                throw new InvalidConfigurationException($"Inconsistency detected: ConverterInstance does not implement {itype.FullName}.", valueDescriptor: this);
            if (iargs[0] != ConverterInstance.MemberType || iargs[1] != ConverterInstance.SqlType)
                throw new InvalidConfigurationException("ConverterType has inconsistent values for MemberType and/or SqlType.", valueDescriptor: this);
            if (ConverterInstance.MemberType != UnderlyingMemberType)
                throw new InvalidConfigurationException("ConverterType.MemberType does not match this.MemberType.", valueDescriptor: this);
            if (ConverterInstance.ConverterType != null)
                throw new InvalidConfigurationException("ConverterType cannot define another converter.", valueDescriptor: this);
            if (!Enum.IsDefined(typeof(SqlDbType), ConverterInstance.SqlDbType))
                throw new InvalidConfigurationException("ConverterType does not define a valid SqlDbType.", valueDescriptor: this);
            if (!ISqlValueDescriptor.TypeMap.ContainsKey(ConverterInstance.SqlType))
                throw new InvalidConfigurationException("ConverterType.SqlType is not a a primitive SQL type.", valueDescriptor: this);

            ConverterType = itype;  // Make this type an ISqlValueConverter<>
            SqlDbType = ConverterInstance.SqlDbType;
            Size = ConverterInstance.Size;
            SqlType = ConverterInstance.SqlType;
        }

        // VALIDATE THE WHOLE SETUP.  APPLY DEFAULTS IF UNAMBIGUOUS.
        if (MemberType == null)
            throw new InvalidConfigurationException("MemberType undefined.", valueDescriptor: this);
        if (Ordinal < 0 && Ordinal != int.MinValue)
            throw new InvalidConfigurationException("Invalid ordinal: must be unspecified or non-negative.", valueDescriptor: this);
        if (SqlType == null)
            throw new InvalidConfigurationException("SqlType undefined.", valueDescriptor: this);
        if (!ISqlValueDescriptor.TypeMap.TryGetValue(SqlType, out var compatibleSqlDbTypes))
            throw new InvalidConfigurationException("SqlType has no direct mapping to an SQL type.  Converter must be specified.", type: SqlType, valueDescriptor: this);
        if (!Enum.IsDefined(typeof(SqlDbType), SqlDbType)) {
            if (compatibleSqlDbTypes.Count > 1)
                throw new InvalidConfigurationException("SqlType has ambiguous mapping; SqlDbType must be specified.", valueDescriptor: this, type: SqlType);
            SqlDbType = compatibleSqlDbTypes[0];
        } else if (!compatibleSqlDbTypes.Contains(SqlDbType)) {
            throw new InvalidConfigurationException("Incompatible or invalid SqlDbType specified.", valueDescriptor: this);
        }
        if (ISqlValueDescriptor.SizedTypes.Contains(SqlDbType)) {
            if (Size == int.MinValue)
                throw new InvalidConfigurationException("BINARY and CHAR SQL type must specify a size; use -1 for MAX.", valueDescriptor: this);
            if (Size < 1 && Size != -1)
                throw new InvalidConfigurationException("Invalid size.  Must be a positive number or -1 (MAX).", valueDescriptor: this);
        } else if (Size != int.MinValue) {
            throw new InvalidConfigurationException("Only BINARY and CHAR types can specify a size.", valueDescriptor: this);
        }

        // GENERATE ACCESSOR METHODS.
        this.getDelegate = BuildGetDelegate();
        this.setDelegate = BuildSetDelegate();
    }

    // Called by ctor.
    ISqlValueDescriptor CreateTypeConverter(Type converterType) {
        var interfacecount = ISqlValueDescriptor.FindInterface(converterType, typeof(ISqlValueConverter<,>), out var itype, out var iargs);
        if (interfacecount != 1)
            throw new InvalidConfigurationException("ConverterType does not have exactly one implementation of ISqluValueConverter<>.", this);

        var ctor = converterType.GetConstructor(Array.Empty<Type>());
        if (ctor == null)
            throw new InvalidConfigurationException("ConverterType does not have a public parameterless constructor.", this, type: converterType);

        var instance = (ISqlValueDescriptor)ctor.Invoke(null);

        if (iargs[0] != instance.MemberType || iargs[1] != instance.SqlType)
            throw new InvalidConfigurationException("ConverterType has inconsistent values for MemberType and/or SqlType.", this);
        if (instance.MemberType != UnderlyingMemberType)
            throw new InvalidConfigurationException("ConverterType.MemberType does not match this.MemberType.", this);

        if (instance.ConverterType != null)
            throw new InvalidConfigurationException("ConverterType cannot define another converter.", this);
        if (!Enum.IsDefined(typeof(System.Data.SqlDbType), instance.SqlDbType))
            throw new InvalidConfigurationException("ConverterType does not define a valid SqlDbType.", this);
        if (!ISqlValueDescriptor.TypeMap.TryGetValue(instance.SqlType, out var compatbileDbTypes))
            throw new InvalidConfigurationException("ConverterType.SqlType is not a a primitive SQL type.", this);
        if (!compatbileDbTypes.Contains(instance.SqlDbType))
            throw new InvalidConfigurationException("ConverterType.SqlType is incompatible with its SqlDbType.", this);

        return instance;
    }

    // Called by ctor.  tenumerable is constructed IEnumerable<>
    ISqlValueDescriptor CreateIEnumerableConverter(SqlContext factory) {
        if (MemberType.GetGenericTypeDefinition() != typeof(IEnumerable<>)) // GetGenericTypeDefinition can also fail! :)
            throw new NotImplementedException("BUG: member is not an IEnumerable<>");

        var tenumerable = MemberType.GetGenericArguments()[0];
        var a = tenumerable.GetCustomAttribute<SqlTableAttribute>();
        if (string.IsNullOrEmpty(a?.Name))
            throw new InvalidConfigurationException("Type used in IEnumerable<> must be given name with SqlTableAttribute.",
                type: tenumerable, valueDescriptor: this);

        var ra = factory.GetRowAccessor(tenumerable);
        var it = typeof(TableValueConverter<>).MakeGenericType(tenumerable);
        var ctor = it.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new Type[] { ra.GetType(), typeof(string) });
        return (ISqlValueDescriptor)ctor.Invoke(new object[] { ra, a.Name });
    }

    #region Get, set and ADO.NET

    /// <summary>
    /// Converts a member value from the object instance to an SQL value.
    /// </summary>
    /// <exception cref="InvalidValueException">
    /// The size of the returned value exceeds the size declared by value descriptor.
    /// </exception>
    public object GetValue(object instance) {
        if (getDelegate == null)
            throw new InvalidConfigurationException("The member is not readable.", valueDescriptor: this);

        var ret = getDelegate(instance);
        if (Size > 0 && ret is not DBNull)
            CheckSize(ret);
        return ret;

        void CheckSize(object value) {
            Debug.Assert(value != null && Size > 0, "Must be called only with non-null value for sized SQL types.");
            int actualLength = int.MinValue;
            switch (SqlDbType) {
            case SqlDbType.Binary:
            case SqlDbType.VarBinary:
                actualLength = ((byte[])value).Length;
                break;
            case SqlDbType.Char:
            case SqlDbType.VarChar:
            case SqlDbType.NChar:
            case SqlDbType.NVarChar:
                actualLength = ((string)value).Length;
                break;
            }
            if (actualLength >= 0 && actualLength > Size)
                throw new InvalidValueException(this, string.Format("The value exceeds the maximum declared size of {0}.", Size));
        }
    }

    /// <summary>
    /// Converts an SQL value to a mamber value and sets it on the instance.
    /// </summary>
    public void SetValue(object instance, object value) {
        if (setDelegate == null)
            throw new InvalidConfigurationException("The member is not writable.", valueDescriptor: this);
        setDelegate(instance, value);
    }

    /// <summary>
    /// Creates an SQL parameter compatible with <c>this</c>.
    /// </summary>
    /// <returns>A new instance of <c>SqlParameter</c> with properties defined by <c>this</c>.</returns>
    public SqlParameter CreateParameter() {
        ParameterDirection direction = this.ParameterDirection;
        if (!Enum.IsDefined(typeof(ParameterDirection), direction))
            direction = ParameterDirection.Input;
        if (!CanWrite && ParameterIsOutput)
            throw new InvalidConfigurationException("Read-only column cannot be used for the specified parameter direction.", valueDescriptor: this);

        var p = new SqlParameter(SqlName.P, SqlDbType) { Direction = direction };
        if (Size != int.MinValue)
            p.Size = Size;
        if (SqlDbType == SqlDbType.Structured)
            p.TypeName = ConverterInstance.SqlName.Q;

        return p;
    }

    /// <summary>
    /// Creates a <c>DataColumn</c> compatible with <c>this</c> and adds it to <paramref name="dt"/>.
    /// (An ordinal on the column can be set only after it has been added to a data table.)
    /// </summary>
    /// <param name="dt">An instance of data table to add the column to.</param>
    /// <returns>A new instance of <c>DataColumn</c> added to <paramref name="dt"/>.</returns>
    public DataColumn CreateDataColumn(DataTable dt) {
        ArgumentNullException.ThrowIfNull(dt);
        var dc = new DataColumn(SqlName.U, SqlType) { AllowDBNull = IsNullable };
        if (Size != int.MinValue)
            dc.MaxLength = Size;
        dt.Columns.Add(dc);
        dc.SetOrdinal(Ordinal);
        return dc;
    }

    #endregion

    #region Get delegate builder

    private Func<object, object> BuildGetDelegate() {
        var pInstance = E.Parameter(typeof(object), "pinstance");
        var vMember = E.Variable(MemberType, "vmember");
        var vSql = E.Variable(typeof(object), "vsql");

        var block = E.Block(
            typeof(object),
            new ParameterExpression[] { vMember, vSql },
            BuildBlock());

        return E.Lambda<Func<object, object>>(block, pInstance).Compile();

        IEnumerable<E> BuildBlock() {
            // Get member value.
            if (MemberInfo is PropertyInfo pi) {
                if (!pi.CanRead)
                    throw new InvalidConfigurationException("Non-readable properties are not supported.", valueDescriptor: this);
                yield return E.Assign(
                    vMember,
                    E.Property(E.Convert(pInstance, ContainingType), pi));
            } else if (MemberInfo is FieldInfo fi) {
                yield return E.Assign(
                    vMember,
                    E.Field(E.Convert(pInstance, ContainingType), fi));
            } else {
                throw new InvalidConfigurationException("Unsupported member kind.", valueDescriptor: this);
            }

            var convertMethod = GetConvertToSql();
            yield return E.Assign(
                vSql,
                E.Call(E.Constant(this), convertMethod, vMember));
            yield return vSql;
        }

        MethodInfo GetConvertToSql() {
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo mi;
            if (MemberType.IsValueType) {
                mi = IsNullable ?
                    GetType().GetMethod(nameof(ConvertToSql3), bf) :
                    GetType().GetMethod(nameof(ConvertToSql2), bf);
            } else {
                mi = GetType().GetMethod(nameof(ConvertToSql1), bf);
            }
            return mi.MakeGenericMethod(UnderlyingMemberType, SqlType);
        }
    }

    object ConvertToSql1<TMember, TSql>(TMember memberValue) where TMember : class {
        if (memberValue == null)
            return DBNull.Value;

        object sqlValue;
        if (ConverterInstance == null) {
            sqlValue = memberValue;
        } else {
            var converter = (ISqlValueConverter<TMember, TSql>)ConverterInstance;
            sqlValue = converter.ConvertToSql(memberValue);
        }
        return sqlValue;
    }

    /// <summary>
    /// Handles non-nullable structs.
    /// </summary>
    object ConvertToSql2<TMember, TSql>(TMember memberValue) where TMember : struct {
        object sqlValue;
        if (ConverterInstance == null) {
            sqlValue = memberValue;
        } else {
            var converter = (ISqlValueConverter<TMember, TSql>)ConverterInstance;
            sqlValue = converter.ConvertToSql(memberValue);
        }
        return sqlValue;
    }

    object ConvertToSql3<TMember, TSql>(Nullable<TMember> memberValue) where TMember : struct {
        if (!memberValue.HasValue)
            return DBNull.Value;

        object sqlValue;
        if (ConverterInstance == null) {
            sqlValue = memberValue.Value;
        } else {
            var converter = (ISqlValueConverter<TMember, TSql>)ConverterInstance;
            sqlValue = converter.ConvertToSql(memberValue.Value);
        }
        return sqlValue;
    }

    #endregion

    #region Set delegate builder

    private Action<object, object> BuildSetDelegate() {
        var pInstance = E.Parameter(typeof(object), "pinstance");
        var pSql = E.Parameter(typeof(object), "psql");
        var vMember = E.Variable(MemberType, "vmember");
        var canWrite = true;

        var block = E.Block(
            new ParameterExpression[] { vMember },
            BuildBlock());
        return !canWrite ? null : E.Lambda<Action<object, object>>(block, pInstance, pSql).Compile();

        IEnumerable<E> BuildBlock() {
            var convertMethod = GetConvertToMember();
            yield return E.Assign(
                vMember,
                E.Call(E.Constant(this), convertMethod, pSql));

            if (MemberInfo is PropertyInfo pi) {
                if (!pi.CanWrite)
                    canWrite = false;
                else yield return E.Assign(
                    E.Property(E.Convert(pInstance, ContainingType), pi), vMember);
            } else if (MemberInfo is FieldInfo fi) {
                yield return E.Assign(
                    E.Field(E.Convert(pInstance, ContainingType), fi), vMember);
            }
        }

        MethodInfo GetConvertToMember() {
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo mi;
            if (MemberType.IsValueType) {
                mi = IsNullable ?
                    GetType().GetMethod(nameof(ConvertToMember3), bf) :
                    GetType().GetMethod(nameof(ConvertToMember2), bf);
            } else {
                mi = GetType().GetMethod(nameof(ConvertToMember1), bf);
            }
            return mi.MakeGenericMethod(UnderlyingMemberType, SqlType);
        }
    }

    TMember ConvertToMember1<TMember, TSql>(object sqlValue) where TMember : class {
        if (sqlValue == null || sqlValue is DBNull)
            return null;
        
        TMember memberValue;
        if (ConverterInstance == null) {
            memberValue = (TMember)sqlValue;
        }
        else {
            var converter = (ISqlValueConverter<TMember, TSql>)ConverterInstance;
            memberValue = converter.ConvertToMember((TSql)sqlValue);
        }
        return memberValue;
    }

    TMember ConvertToMember2<TMember, TSql>(object sqlValue) where TMember : struct {
        TMember memberValue;
        if (ConverterInstance == null) {
            memberValue = (TMember)sqlValue;
        } else {
            var converter = (ISqlValueConverter<TMember, TSql>)ConverterInstance;
            memberValue = converter.ConvertToMember((TSql)sqlValue);
        }
        return memberValue;
    }

    Nullable<TMember> ConvertToMember3<TMember, TSql>(object sqlValue) where TMember : struct {
        if (sqlValue == null || sqlValue is DBNull)
            return default;

        Nullable<TMember> memberValue;
        if (ConverterInstance == null) {
            memberValue = (TMember)sqlValue;
        } else {
            var converter = (ISqlValueConverter<TMember, TSql>)ConverterInstance;
            memberValue = converter.ConvertToMember((TSql)sqlValue);
        }
        return memberValue;
    }

    #endregion
}
#endif
