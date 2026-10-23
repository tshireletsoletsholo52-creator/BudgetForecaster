using BudgetForecaster.Core.Services;

namespace BudgetForecaster.Tests;

public class ForecastServiceTests
{
    [Fact]
    public void ZeroReturn_IsSimpleArithmetic()
    {
        var r = ForecastService.Calculate(new ForecastInputs(1000m, 100m, 0m, 0m), 1);

        Assert.Equal(2200m, r.FutureValue);
        Assert.Equal(2200m, r.TotalContributed);
        Assert.Equal(0m, r.InterestEarned);
    }

    [Fact]
    public void MonthlyContributions_MatchAnnuityFormula()
    {
        // R1,000 a month at 6% a year for 1 year: 1000 * ((1.005^12 - 1) / 0.005) = 12,335.56
        var r = ForecastService.Calculate(new ForecastInputs(0m, 1000m, 0.06m, 0m), 1);

        Assert.Equal(12335.56m, r.FutureValue, 2);
        Assert.Equal(12000m, r.TotalContributed);
    }

    [Fact]
    public void StartingBalanceOnly_CompoundsMonthly()
    {
        // 10,000 * 1.005^12 = 10,616.78
        var r = ForecastService.Calculate(new ForecastInputs(10000m, 0m, 0.06m, 0m), 1);

        Assert.Equal(10616.78m, r.FutureValue, 2);
    }

    [Fact]
    public void TenYears_MatchesKnownAnnuityValue()
    {
        // 1000 * ((1.005^120 - 1) / 0.005) = 163,879.35
        var r = ForecastService.Calculate(new ForecastInputs(0m, 1000m, 0.06m, 0m), 10);

        Assert.Equal(163879.35m, r.FutureValue, 2);
        Assert.Equal(120000m, r.TotalContributed);
    }

    [Fact]
    public void Inflation_ReducesRealValue()
    {
        var r = ForecastService.Calculate(new ForecastInputs(10000m, 0m, 0m, 0.05m), 1);

        Assert.Equal(10000m, r.FutureValue);
        Assert.Equal(9523.81m, r.RealValue, 2);
    }

    [Fact]
    public void ZeroInflation_RealEqualsNominal()
    {
        var r = ForecastService.Calculate(new ForecastInputs(500m, 200m, 0.07m, 0m), 5);

        Assert.Equal(r.FutureValue, r.RealValue);
    }

    [Fact]
    public void NegativeContribution_ReportsWhenSavingsRunOut()
    {
        // 1,000 start, losing 300 a month, no interest: negative in month 4.
        var r = ForecastService.Calculate(new ForecastInputs(1000m, -300m, 0m, 0m), 1);

        Assert.Equal(4, r.DepletedInMonth);
        Assert.True(r.FutureValue < 0);
    }

    [Fact]
    public void CalculateAll_ReturnsOneFiveAndTenYears()
    {
        var results = ForecastService.CalculateAll(new ForecastInputs(0m, 1000m, 0.06m, 0.05m));

        Assert.Equal([1, 5, 10], results.Select(r => r.Years).ToArray());
    }

    [Fact]
    public void AverageNetCash_AveragesTheMonthsGiven()
    {
        Assert.Equal(6000m, ForecastService.AverageNetCash([5000m, 6000m, 7000m]));
        Assert.Equal(0m, ForecastService.AverageNetCash([]));
    }

    [Fact]
    public void ZeroOrNegativeYears_Throws()
    {
        var inputs = new ForecastInputs(0m, 0m, 0m, 0m);

        Assert.Throws<ArgumentOutOfRangeException>(() => ForecastService.Calculate(inputs, 0));
    }
}
