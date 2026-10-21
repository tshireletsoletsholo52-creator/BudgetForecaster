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

        await _transactions.AddAsync(new Transaction
        {
            CategoryId = SelectedCategory.Id,
            Amount = Math.Round(amount, 2),
            Date = DateOnly.FromDateTime(SelectedDate),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()
        });

        AmountText = "";
        Note = "";
        Month = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(int id)
    {
        await _transactions.DeleteAsync(id);
        await RefreshAsync();
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
