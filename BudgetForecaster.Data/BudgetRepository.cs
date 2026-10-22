using System.Globalization;
using BudgetForecaster.Core;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;

namespace BudgetForecaster.Data;

public class BudgetRepository : IBudgetRepository
{
    private readonly SqliteDatabase _db;

    public BudgetRepository(SqliteDatabase db) => _db = db;

    // Months are stored as 'YYYY-MM' text, as in the requirements document.
    private static string Key(DateOnly month) =>
        month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<Budget>> GetForMonthAsync(DateOnly month)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CategoryId, Month, AmountCents
            FROM Budget
            WHERE Month = $month;
            """;
        cmd.Parameters.AddWithValue("$month", Key(month));

        var result = new List<Budget>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new Budget
            {
                Id = reader.GetInt32(0),
                CategoryId = reader.GetInt32(1),
                Month = DateOnly.ParseExact(reader.GetString(2) + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Amount = Money.FromCents(reader.GetInt64(3))
            });
        }
        return result;
    }

    public async Task SetAsync(int categoryId, DateOnly month, decimal amount)
    {
        if (amount < 0)
            throw new ArgumentException("A budget cannot be negative.", nameof(amount));

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Budget (CategoryId, Month, AmountCents)
            VALUES ($categoryId, $month, $cents)
            ON CONFLICT (CategoryId, Month) DO UPDATE SET AmountCents = excluded.AmountCents;
            """;
        cmd.Parameters.AddWithValue("$categoryId", categoryId);
        cmd.Parameters.AddWithValue("$month", Key(month));
        cmd.Parameters.AddWithValue("$cents", Money.ToCents(amount));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> CopyFromPreviousMonthAsync(DateOnly month)
    {
        var previous = new DateOnly(month.Year, month.Month, 1).AddMonths(-1);

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO Budget (CategoryId, Month, AmountCents)
            SELECT CategoryId, $target, AmountCents
            FROM Budget
            WHERE Month = $source;
            """;
        cmd.Parameters.AddWithValue("$target", Key(month));
        cmd.Parameters.AddWithValue("$source", Key(previous));
        return await cmd.ExecuteNonQueryAsync();
    }
}
