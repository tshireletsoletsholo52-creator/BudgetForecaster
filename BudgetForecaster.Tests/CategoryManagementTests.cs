using BudgetForecaster.Core;
using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;

namespace BudgetForecaster.Tests;

public class CategoryManagementTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bf_test_{Guid.NewGuid():N}.db");
    private readonly SqliteDatabase _db;
    private readonly CategoryRepository _categories;
    private readonly TransactionRepository _transactions;

    public CategoryManagementTests()
    {
        _db = new SqliteDatabase(_path);
        _categories = new CategoryRepository(_db);
        _transactions = new TransactionRepository(_db);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private async Task<Category> AddPetsAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        return await _categories.AddAsync(new Category { Name = "Pets", Type = CategoryType.Expense });
    }

    [Fact]
    public async Task Add_TrimsTheName()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        var added = await _categories.AddAsync(new Category { Name = "  Pets  ", Type = CategoryType.Expense });

        Assert.Equal("Pets", added.Name);
        Assert.Contains(await _categories.GetAllAsync(), c => c.Name == "Pets");
    }

    [Fact]
    public async Task Add_DuplicateIgnoringCase_IsRejected()
    {
        await AddPetsAsync();

        await Assert.ThrowsAsync<CategoryValidationException>(() =>
            _categories.AddAsync(new Category { Name = "pEtS", Type = CategoryType.Expense }));
    }

    [Fact]
    public async Task Add_SameNameInOtherType_IsAllowed()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        await _categories.AddAsync(new Category { Name = "Bonus", Type = CategoryType.Income });
        await _categories.AddAsync(new Category { Name = "Bonus", Type = CategoryType.Expense });

        Assert.Equal(2, (await _categories.GetAllAsync()).Count(c => c.Name == "Bonus"));
    }

    [Fact]
    public async Task Add_NameOfSeededCategory_IsRejected()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        await Assert.ThrowsAsync<CategoryValidationException>(() =>
            _categories.AddAsync(new Category { Name = "groceries", Type = CategoryType.Expense }));
    }

    [Fact]
    public async Task Add_NameOfArchivedCategory_SuggestsRestoring()
    {
        var pets = await AddPetsAsync();
        await _categories.ArchiveAsync(pets.Id);

        var ex = await Assert.ThrowsAsync<CategoryValidationException>(() =>
            _categories.AddAsync(new Category { Name = "Pets", Type = CategoryType.Expense }));

        Assert.Contains("archived", ex.Message);
    }

    [Fact]
    public async Task Rename_ChangesTheName()
    {
        var pets = await AddPetsAsync();

        await _categories.RenameAsync(pets.Id, "  Pet care ");

        var all = await _categories.GetAllAsync();
        Assert.Contains(all, c => c.Name == "Pet care");
        Assert.DoesNotContain(all, c => c.Name == "Pets");
    }

    [Fact]
    public async Task Rename_ToAnExistingName_IsRejected()
    {
        var pets = await AddPetsAsync();

        await Assert.ThrowsAsync<CategoryValidationException>(() => _categories.RenameAsync(pets.Id, "groceries"));
    }

    [Fact]
    public async Task Rename_ChangingOnlyTheCase_IsAllowed()
    {
        var pets = await AddPetsAsync();

        await _categories.RenameAsync(pets.Id, "PETS");

        Assert.Contains(await _categories.GetAllAsync(), c => c.Id == pets.Id && c.Name == "PETS");
    }

    [Fact]
    public async Task Rename_UnknownId_IsRejected()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        await Assert.ThrowsAsync<CategoryValidationException>(() => _categories.RenameAsync(9999, "Anything"));
    }

    [Fact]
    public async Task Rename_BlankName_IsRejected()
    {
        var pets = await AddPetsAsync();

        await Assert.ThrowsAsync<CategoryValidationException>(() => _categories.RenameAsync(pets.Id, "   "));
    }

    [Fact]
    public async Task Rename_KeepsTypeAndTransactionLink()
    {
        var pets = await AddPetsAsync();
        await _transactions.AddAsync(new Transaction { CategoryId = pets.Id, Amount = 50m, Date = new DateOnly(2026, 6, 5) });

        await _categories.RenameAsync(pets.Id, "Pet care");

        var renamed = (await _categories.GetAllAsync()).Single(c => c.Id == pets.Id);
        Assert.Equal("Pet care", renamed.Name);
        Assert.Equal(CategoryType.Expense, renamed.Type);

        var june = await _transactions.GetForMonthAsync(new DateOnly(2026, 6, 1));
        Assert.Equal(pets.Id, Assert.Single(june).CategoryId);
    }

    [Fact]
    public async Task Archive_KeepsTransactions_AndRestoreBringsCategoryBack()
    {
        var pets = await AddPetsAsync();
        await _transactions.AddAsync(new Transaction { CategoryId = pets.Id, Amount = 50m, Date = new DateOnly(2026, 6, 5) });

        await _categories.ArchiveAsync(pets.Id);

        Assert.DoesNotContain(await _categories.GetAllAsync(), c => c.Id == pets.Id);
        Assert.Single(await _transactions.GetForMonthAsync(new DateOnly(2026, 6, 1)));

        await _categories.RestoreAsync(pets.Id);

        Assert.Contains(await _categories.GetAllAsync(), c => c.Id == pets.Id);
    }
}
