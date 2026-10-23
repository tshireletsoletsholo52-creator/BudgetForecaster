using Microsoft.Extensions.Logging;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Data;


namespace BudgetForecaster;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "financials.db");
        var db = new SqliteDatabase(dbPath);

        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<ICategoryRepository>(new CategoryRepository(db));
        builder.Services.AddSingleton<ITransactionRepository>(new TransactionRepository(db));
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddSingleton<IBudgetRepository>(new BudgetRepository(db));
        builder.Services.AddSingleton<BudgetViewModel>();
        builder.Services.AddSingleton<StatementViewModel>();
        builder.Services.AddTransient<BudgetPage>();
        builder.Services.AddTransient<StatementPage>();
        return builder.Build();
	}
}
