using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;

namespace BudgetForecaster.Tests;

public class TransactionEditTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bf_test_{Guid.NewGuid():N}.db");
    private readonly SqliteDatabase _db;
    private readonly CategoryRepository _categories;
    private readonly TransactionRepository _transactions;

    public TransactionEditTests()
    {
        _db = new SqliteDatabase(_path);
        _categories = new CategoryRepository(_db);
        _transactions = new TransactionRepository(_db);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private async Task<(int Expense, int Income)> CategoryIdsAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        var all = await _categories.GetAllAsync();
        return (all.First(c => c.Type == CategoryType.Expense).Id,
                all.First(c => c.Type == CategoryType.Income).Id);
    }

    [Fact]
    public async Task GetById_ReturnsTheSavedTransaction()
    {
        var (expense, _) = await CategoryIdsAsync();
        var saved = await _transactions.AddAsync(new Transaction
        {
            CategoryId = expense, Amount = 99.95m, Date = new DateOnly(2026, 6, 10), Note = "Shoes"
        });

        var found = await _transactions.GetByIdAsync(saved.Id);

        Assert.NotNull(found);
        Assert.Equal(99.95m, found.Amount);
        Assert.Equal("Shoes", found.Note);
        Assert.Equal(new DateOnly(2026, 6, 10), found.Date);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        await CategoryIdsAsync();

        Assert.Null(await _transactions.GetByIdAsync(12345));
    }

    [Fact]
    public async Task Update_ChangesAllFields()
    {
        var (expense, income) = await CategoryIdsAsync();
        var saved = await _transactions.AddAsync(new Transaction
        {
            CategoryId = expense, Amount = 100m, Date = new DateOnly(2026, 6, 10), Note = "a"
        });

        saved.CategoryId = income;
        saved.Amount = 250.75m;
        saved.Date = new DateOnly(2026, 7, 2);
        saved.Note = "b";
        await _transactions.UpdateAsync(saved);

        var found = await _transactions.GetByIdAsync(saved.Id);
        Assert.NotNull(found);
        Assert.Equal(income, found.CategoryId);
        Assert.Equal(250.75m, found.Amount);
        Assert.Equal(new DateOnly(2026, 7, 2), found.Date);
        Assert.Equal("b", found.Note);
        Assert.Empty(await _transactions.GetForMonthAsync(new DateOnly(2026, 6, 1)));
        Assert.Single(await _transactions.GetForMonthAsync(new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public async Task Update_ToZeroAmount_IsRejected()
    {
        var (expense, _) = await CategoryIdsAsync();
        var saved = await _transactions.AddAsync(new Transaction
        {
            CategoryId = expense, Amount = 10m, Date = new DateOnly(2026, 6, 10)
        });

        saved.Amount = 0m;

        await Assert.ThrowsAsync<ArgumentException>(() => _transactions.UpdateAsync(saved));
    }
}
