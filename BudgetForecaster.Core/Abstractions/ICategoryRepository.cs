using BudgetForecaster.Core.Models;

namespace BudgetForecaster.Core.Abstractions;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(bool includeArchived = false);
    Task<Category> AddAsync(Category category);
    Task ArchiveAsync(int id);
}
