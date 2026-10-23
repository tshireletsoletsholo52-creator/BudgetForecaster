namespace BudgetForecaster.Core.Services;

/// <summary>Rates are fractions: 6% is 0.06.</summary>
public record ForecastInputs(
    decimal StartingBalance,
    decimal MonthlyContribution,
    decimal AnnualReturn,
    decimal AnnualInflation);

public record ForecastResult(
    int Years,
    decimal FutureValue,
    decimal RealValue,
    decimal TotalContributed,
    decimal InterestEarned,
    int? DepletedInMonth);

public static class ForecastService
{
    public static readonly int[] DefaultHorizons = [1, 5, 10];

    public static ForecastResult Calculate(ForecastInputs inputs, int years)
    {
        if (years <= 0)
            throw new ArgumentOutOfRangeException(nameof(years), "Years must be greater than zero.");

        var monthlyRate = inputs.AnnualReturn / 12m;
        var months = years * 12;

        var balance = inputs.StartingBalance;
        int? depletedIn = null;

        for (var month = 1; month <= months; month++)
        {
            balance = balance * (1m + monthlyRate) + inputs.MonthlyContribution;
            if (balance < 0 && depletedIn is null) depletedIn = month;
        }

        var inflationFactor = 1m;
        for (var year = 0; year < years; year++)
            inflationFactor *= 1m + inputs.AnnualInflation;

        var contributed = inputs.StartingBalance + inputs.MonthlyContribution * months;

        return new ForecastResult(
            years,
            balance,
            balance / inflationFactor,
            contributed,
            balance - contributed,
            depletedIn);
    }

    public static IReadOnlyList<ForecastResult> CalculateAll(ForecastInputs inputs) =>
        DefaultHorizons.Select(y => Calculate(inputs, y)).ToList();

    /// <summary>
    /// Default monthly contribution: the average actual net cash of the months supplied
    /// (the caller passes the last completed months that have data).
    /// </summary>
    public static decimal AverageNetCash(IEnumerable<decimal> monthlyNetCash)
    {
        var list = monthlyNetCash.ToList();
        return list.Count == 0 ? 0m : Math.Round(list.Average(), 2);
    }
}
