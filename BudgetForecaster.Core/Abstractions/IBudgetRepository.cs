using BudgetForecaster.Core.Models;

namespace BudgetForecaster.Core.Abstractions;

public interface IBudgetRepository
{
    Task<IReadOnlyList<Budget>> GetForMonthAsync(DateOnly month);

    /// <summary>Creates or updates the budget for one category in one month.</summary>
    Task SetAsync(int categoryId, DateOnly month, decimal amount);

    /// <summary>
    /// Copies the previous month's budgets into the given month.
    /// Categories that already have a budget in the target month are left untouched.
    /// Returns how many budgets were copied.
    /// </summary>
    Task<int> CopyFromPreviousMonthAsync(DateOnly month);
}
