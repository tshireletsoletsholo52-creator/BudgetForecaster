using BudgetForecaster.Core;

namespace BudgetForecaster.Tests;

public class MoneyTests
{
    [Fact]
    public void ToCents_ConvertsExactly()
    {
        Assert.Equal(1999, Money.ToCents(19.99m));
    }

    [Fact]
    public void FromCents_ConvertsExactly()
    {
        Assert.Equal(19.99m, Money.FromCents(1999));
    }

    [Fact]
    public void ToCents_RoundsHalfAwayFromZero()
    {
        Assert.Equal(1001, Money.ToCents(10.005m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(850000)]
    public void RoundTrip_IsLossless(long cents)
    {
        Assert.Equal(cents, Money.ToCents(Money.FromCents(cents)));
    }
}