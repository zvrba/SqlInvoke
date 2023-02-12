using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Quine.SqlInvoke.Test;

[CollectionDefinition(Name)]
public class DbTestCollection : ICollectionFixture<DbFixture>
{
    /// <summary>
    /// Test collection name.
    /// </summary>
    public const string Name = "DB";
}

public class DbFixture : IDisposable
{
    static readonly string MasterCstr = @$"Server=(localdb)\MSSQLLocalDb;Integrated Security=True;Initial Catalog=master";
    static readonly string DbName = "SqlInvokeTest";

    // Absolute path to the database (MDF) file.
    static readonly string MdfPath = Path.Combine(
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
        @"SqlInvokeTest.mdf");

    /// <summary>
    /// Shared, open connection.
    /// </summary>
    public SqlConnection Connection { get; }

    /// <summary>
    /// SQL context for all commands and entities.
    /// </summary>
    public TestContext SqlContext { get; }

    public DbFixture() {
        using var sqlc = new SqlConnection(MasterCstr);
        sqlc.Open();
        using (var cmd1 = sqlc.CreateCommand()) {
            cmd1.CommandText = string.Format("DROP DATABASE IF EXISTS [{0}]", DbName);
            cmd1.ExecuteNonQuery();
        }
        using (var cmd2 = sqlc.CreateCommand()) {
            cmd2.CommandText = string.Format(@"RESTORE DATABASE [{0}] FROM DISK = '{1}' WITH REPLACE", DbName, MdfPath);
            cmd2.ExecuteNonQuery();
        }

        Connection = GetSqlConnection();
        Connection.Open();
        SqlContext = new();
    }

    public void Dispose() {
        Connection.Dispose();

        using var sqlc = new SqlConnection(MasterCstr);
        sqlc.Open();
        using (var cmd1 = sqlc.CreateCommand()) {
            cmd1.CommandText = string.Format("ALTER DATABASE [{0}] SET OFFLINE WITH ROLLBACK IMMEDIATE", DbName);
            cmd1.ExecuteNonQuery();
        }
        using (var cmd2 = sqlc.CreateCommand()) {
            cmd2.CommandText = string.Format("DROP DATABASE [{0}]", DbName);
            cmd2.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Returns an open connection.
    /// </summary>
    public static async Task<SqlConnection> OpenSqlConnectionAsync() {
        var sqlc = GetSqlConnection();
        await sqlc.OpenAsync();
        return sqlc;
    }

    /// <summary>
    /// Returns a configured connection without opening it.
    /// </summary>
    public static SqlConnection GetSqlConnection() {
        var cstr = @$"Server=(localdb)\MSSQLLocalDb;Integrated Security=True;Initial Catalog={DbName}";
        return new SqlConnection(cstr);
    }

    public async Task FillTable<T>(List<T> data) where T : class {
        var tx = (SqlTransaction)await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using var insertCmd = SqlContext.GetRowAccessor<T>().EntityOperations.GetInsert(Connection, tx);
        try {
            foreach (var item in data)
                await insertCmd.ExecuteAsync(item);
            tx.Commit();
        }
        catch {
            tx.Rollback();
            throw;
        }
        finally {
            tx.Dispose();
        }
    }
}
