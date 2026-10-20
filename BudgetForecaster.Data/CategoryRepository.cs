using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;

namespace BudgetForecaster.Data;

public class CategoryRepository : ICategoryRepository
{
    private readonly SqliteDatabase _db;

    public CategoryRepository(SqliteDatabase db) => _db = db;

    public async Task<IReadOnlyList<Category>> GetAllAsync(bool includeArchived = false)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT Id, Name, Type, IsArchived FROM Category ORDER BY Type, Name;"
            : "SELECT Id, Name, Type, IsArchived FROM Category WHERE IsArchived = 0 ORDER BY Type, Name;";

        var result = new List<Category>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new Category
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Type = (CategoryType)reader.GetInt32(2),
                IsArchived = reader.GetInt32(3) == 1
            });
        }
        return result;
    }

    public async Task<Category> AddAsync(Category category)
    {
        if (string.IsNullOrWhiteSpace(category.Name))
            throw new ArgumentException("Category name is required.", nameof(category));

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Category (Name, Type, IsArchived) VALUES ($name, $type, 0);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", category.Name.Trim());
        cmd.Parameters.AddWithValue("$type", (int)category.Type);

        category.Id = (int)(long)(await cmd.ExecuteScalarAsync())!;
        category.Name = category.Name.Trim();
        return category;
    }

    public async Task ArchiveAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Category SET IsArchived = 1 WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
