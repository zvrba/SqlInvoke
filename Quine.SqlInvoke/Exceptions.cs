#if NET6_0_OR_GREATER
using System;
using System.Diagnostics;

namespace Quine.SqlInvoke;

/// <summary>
/// Thrown during model building when an invalid mapping to SQL has been detected.
/// </summary>
public sealed class InvalidConfigurationException : Exception
{
    /// <summary>
    /// The type on which invalid configuration got detected.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// The member on which invalid configuration has been detected.  May be null.
    /// </summary>
    public System.Reflection.MemberInfo MemberInfo { get; }

    /// <summary>
    /// The value descriptor which validation threw this exception.  May be null.
    /// </summary>
    public ISqlValueDescriptor ValueDescriptor { get; }

    public override string Message => message.Value;

    internal InvalidConfigurationException(string message,
        ISqlValueDescriptor valueDescriptor = null,
        System.Reflection.MemberInfo memberInfo = null,
        Type type = null) : base(message)
    {
        this.Type = type;
        this.MemberInfo = memberInfo;
        this.ValueDescriptor = valueDescriptor;
        this.message = new Lazy<string>(GetMessage);
    }

    private readonly Lazy<string> message;
    private string GetMessage() {
        var sb = new System.Text.StringBuilder(base.Message);
        if (Type != null) {
            sb.AppendLine();
            sb.AppendFormat("Offending type: {0}", Type.FullName);
        }
        if (MemberInfo != null) {
            sb.AppendLine();
            sb.AppendFormat("Offending member: {0}.{1}", MemberInfo.DeclaringType.FullName, MemberInfo.Name);
        }
        if (ValueDescriptor != null) {
            sb.AppendLine();
            sb.AppendFormat("Offending value descriptor: SqlName={0}, MemberType={1}",
                ValueDescriptor.SqlName.U ?? "(none)",
                ValueDescriptor.MemberType.FullName);
            if (ValueDescriptor.ConverterType != null)
                sb.AppendFormat(", Converter={0}", ValueDescriptor.ConverterType.FullName);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Thrown during SQL execution when the value does not conform to the declared member and/or SQL type.
/// </summary>
public sealed class InvalidValueException : FormatException
{
    /// <summary>
    /// Member accessor that threw the exception.  May be null.
    /// </summary>
    public ISqlValueDescriptor ValueDescriptor { get; }

    public override string Message => message.Value;

    internal InvalidValueException(ISqlValueDescriptor valueDescriptor, string message) : base(message) {
        this.ValueDescriptor = valueDescriptor;
        this.Source = "Quine.Base.SqlInvoke";
        this.message = new Lazy<string>(GetMessage);
    }

    private readonly Lazy<string> message;
    private string GetMessage() {
        var sb = new System.Text.StringBuilder(base.Message);
        if (ValueDescriptor != null) {
            sb.AppendLine();
            sb.AppendFormat("Offending value: SqlName={0}, MemberType={1}", ValueDescriptor.SqlName, ValueDescriptor.MemberType.FullName);
            if (ValueDescriptor is SqlColumnAccessor va) {
                sb.AppendLine();
                sb.AppendFormat("Offending member: {0}.{1}", va.ContainingType.FullName, va.MemberInfo.Name);
            }
        }
        return sb.ToString();
    }
}
#endif