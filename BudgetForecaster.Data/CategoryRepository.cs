using BudgetForecaster.Core;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using Microsoft.Data.Sqlite;

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
        var name = CategoryRules.NormalizeName(category.Name);

        await using var conn = await _db.OpenAsync();
        await EnsureNameIsFreeAsync(conn, name, category.Type, excludeId: null);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Category (Name, Type, IsArchived) VALUES ($name, $type, 0);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$type", (int)category.Type);

        category.Id = (int)(long)(await cmd.ExecuteScalarAsync())!;
        category.Name = name;
        category.IsArchived = false;
        return category;
    }

    public async Task RenameAsync(int id, string newName)
    {
        var name = CategoryRules.NormalizeName(newName);

        await using var conn = await _db.OpenAsync();

        CategoryType type;
        await using (var find = conn.CreateCommand())
        {
            find.CommandText = "SELECT Type FROM Category WHERE Id = $id;";
            find.Parameters.AddWithValue("$id", id);
            var found = await find.ExecuteScalarAsync();
            if (found is null)
                throw new CategoryValidationException("That category no longer exists.");
            type = (CategoryType)Convert.ToInt32(found);
        }

        await EnsureNameIsFreeAsync(conn, name, type, excludeId: id);

        await using var update = conn.CreateCommand();
        update.CommandText = "UPDATE Category SET Name = $name WHERE Id = $id;";
        update.Parameters.AddWithValue("$name", name);
        update.Parameters.AddWithValue("$id", id);
        await update.ExecuteNonQueryAsync();
    }

    public Task ArchiveAsync(int id) => SetArchivedAsync(id, archived: true);

    public Task RestoreAsync(int id) => SetArchivedAsync(id, archived: false);

    private async Task SetArchivedAsync(int id, bool archived)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Category SET IsArchived = $flag WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$flag", archived ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // Names must be unique per type, ignoring upper/lower case, and archived categories count too.
    private static async Task EnsureNameIsFreeAsync(SqliteConnection conn, string name, CategoryType type, int? excludeId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IsArchived FROM Category
            WHERE Name = $name COLLATE NOCASE
              AND Type = $type
              AND ($excludeId IS NULL OR Id <> $excludeId)
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$type", (int)type);
        cmd.Parameters.AddWithValue("$excludeId", (object?)excludeId ?? DBNull.Value);

        var existing = await cmd.ExecuteScalarAsync();
        if (existing is null) return;

        var archived = Convert.ToInt32(existing) == 1;
        throw new CategoryValidationException(archived
            ? $"\"{name}\" already exists but is archived. Restore it instead."
            : $"A category named \"{name}\" already exists.");
    }
}
