namespace BudgetForecaster.Core.Models;

public class Transaction
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }   // always positive; the category decides income vs expense
    public DateOnly Date { get; set; }
    public string? Note { get; set; }
}