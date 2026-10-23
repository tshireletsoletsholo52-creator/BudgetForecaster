using System.Collections.ObjectModel;
using System.Globalization;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using BudgetForecaster.Core.Services;
using BudgetForecaster.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;

namespace BudgetForecaster;

public record StatementRowDisplay(string Name, string StatusText, Color StatusColor, string DetailText);

public partial class StatementViewModel : ObservableObject
{
    private readonly SqliteDatabase _db;
    private readonly ICategoryRepository _categories;
    private readonly IBudgetRepository _budgets;
    private readonly ITransactionRepository _transactions;

    public StatementViewModel(
        SqliteDatabase db,
        ICategoryRepository categories,
        IBudgetRepository budgets,
        ITransactionRepository transactions)
    {
        _db = db;
        _categories = categories;
        _budgets = budgets;
        _transactions = transactions;
    }

    public ObservableCollection<StatementRowDisplay> IncomeRows { get; } = new();
    public ObservableCollection<StatementRowDisplay> ExpenseRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel))]
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _incomeBudgetedText = "R 0.00";
    [ObservableProperty] private string _incomeActualText = "R 0.00";
    [ObservableProperty] private string _expensesBudgetedText = "R 0.00";
    [ObservableProperty] private string _expensesActualText = "R 0.00";
    [ObservableProperty] private string _netBudgetedText = "R 0.00";
    [ObservableProperty] private string _netActualText = "R 0.00";

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
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        Month = Month.AddMonths(1);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var month = DateOnly.FromDateTime(Month);

        var categories = await _categories.GetAllAsync(includeArchived: true);
        var budgets = await _budgets.GetForMonthAsync(month);
        var transactions = await _transactions.GetForMonthAsync(month);

        var statement = StatementService.Generate(month, categories, budgets, transactions);

        Fill(IncomeRows, statement.IncomeRows);
        Fill(ExpenseRows, statement.ExpenseRows);

        IncomeBudgetedText = Format(statement.IncomeBudgeted);
        IncomeActualText = Format(statement.IncomeActual);
        ExpensesBudgetedText = Format(statement.ExpensesBudgeted);
        ExpensesActualText = Format(statement.ExpensesActual);
        NetBudgetedText = Format(statement.NetBudgeted);
        NetActualText = Format(statement.NetActual);

        IsEmpty = IncomeRows.Count == 0 && ExpenseRows.Count == 0;
    }

    // Categories with no budget and no activity this month are left out to keep the statement readable.
    private static void Fill(ObservableCollection<StatementRowDisplay> target, IEnumerable<StatementRow> rows)
    {
        target.Clear();
        foreach (var r in rows.Where(r => r.Budgeted != 0 || r.Actual != 0))
        {
            target.Add(new StatementRowDisplay(
                r.CategoryName,
                StatusText(r),
                StatusColor(r.Status),
                $"Budget {Format(r.Budgeted)}  |  Actual {Format(r.Actual)}  |  Variance {FormatVariance(r.Variance)}"));
        }
    }

    private static string StatusText(StatementRow r) => (r.Type, r.Status) switch
    {
        (CategoryType.Income, VarianceStatus.OnTrack) => "On target",
        (_, VarianceStatus.OnTrack) => "On track",
        (CategoryType.Expense, VarianceStatus.Unfavourable) => "Over budget",
        (CategoryType.Expense, VarianceStatus.Favourable) => "Under budget",
        (CategoryType.Income, VarianceStatus.Favourable) => "Above target",
        _ => "Below target"
    };

    private static Color StatusColor(VarianceStatus status) => status switch
    {
        VarianceStatus.Favourable => Colors.SeaGreen,
        VarianceStatus.Unfavourable => Colors.IndianRed,
        _ => Colors.Gray
    };

    private static string Format(decimal amount) =>
        (amount < 0 ? "-R " : "R ") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    private static string FormatVariance(decimal variance) =>
        variance == 0 ? "R 0.00" : (variance > 0 ? "+ " : "- ") + Format(Math.Abs(variance));
}
