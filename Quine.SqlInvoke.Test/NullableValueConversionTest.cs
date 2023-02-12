using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Quine.SqlInvoke.Test;

[Collection(DbTestCollection.Name)]
[TestCaseOrderer("Quine.SqlInvoke.Test.AlphabeticalTestCaseOrder", "Quine.SqlInvoke.Test")]
public class NullableValueConversionTest
{
    private readonly DbFixture dbf;
    private readonly List<Models.NullableConversionModel_Raw> data;
    private readonly Models.SelectSimple selectSimple;

    public NullableValueConversionTest(DbFixture dbf) {
        this.dbf = dbf;
        this.data = Models.NullableConversionModel_Raw.GenerateData(false);
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

    [Fact]
    public async Task B_FetchAll_Values_Same() {
        using var q = (selectSimple with {
            Columns = "*",
            Condition = "ORDER BY [Fv]" // pseudo-key
        }).CreateExecutable(dbf.Connection);

        List<Models.NullableConversionModel_Raw> result;
        using (var r = await q.ExecuteReaderAsync(DBNull.Value))
            result = await r.GetRowsAsync<Models.NullableConversionModel_Raw>().ToListAsync();

        data.Sort((x, y) => Math.Sign(x.Fv - y.Fv));
        Assert.Equal(data.Count, result.Count);
        for (int i = 0; i < data.Count; ++i)
            Assert.Equal(data[i], result[i]);
    }

    [Fact]
    public async Task C1_ConverterThrowsOnInvalidNull() {
        using var r = await (selectSimple with {
            Columns = "*",
            Condition = "WHERE [Ev] IS NULL OR [Ev] IN ('A2', 'B2')"
        }).ExecuteReaderAsync(DBNull.Value, dbf.Connection);

        // Invalid cast because DBNull is attempted casted to int.
        await Assert.ThrowsAsync<InvalidCastException>(() => r.GetRowsAsync<Models.NullableConversionModel2>().ToListAsync());
    }

    [Fact]
    public async Task C2_ConverterThrowsOnInvaludValue() {
        using var r = await (selectSimple with {
            Columns = "*",
            Condition = "WHERE [Ev] IS NOT NULL OR [Ev] IN ('A1', 'B1')"
        }).ExecuteReaderAsync(DBNull.Value, dbf.Connection);

        // FormatException thrown by converter.
        // Model2 expects non-null and Converter2.  The above feeds values suitable for Converter1.
        await Assert.ThrowsAsync<FormatException>(() => r.GetRowsAsync<Models.NullableConversionModel2>().ToListAsync());
    }

    [Fact]
    public async Task C3_TypeConverterTest() {
        using var q = (selectSimple with {
            Columns = "*",
            Condition = "WHERE [Ev] IS NULL OR [Ev] IN ('A1', 'B1') ORDER BY [Fv]"
        }).CreateExecutable(dbf.Connection);

        List<Models.NullableConversionModel1> result;
        using (var r = await q.ExecuteReaderAsync(DBNull.Value))
            result = await r.GetRowsAsync<Models.NullableConversionModel1>().ToListAsync();
        Assert.True(result.Count > 0);

        var c = new Models.SomeEnumConverter1();
        var filteredData = data.Where(x => x.Ev == null || x.Ev == "A1" || x.Ev == "B1").ToList();
        Assert.Equal(filteredData.Count, result.Count);

        for (int i = 0; i < filteredData.Count; ++i) {
            Assert.Equal(filteredData[i].Id, result[i].Id);
            
            if (filteredData[i].Ev == null) Assert.False(result[i].Ev.HasValue);
            else Assert.Equal(c.ConvertToMember(filteredData[i].Ev), result[i].Ev);

            Assert.Equal(filteredData[i].Fv, result[i].Fv);
        }
    }

    [Fact]
    public async Task C4_MemberConverterTest() {
        using var q = (selectSimple with {
            Columns = "*",
            Condition = "WHERE [Id] IS NOT NULL AND [Ev] IN ('A2', 'B2') ORDER BY [Fv]"
        }).CreateExecutable(dbf.Connection);

        List<Models.NullableConversionModel2> result;
        using (var r = await q.ExecuteReaderAsync(DBNull.Value))
            result = await r.GetRowsAsync<Models.NullableConversionModel2>().ToListAsync();
        Assert.True(result.Count > 0);

        var c = new Models.SomeEnumConverter2();
        var filteredData = data.Where(x => x.Id.HasValue && (x.Ev == "A2" || x.Ev == "B2")).ToList();
        Assert.Equal(filteredData.Count, result.Count);

        for (int i = 0; i < filteredData.Count; ++i) {
            Assert.Equal(filteredData[i].Id, result[i].Id);
            Assert.Equal(c.ConvertToMember(filteredData[i].Ev), result[i].Ev);
            Assert.Equal(filteredData[i].Fv, result[i].Fv);
        }
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
