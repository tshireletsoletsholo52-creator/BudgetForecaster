$ErrorActionPreference = 'Stop'

if (-not (Test-Path .\BudgetForecaster.slnx)) {
    Write-Host "Run this from C:\dev\BudgetForecasterSolution (the folder with BudgetForecaster.slnx)." -ForegroundColor Red
    exit 1
}

Set-Content -Path BudgetForecaster.Core\Abstractions\ITransactionRepository.cs -Encoding UTF8 -Value @'
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
'@

Set-Content -Path BudgetForecaster.Data\TransactionRepository.cs -Encoding UTF8 -Value @'
using System.Globalization;
using BudgetForecaster.Core;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using Microsoft.Data.Sqlite;

namespace BudgetForecaster.Data;

public class TransactionRepository : ITransactionRepository
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly SqliteDatabase _db;

    public TransactionRepository(SqliteDatabase db) => _db = db;

    public async Task<Transaction> AddAsync(Transaction transaction)
    {
        Validate(transaction);

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "Transaction" (CategoryId, AmountCents, Date, Note)
            VALUES ($categoryId, $cents, $date, $note);
            SELECT last_insert_rowid();
            """;
        AddParameters(cmd, transaction);

        transaction.Id = (int)(long)(await cmd.ExecuteScalarAsync())!;
        return transaction;
    }

    public async Task UpdateAsync(Transaction transaction)
    {
        Validate(transaction);

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE "Transaction"
            SET CategoryId = $categoryId, AmountCents = $cents, Date = $date, Note = $note
            WHERE Id = $id;
            """;
        AddParameters(cmd, transaction);
        cmd.Parameters.AddWithValue("$id", transaction.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"Transaction\" WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Transaction?> GetByIdAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CategoryId, AmountCents, Date, Note
            FROM "Transaction"
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Transaction>> GetForMonthAsync(DateOnly month)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var next = start.AddMonths(1);

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CategoryId, AmountCents, Date, Note
            FROM "Transaction"
            WHERE Date >= $start AND Date < $next
            ORDER BY Date DESC, Id DESC;
            """;
        cmd.Parameters.AddWithValue("$start", start.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$next", next.ToString(DateFormat, CultureInfo.InvariantCulture));

        var result = new List<Transaction>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    private static Transaction Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        CategoryId = reader.GetInt32(1),
        Amount = Money.FromCents(reader.GetInt64(2)),
        Date = DateOnly.ParseExact(reader.GetString(3), DateFormat, CultureInfo.InvariantCulture),
        Note = reader.IsDBNull(4) ? null : reader.GetString(4)
    };

    private static void Validate(Transaction t)
    {
        if (t.Amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(t));
        if (t.CategoryId <= 0)
            throw new ArgumentException("A category is required.", nameof(t));
    }

    private static void AddParameters(SqliteCommand cmd, Transaction t)
    {
        cmd.Parameters.AddWithValue("$categoryId", t.CategoryId);
        cmd.Parameters.AddWithValue("$cents", Money.ToCents(t.Amount));
        cmd.Parameters.AddWithValue("$date", t.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$note", (object?)t.Note ?? DBNull.Value);
    }
}
'@

Set-Content -Path BudgetForecaster.Tests\TransactionEditTests.cs -Encoding UTF8 -Value @'
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
'@

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

            var all = await _categories.GetAllAsync(includeArchived: true);
            _lookup = all.ToDictionary(c => c.Id);

            foreach (var c in all.Where(c => !c.IsArchived))
                Categories.Add(c);

            SelectedCategory = Categories.FirstOrDefault(c => c.Type == CategoryType.Expense);
            _initialized = true;
        }

        await RefreshAsync();
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

Set-Content -Path BudgetForecaster\MainPage.xaml -Encoding UTF8 -Value @'
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:local="clr-namespace:BudgetForecaster"
             x:Class="BudgetForecaster.MainPage"
             Title="Runway">

    <Grid RowDefinitions="Auto,Auto,Auto,*" Padding="16" RowSpacing="12">

        <!-- Month selector -->
        <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto">
            <Button Grid.Column="0" Text="&lt;" Command="{Binding PreviousMonthCommand}" />
            <Label Grid.Column="1" Text="{Binding MonthLabel}" FontSize="22" FontAttributes="Bold"
                   HorizontalOptions="Center" VerticalOptions="Center" />
            <Button Grid.Column="2" Text="&gt;" Command="{Binding NextMonthCommand}" />
        </Grid>

        <!-- Totals -->
        <Grid Grid.Row="1" ColumnDefinitions="*,*,*">
            <VerticalStackLayout Grid.Column="0">
                <Label Text="Income" FontSize="12" />
                <Label Text="{Binding IncomeText}" FontAttributes="Bold" />
            </VerticalStackLayout>
            <VerticalStackLayout Grid.Column="1">
                <Label Text="Expenses" FontSize="12" />
                <Label Text="{Binding ExpensesText}" FontAttributes="Bold" />
            </VerticalStackLayout>
            <VerticalStackLayout Grid.Column="2">
                <Label Text="Net cash" FontSize="12" />
                <Label Text="{Binding NetText}" FontAttributes="Bold" />
            </VerticalStackLayout>
        </Grid>

        <!-- Add / edit transaction -->
        <VerticalStackLayout Grid.Row="2" Spacing="8">
            <Label Text="Editing a transaction" FontAttributes="Bold" TextColor="OrangeRed"
                   IsVisible="{Binding IsEditing}" />
            <Entry Placeholder="Amount (e.g. 150.50)" Keyboard="Numeric" Text="{Binding AmountText}" />
            <Picker Title="Category" ItemsSource="{Binding Categories}"
                    ItemDisplayBinding="{Binding Name}" SelectedItem="{Binding SelectedCategory}" />
            <DatePicker Date="{Binding SelectedDate}" />
            <Entry Placeholder="Note (optional)" Text="{Binding Note}" />
            <Button Text="{Binding SaveButtonText}" Command="{Binding SaveCommand}" />
            <Button Text="Cancel edit" Command="{Binding CancelEditCommand}" IsVisible="{Binding IsEditing}" />
            <Label Text="{Binding ErrorMessage}" TextColor="Red" />
        </VerticalStackLayout>

        <!-- Transaction list -->
        <CollectionView Grid.Row="3" ItemsSource="{Binding Rows}">
            <CollectionView.EmptyView>
                <Label Text="No transactions this month." HorizontalOptions="Center" Margin="0,24" />
            </CollectionView.EmptyView>
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid ColumnDefinitions="70,*,Auto,Auto,Auto" ColumnSpacing="8" Padding="0,6">
                        <Label Grid.Column="0" Text="{Binding DateText}" VerticalOptions="Center" />
                        <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
                            <Label Text="{Binding CategoryName}" FontAttributes="Bold" />
                            <Label Text="{Binding Note}" FontSize="12" />
                        </VerticalStackLayout>
                        <Label Grid.Column="2" Text="{Binding AmountText}" VerticalOptions="Center" />
                        <Button Grid.Column="3" Text="Edit"
                                Command="{Binding Source={RelativeSource AncestorType={x:Type local:MainViewModel}}, Path=EditCommand}"
                                CommandParameter="{Binding Id}" />
                        <Button Grid.Column="4" Text="Delete"
                                Command="{Binding Source={RelativeSource AncestorType={x:Type local:MainViewModel}}, Path=DeleteCommand}"
                                CommandParameter="{Binding Id}" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
'@

Write-Host ""
Write-Host "Files written. Running the tests..." -ForegroundColor Green
dotnet test BudgetForecaster.Tests
