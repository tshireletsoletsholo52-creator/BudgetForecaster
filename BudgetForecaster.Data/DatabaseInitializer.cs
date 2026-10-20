using BudgetForecaster.Core.Models;
using Microsoft.Data.Sqlite;

namespace BudgetForecaster.Data;

public static class DatabaseInitializer
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Category (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            Name        TEXT    NOT NULL,
            Type        INTEGER NOT NULL CHECK (Type IN (0, 1)),
            IsArchived  INTEGER NOT NULL DEFAULT 0,
            UNIQUE (Name, Type)
        );

        CREATE TABLE IF NOT EXISTS Budget (
            Id           INTEGER PRIMARY KEY AUTOINCREMENT,
            CategoryId   INTEGER NOT NULL REFERENCES Category(Id),
            Month        TEXT    NOT NULL,
            AmountCents  INTEGER NOT NULL CHECK (AmountCents >= 0),
            UNIQUE (CategoryId, Month)
        );

        CREATE TABLE IF NOT EXISTS "Transaction" (
            Id           INTEGER PRIMARY KEY AUTOINCREMENT,
            CategoryId   INTEGER NOT NULL REFERENCES Category(Id),
            AmountCents  INTEGER NOT NULL CHECK (AmountCents > 0),
            Date         TEXT    NOT NULL,
            Note         TEXT,
            CreatedAt    TEXT    NOT NULL DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS Setting (
            Key    TEXT PRIMARY KEY,
            Value  TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Transaction_Date     ON "Transaction"(Date);
        CREATE INDEX IF NOT EXISTS IX_Transaction_Category ON "Transaction"(CategoryId);
        """;

    private static readonly (string Name, CategoryType Type)[] DefaultCategories =
    [
        ("Salary", CategoryType.Income),
        ("Side income", CategoryType.Income),
        ("Gifts", CategoryType.Income),
        ("Other income", CategoryType.Income),
        ("Rent", CategoryType.Expense),
        ("Groceries", CategoryType.Expense),
        ("Transport", CategoryType.Expense),
        ("Utilities", CategoryType.Expense),
        ("Airtime and data", CategoryType.Expense),
        ("Eating out", CategoryType.Expense),
        ("Entertainment", CategoryType.Expense),
        ("Health", CategoryType.Expense),
        ("Education", CategoryType.Expense),
        ("Savings", CategoryType.Expense),
        ("Other", CategoryType.Expense)
    ];

    public static async Task InitializeAsync(SqliteDatabase db)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();

        await using (var create = conn.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = Schema;
            await create.ExecuteNonQueryAsync();
        }

        long count;
        await using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM Category;";
            count = (long)(await check.ExecuteScalarAsync())!;
        }

        if (count == 0)
        {
            foreach (var (name, type) in DefaultCategories)
            {
                await using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO Category (Name, Type) VALUES ($name, $type);";
                insert.Parameters.AddWithValue("$name", name);
                insert.Parameters.AddWithValue("$type", (int)type);
                await insert.ExecuteNonQueryAsync();
            }
        }

        await tx.CommitAsync();
    }
}
