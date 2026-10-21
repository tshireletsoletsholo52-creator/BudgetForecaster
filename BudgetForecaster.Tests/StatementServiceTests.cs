using BudgetForecaster.Core.Models;
using BudgetForecaster.Core.Services;

namespace BudgetForecaster.Tests;

public class StatementServiceTests
{
    private static readonly DateOnly June = new(2026, 6, 1);

    private static readonly Category Salary = new() { Id = 1, Name = "Salary", Type = CategoryType.Income };
    private static readonly Category Groceries = new() { Id = 2, Name = "Groceries", Type = CategoryType.Expense };
    private static readonly Category Rent = new() { Id = 3, Name = "Rent", Type = CategoryType.Expense };

    private static Budget B(int cat, decimal amt, DateOnly? month = null) =>
        new() { CategoryId = cat, Amount = amt, Month = month ?? June };

    private static Transaction T(int cat, decimal amt, DateOnly date) =>
        new() { CategoryId = cat, Amount = amt, Date = date };

    [Fact]
    public void Expense_OverBudget_IsUnfavourable()
    {
        var s = StatementService.Generate(June, [Groceries], [B(2, 3000)], [T(2, 3400, new DateOnly(2026, 6, 10))]);

        var row = Assert.Single(s.ExpenseRows);
        Assert.Equal(400m, row.Variance);
        Assert.Equal(VarianceStatus.Unfavourable, row.Status);
    }

    [Fact]
    public void Expense_UnderBudget_IsFavourable()
    {
        var s = StatementService.Generate(June, [Groceries], [B(2, 3000)], [T(2, 2400, new DateOnly(2026, 6, 10))]);

        var row = Assert.Single(s.ExpenseRows);
        Assert.Equal(-600m, row.Variance);
        Assert.Equal(VarianceStatus.Favourable, row.Status);
    }

    [Fact]
    public void Income_BelowTarget_IsUnfavourable()
    {
        var s = StatementService.Generate(June, [Salary], [B(1, 20000)], [T(1, 18000, new DateOnly(2026, 6, 25))]);

        var row = Assert.Single(s.IncomeRows);
        Assert.Equal(-2000m, row.Variance);
        Assert.Equal(VarianceStatus.Unfavourable, row.Status);
    }

    [Fact]
    public void Income_AboveTarget_IsFavourable()
    {
        var s = StatementService.Generate(June, [Salary], [B(1, 20000)], [T(1, 21000, new DateOnly(2026, 6, 25))]);

        Assert.Equal(VarianceStatus.Favourable, Assert.Single(s.IncomeRows).Status);
    }

    [Fact]
    public void ExactlyOnBudget_IsOnTrack()
    {
        var s = StatementService.Generate(June, [Rent], [B(3, 8500)], [T(3, 8500, new DateOnly(2026, 6, 1))]);

        Assert.Equal(VarianceStatus.OnTrack, Assert.Single(s.ExpenseRows).Status);
    }

    [Fact]
    public void UnbudgetedSpending_StillAppears_WithZeroBudget()
    {
        var s = StatementService.Generate(June, [Groceries], [], [T(2, 150, new DateOnly(2026, 6, 5))]);

        var row = Assert.Single(s.ExpenseRows);
        Assert.Equal(0m, row.Budgeted);
        Assert.Equal(150m, row.Actual);
        Assert.Equal(VarianceStatus.Unfavourable, row.Status);
    }

    [Fact]
    public void OtherMonths_AreExcluded()
    {
        var s = StatementService.Generate(
            June,
            [Groceries],
            [B(2, 3000), B(2, 9999, new DateOnly(2026, 7, 1))],
            [
                T(2, 100, new DateOnly(2026, 5, 31)),
                T(2, 200, new DateOnly(2026, 6, 1)),
                T(2, 300, new DateOnly(2026, 6, 30)),
                T(2, 400, new DateOnly(2026, 7, 1))
            ]);

        var row = Assert.Single(s.ExpenseRows);
        Assert.Equal(3000m, row.Budgeted);
        Assert.Equal(500m, row.Actual);
    }

    [Fact]
    public void ArchivedCategory_WithNoActivity_IsHidden_ButShownWithActivity()
    {
        var old = new Category { Id = 9, Name = "Old", Type = CategoryType.Expense, IsArchived = true };

        var hidden = StatementService.Generate(June, [old], [], []);
        var shown = StatementService.Generate(June, [old], [], [T(9, 50, new DateOnly(2026, 6, 3))]);

        Assert.Empty(hidden.ExpenseRows);
        Assert.Single(shown.ExpenseRows);
    }

    [Fact]
    public void NetCash_IsIncomeMinusExpenses_ForBudgetAndActual()
    {
        var s = StatementService.Generate(
            June,
            [Salary, Groceries, Rent],
            [B(1, 20000), B(2, 3000), B(3, 8500)],
            [
                T(1, 20000, new DateOnly(2026, 6, 25)),
                T(2, 3400, new DateOnly(2026, 6, 10)),
                T(3, 8500, new DateOnly(2026, 6, 1))
            ]);

        Assert.Equal(8500m, s.NetBudgeted);
        Assert.Equal(8100m, s.NetActual);
    }
}
