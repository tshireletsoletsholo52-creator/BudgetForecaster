using Microsoft.Data.Sqlite;

namespace BudgetForecaster.Data;

public class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // SQLite ignores foreign keys unless you switch them on for every connection.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }
}
