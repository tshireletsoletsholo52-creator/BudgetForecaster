using System.Globalization;
using BudgetForecaster.Core;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using Microsoft.Data.Sqlite;

namespace BudgetForecaster.Data;

public class TransactionRepository : ITransactionRepository
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly SqliteDatabase _db;

    public TransactionRepository(SqliteDatabase db) => _db = db;

    public async Task<Transaction> AddAsync(Transaction transaction)
    {
        Validate(transaction);

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "Transaction" (CategoryId, AmountCents, Date, Note)
            VALUES ($categoryId, $cents, $date, $note);
            SELECT last_insert_rowid();
            """;
        AddParameters(cmd, transaction);

        transaction.Id = (int)(long)(await cmd.ExecuteScalarAsync())!;
        return transaction;
    }

    public async Task UpdateAsync(Transaction transaction)
    {
        Validate(transaction);

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE "Transaction"
            SET CategoryId = $categoryId, AmountCents = $cents, Date = $date, Note = $note
            WHERE Id = $id;
            """;
        AddParameters(cmd, transaction);
        cmd.Parameters.AddWithValue("$id", transaction.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"Transaction\" WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Transaction?> GetByIdAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CategoryId, AmountCents, Date, Note
            FROM "Transaction"
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Transaction>> GetForMonthAsync(DateOnly month)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var next = start.AddMonths(1);

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CategoryId, AmountCents, Date, Note
            FROM "Transaction"
            WHERE Date >= $start AND Date < $next
            ORDER BY Date DESC, Id DESC;
            """;
        cmd.Parameters.AddWithValue("$start", start.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$next", next.ToString(DateFormat, CultureInfo.InvariantCulture));

        var result = new List<Transaction>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    private static Transaction Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        CategoryId = reader.GetInt32(1),
        Amount = Money.FromCents(reader.GetInt64(2)),
        Date = DateOnly.ParseExact(reader.GetString(3), DateFormat, CultureInfo.InvariantCulture),
        Note = reader.IsDBNull(4) ? null : reader.GetString(4)
    };

    private static void Validate(Transaction t)
    {
        if (t.Amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(t));
        if (t.CategoryId <= 0)
            throw new ArgumentException("A category is required.", nameof(t));
    }

    private static void AddParameters(SqliteCommand cmd, Transaction t)
    {
        cmd.Parameters.AddWithValue("$categoryId", t.CategoryId);
        cmd.Parameters.AddWithValue("$cents", Money.ToCents(t.Amount));
        cmd.Parameters.AddWithValue("$date", t.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$note", (object?)t.Note ?? DBNull.Value);
    }
}
