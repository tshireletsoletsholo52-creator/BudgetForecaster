using BudgetForecaster.Core.Models;

namespace BudgetForecaster.Core.Abstractions;

public interface ITransactionRepository
{
    Task<Transaction> AddAsync(Transaction transaction);
    Task UpdateAsync(Transaction transaction);
    Task DeleteAsync(int id);

    /// <summary>Returns the transaction, or null if it does not exist.</summary>
    Task<Transaction?> GetByIdAsync(int id);

    /// <summary>Transactions in the calendar month containing the given date, newest first.</summary>
    Task<IReadOnlyList<Transaction>> GetForMonthAsync(DateOnly month);
}
