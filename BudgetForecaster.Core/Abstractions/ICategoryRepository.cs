using BudgetForecaster.Core.Models;

namespace BudgetForecaster.Core.Abstractions;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(bool includeArchived = false);

    /// <summary>Adds a category. Throws CategoryValidationException for a blank, too long or duplicate name.</summary>
    Task<Category> AddAsync(Category category);

    /// <summary>Renames a category. Its type never changes. Throws CategoryValidationException for a bad or duplicate name.</summary>
    Task RenameAsync(int id, string newName);

    /// <summary>Hides a category from pickers. Its transactions and budgets are kept.</summary>
    Task ArchiveAsync(int id);

    /// <summary>Makes an archived category available again.</summary>
    Task RestoreAsync(int id);
}
