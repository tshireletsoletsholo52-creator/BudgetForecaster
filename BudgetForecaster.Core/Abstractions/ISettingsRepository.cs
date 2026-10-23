namespace BudgetForecaster.Core.Abstractions;

public interface ISettingsRepository
{
    /// <summary>Returns the stored value, or null if the key has never been set.</summary>
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
}
