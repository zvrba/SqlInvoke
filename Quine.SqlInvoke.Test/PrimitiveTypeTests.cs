using System;
using System.Data;
using System.Threading.Tasks;
using Xunit;

namespace Quine.SqlInvoke.Test;

[Collection(DbTestCollection.Name)]
public class PrimitiveTypeTests
{
    private readonly DbFixture dbf;
    private readonly TypeTestParameters prototype;
    private readonly Guid guid = Guid.NewGuid();

    public PrimitiveTypeTests(DbFixture dbf) {
        this.dbf = dbf;
        this.prototype = new() {
            Bool = false,
            I8 = 11,
            I16 = short.MaxValue - 2,
            I32 = int.MaxValue - 2,
            I64 = long.MinValue + 2,
            Bin1 = new byte[16],
            Bin2 = new byte[16],
            Bin3 = new byte[128],
            Ch1 = "1234",
            Ch2 = "1234",
            Ch3 = "1234",
            Nc1 = "123å",
            Nc2 = "123ø",
            Nc3 = "12æ",
            Dt = new DateTime(2022, 5, 26, 1, 1, 1),
            Dto = new DateTimeOffset(new DateTime(2022, 5, 26, 1, 1, 1), TimeSpan.FromHours(2)),
            Tm = new TimeSpan(0, 22, 35, 14, 317),
            F32 = (1 << 24) - 1,
            F64 = (1L << 52) - 1,
            G = guid
        };
    }

    [Fact]
    public async Task BasicParameterTest() {
       var p = (TypeTestParameters)prototype.Clone();
       using (var e = dbf.SqlContext.TypeTestProc.CreateExecutable(dbf.Connection))
            await e.ExecuteNonQueryAsync(p);
        
        Assert.True(p.Bool);
        Assert.Equal(12, p.I8);
        Assert.Equal(short.MaxValue - 1, p.I16);
        Assert.Equal(int.MaxValue - 1, p.I32);
        Assert.Equal(long.MinValue + 3, p.I64);
        
        Assert.Equal(16, p.Bin1.Length);
        Assert.Equal(4, p.Bin2.Length);
        Assert.Equal(16, p.Bin3.Length);
        
        // TODO: Check Bin for correct content.

        var ch1 = ("ASDF" + prototype.Ch1).PadRight(16);
        Assert.Equal(ch1, p.Ch1);   // Char is always right-padded with spaces
        Assert.Equal("ASDF" + prototype.Ch2, p.Ch2);
        Assert.Equal("ASDF" + prototype.Ch3, p.Ch3);

        var nc1 = ("ÆAD" + prototype.Nc1).PadRight(16);
        Assert.Equal(nc1, p.Nc1);

        var nc2 = "ÆAD" + prototype.Nc2;
        Assert.Equal(nc2, p.Nc2);

        var nc3 = "ÆADXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX" + prototype.Nc3;
        Assert.Equal(nc3, p.Nc3);

        Assert.Equal(prototype.Dt.AddDays(1), p.Dt);
        Assert.Equal(prototype.Dto.AddDays(1), p.Dto);
        Assert.Equal(1 << 24, p.F32);
        Assert.Equal(1L << 52, p.F64);

        Assert.Equal(prototype.G, p.G);

        Assert.Equal(-12, p.Retval);
    }

    [Fact]
    public async Task LengthCheckTest() {
        var p = (TypeTestParameters)prototype.Clone();
        p.Ch2 = "1234".PadRight(17);
        using (var e = dbf.SqlContext.TypeTestProc.CreateExecutable(dbf.Connection))
            await Assert.ThrowsAsync<InvalidValueException>(() => e.ExecuteNonQueryAsync(p));
    }

    [Fact]
    public async Task NullValuesCheckTest() {
        var p = new TypeTestParameters();
        using (var e = dbf.SqlContext.TypeTestProc.CreateExecutable(dbf.Connection))
            await e.ExecuteNonQueryAsync(p);
        // Shall not throw.
    }

    [Fact]
    public async Task TypeCheckTest() {
        var p = new TypeTestParameters_InvalidType();
        p.F32 = "asdf";
        using (var e = dbf.SqlContext.InvalidTypeTestProc.CreateExecutable(dbf.Connection))
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => e.ExecuteNonQueryAsync(p));
    }

    public class TypeTestParameters : ICloneable
    {
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public bool Bool;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public byte I8;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public short I16;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public int I32;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public long I64;

        [SqlMarshal(SqlDbType.Binary, 16, ParameterDirection.InputOutput)] public byte[] Bin1;
        [SqlMarshal(SqlDbType.VarBinary, 16, ParameterDirection.InputOutput)] public byte[] Bin2;
        [SqlMarshal(SqlDbType.VarBinary, -1, ParameterDirection.InputOutput)] public byte[] Bin3;

        [SqlMarshal(SqlDbType.Char, 16, ParameterDirection.InputOutput)] public string Ch1;
        [SqlMarshal(SqlDbType.VarChar, 16, ParameterDirection.InputOutput)] public string Ch2;
        [SqlMarshal(SqlDbType.VarChar, -1, ParameterDirection.InputOutput)] public string Ch3;

        [SqlMarshal(SqlDbType.NChar, 16, ParameterDirection.InputOutput)] public string Nc1;
        [SqlMarshal(SqlDbType.NVarChar, 16, ParameterDirection.InputOutput)] public string Nc2;
        [SqlMarshal(SqlDbType.NVarChar, -1, ParameterDirection.InputOutput)] public string Nc3;

        [SqlMarshal(SqlDbType.DateTime2, direction: ParameterDirection.InputOutput)] public DateTime Dt;
        [SqlMarshal(SqlDbType.DateTimeOffset, direction: ParameterDirection.InputOutput)] public DateTimeOffset Dto;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public TimeSpan Tm;

        [SqlMarshal(direction: ParameterDirection.InputOutput)] public float F32;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public double F64;

        [SqlMarshal(direction: ParameterDirection.Input)] public Guid G;

        [SqlMarshal(direction: ParameterDirection.ReturnValue)] public int Retval;

        public object Clone() {
            var ret = (TypeTestParameters)MemberwiseClone();
            ret.Bin1 = (byte[])Bin1.Clone();
            ret.Bin2 = (byte[])Bin2.Clone();
            ret.Bin3 = (byte[])Bin3.Clone();
            return ret;
        }
    }

    public class TypeTestParameters_InvalidType : ICloneable
    {
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public bool Bool;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public byte I8;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public short I16;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public int I32;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public long I64;

        [SqlMarshal(SqlDbType.Binary, 16, ParameterDirection.InputOutput)] public byte[] Bin1;
        [SqlMarshal(SqlDbType.VarBinary, 16, ParameterDirection.InputOutput)] public byte[] Bin2;
        [SqlMarshal(SqlDbType.VarBinary, -1, ParameterDirection.InputOutput)] public byte[] Bin3;

        [SqlMarshal(SqlDbType.Char, 16, ParameterDirection.InputOutput)] public string Ch1;
        [SqlMarshal(SqlDbType.VarChar, 16, ParameterDirection.InputOutput)] public string Ch2;
        [SqlMarshal(SqlDbType.VarChar, -1, ParameterDirection.InputOutput)] public string Ch3;

        [SqlMarshal(SqlDbType.NChar, 16, ParameterDirection.InputOutput)] public string Nc1;
        [SqlMarshal(SqlDbType.NVarChar, 16, ParameterDirection.InputOutput)] public string Nc2;
        [SqlMarshal(SqlDbType.NVarChar, -1, ParameterDirection.InputOutput)] public string Nc3;

        [SqlMarshal(SqlDbType.DateTime2, direction: ParameterDirection.InputOutput)] public DateTime Dt;
        [SqlMarshal(SqlDbType.DateTimeOffset, direction: ParameterDirection.InputOutput)] public DateTimeOffset Dto;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public TimeSpan Tm;

        [SqlMarshal(SqlDbType.Char, 12, direction: ParameterDirection.InputOutput)] public string F32;
        [SqlMarshal(direction: ParameterDirection.InputOutput)] public double F64;

        [SqlMarshal(direction: ParameterDirection.Input)] public Guid G;

        public object Clone() {
            var ret = (TypeTestParameters)MemberwiseClone();
            ret.Bin1 = (byte[])Bin1.Clone();
            ret.Bin2 = (byte[])Bin2.Clone();
            ret.Bin3 = (byte[])Bin3.Clone();
            return ret;
        }
    }
}
