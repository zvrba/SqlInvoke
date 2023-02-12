using System;
using System.Linq.Expressions;
using System.Reflection;
using Xunit;

namespace Quine.SqlInvoke.Test;

public class ExpressionParseTest
{
    // Expression parser that accepts only immediate field access.
    [Fact]
    public void ExtractFieldOrPropertyFromExpression() {
        Expression<Func<C1, object>> e1 = (c) => new {
            c.X, c.Y,
        };
        var members1 = SqlRowAccessor<C1>.ParseLambdaForProjection(e1);
        Assert.Equal(2, members1.Length);
        Assert.Equal(members1[0], typeof(C1).GetProperty("X"));
        Assert.Equal(members1[1], typeof(C1).GetField("Y"));

        Expression<Func<C1, object>> e2 = (c) => new {
            c.X, c.C2.Z
        };
        Assert.Throws<NotSupportedException>(() => SqlRowAccessor<C1>.ParseLambdaForProjection(e2));
    }

    [Fact]
    public void GetOuterType() {
        var t = typeof(C1.Inner);
        Assert.True(t.IsNested);
        Assert.Equal(typeof(C1), t.DeclaringType);
    }

    [Fact]
    public void RecordWithCtorHasSetters() {
        var t = typeof(C3);
        var p = t.GetProperty("X");
        Assert.True(p.CanWrite);
    }

    class C1
    {
        public int X { get; set; }
        public float Y;
        public C2 C2;

        public class Inner { }
    }

    class C2
    {
        public int Z;
    }

    record class C3(int X, float Y);
}
