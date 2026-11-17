namespace BudgetForecaster.Core;

/// <summary>Thrown when a category name or action breaks a rule. The message is safe to show to the user.</summary>
public class CategoryValidationException : Exception
{
    public CategoryValidationException(string message) : base(message) { }
}
