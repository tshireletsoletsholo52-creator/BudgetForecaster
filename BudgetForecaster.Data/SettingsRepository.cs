using BudgetForecaster.Core.Abstractions;

namespace BudgetForecaster.Data;

public class SettingsRepository : ISettingsRepository
{
    private readonly SqliteDatabase _db;

    public SettingsRepository(SqliteDatabase db) => _db = db;

    public async Task<string?> GetAsync(string key)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Setting WHERE \"Key\" = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    public async Task SetAsync(string key, string value)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Setting ("Key", Value) VALUES ($key, $value)
            ON CONFLICT ("Key") DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync();
    }
}
