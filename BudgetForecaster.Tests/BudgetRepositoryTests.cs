using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;

namespace BudgetForecaster.Tests;

public class BudgetRepositoryTests : IDisposable
{
    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July = new(2026, 7, 1);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bf_test_{Guid.NewGuid():N}.db");
    private readonly SqliteDatabase _db;
    private readonly CategoryRepository _categories;
    private readonly BudgetRepository _budgets;

    public BudgetRepositoryTests()
    {
        _db = new SqliteDatabase(_path);
        _categories = new CategoryRepository(_db);
        _budgets = new BudgetRepository(_db);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private async Task<(int First, int Second)> TwoExpenseCategoriesAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        var expenses = (await _categories.GetAllAsync()).Where(c => c.Type == CategoryType.Expense).Take(2).ToList();
        return (expenses[0].Id, expenses[1].Id);
    }

    [Fact]
    public async Task Set_SavesBudget_AndItCanBeReadBack()
    {
        var (groceries, _) = await TwoExpenseCategoriesAsync();

        await _budgets.SetAsync(groceries, June, 3000.50m);

        var saved = Assert.Single(await _budgets.GetForMonthAsync(June));
        Assert.Equal(groceries, saved.CategoryId);
        Assert.Equal(3000.50m, saved.Amount);
        Assert.Equal(June, saved.Month);
    }

    [Fact]
    public async Task Set_Twice_UpdatesInsteadOfDuplicating()
    {
        var (groceries, _) = await TwoExpenseCategoriesAsync();

        await _budgets.SetAsync(groceries, June, 3000m);
        await _budgets.SetAsync(groceries, June, 3500m);

        var saved = Assert.Single(await _budgets.GetForMonthAsync(June));
        Assert.Equal(3500m, saved.Amount);
    }

    [Fact]
    public async Task GetForMonth_ReturnsOnlyThatMonth()
    {
        var (groceries, _) = await TwoExpenseCategoriesAsync();

        await _budgets.SetAsync(groceries, May, 100m);
        await _budgets.SetAsync(groceries, June, 200m);
        await _budgets.SetAsync(groceries, July, 300m);

        var june = Assert.Single(await _budgets.GetForMonthAsync(June));
        Assert.Equal(200m, june.Amount);
    }

    [Fact]
    public async Task Set_NegativeAmount_IsRejected()
    {
        var (groceries, _) = await TwoExpenseCategoriesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _budgets.SetAsync(groceries, June, -1m));
    }

    [Fact]
    public async Task Set_UnknownCategory_IsRejectedByDatabase()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        await Assert.ThrowsAnyAsync<Exception>(() => _budgets.SetAsync(9999, June, 100m));
    }

    [Fact]
    public async Task CopyFromPreviousMonth_CopiesAllBudgets()
    {
        var (groceries, rent) = await TwoExpenseCategoriesAsync();
        await _budgets.SetAsync(groceries, May, 3000m);
        await _budgets.SetAsync(rent, May, 8500m);

        var copied = await _budgets.CopyFromPreviousMonthAsync(June);

        Assert.Equal(2, copied);
        Assert.Equal(2, (await _budgets.GetForMonthAsync(June)).Count);
    }

    [Fact]
    public async Task CopyFromPreviousMonth_DoesNotOverwriteExistingBudgets()
    {
        var (groceries, rent) = await TwoExpenseCategoriesAsync();
        await _budgets.SetAsync(groceries, May, 3000m);
        await _budgets.SetAsync(rent, May, 8500m);
        await _budgets.SetAsync(groceries, June, 3500m);

        var copied = await _budgets.CopyFromPreviousMonthAsync(June);

        Assert.Equal(1, copied);
        var june = await _budgets.GetForMonthAsync(June);
        Assert.Equal(3500m, june.Single(b => b.CategoryId == groceries).Amount);
        Assert.Equal(8500m, june.Single(b => b.CategoryId == rent).Amount);
    }
}
