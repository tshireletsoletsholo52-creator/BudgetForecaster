namespace BudgetForecaster.Core.Models;

public class Budget
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public DateOnly Month { get; set; }   // always the 1st day of the month
    public decimal Amount { get; set; }
}