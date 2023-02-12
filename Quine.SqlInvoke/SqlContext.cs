#if NET6_0_OR_GREATER
using Microsoft.Data.SqlClient;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Quine.SqlInvoke;

/// <summary>
/// Caches metadata about all classes and converters used to invoke SQL statements.  Instances of this class
/// are expensive, so they should be retained for as long as possible.  Singleton instance for the whole
/// application should work in most cases.  All methods are thread-safe.
/// </summary>
/// <remarks>
/// Derived classes can declare public and non-public properties (NOT fields!), with private setters, of type
/// <see cref="SqlRowAccessor{T}"/>.  The constructor populates these with instances bound to this context.  Only
/// directly declared properties are initialized.
/// </remarks>
public class SqlContext
{
    private readonly object @lock = new();
    private readonly Dictionary<Type, ISqlValueDescriptor> converters = new();
    private readonly Dictionary<Type, object> rowAccessors = new();

    /// <summary>
    /// Error code number in <c>SqlException</c> when more than one row would be affected.
    /// </summary>
    public const int ErrorNumber_InvalidEntityKey = (1 << 20) + 1;

    /// <summary>
    /// Constructor.
    /// </summary>
    public SqlContext() {
        var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var p in properties) {
            if (IsRowAccessorProperty(p, out var trow))
                p.SetValue(this, GetRowAccessor(trow));
            if (p.CanWrite && IsInvokableProperty(p, out var tparameters))
                p.SetValue(this, CreateInvokable(p.PropertyType, tparameters));
        }

        static bool IsRowAccessorProperty(PropertyInfo pi, out Type trow) {
            if (pi.PropertyType.IsConstructedGenericType && pi.PropertyType.GetGenericTypeDefinition() == typeof(SqlRowAccessor<>)) {
                trow = pi.PropertyType.GetGenericArguments()[0];
                return true;
            }
            trow = null;
            return false;
        }

        static bool IsInvokableProperty(PropertyInfo pi, out Type tparameters) {
            tparameters = null;
            for (var t = pi.PropertyType; t != null; t = t.BaseType) {
                if (!t.IsConstructedGenericType)
                    continue;
                if (t.GetGenericTypeDefinition() == typeof(SqlInvokable<>)) {
                    tparameters = t.GetGenericArguments()[0];
                    return true;
                }
            }
            return false;
        }

        object CreateInvokable(Type tinvokable, Type tparameters) {
            var ra = GetRowAccessor(tparameters);
            var cm = ra.GetType().GetMethod("CreateInvokable", Type.EmptyTypes).MakeGenericMethod(new Type[] { tinvokable });
            return cm.Invoke(ra, null);
        }
    }

    /// <summary>
    /// Gets a row accessor for the type.  Row accessors are cached by this instance.
    /// </summary>
    /// <typeparam name="T">Type for which to get the row accessor.</typeparam>
    public SqlRowAccessor<T> GetRowAccessor<T>() where T : class =>
        (SqlRowAccessor<T>)GetRowAccessor(typeof(T));

    /// <summary>
    /// Include public and non-public members.  They're considered as candidates only if they also have a SqlMarshal attribute.
    /// </summary>
    private const BindingFlags MemberBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    
    internal object GetRowAccessor(Type trow) {
        lock (@lock) {
            if (!rowAccessors.TryGetValue(trow, out var a)) {
                a = CreateAccessor();
                rowAccessors.Add(trow, a);
            }
            return a;
        }

        object CreateAccessor() {
            var columns = new List<SqlColumnAccessor>();
            foreach (var fi in trow.GetFields(MemberBindingFlags).Where(IsMarshallable))
                AddUnique(columns, new SqlColumnAccessor(this, trow, fi));
            foreach (var pi in trow.GetProperties(MemberBindingFlags).Where(IsMarshallable))
                AddUnique(columns, new SqlColumnAccessor(this, trow, pi));

            List<SqlColumnAccessor> kcolumns = null;
            var a = (SqlTableAttribute)trow.GetCustomAttribute(typeof(SqlTableAttribute));
            if (a?.Keys?.Count > 0) {
                kcolumns = new();
                if (string.IsNullOrWhiteSpace(a.Name))
                    throw new InvalidConfigurationException("Type cannot specify KeyColumns withour specifying TableName.", type: trow);
                foreach (var colname in a.Keys) {
                    var kc = columns.Find(x => x.MemberInfo.Name == colname);
                    if (kc == null)
                        throw new InvalidConfigurationException($"Type does not contain a member named {colname} that was used as key part.", type: trow);
                    kcolumns.Add(kc);
                }
            }

            var it = typeof(SqlRowAccessor<>).MakeGenericType(trow);
            var ctor = it.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new Type[] {
                typeof(SqlContext),
                typeof(IEnumerable<SqlColumnAccessor>),
                typeof(QuotedName),
                typeof(IEnumerable<SqlColumnAccessor>) });
            
            return ctor.Invoke(new object[] { this, columns, (QuotedName)a?.Name, kcolumns });
        }

        void AddUnique(List<SqlColumnAccessor> l, SqlColumnAccessor a) {
            if (l.Find(x => x.SqlName == a.SqlName) != null)
                throw new InvalidConfigurationException("Another member with the same SqlName already exists.", valueDescriptor: a);
            l.Add(a);
        }

        static bool IsMarshallable(MemberInfo mi) => mi.GetCustomAttribute<SqlMarshalAttribute>() != null;
    }

    /// <summary>
    /// Looks up converter by <paramref name="type"/> in the cache.  If not found, creates it using <paramref name="factory"/>
    /// and caches it for further lookups.
    /// </summary>
    /// <remarks>
    /// The lookup type will be either a type implementing <c>ISqlValueConverter</c> or an <c>IEnumerable{T}</c>.
    /// Thus the same dictionary can be used for both.
    /// </remarks>
    internal ISqlValueDescriptor GetConverter(Type type, Func<Type, ISqlValueDescriptor> factory) {
        lock (@lock) {
            if (!converters.TryGetValue(type, out var instance)) {
                instance = factory(type);
                converters.Add(type, instance);
            }
            return instance;
        }
    }
}
#endif
