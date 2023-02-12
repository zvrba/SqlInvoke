using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

using Xunit;

namespace Quine.SqlInvoke.Test;

[Collection(DbTestCollection.Name)]
[TestCaseOrderer("Quine.SqlInvoke.Test.AlphabeticalTestCaseOrder", "Quine.SqlInvoke.Test")]
public class TransactionRollback
{
    private readonly DbFixture dbf;
    private readonly List<Models.NullableConversionModel1> data;
    private readonly Models.SelectSimple selectSimple;

    public TransactionRollback(DbFixture dbf) {
        this.dbf = dbf;
        
        var l = Models.NullableConversionModel_Raw.GenerateData(false);
        var c = new Models.SomeEnumConverter1();
        this.data = l.Where(x => x.Ev == "A1" || x.Ev == "B1").Select(x => new Models.NullableConversionModel1() {
            Id = x.Id,
            Ev = c.ConvertToMember(x.Ev),
            Fv = x.Fv
        }).ToList();

        this.selectSimple = dbf.SqlContext.SelectSimple with { TableName = Models.NullableConversionModel_Raw.TableName };
    }

    [Fact]
    public async Task A0_InitialInsertTest() {
        using var q = (selectSimple with { Columns = "COUNT(*)" }).CreateExecutable(dbf.Connection);
        var c = await q.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(0, c);

        await dbf.FillTable(data);

        c = await q.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(data.Count, c);
    }

    // Id = 2 due to how data is generated.

    [Fact]
    public async Task D1_SelectEntityOnNonKeyThrows() {
        using var select = dbf.SqlContext.Model1.EntityOperations.GetSelect(dbf.Connection);
        var key = new Models.NullableConversionModel1() { Id = 2 };
        var exn = await Assert.ThrowsAsync<SqlException>(() => select.ExecuteAsync(key));
        Assert.Equal(exn.Number, SqlContext.ErrorNumber_InvalidEntityKey);
    }

    [Fact]
    public async Task D2_UpdateEntityOnNonKeyThrows() {
        using var update = dbf.SqlContext.Model1.EntityOperations.GetUpdate(dbf.Connection);
        var key = new Models.NullableConversionModel1() { Id = 2 };
        await UpdateOnNonKeyAsync(update, key, 212);    // Float number must be unique in all tests.
    }

    [Fact]
    public async Task D3_DeleteEntityOnNonKeyThrows() {
        using var delete = dbf.SqlContext.Model1.EntityOperations.GetUpdate(dbf.Connection);
        var key = new Models.NullableConversionModel1() { Id = 2 };
        await UpdateOnNonKeyAsync(delete, key, 213);
    }

    private async Task UpdateOnNonKeyAsync(SqlEntityOperationsBuilder<Models.NullableConversionModel1>.IEntityOperation operation, Models.NullableConversionModel1 key, float insertValue) {
        using var select = (selectSimple with {
            Columns = "COUNT(*)",
            Condition = "WHERE [Id] = 2"
        }).CreateExecutable(dbf.Connection);
        var originalCount = await select.ExecuteScalarAsync<int>(DBNull.Value);

        var exn1 = await Assert.ThrowsAsync<SqlException>(() => operation.ExecuteAsync(key));
        Assert.Equal(SqlContext.ErrorNumber_InvalidEntityKey, exn1.Number);
        var count1 = (int)await select.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(originalCount, count1);

        // This tests that the transaction savepoint built into the commands works as intended.
        // All subsequent commands must bind to the transaction up until to commit or rollback.
        using (var tx = (SqlTransaction)await dbf.Connection.BeginTransactionAsync()) {
            using var insert = dbf.SqlContext.Model1.EntityOperations.GetInsert(dbf.Connection, tx);
            await insert.ExecuteAsync(new Models.NullableConversionModel1() { Fv = insertValue });

            operation.SqlCommand.Transaction = tx;
            var exn2 = await Assert.ThrowsAsync<SqlException>(() => operation.ExecuteAsync(key));
            Assert.Equal(SqlContext.ErrorNumber_InvalidEntityKey, exn1.Number);

            select.SqlCommand.Transaction = tx;
            var count2 = (int)await select.ExecuteScalarAsync<int>(DBNull.Value);
            Assert.Equal(originalCount, count2);

            tx.Commit();    // Rolled back to savepoint, insert will take effect.
        }

        select.SqlCommand.Transaction = null;
        var count3 = (int)await select.ExecuteScalarAsync<int>(DBNull.Value);
        Assert.Equal(originalCount, count3);

        var count4 = await (selectSimple with {
            Columns = "COUNT(*)",
            Condition = "WHERE [Fv] = " + insertValue
        }).ExecuteScalarAsync<int>(DBNull.Value, dbf.Connection);

        Assert.Equal(1, count4);
    }

    [Fact]
    public async Task Z9_ClearTableTest() {
        var truncate = dbf.SqlContext.TruncateTable with { TableName = Models.NullableConversionModel_Raw.TableName };
        await truncate.ExecuteNonQueryAsync(DBNull.Value, dbf.Connection);

        var c = await (selectSimple with { Columns = "COUNT(*)", Condition = string.Empty })
            .ExecuteScalarAsync<int>(DBNull.Value, dbf.Connection);
        Assert.Equal(0, c);
    }
}
