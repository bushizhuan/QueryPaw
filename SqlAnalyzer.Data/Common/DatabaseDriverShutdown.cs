using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace SqlAnalyzer.Data.Common;

public static class DatabaseDriverShutdown
{
    public static void ClearAllPools()
    {
        TryClear(SqlConnection.ClearAllPools);
        TryClear(MySqlConnection.ClearAllPools);
        TryClear(NpgsqlConnection.ClearAllPools);
        TryClear(OracleConnection.ClearAllPools);
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
