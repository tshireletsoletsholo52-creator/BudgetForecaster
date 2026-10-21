using BudgetForecaster.Core.Models;

namespace BudgetForecaster.Core.Services;

public enum VarianceStatus { OnTrack, Favourable, Unfavourable }

public record StatementRow(int CategoryId, string CategoryName, CategoryType Type, decimal Budgeted, decimal Actual)
{
    public decimal Variance => Actual - Budgeted;

    public VarianceStatus Status
    {
        get
        {
            if (Variance == 0) return VarianceStatus.OnTrack;
            if (Type == CategoryType.Expense)
                return Variance > 0 ? VarianceStatus.Unfavourable : VarianceStatus.Favourable;
            return Variance > 0 ? VarianceStatus.Favourable : VarianceStatus.Unfavourable;
        }
    }
}

public record Statement(
    DateOnly Month,
    IReadOnlyList<StatementRow> IncomeRows,
    IReadOnlyList<StatementRow> ExpenseRows)
{
    public decimal IncomeBudgeted => IncomeRows.Sum(r => r.Budgeted);
    public decimal IncomeActual => IncomeRows.Sum(r => r.Actual);
    public decimal ExpensesBudgeted => ExpenseRows.Sum(r => r.Budgeted);
    public decimal ExpensesActual => ExpenseRows.Sum(r => r.Actual);
    public decimal NetBudgeted => IncomeBudgeted - ExpensesBudgeted;
    public decimal NetActual => IncomeActual - ExpensesActual;
}

public static class StatementService
{
    public static Statement Generate(
        DateOnly month,
        IEnumerable<Category> categories,
        IEnumerable<Budget> budgets,
        IEnumerable<Transaction> transactions)
    {
        var monthBudgets = budgets
            .Where(b => b.Month.Year == month.Year && b.Month.Month == month.Month)
            .GroupBy(b => b.CategoryId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.Amount));

        var monthActuals = transactions
            .Where(t => t.Date.Year == month.Year && t.Date.Month == month.Month)
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var rows = new List<StatementRow>();
        foreach (var c in categories)
        {
            monthBudgets.TryGetValue(c.Id, out var budgeted);
            monthActuals.TryGetValue(c.Id, out var actual);

            // R6: archived categories only appear if they have activity this month.
            if (c.IsArchived && budgeted == 0 && actual == 0) continue;

            rows.Add(new StatementRow(c.Id, c.Name, c.Type, budgeted, actual));
        }

        return new Statement(
            new DateOnly(month.Year, month.Month, 1),
            rows.Where(r => r.Type == CategoryType.Income).OrderBy(r => r.CategoryName).ToList(),
            rows.Where(r => r.Type == CategoryType.Expense).OrderBy(r => r.CategoryName).ToList());
    }
}
