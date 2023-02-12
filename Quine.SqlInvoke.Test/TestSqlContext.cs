using System;
using System.Data;

namespace Quine.SqlInvoke.Test;

//
// Demonstrates how declare predefined SQL mappings and statements.
//
public class TestContext : SqlContext
{
    //
    // Invokables with only a getter (i.e., no set/init method) have to be initialized in constructor.
    // Others are initialized by the base class.
    //

    public TestContext() {
        // Row accessors can be created from the context on the fly.
        TypeTestProc = GetRowAccessor<PrimitiveTypeTests.TypeTestParameters>()
            .CreateInvokable("dbo.TypeTest", CommandType.StoredProcedure);
        InvalidTypeTestProc = GetRowAccessor<PrimitiveTypeTests.TypeTestParameters_InvalidType>()
            .CreateInvokable("dbo.TypeTest", CommandType.StoredProcedure);
    }

    // Yes, setters are allowed (and recommended) to be "init".
    // They are reflectively initialized by the base class ctor.

    // Mappers for model classes.
    public SqlRowAccessor<Models.NullableConversionModel1> Model1 { get; init; }
    public SqlRowAccessor<Models.EntityConversionsModel> EntityModel { get; init; }
    public SqlRowAccessor<Models.SelectorListModel> TvpModel { get; init; }

    // Executable statements.
    public Models.TruncateTable TruncateTable { get; init; }
    public Models.SelectSimple SelectSimple { get; init; }

    // Invokables initialized by this ctor MUST NOT declare a setter or init.
    public SqlInvokable<PrimitiveTypeTests.TypeTestParameters> TypeTestProc { get; }
    public SqlInvokable<PrimitiveTypeTests.TypeTestParameters_InvalidType> InvalidTypeTestProc { get; }
}
