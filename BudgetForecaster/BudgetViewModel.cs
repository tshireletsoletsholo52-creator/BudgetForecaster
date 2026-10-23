using System.Collections.ObjectModel;
using System.Globalization;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetForecaster;

public partial class BudgetRowViewModel : ObservableObject
{
    public BudgetRowViewModel(Category category, decimal? existing)
    {
        CategoryId = category.Id;
        Name = category.Name;
        TypeLabel = category.Type == CategoryType.Income ? "Income" : "Expense";
        HadBudget = existing.HasValue;
        _amountText = existing.HasValue ? existing.Value.ToString("F2", CultureInfo.CurrentCulture) : "";
    }

    public int CategoryId { get; }
    public string Name { get; }
    public string TypeLabel { get; }
    public bool HadBudget { get; }

    [ObservableProperty] private string _amountText;
}

public partial class BudgetViewModel : ObservableObject
{
    private readonly SqliteDatabase _db;
    private readonly ICategoryRepository _categories;
    private readonly IBudgetRepository _budgets;

    public BudgetViewModel(SqliteDatabase db, ICategoryRepository categories, IBudgetRepository budgets)
    {
        _db = db;
        _categories = categories;
        _budgets = budgets;
    }

    public ObservableCollection<BudgetRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel))]
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _infoMessage = "";

    public string MonthLabel => Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public async Task LoadAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        Month = Month.AddMonths(-1);
        ClearMessages();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        Month = Month.AddMonths(1);
        ClearMessages();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CopyLastMonthAsync()
    {
        ClearMessages();
        var copied = await _budgets.CopyFromPreviousMonthAsync(DateOnly.FromDateTime(Month));
        await RefreshAsync();

        InfoMessage = copied == 0
            ? "Nothing to copy: last month has no budget, or this month is already set."
            : $"Copied {copied} budget(s) from last month.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearMessages();

        var toSave = new List<(int CategoryId, decimal Amount)>();
        foreach (var row in Rows)
        {
            var text = row.AmountText?.Trim() ?? "";

            if (text.Length == 0)
            {
                // A blank box clears an existing budget, and is ignored otherwise.
                if (row.HadBudget) toSave.Add((row.CategoryId, 0m));
                continue;
            }

            if (!TryParseAmount(text, out var amount) || amount < 0)
            {
                ErrorMessage = $"Check the amount for {row.Name}.";
                return;
            }

            toSave.Add((row.CategoryId, Math.Round(amount, 2)));
        }

        var month = DateOnly.FromDateTime(Month);
        foreach (var (categoryId, amount) in toSave)
            await _budgets.SetAsync(categoryId, month, amount);

        await RefreshAsync();
        InfoMessage = "Budget saved.";
    }

    private async Task RefreshAsync()
    {
        var categories = await _categories.GetAllAsync();
        var budgets = await _budgets.GetForMonthAsync(DateOnly.FromDateTime(Month));
        var byCategory = budgets.ToDictionary(b => b.CategoryId, b => b.Amount);

        Rows.Clear();
        foreach (var c in categories)
            Rows.Add(new BudgetRowViewModel(c, byCategory.TryGetValue(c.Id, out var a) ? a : null));
    }

    private void ClearMessages()
    {
        ErrorMessage = "";
        InfoMessage = "";
    }

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
}
