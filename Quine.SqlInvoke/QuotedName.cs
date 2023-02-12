using System;
using System.Diagnostics.CodeAnalysis;

namespace Quine.SqlInvoke
{
/// <summary>
/// Represents a schema-qualified name of an SQL object.  Default-constructed instance returns null for all properties.
/// Instances are created with implicit conversion from string.
/// </summary>
public readonly struct QuotedName : IEquatable<QuotedName>
{
    private readonly string schema;
    private readonly string name;

    private QuotedName(string name, string schema) {
        this.schema = schema;
        this.name = name;
    }

    /// <summary>
    /// Returns a quoted name using <c>[]</c> for quotes, or null if this is default instance.
    /// </summary>
    public string Q => schema == null ? (name == null ? null : Quote(name)) : Quote(schema) + "." + Quote(name);

    /// <summary>
    /// Returns a parameter name, i.e., with <c>@</c> prefix, or null if this is default instance.
    /// This is valid only if schema name part is missing.
    /// </summary>
    public string P => schema == null ? (name == null ? null : Param(name)) : throw new InvalidOperationException("Name with a schema part cannot be used as a parameter.");

    /// <summary>
    /// Returns a raw, unquoted name, or null if this is default instance.
    /// </summary>
    public string U => schema == null ? name : schema + "." + name;

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="qn">
    /// One- or two-part name of a parameter or an SQL object, separated with a dot.  If the name is
    /// quoted with <c>[]</c>, the quotes are stripped.  Empty and null values are converted to default instance.
    /// </param>
    public static implicit operator QuotedName(string qn) {
        if (string.IsNullOrWhiteSpace(qn))
            return new QuotedName(null, null);

        qn = qn.Replace("[", "").Replace("]", "");

        var i = qn.IndexOf('.');
        if (i < 0) {
            if (qn[0] == '@')
                throw new ArgumentException("@ prefix is invalid.  Use plain name.", nameof(qn));
            return new QuotedName(qn, null);
        } 

        var name = qn.Substring(i + 1);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name part is missing.");

        var schema = qn.Substring(0, i);
        if (string.IsNullOrWhiteSpace(schema))
            throw new ArgumentException("Schema part is missing.");

        if (schema[0] == '@' || name[0] == '@')
            throw new ArgumentException("@ prefix is invalid.  Use plain name.", nameof(qn));

        return new QuotedName(name, schema);
    }

    private static string Quote(string s) => "[" + s + "]";
    private static string Param(string s) => "@" + s;

    public override string ToString() => U;

    public bool Equals(QuotedName other) => schema == other.schema && name == other.name;
    public override bool Equals(object other) => (other is QuotedName qn) && Equals(qn);
    public override int GetHashCode() => (schema?.GetHashCode() ?? 1) * (name?.GetHashCode() ?? 0);
    public static bool operator ==(QuotedName left, QuotedName right) => left.Equals(right);
    public static bool operator !=(QuotedName left, QuotedName right) => !left.Equals(right);
}
}
