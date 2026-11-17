using System.Text.RegularExpressions;

namespace BudgetForecaster.Core;

public static class CategoryRules
{
    public const int MaxNameLength = 40;

    /// <summary>Trims the name and collapses repeated spaces. Throws if it is blank or too long.</summary>
    public static string NormalizeName(string? raw)
    {
        var name = Regex.Replace(raw ?? "", @"\s+", " ").Trim();

        if (name.Length == 0)
            throw new CategoryValidationException("Enter a category name.");
        if (name.Length > MaxNameLength)
            throw new CategoryValidationException($"Category names can be at most {MaxNameLength} characters.");

        return name;
    }
}
