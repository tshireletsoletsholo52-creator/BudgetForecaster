using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;

namespace BudgetForecaster.Tests;

public class DataLayerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bf_test_{Guid.NewGuid():N}.db");
    private readonly SqliteDatabase _db;
    private readonly CategoryRepository _categories;
    private readonly TransactionRepository _transactions;

    public DataLayerTests()
    {
        _db = new SqliteDatabase(_path);
        _categories = new CategoryRepository(_db);
        _transactions = new TransactionRepository(_db);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private async Task<int> FirstExpenseCategoryIdAsync()
    {
        var all = await _categories.GetAllAsync();
        return all.First(c => c.Type == CategoryType.Expense).Id;
    }

    [Fact]
    public async Task Initialize_SeedsDefaultCategories()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        var all = await _categories.GetAllAsync();

        Assert.Equal(15, all.Count);
        Assert.Contains(all, c => c.Name == "Salary" && c.Type == CategoryType.Income);
        Assert.Contains(all, c => c.Name == "Rent" && c.Type == CategoryType.Expense);
    }

    [Fact]
    public async Task Initialize_CalledTwice_DoesNotDuplicateCategories()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        await DatabaseInitializer.InitializeAsync(_db);

        Assert.Equal(15, (await _categories.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ArchivedCategory_IsHiddenByDefault()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        var added = await _categories.AddAsync(new Category { Name = "Pets", Type = CategoryType.Expense });

        await _categories.ArchiveAsync(added.Id);

        Assert.DoesNotContain(await _categories.GetAllAsync(), c => c.Id == added.Id);
        Assert.Contains(await _categories.GetAllAsync(includeArchived: true), c => c.Id == added.Id);
    }

    [Fact]
    public async Task AddTransaction_RoundTripsAmountExactly()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        var categoryId = await FirstExpenseCategoryIdAsync();

        await _transactions.AddAsync(new Transaction
        {
            CategoryId = categoryId,
            Amount = 19.99m,
            Date = new DateOnly(2026, 6, 15),
            Note = "Lunch"
        });

        var june = await _transactions.GetForMonthAsync(new DateOnly(2026, 6, 1));

        var saved = Assert.Single(june);
        Assert.Equal(19.99m, saved.Amount);
        Assert.Equal("Lunch", saved.Note);
    }

    [Fact]
    public async Task GetForMonth_RespectsMonthBoundaries()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        var categoryId = await FirstExpenseCategoryIdAsync();

        await _transactions.AddAsync(new Transaction { CategoryId = categoryId, Amount = 10m, Date = new DateOnly(2026, 5, 31) });
        await _transactions.AddAsync(new Transaction { CategoryId = categoryId, Amount = 20m, Date = new DateOnly(2026, 6, 1) });
        await _transactions.AddAsync(new Transaction { CategoryId = categoryId, Amount = 30m, Date = new DateOnly(2026, 6, 30) });
        await _transactions.AddAsync(new Transaction { CategoryId = categoryId, Amount = 40m, Date = new DateOnly(2026, 7, 1) });

        var june = await _transactions.GetForMonthAsync(new DateOnly(2026, 6, 10));

        Assert.Equal(2, june.Count);
        Assert.Equal(50m, june.Sum(t => t.Amount));
    }

    [Fact]
    public async Task DeleteTransaction_RemovesIt()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        var categoryId = await FirstExpenseCategoryIdAsync();
        var saved = await _transactions.AddAsync(new Transaction { CategoryId = categoryId, Amount = 5m, Date = new DateOnly(2026, 6, 1) });

        await _transactions.DeleteAsync(saved.Id);

        Assert.Empty(await _transactions.GetForMonthAsync(new DateOnly(2026, 6, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AddTransaction_RejectsZeroOrNegativeAmounts(int amount)
    {
        await DatabaseInitializer.InitializeAsync(_db);
        var categoryId = await FirstExpenseCategoryIdAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _transactions.AddAsync(new Transaction { CategoryId = categoryId, Amount = amount, Date = new DateOnly(2026, 6, 1) }));
    }

    [Fact]
    public async Task AddTransaction_WithUnknownCategory_IsRejectedByDatabase()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _transactions.AddAsync(new Transaction { CategoryId = 9999, Amount = 10m, Date = new DateOnly(2026, 6, 1) }));
    }
}
