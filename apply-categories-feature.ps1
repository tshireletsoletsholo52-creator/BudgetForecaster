$ErrorActionPreference = 'Stop'

if (-not (Test-Path .\BudgetForecaster.slnx)) {
    Write-Host "Run this from C:\dev\BudgetForecasterSolution (the folder with BudgetForecaster.slnx)." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- Core
Set-Content -Path BudgetForecaster.Core\CategoryValidationException.cs -Encoding UTF8 -Value @'
namespace BudgetForecaster.Core;

/// <summary>Thrown when a category name or action breaks a rule. The message is safe to show to the user.</summary>
public class CategoryValidationException : Exception
{
    public CategoryValidationException(string message) : base(message) { }
}
'@

Set-Content -Path BudgetForecaster.Core\CategoryRules.cs -Encoding UTF8 -Value @'
using System.Text.RegularExpressions;

namespace BudgetForecaster.Core;

public static class CategoryRules
{
    public const int MaxNameLength = 40;

    /// <summary>Trims the name and collapses repeated spaces. Throws if it is blank or too long.</summary>
    public static string NormalizeName(string? raw)
    {
        var name = Regex.Replace(raw ?? "", @"\s+", " ").Trim();

        if (name.Length == 0)
            throw new CategoryValidationException("Enter a category name.");
        if (name.Length > MaxNameLength)
            throw new CategoryValidationException($"Category names can be at most {MaxNameLength} characters.");

        return name;
    }
}
'@

Set-Content -Path BudgetForecaster.Core\Abstractions\ICategoryRepository.cs -Encoding UTF8 -Value @'
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
'@

# ---------------------------------------------------------------- Data
Set-Content -Path BudgetForecaster.Data\CategoryRepository.cs -Encoding UTF8 -Value @'
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
'@

# ---------------------------------------------------------------- Tests
Set-Content -Path BudgetForecaster.Tests\CategoryRulesTests.cs -Encoding UTF8 -Value @'
using BudgetForecaster.Core;

namespace BudgetForecaster.Tests;

public class CategoryRulesTests
{
    [Fact]
    public void NormalizeName_TrimsAndCollapsesSpaces()
    {
        Assert.Equal("Eating out", CategoryRules.NormalizeName("  Eating    out  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeName_BlankIsRejected(string? raw)
    {
        Assert.Throws<CategoryValidationException>(() => CategoryRules.NormalizeName(raw));
    }

    [Fact]
    public void NormalizeName_ExactlyMaxLength_IsAccepted()
    {
        Assert.Equal(CategoryRules.MaxNameLength, CategoryRules.NormalizeName(new string('a', CategoryRules.MaxNameLength)).Length);
    }

    [Fact]
    public void NormalizeName_OverMaxLength_IsRejected()
    {
        Assert.Throws<CategoryValidationException>(() => CategoryRules.NormalizeName(new string('a', CategoryRules.MaxNameLength + 1)));
    }
}
'@

Set-Content -Path BudgetForecaster.Tests\CategoryManagementTests.cs -Encoding UTF8 -Value @'
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
'@

# ---------------------------------------------------------------- App: Categories screen
Set-Content -Path BudgetForecaster\CategoriesViewModel.cs -Encoding UTF8 -Value @'
using System.Collections.ObjectModel;
using BudgetForecaster.Core;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetForecaster;

public record CategoryRow(int Id, string Name, string TypeLabel, bool IsArchived)
{
    public bool IsActive => !IsArchived;
    public string Subtitle => IsArchived ? $"{TypeLabel} (archived)" : TypeLabel;
}

public partial class CategoriesViewModel : ObservableObject
{
    private readonly SqliteDatabase _db;
    private readonly ICategoryRepository _categories;

    public CategoriesViewModel(SqliteDatabase db, ICategoryRepository categories)
    {
        _db = db;
        _categories = categories;
    }

    public ObservableCollection<CategoryRow> Rows { get; } = new();
    public string[] TypeChoices { get; } = { "Expense", "Income" };

    [ObservableProperty] private string _nameText = "";
    [ObservableProperty] private string _selectedTypeName = "Expense";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _infoMessage = "";
    [ObservableProperty] private bool _showArchived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenaming))]
    [NotifyPropertyChangedFor(nameof(IsNotRenaming))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int? _renamingId;

    public bool IsRenaming => RenamingId.HasValue;
    public bool IsNotRenaming => !IsRenaming;
    public string SaveButtonText => IsRenaming ? "Save name" : "Add category";

    public async Task LoadAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        await RefreshAsync();
    }

    partial void OnShowArchivedChanged(bool value)
    {
        _ = RefreshSafeAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearMessages();

        try
        {
            if (RenamingId is int id)
            {
                await _categories.RenameAsync(id, NameText);
                InfoMessage = "Category renamed.";
            }
            else
            {
                var type = SelectedTypeName == "Income" ? CategoryType.Income : CategoryType.Expense;
                await _categories.AddAsync(new Category { Name = NameText, Type = type });
                InfoMessage = "Category added.";
            }
        }
        catch (CategoryValidationException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        ResetForm();
        await RefreshAsync();
    }

    [RelayCommand]
    private void StartRename(int id)
    {
        ClearMessages();

        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row is null) return;

        RenamingId = row.Id;
        NameText = row.Name;
        SelectedTypeName = row.TypeLabel;
    }

    [RelayCommand]
    private void CancelRename()
    {
        ClearMessages();
        ResetForm();
    }

    [RelayCommand]
    private async Task ArchiveAsync(int id)
    {
        ClearMessages();
        await _categories.ArchiveAsync(id);
        if (RenamingId == id) ResetForm();
        InfoMessage = "Archived. Past transactions and budgets are kept.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RestoreAsync(int id)
    {
        ClearMessages();
        await _categories.RestoreAsync(id);
        InfoMessage = "Category restored.";
        await RefreshAsync();
    }

    private void ResetForm()
    {
        RenamingId = null;
        NameText = "";
        SelectedTypeName = "Expense";
    }

    private void ClearMessages()
    {
        ErrorMessage = "";
        InfoMessage = "";
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task RefreshAsync()
    {
        var all = await _categories.GetAllAsync(includeArchived: ShowArchived);

        Rows.Clear();
        foreach (var c in all)
        {
            Rows.Add(new CategoryRow(
                c.Id,
                c.Name,
                c.Type == CategoryType.Income ? "Income" : "Expense",
                c.IsArchived));
        }
    }
}
'@

Set-Content -Path BudgetForecaster\CategoriesPage.xaml.cs -Encoding UTF8 -Value @'
namespace BudgetForecaster;

public partial class CategoriesPage : ContentPage
{
    private readonly CategoriesViewModel _vm;

    public CategoriesPage(CategoriesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
'@

Set-Content -Path BudgetForecaster\CategoriesPage.xaml -Encoding UTF8 -Value @'
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:local="clr-namespace:BudgetForecaster"
             x:Class="BudgetForecaster.CategoriesPage"
             Title="Categories">

    <Grid RowDefinitions="Auto,Auto,*" Padding="16" RowSpacing="12">

        <VerticalStackLayout Grid.Row="0" Spacing="8">
            <Label Text="Renaming a category" FontAttributes="Bold" TextColor="OrangeRed"
                   IsVisible="{Binding IsRenaming}" />
            <Entry Placeholder="Category name (e.g. Pets)" MaxLength="40" Text="{Binding NameText}" />
            <Picker Title="Type" ItemsSource="{Binding TypeChoices}"
                    SelectedItem="{Binding SelectedTypeName}" IsEnabled="{Binding IsNotRenaming}" />
            <Button Text="{Binding SaveButtonText}" Command="{Binding SaveCommand}" />
            <Button Text="Cancel" Command="{Binding CancelRenameCommand}" IsVisible="{Binding IsRenaming}" />
        </VerticalStackLayout>

        <VerticalStackLayout Grid.Row="1" Spacing="4">
            <Label Text="{Binding ErrorMessage}" TextColor="Red" />
            <Label Text="{Binding InfoMessage}" TextColor="SeaGreen" />
            <HorizontalStackLayout Spacing="8">
                <Switch IsToggled="{Binding ShowArchived}" />
                <Label Text="Show archived" VerticalOptions="Center" />
            </HorizontalStackLayout>
        </VerticalStackLayout>

        <CollectionView Grid.Row="2" ItemsSource="{Binding Rows}">
            <CollectionView.EmptyView>
                <Label Text="No categories yet." HorizontalOptions="Center" Margin="0,24" />
            </CollectionView.EmptyView>
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="8" Padding="0,6">
                        <VerticalStackLayout Grid.Column="0" VerticalOptions="Center">
                            <Label Text="{Binding Name}" FontAttributes="Bold" />
                            <Label Text="{Binding Subtitle}" FontSize="12" />
                        </VerticalStackLayout>
                        <Button Grid.Column="1" Text="Rename" IsVisible="{Binding IsActive}"
                                Command="{Binding Source={RelativeSource AncestorType={x:Type local:CategoriesViewModel}}, Path=StartRenameCommand}"
                                CommandParameter="{Binding Id}" />
                        <Button Grid.Column="2" Text="Archive" IsVisible="{Binding IsActive}"
                                Command="{Binding Source={RelativeSource AncestorType={x:Type local:CategoriesViewModel}}, Path=ArchiveCommand}"
                                CommandParameter="{Binding Id}" />
                        <Button Grid.Column="2" Text="Restore" IsVisible="{Binding IsArchived}"
                                Command="{Binding Source={RelativeSource AncestorType={x:Type local:CategoriesViewModel}}, Path=RestoreCommand}"
                                CommandParameter="{Binding Id}" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
'@

Set-Content -Path BudgetForecaster\AppShell.xaml -Encoding UTF8 -Value @'
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="BudgetForecaster.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:local="clr-namespace:BudgetForecaster"
    Title="Runway">

    <TabBar>
        <ShellContent Title="Transactions" ContentTemplate="{DataTemplate local:MainPage}" Route="MainPage" />
        <ShellContent Title="Budget" ContentTemplate="{DataTemplate local:BudgetPage}" Route="BudgetPage" />
        <ShellContent Title="Statement" ContentTemplate="{DataTemplate local:StatementPage}" Route="StatementPage" />
        <ShellContent Title="Forecast" ContentTemplate="{DataTemplate local:ForecastPage}" Route="ForecastPage" />
        <ShellContent Title="Categories" ContentTemplate="{DataTemplate local:CategoriesPage}" Route="CategoriesPage" />
    </TabBar>
</Shell>
'@

# The Transactions screen must reload categories every time it appears, so new, renamed
# and archived categories show up in its picker without restarting the app.
Set-Content -Path BudgetForecaster\MainViewModel.cs -Encoding UTF8 -Value @'
using System.Collections.ObjectModel;
using System.Globalization;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetForecaster;

public record TransactionRow(int Id, string CategoryName, string Note, string DateText, string AmountText);

public partial class MainViewModel : ObservableObject
{
    private readonly SqliteDatabase _db;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private Dictionary<int, Category> _lookup = new();
    private bool _initialized;

    public MainViewModel(SqliteDatabase db, ICategoryRepository categories, ITransactionRepository transactions)
    {
        _db = db;
        _categories = categories;
        _transactions = transactions;
    }

    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<TransactionRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel))]
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _incomeText = "R 0.00";
    [ObservableProperty] private string _expensesText = "R 0.00";
    [ObservableProperty] private string _netText = "R 0.00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int? _editingId;

    public bool IsEditing => EditingId.HasValue;
    public string SaveButtonText => IsEditing ? "Save changes" : "Add transaction";
    public string MonthLabel => Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public async Task LoadAsync()
    {
        if (!_initialized)
        {
            await DatabaseInitializer.InitializeAsync(_db);
            _initialized = true;
        }

        await ReloadCategoriesAsync();
        await RefreshAsync();
    }

    private async Task ReloadCategoriesAsync()
    {
        var all = await _categories.GetAllAsync(includeArchived: true);
        _lookup = all.ToDictionary(c => c.Id);

        var previousId = SelectedCategory?.Id;
        var visible = all.Where(c => !c.IsArchived).ToList();

        // While editing, keep the transaction's category in the picker even if it was archived.
        if (EditingId.HasValue && previousId is int keepId
            && visible.All(c => c.Id != keepId) && _lookup.TryGetValue(keepId, out var keep))
        {
            visible.Add(keep);
        }

        Categories.Clear();
        foreach (var c in visible) Categories.Add(c);

        SelectedCategory = Categories.FirstOrDefault(c => c.Id == previousId)
                           ?? Categories.FirstOrDefault(c => c.Type == CategoryType.Expense);
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        Month = Month.AddMonths(-1);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        Month = Month.AddMonths(1);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = "";

        if (!TryParseAmount(AmountText, out var amount) || amount <= 0)
        {
            ErrorMessage = "Enter an amount greater than zero.";
            return;
        }
        if (SelectedCategory is null)
        {
            ErrorMessage = "Pick a category.";
            return;
        }

        var transaction = new Transaction
        {
            Id = EditingId ?? 0,
            CategoryId = SelectedCategory.Id,
            Amount = Math.Round(amount, 2),
            Date = DateOnly.FromDateTime(SelectedDate),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()
        };

        if (EditingId.HasValue)
            await _transactions.UpdateAsync(transaction);
        else
            await _transactions.AddAsync(transaction);

        var savedDate = SelectedDate;
        var wasEditing = IsEditing;

        ResetForm(resetDate: wasEditing);
        Month = new DateTime(savedDate.Year, savedDate.Month, 1);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task EditAsync(int id)
    {
        ErrorMessage = "";

        var t = await _transactions.GetByIdAsync(id);
        if (t is null)
        {
            ErrorMessage = "That transaction no longer exists.";
            await RefreshAsync();
            return;
        }

        // A transaction may belong to an archived category that is not in the picker.
        if (Categories.All(c => c.Id != t.CategoryId) && _lookup.TryGetValue(t.CategoryId, out var archived))
            Categories.Add(archived);

        EditingId = t.Id;
        AmountText = t.Amount.ToString("F2", CultureInfo.CurrentCulture);
        SelectedCategory = Categories.First(c => c.Id == t.CategoryId);
        SelectedDate = t.Date.ToDateTime(TimeOnly.MinValue);
        Note = t.Note ?? "";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ErrorMessage = "";
        ResetForm(resetDate: true);
    }

    [RelayCommand]
    private async Task DeleteAsync(int id)
    {
        await _transactions.DeleteAsync(id);
        if (EditingId == id) ResetForm(resetDate: true);
        await RefreshAsync();
    }

    private void ResetForm(bool resetDate)
    {
        EditingId = null;
        AmountText = "";
        Note = "";
        if (resetDate) SelectedDate = DateTime.Today;
    }

    private async Task RefreshAsync()
    {
        var items = await _transactions.GetForMonthAsync(DateOnly.FromDateTime(Month));

        Rows.Clear();
        decimal income = 0, expenses = 0;

        foreach (var t in items)
        {
            var category = _lookup[t.CategoryId];
            var isIncome = category.Type == CategoryType.Income;

            if (isIncome) income += t.Amount; else expenses += t.Amount;

            Rows.Add(new TransactionRow(
                t.Id,
                category.Name,
                t.Note ?? "",
                t.Date.ToString("dd MMM", CultureInfo.InvariantCulture),
                (isIncome ? "+ " : "- ") + Format(t.Amount)));
        }

        IncomeText = Format(income);
        ExpensesText = Format(expenses);
        NetText = Format(income - expenses);
    }

    private static string Format(decimal amount) =>
        (amount < 0 ? "-R " : "R ") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
}
'@

# ---------------------------------------------------------------- Register the new screen in MauiProgram.cs
$mp = "BudgetForecaster\MauiProgram.cs"
$text = Get-Content $mp -Raw
if ($text -match "CategoriesViewModel") {
    Write-Host "MauiProgram.cs already registers the Categories screen. Skipping." -ForegroundColor Yellow
}
elseif ($text -notmatch "return builder\.Build\(\);") {
    Write-Host "Could not find 'return builder.Build();' in MauiProgram.cs. Add these two lines above it yourself:" -ForegroundColor Red
    Write-Host "    builder.Services.AddSingleton<CategoriesViewModel>();"
    Write-Host "    builder.Services.AddTransient<CategoriesPage>();"
}
else {
    $add = "builder.Services.AddSingleton<CategoriesViewModel>();`r`n        builder.Services.AddTransient<CategoriesPage>();`r`n`r`n        return builder.Build();"
    $text = $text -replace "return builder\.Build\(\);", $add
    Set-Content -Path $mp -Value $text -Encoding UTF8 -NoNewline
    Write-Host "Registered the Categories screen in MauiProgram.cs." -ForegroundColor Green
}

Write-Host ""
Write-Host "Files written. Running the tests..." -ForegroundColor Green
dotnet test BudgetForecaster.Tests

Write-Host ""
Write-Host "Check: MauiProgram.cs should list CategoriesViewModel below." -ForegroundColor Cyan
Select-String -Path $mp -Pattern "Categories"
