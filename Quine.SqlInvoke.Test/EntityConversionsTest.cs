using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

using Xunit;

namespace Quine.SqlInvoke.Test;

//
// Demonstrates basic CRUD operations and table-valued parameters.
//

[Collection(DbTestCollection.Name)]
[TestCaseOrderer("Quine.SqlInvoke.Test.AlphabeticalTestCaseOrder", "Quine.SqlInvoke.Test")]
public class EntityConversionsTest
{
    private readonly DbFixture dbf;
    private readonly List<Models.EntityConversionsModel> data;
    private readonly Models.SelectSimple selectCount;

    public EntityConversionsTest(DbFixture dbf) {
        this.dbf = dbf;
        this.data = new();
        for (int i = 0; i < 511; ++i) {
            var m = new Models.EntityConversionsModel() {
                Id1 = i >> 4,
                Id2 = i & 15,
                Ev = (Models.SomeEnum)(i % 2),
                Fv = i / 1024.0f    // Exact!
            };
            data.Add(m);
        }
        this.selectCount = dbf.SqlContext.SelectSimple with {
            Columns = "COUNT(*)",
            TableName = Models.EntityConversionsModel.TableName,
            Condition = string.Empty
        };
    }

    [Fact]
    public async Task A0_InitialInsertTest() {
        using var select = selectCount.CreateExecutable(dbf.Connection);

        var c = await select.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(0, c);

        using (var tx = (SqlTransaction)await dbf.Connection.BeginTransactionAsync()) {
            using var insert = dbf.SqlContext.EntityModel.EntityOperations.GetInsert(dbf.Connection, tx);
            await insert.SqlCommand.PrepareAsync();
            
            foreach (var item in data)
                await insert.ExecuteAsync(item);
            await tx.CommitAsync();
        }

        c = await select.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(data.Count, c);
    }

    [Fact]
    public void A1_Projections() {
        var a0 = dbf.SqlContext.EntityModel;
        
        var a1 = a0.Project(x => new { x.Id1, x.Ev });
        Assert.Equal(2, a1.Columns.Count);
        Assert.Contains(a1.Columns, x => x.SqlName == "Id1");
        Assert.Contains(a1.Columns, x => x.SqlName == "Ev");
        Assert.True(a1.KeyColumns.SetEquals(a0.KeyColumns));

        var a2 = a0.Project(x => new { x.Id1, x.Fv });
        Assert.Equal(2, a2.Columns.Count);
        Assert.Contains(a2.Columns, x => x.SqlName == "Id1");
        Assert.Contains(a2.Columns, x => x.SqlName == "Fv");
        Assert.True(a2.KeyColumns.SetEquals(a0.KeyColumns));

        var a3 = a0.Project(x => new { x.Id1, x.Id2, x.Ev });
        Assert.Equal(3, a3.Columns.Count);
        Assert.Contains(a3.Columns, x => x.SqlName == "Id1");
        Assert.Contains(a3.Columns, x => x.SqlName == "Id2");
        Assert.Contains(a3.Columns, x => x.SqlName == "Ev");
        Assert.True(a3.KeyColumns.SetEquals(a0.KeyColumns));

        Assert.Throws<InvalidConfigurationException>(() => a0.Project(x => new { }));
    }

    [Fact]
    public async Task B_SelectSingle() {
        using var select = dbf.SqlContext.EntityModel.EntityOperations.GetSelect(dbf.Connection);
        var item = data[117];
        var key1 = new Models.EntityConversionsModel() { Id1 = data[117].Id1, Id2 = data[117].Id2 };
        var r1 = await select.ExecuteAsync(key1);
        Assert.True(r1);
        Assert.Equal(item.Id1, key1.Id1);
        Assert.Equal(item.Id2, key1.Id2);
        Assert.Equal(item.Ev, key1.Ev);
        Assert.Equal(item.Fv, key1.Fv);

        var key2 = new Models.EntityConversionsModel() { Id1 = 511, Id2 = 511 };
        var r2 = await select.ExecuteAsync(key2);
        Assert.False(r2);
    }

    [Fact]
    public async Task C_UpdateSingle() {
        using var select = dbf.SqlContext.EntityModel.EntityOperations.GetSelect(dbf.Connection);
        using var update = dbf.SqlContext.EntityModel.Project(x => new { x.Fv }, true).EntityOperations.GetUpdate(dbf.Connection);
        
        var item = data[312];
        var modified = item with { Ev = Models.SomeEnum.BValue, Fv = 11 };  // Ev shall not be changed by update
        var r1 = await update.ExecuteAsync(modified);
        Assert.True(r1);

        var updated = item with { };  // Clone
        var r2 = await select.ExecuteAsync(updated);
        Assert.True(r2);
        Assert.Equal(item.Ev, updated.Ev);      // Not changed by update
        Assert.Equal(modified.Fv, updated.Fv);  // Changed by update

        var key2 = new Models.EntityConversionsModel() { Id1 = 511, Id2 = 511 };
        var r3 = await update.ExecuteAsync(key2);
        Assert.False(r3);
    }

    [Fact]
    public async Task D_DeleteSingle() {
        using var delete = dbf.SqlContext.EntityModel.EntityOperations.GetDelete(dbf.Connection);
        var item = data[408];
        var r1 = await delete.ExecuteAsync(item);
        Assert.True(r1);

        int c;
        using (var e = selectCount.CreateExecutable(dbf.Connection))
            c = (int)await e.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(data.Count - 1, c);

        var r2 = await delete.ExecuteAsync(item with { Id1 = -1 });
        Assert.False(r2);
    }

    //
    // Demonstrates table-valued parameters and how an invokable can be its own parameter set.
    //

    [Fact]
    public async Task E_SprocTvpParameter() {
        var q = dbf.SqlContext.GetRowAccessor<ProcQuery>().CreateInvokable<ProcQuery>() with {
            Selectors = new List<Models.SelectorListModel>() {
                new Models.SelectorListModel { Id = 1, Ev = Models.SomeEnum.AValue },
                new Models.SelectorListModel { Id = 3, Ev = Models.SomeEnum.BValue }
            }
        };
        
        List<Models.EntityConversionsModel> qresult;
        using (var r = await q.Self.ExecuteReaderAsync(dbf.Connection))
            qresult = await r.GetRowsAsync<Models.EntityConversionsModel>().ToListAsync();

        // Output parameters are populated only after disposal of the reader.
        qresult.Sort((x, y) => Math.Sign(x.Fv - y.Fv));    // The sproc query is missing order by

        // Reference data to compare with.
        var refdata = data.Where(x =>
            (x.Id1 == 1 && x.Ev == Models.SomeEnum.AValue) ||
            (x.Id1 == 3 && x.Ev == Models.SomeEnum.BValue));

        foreach (var item in refdata)
            Assert.Contains(item, qresult);

        var refsum = refdata.Select(x => x.Fv).Sum();
        Assert.Equal(refsum, q.Sum);
    }

    [Fact]
    public async Task F_InvokableStatement() {
        const float Threshold = 0.2f;
        var q = dbf.SqlContext.GetRowAccessor<StatementParameter>()
            .CreateInvokable($"SELECT * FROM {Models.EntityConversionsModel.TableName} WHERE [Fv] < @Fv");

        List<Models.EntityConversionsModel> ret;
        using (var e = q.CreateExecutable(dbf.Connection))
        using (var r = await e.ExecuteReaderAsync(new StatementParameter { Fv = Threshold }))
            ret = await r.GetRowsAsync<Models.EntityConversionsModel>().ToListAsync();

        var l = data.Where(x => x.Fv < Threshold);
        Assert.Equal(l.Count(), ret.Count);
        Assert.Equal(l.Select(x => x.Fv).Sum(), ret.Select(x => x.Fv).Sum());
    }

    [Fact]
    public void G_InvalidSelfThrows() {
        Assert.Throws<NotSupportedException>(() => dbf.SqlContext.TruncateTable.Self);
    }

    [Fact]
    public async Task Z9_ClearTableTest() {
        var truncate = dbf.SqlContext.TruncateTable with { TableName = Models.EntityConversionsModel.TableName };
        using (var e = truncate.CreateExecutable(dbf.Connection))
            await e.ExecuteNonQueryAsync(DBNull.Value);

        int c;
        using (var e = selectCount.CreateExecutable(dbf.Connection))
            c = await e.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(0, c);
    }

    //
    // An invokable can be its own parameter set.
    //

    private record ProcQuery : SqlInvokable<ProcQuery>
    {
        public override string CommandText => "dbo.ProcQuery";
        public override CommandType CommandType => CommandType.StoredProcedure;

        //
        // TVPs MUST be declared as IEnumerable<>
        //

        [SqlMarshal(SqlDbType.Structured)]
        public IEnumerable<Models.SelectorListModel> Selectors { get; set; }

        [SqlMarshal(direction: ParameterDirection.Output)]
        public float Sum { get; set; }
    }

    private class StatementParameter {
        [SqlMarshal] public float Fv;
    }
}
