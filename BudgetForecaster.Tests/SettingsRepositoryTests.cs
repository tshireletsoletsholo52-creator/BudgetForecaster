using BudgetForecaster.Data;

namespace BudgetForecaster.Tests;

public class SettingsRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bf_test_{Guid.NewGuid():N}.db");
    private readonly SqliteDatabase _db;
    private readonly SettingsRepository _settings;

    public SettingsRepositoryTests()
    {
        _db = new SqliteDatabase(_path);
        _settings = new SettingsRepository(_db);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task MissingKey_ReturnsNull()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        Assert.Null(await _settings.GetAsync("does.not.exist"));
    }

    [Fact]
    public async Task SetThenGet_RoundTrips()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        await _settings.SetAsync("forecast.return", "6.5");

        Assert.Equal("6.5", await _settings.GetAsync("forecast.return"));
    }

    [Fact]
    public async Task SetTwice_OverwritesInsteadOfDuplicating()
    {
        await DatabaseInitializer.InitializeAsync(_db);

        await _settings.SetAsync("forecast.return", "6");
        await _settings.SetAsync("forecast.return", "8");

        Assert.Equal("8", await _settings.GetAsync("forecast.return"));
    }
}
