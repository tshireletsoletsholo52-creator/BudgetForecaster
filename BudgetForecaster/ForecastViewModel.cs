using System.Collections.ObjectModel;
using System.Globalization;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using BudgetForecaster.Core.Services;
using BudgetForecaster.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetForecaster;

public record ForecastRowDisplay(string Horizon, string FutureText, string RealText, string DetailText);

public partial class ForecastViewModel : ObservableObject
{
    private const string KeyStart = "forecast.startingBalance";
    private const string KeyContribution = "forecast.contribution";
    private const string KeyReturn = "forecast.annualReturnPercent";
    private const string KeyInflation = "forecast.annualInflationPercent";

    private readonly SqliteDatabase _db;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly ISettingsRepository _settings;

    private decimal? _averageNetCash;

    public ForecastViewModel(
        SqliteDatabase db,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        ISettingsRepository settings)
    {
        _db = db;
        _categories = categories;
        _transactions = transactions;
        _settings = settings;
    }

    public ObservableCollection<ForecastRowDisplay> Results { get; } = new();

    [ObservableProperty] private string _startingBalanceText = "0";
    [ObservableProperty] private string _contributionText = "0";
    [ObservableProperty] private string _returnText = "6";
    [ObservableProperty] private string _inflationText = "5";
    [ObservableProperty] private string _averageInfo = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _warningMessage = "";
    [ObservableProperty] private bool _hasResults;

    public string Disclaimer =>
        "This is an estimate based on the numbers above, not financial advice. Real returns and prices vary.";

    public async Task LoadAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        StartingBalanceText = await _settings.GetAsync(KeyStart) ?? "0";
        ReturnText = await _settings.GetAsync(KeyReturn) ?? "6";
        InflationText = await _settings.GetAsync(KeyInflation) ?? "5";

        _averageNetCash = await ComputeAverageNetCashAsync();
        AverageInfo = _averageNetCash is null
            ? "No completed months with transactions yet, so enter a monthly contribution yourself."
            : $"Average net cash over your last completed months with data: R {_averageNetCash.Value.ToString("N2", CultureInfo.InvariantCulture)}.";

        var savedContribution = await _settings.GetAsync(KeyContribution);
        ContributionText = savedContribution
            ?? (_averageNetCash?.ToString("F2", CultureInfo.InvariantCulture) ?? "0");

        await CalculateAsync();
    }

    [RelayCommand]
    private async Task UseAverageAsync()
    {
        if (_averageNetCash is null)
        {
            ErrorMessage = "There is no average yet. Add transactions for a completed month first.";
            return;
        }

        ErrorMessage = "";
        ContributionText = _averageNetCash.Value.ToString("F2", CultureInfo.InvariantCulture);
        await CalculateAsync();
    }

    [RelayCommand]
    private async Task CalculateAsync()
    {
        ErrorMessage = "";
        WarningMessage = "";

        if (!TryParse(StartingBalanceText, out var start))
        {
            ErrorMessage = "Starting savings must be a number.";
            return;
        }
        if (!TryParse(ContributionText, out var contribution))
        {
            ErrorMessage = "Monthly contribution must be a number (negative if you are overspending).";
            return;
        }
        if (!TryParse(ReturnText, out var returnPercent) || returnPercent < -50 || returnPercent > 100)
        {
            ErrorMessage = "Expected annual return must be between -50 and 100 percent.";
            return;
        }
        if (!TryParse(InflationText, out var inflationPercent) || inflationPercent < 0 || inflationPercent > 100)
        {
            ErrorMessage = "Inflation must be between 0 and 100 percent.";
            return;
        }

        await _settings.SetAsync(KeyStart, StartingBalanceText.Trim());
        await _settings.SetAsync(KeyContribution, ContributionText.Trim());
        await _settings.SetAsync(KeyReturn, ReturnText.Trim());
        await _settings.SetAsync(KeyInflation, InflationText.Trim());

        var inputs = new ForecastInputs(start, contribution, returnPercent / 100m, inflationPercent / 100m);
        var results = ForecastService.CalculateAll(inputs);

        Results.Clear();
        foreach (var r in results)
        {
            Results.Add(new ForecastRowDisplay(
                r.Years == 1 ? "After 1 year" : $"After {r.Years} years",
                Format(r.FutureValue),
                $"In today's money: {Format(r.RealValue)}",
                $"Contributed {Format(r.TotalContributed)}  |  Growth {Format(r.InterestEarned)}"));
        }
        HasResults = Results.Count > 0;

        var depleted = results.Last().DepletedInMonth;
        if (depleted is int month)
        {
            var years = month / 12;
            var rest = month % 12;
            WarningMessage = $"At this rate your savings run out after about {years} year(s) and {rest} month(s).";
        }
    }

    // Average actual net cash (income minus expenses) of the last 3 completed months that have transactions.
    private async Task<decimal?> ComputeAverageNetCashAsync()
    {
        var categories = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id);
        var thisMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var nets = new List<decimal>();

        for (var i = 1; i <= 3; i++)
        {
            var items = await _transactions.GetForMonthAsync(thisMonth.AddMonths(-i));
            if (items.Count == 0) continue;

            var income = items.Where(t => categories[t.CategoryId].Type == CategoryType.Income).Sum(t => t.Amount);
            var expenses = items.Where(t => categories[t.CategoryId].Type == CategoryType.Expense).Sum(t => t.Amount);
            nets.Add(income - expenses);
        }

        return nets.Count == 0 ? null : ForecastService.AverageNetCash(nets);
    }

    private static string Format(decimal amount) =>
        (amount < 0 ? "-R " : "R ") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
