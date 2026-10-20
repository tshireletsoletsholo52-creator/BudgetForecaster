namespace BudgetForecaster.Core;

public static class Money
{
    public static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    public static decimal FromCents(long cents) => cents / 100m;
}