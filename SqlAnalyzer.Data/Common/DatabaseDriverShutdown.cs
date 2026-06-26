using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using SqlAnalyzer.Data.Redis;

namespace SqlAnalyzer.Data.Common;

public static class DatabaseDriverShutdown
{
    public static void ClearAllPools()
    {
        TryClear(SqlConnection.ClearAllPools);
        TryClear(SqliteConnection.ClearAllPools);
        TryClear(MySqlConnection.ClearAllPools);
        TryClear(NpgsqlConnection.ClearAllPools);
        TryClear(OracleConnection.ClearAllPools);
        TryClear(RedisConnectionManager.ClearAll);
    }

    private static void TryClear(Action clearPools)
    {
        try
        {
            clearPools();
        }
        catch
        {
        }
    }
}
